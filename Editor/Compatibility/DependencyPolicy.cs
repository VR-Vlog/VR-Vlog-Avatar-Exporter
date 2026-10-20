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
                return "UniVRM / UniGLTF の対応版: " + UniVrmRange +
                    "。現在: " + Display(vrm) + " / " + Display(gltf) + "。";
            if (!string.Equals(vrm, gltf, StringComparison.Ordinal))
                return "UniVRM と UniGLTF のバージョンが異なります（" + vrm + " / " + gltf + "）。同じ版を選んでください。";
            return null;
        }

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
        public static string ModularAvatarAppearanceError(string version, Func<string, string> translate)
        {
            if (SupportsModularAvatarAppearance(version)) return null;
            const string format = "Modular Avatar {0}では、Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を書き出し用に固定できません。対応範囲は{1}以上{2}未満です。VCC／ALCOMでModular Avatarを1.18.xに変更してください。アバターは変更していません。これらのMAコンポーネントを使っていないアバターは、どのMAの版でも書き出せます。";
            return string.Format(translate(format), string.IsNullOrEmpty(version) ? "?" : version, ModularAvatarMinimum, ModularAvatarBelow);
        }

        public enum LilToonSupport { Supported, Missing, Unreadable, Older, Newer }

        // Versions above LilToonMaximum are unverified. Setting this to true
        // relies on LilToonStructureCheck, which every export entry runs after
        // this version check: it stops when the shaders the avatar uses differ
        // from the 2.3.4 catalogue in names, types, dimensions or passes.
        // Behaviour changes with an identical structure are not detected.
        public static readonly bool AcceptsNewerLilToon = false;

        static readonly int[] Minimum = ParseRelease(LilToonMinimum) ?? throw new InvalidOperationException("Invalid LilToonMinimum.");
        static readonly int[] Maximum = ParseRelease(LilToonMaximum) ?? throw new InvalidOperationException("Invalid LilToonMaximum.");

        // LilToonVersion is the catalogue every export conforms to (and the
        // value of sourceLilToonVersion); LilToonMinimum..LilToonMaximum is the
        // installed range whose own migration reaches that catalogue.
        public static LilToonSupport ClassifyLilToon(string version)
        {
            if (string.IsNullOrEmpty(version)) return LilToonSupport.Missing;
            var parsed = ParseRelease(version);
            if (parsed == null) return LilToonSupport.Unreadable;
            if (CompareRelease(parsed, Minimum) < 0) return LilToonSupport.Older;
            if (CompareRelease(parsed, Maximum) > 0) return LilToonSupport.Newer;
            return LilToonSupport.Supported;
        }

        public static bool AcceptsLilToon(string version)
        {
            var support = ClassifyLilToon(version);
            return support == LilToonSupport.Supported || (support == LilToonSupport.Newer && AcceptsNewerLilToon);
        }

        public static string LilToonError(string version) => LilToonError(version, text => text);

        // translate receives the Japanese format and returns the localized
        // format; {0} is the installed version and {1}..{2} the supported range.
        public static string LilToonError(string version, Func<string, string> translate)
        {
            if (AcceptsLilToon(version)) return null;
            string format;
            switch (ClassifyLilToon(version))
            {
                case LilToonSupport.Missing:
                    format = "lilToonが見つかりません。VCC／ALCOMでlilToon {1}〜{2}を追加してください。";
                    break;
                case LilToonSupport.Older:
                    format = "lilToon {0}は対応範囲（{1}〜{2}）より古い版です。VCC／ALCOMでlilToonを{1}〜{2}の版へ更新してください。更新するとlilToonの移行処理でマテリアルが移行され、そのまま書き出せます。";
                    break;
                case LilToonSupport.Newer:
                    format = "lilToon {0}はこのExporterで確かめた範囲（{1}〜{2}）より新しい版です。VCC／ALCOMでlilToonを{2}に戻すか、この版に対応したExporterの公開を待ってください。";
                    break;
                default:
                    format = "lilToon {0}はプレリリース版か、版の番号を読み取れません。VCC／ALCOMでlilToon {1}〜{2}の正式版を選んでください。";
                    break;
            }
            return string.Format(translate(format), version, LilToonMinimum, LilToonMaximum);
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

        public static void RequireUniVrm(string vrm, string gltf)
        {
            var error = UniVrmError(vrm, gltf);
            if (error != null) throw new InvalidOperationException(error + Recovery);
        }

        public const string Recovery = " VCC／ALCOMで対応版を確認してください。別のツールと競合する場合は、更新前のプロジェクトのコピーで書き出してください。";
    }
}
