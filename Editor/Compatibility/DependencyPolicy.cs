using System;

namespace VRVlog.LilToonExporter.Compatibility
{
    // No UniVRM/MA/lilToon types: diagnostics survive an unavailable backend.
    public static partial class DependencyPolicy
    {
        // VRChat SDK Base/Avatars in which VRCAnimatorPlayAudio may be omitted from
        // an expression capture copy: the same release, VrchatSdkMinimum <= v < VrchatSdkBelow.
        public static bool SupportsVrchatSdkAudio(string avatars, string sdkBase)
        {
            var parsed = avatars == null ? null : ParseRelease(avatars);
            return parsed != null && string.Equals(avatars, sdkBase, StringComparison.Ordinal) &&
                CompareRelease(parsed, VrchatSdkLow) >= 0 && CompareRelease(parsed, VrchatSdkHigh) < 0;
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
            return string.Format(translate("アプリではlilToon {1}として描画します（導入済みのバージョンは{0}）"), version, LilToonVersion);
        }

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

        public const string Recovery = " VCC／ALCOMで対応バージョンを確認してください。別のツールと競合する場合は、更新前のプロジェクトのコピーで書き出してください。";
    }
}
