using System;

namespace VRVlog.LilToonExporter.Compatibility
{
    // No UniVRM/MA/lilToon types: diagnostics survive an unavailable backend.
    public static partial class DependencyPolicy
    {
        // Every release of the UniVrmMajor.UniVrmMinor patch line (UniVrmRange).
        // UniVrmVersions lists the patches verified with real packages.
        public static bool SupportsUniVrm(string version)
        {
            var parsed = version == null ? null : ParseRelease(version);
            return parsed != null && parsed[0] == UniVrmMajor && parsed[1] == UniVrmMinor;
        }

        public static string UniVrmError(string vrm, string gltf)
        {
            if (!SupportsUniVrm(vrm) || !SupportsUniVrm(gltf))
                return "UniVRM / UniGLTF の対応バージョン: " + UniVrmRange +
                    "。現在: " + Display(vrm) + " / " + Display(gltf) + "。";
            if (!string.Equals(vrm, gltf, StringComparison.Ordinal))
                return "UniVRM と UniGLTF のバージョンが異なります（" + vrm + " / " + gltf + "）。同じバージョンを選んでください。";
            return null;
        }

        // VRChat SDK Base/Avatars in which VRCAnimatorPlayAudio may be omitted from
        // an expression capture copy: the same version, VrchatSdkMinimum <= X.Y.Z < VrchatSdkBelow.
        // A pre-release (X.Y.Z-beta.N) counts as its X.Y.Z: VRCAnimatorPlayAudio and
        // VRC_AnimatorPlayAudio are the same in every 3.5.2+ pre-release as in the stable ones.
        public static bool SupportsVrchatSdkAudio(string avatars, string sdkBase)
        {
            var parsed = avatars == null ? null : ParseRelease(StripPreRelease(avatars));
            return parsed != null && string.Equals(avatars, sdkBase, StringComparison.Ordinal) &&
                CompareRelease(parsed, VrchatSdkLow) >= 0 && CompareRelease(parsed, VrchatSdkHigh) < 0;
        }

