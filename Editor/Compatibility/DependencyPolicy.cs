using System;

namespace VRVlog.LilToonExporter.Compatibility
{
    // No UniVRM/MA/lilToon types: diagnostics survive an unavailable backend.
    public static partial class DependencyPolicy
    {
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
            // SemVer: dot-separated, non-empty identifiers of [0-9A-Za-z-].
            foreach (var identifier in version.Substring(dash + 1).Split('.'))
            {
                if (identifier.Length == 0) return version;
                foreach (var c in identifier)
                    if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c == '-')) return version;
            }
            return version.Substring(0, dash);
        }

        static readonly int[] VrchatSdkLow = ParseRelease(VrchatSdkMinimum) ?? throw new InvalidOperationException("Invalid VrchatSdkMinimum.");
        static readonly int[] VrchatSdkHigh = ParseRelease(VrchatSdkBelow) ?? throw new InvalidOperationException("Invalid VrchatSdkBelow.");

        // Modular Avatar releases whose preview API (ReactiveObjectAnalyzer and
        // each version's own mesh deletion) the appearance freeze reads:
        // ModularAvatarMinimum or later, with no upper bound. A pre-release
        // (X.Y.Z-pre) counts as its X.Y.Z. A newer MA that changed that API
        // stops in MaAppearanceSnapshot with ModularAvatarFreezeFailedError.
        public static bool SupportsModularAvatarAppearance(string version)
        {
            var parsed = version == null ? null : ParseRelease(StripPreRelease(version));
            return parsed != null && CompareRelease(parsed, ModularAvatarLow) >= 0;
        }

        // NDMF NdmfMajor.x from NdmfMinimum, plus the verified NdmfPrereleases.
        public static bool SupportsNdmf(string version)
        {
            if (Listed(NdmfPrereleases, version)) return true;
            var parsed = version == null ? null : ParseRelease(version);
            return parsed != null && parsed[0] == NdmfMajor && CompareRelease(parsed, NdmfLow) >= 0;
        }

        static readonly int[] NdmfLow = ParseRelease(NdmfMinimum) ?? throw new InvalidOperationException("Invalid NdmfMinimum.");

        static bool Listed(string list, string version) =>
            !string.IsNullOrEmpty(version) && Array.IndexOf(list.Split(new[] { ", " }, StringSplitOptions.None), version) >= 0;

        static readonly int[] ModularAvatarLow = ParseRelease(ModularAvatarMinimum) ?? throw new InvalidOperationException("Invalid ModularAvatarMinimum.");

        // translate receives the Japanese format and returns the localized
        // format; {0} is the installed version and {1} ModularAvatarMinimum.
        // Only a readable version older than ModularAvatarMinimum stops here.
        // A missing or unreadable version (MA under Assets, +build) returns
        // null so the caller tries MA's API and reports
        // ModularAvatarUnknownVersionError if that API is not there.
        public static string ModularAvatarAppearanceError(string version, Func<string, string> translate)
        {
            if (SupportsModularAvatarAppearance(version) || ModularAvatarVersionUnknown(version)) return null;
            const string format = "Modular Avatar {0}では、Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定できません。{1}以上が必要です。VCC／ALCOMでModular Avatarを更新してください。アバターは変更していません。これらのMAコンポーネントを使っていないアバターは、どのMAのバージョンでも書き出せます。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarMinimum);
        }

        // X.Y.Z or X.Y.Z-pre counts as read; anything else (+build, other spellings) does not.
        public static bool ModularAvatarVersionUnknown(string version) =>
            version == null || ParseRelease(StripPreRelease(version)) == null;

        // Shown when the version could not be read and MA's API then failed.
        public static string ModularAvatarUnknownVersionError(string version, Func<string, string> translate)
        {
            const string format = "Modular Avatarのバージョンを読めなかったため（{0}）、バージョンを確かめずにShape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定しようとしましたが、固定できませんでした。VCC／ALCOMで{1}以上のModular Avatarを入れてください。アバターは変更していません。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarMinimum);
        }

        // Shown when a readable, supported version lacks the preview API the
        // freeze reads (a newer MA that changed it). {1} is the release the
        // freeze was checked with.
        public static string ModularAvatarFreezeFailedError(string version, Func<string, string> translate)
        {
            const string format = "Modular Avatar {0}では、Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定できませんでした。動作を確認しているのはModular Avatar {1}です。VCC／ALCOMでModular Avatarを{1}に変更してから、もう一度書き出してください。アバターは変更していません。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarReference);
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

        // translate receives the Japanese text and returns the localized one.
        public static string LilToonError(string version, Func<string, string> translate) =>
            AcceptsLilToon(version) ? null : translate("lilToonが見つかりません（VCC／ALCOMでlilToonを追加）");

        // Shown in the export result when the installed lilToon is not the
        // catalogue version. {0} is the installed version, {1} the catalogue.
        public static string LilToonWarning(string version, Func<string, string> translate)
        {
            var support = ClassifyLilToon(version);
            if (support == LilToonSupport.Catalogue || support == LilToonSupport.Missing) return null;
            return string.Format(translate(ExportNoteText.LilToonRendered), DisplayLilToon(version, translate), LilToonVersion);
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

        public const string Recovery = " VCC／ALCOMで対応バージョンを確認してください。別のツールと競合する場合は、更新前のプロジェクトのコピーで書き出してください。";
    }
}