        // "3.10.6-beta.2" -> "3.10.6". A missing or malformed suffix is left for ParseRelease to reject.
        static string StripPreRelease(string version)
        {
            var dash = version.IndexOf('-');
            if (dash < 0) return version;
            var suffix = version.Substring(dash + 1);
            if (suffix.Length == 0) return version;
            foreach (var c in suffix)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c == '.' || c == '-')) return version;
            return version.Substring(0, dash);
        }

        static readonly int[] VrchatSdkLow = ParseRelease(VrchatSdkMinimum) ?? throw new InvalidOperationException("Invalid VrchatSdkMinimum.");
        static readonly int[] VrchatSdkHigh = ParseRelease(VrchatSdkBelow) ?? throw new InvalidOperationException("Invalid VrchatSdkBelow.");

        // Modular Avatar releases whose preview API (IMeshSelector, the
        // InitialStates analysis) the appearance freeze reflects:
        // ModularAvatarMinimum <= v < ModularAvatarBelow, stable releases only.
        public static bool SupportsModularAvatarAppearance(string version)
        {
            var parsed = version == null ? null : ParseRelease(version);
            return parsed != null && CompareRelease(parsed, ModularAvatarLow) >= 0 && CompareRelease(parsed, ModularAvatarHigh) < 0;
        }

        static readonly int[] ModularAvatarLow = ParseRelease(ModularAvatarMinimum) ?? throw new InvalidOperationException("Invalid ModularAvatarMinimum.");
        static readonly int[] ModularAvatarHigh = ParseRelease(ModularAvatarBelow) ?? throw new InvalidOperationException("Invalid ModularAvatarBelow.");

        public static string ModularAvatarAppearanceError(string version) => ModularAvatarAppearanceError(version, text => text);

        // translate receives the Japanese format and returns the localized
        // format; {0} is the installed version and {1}..{2} the supported range.
        // Only a readable X.Y.Z outside the range stops here. A missing or
        // unreadable version (MA under Assets, a pre-release) returns null so
        // the caller tries MA's API and reports ModularAvatarUnknownVersionError
        // if that API is not there.
        public static string ModularAvatarAppearanceError(string version, Func<string, string> translate)
        {
            if (SupportsModularAvatarAppearance(version) || ModularAvatarVersionUnknown(version)) return null;
            const string format = "Modular Avatar {0}では、Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定できません。対応範囲は{1}以上{2}未満です。VCC／ALCOMでModular Avatarを1.18.xに変更してください。アバターは変更していません。これらのMAコンポーネントを使っていないアバターは、どのMAのバージョンでも書き出せます。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarMinimum, ModularAvatarBelow);
        }

        public static bool ModularAvatarVersionUnknown(string version) => version == null || ParseRelease(version) == null;

        public static string ModularAvatarUnknownVersionError(string version) => ModularAvatarUnknownVersionError(version, text => text);

        // Shown when the version could not be read and MA's API then failed.
        public static string ModularAvatarUnknownVersionError(string version, Func<string, string> translate)
        {
            const string format = "Modular Avatarのバージョンを読めなかったため（{0}）、バージョンを確かめずにShape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定しようとしましたが、固定できませんでした。対応範囲は{1}以上{2}未満で、VCC／ALCOMで入れた1.18.xのModular Avatarなら固定できます。アバターは変更していません。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarMinimum, ModularAvatarBelow);
        }

        public enum LilToonSupport { Catalogue, Missing, Unreadable, Older, Newer }

        static readonly int[] Catalogue = ParseRelease(LilToonVersion) ?? throw new InvalidOperationException("Invalid LilToonVersion.");

        // LilToonVersion is the catalogue every export conforms to (and the
        // value of sourceLilToonVersion). Any installed lilToon exports: only a
        // missing one stops. Another version is compared with that catalogue
        // by LilToonStructureCheck, which lists what the export leaves out.
        public static LilToonSupport ClassifyLilToon(string version)
        {
            if (string.IsNullOrEmpty(version)) return LilToonSupport.Missing;
            var parsed = ParseRelease(version);
            if (parsed == null) return LilToonSupport.Unreadable;
            var order = CompareRelease(parsed, Catalogue);
            return order < 0 ? LilToonSupport.Older : order > 0 ? LilToonSupport.Newer : LilToonSupport.Catalogue;
        }

        public static bool AcceptsLilToon(string version) => ClassifyLilToon(version) != LilToonSupport.Missing;

        public static string LilToonError(string version) => LilToonError(version, text => text);

        // translate receives the Japanese text and returns the localized one.
        public static string LilToonError(string version, Func<string, string> translate) =>
            AcceptsLilToon(version) ? null : translate("lilToonが見つかりません（VCC／ALCOMでlilToonを追加）");

        public static string LilToonWarning(string version) => LilToonWarning(version, text => text);

        // Shown in the export result when the installed lilToon is not the
        // catalogue version. {0} is the installed version, {1} the catalogue.
        public static string LilToonWarning(string version, Func<string, string> translate)
        {
            var support = ClassifyLilToon(version);
            if (support == LilToonSupport.Catalogue || support == LilToonSupport.Missing) return null;
            return string.Format(translate("アプリではlilToon {1}として描画します（導入済みのバージョンは{0}）"), DisplayLilToon(version, translate), LilToonVersion);
        }

        // Recorded for lilToon under Assets whose package.json gives no
        // version. It is not X.Y.Z, so it exports with LilToonWarning.
        public const string LilToonUnknownVersion = "unknown";

        public static string DisplayLilToon(string version, Func<string, string> translate) =>
            version == LilToonUnknownVersion ? translate("不明") : Display(version);

        // assetsFolder is where lilToon was imported from its unitypackage,
        // or null for the VCC／ALCOM package.
        public static string DescribeLilToon(string version, string assetsFolder, Func<string, string> translate) =>
            assetsFolder == null ? DisplayLilToon(version, translate) :
            string.Format(translate("{0}（Assetsに導入: {1}）"), DisplayLilToon(version, translate), assetsFolder);

        // Numeric X.Y.Z only. Pre-release and build suffixes are not releases.
        static int[] ParseRelease(string version)
        {
            var parts = version.Split('.');
            if (parts.Length != 3) return null;
            var result = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part.Length > 9 || part.Length > 1 && part[0] == '0') return null;
                foreach (var c in part) if (c < '0' || c > '9') return null;
                result[i] = int.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
            }
            return result;
        }

        static int CompareRelease(int[] left, int[] right)
        {
            for (var i = 0; i < 3; i++)
                if (left[i] != right[i]) return left[i].CompareTo(right[i]);
            return 0;
        }

        public static string Display(string version) => string.IsNullOrEmpty(version) ? "未インストール" : version;

        public static void RequireUniVrm(string vrm, string gltf)
        {
            var error = UniVrmError(vrm, gltf);
            if (error != null) throw new InvalidOperationException(error + Recovery);
        }

        public const string Recovery = " VCC／ALCOMで対応バージョンを確認してください。別のツールと競合する場合は、更新前のプロジェクトのコピーで書き出してください。";
    }
}
