using System;
using VRVlog.LilToonExporter.Compatibility;

public static class DependencyCompatibilityTests
{
    static int count;
    static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        Check(DependencyPolicy.VrchatSdkMinimum == "3.5.2" && DependencyPolicy.VrchatSdkBelow == "3.11.0", "VRChat SDK audio range");
        foreach (var version in new[] { "3.5.2", "3.5.3", "3.6.0", "3.6.1", "3.7.0", "3.7.4", "3.8.0", "3.9.0", "3.10.4", "3.10.5", "3.10.6" })
        {
            Check(DependencyPolicy.SupportsVrchatSdkAudio(version, version), "SDK audio omission " + version);
            Check(!DependencyPolicy.SupportsVrchatSdkAudio(version, "3.5.1"), "Base must be the same release " + version);
        }
        foreach (var version in new[] { null, "", "3.5.1", "3.5.0", "3.4.2", "3.5.2-beta.1", "3.11.0", "4.0.0", "3.10.5-beta.1", "3.10", "3.10.5.0", " 3.10.5", "03.10.5" })
            Check(!DependencyPolicy.SupportsVrchatSdkAudio(version, version), "SDK audio stops " + (version ?? "null"));
        Check(!DependencyPolicy.SupportsVrchatSdkAudio("3.10.5", "3.10.4"), "Mixed SDK packages stop");
        Check(!DependencyPolicy.SupportsVrchatSdkAudio("3.10.5", null), "Missing base stops");
        Check(DependencyPolicy.ModularAvatarMinimum == "1.18.0" && DependencyPolicy.ModularAvatarBelow == "1.19.0", "MA appearance range");
        foreach (var version in new[] { "1.18.0", "1.18.1", "1.18.3", "1.18.4", "1.18.7", "1.18.10" })
        {
            Check(DependencyPolicy.SupportsModularAvatarAppearance(version), "MA appearance freeze " + version);
            Check(DependencyPolicy.ModularAvatarAppearanceError(version) == null, "No MA error " + version);
        }
        foreach (var version in new[] { "1.17.1", "1.13.4", "1.10.0", "1.19.0", "2.0.0", "0.18.7" })
        {
            Check(!DependencyPolicy.SupportsModularAvatarAppearance(version), "MA appearance stops " + version);
            Check(!DependencyPolicy.ModularAvatarVersionUnknown(version), "MA version is read " + version);
            var error = DependencyPolicy.ModularAvatarAppearanceError(version);
            Check(error != null && error.Contains("1.18.0以上1.19.0未満") && error.Contains("1.18.x") && error.Contains("アバターは変更していません"),
                "MA error names the range, the fix and that the avatar is unchanged");
            Check(error.Contains("Modular Avatar " + version), "MA error names the installed version");
        }
        // A version that cannot be read (MA under Assets, pre-releases, other
        // spellings) does not stop before MA's API is tried.
        foreach (var version in new[] { null, "", "1.19.0-alpha.0", "1.19.0-alpha.2", "1.18.7-beta.1", "1.18.0-rc.1", "1.18.7+local",
            "1.18", "1.18.7.0", " 1.18.7", "1.18.7 ", "v1.18.7", "01.18.7", "1.18.07", "1.18.x" })
        {
            Check(!DependencyPolicy.SupportsModularAvatarAppearance(version), "MA version is not a supported release " + (version ?? "null"));
            Check(DependencyPolicy.ModularAvatarVersionUnknown(version), "MA version is unknown " + (version ?? "null"));
            Check(DependencyPolicy.ModularAvatarAppearanceError(version) == null, "Unknown MA version does not stop " + (version ?? "null"));
            var error = DependencyPolicy.ModularAvatarUnknownVersionError(version);
            Check(error.StartsWith("Modular Avatarのバージョンを読めなかったため（" + (string.IsNullOrEmpty(version) ? "?" : version) + "）", StringComparison.Ordinal) &&
                error.Contains("1.18.0以上1.19.0未満") && error.Contains("1.18.x") && error.Contains("アバターは変更していません"),
                "Unknown MA version error says the version was not read, the range and the fix");
        }
        Check(DependencyPolicy.ModularAvatarUnknownVersionError(null, text => "[" + text + "]").StartsWith("[Modular Avatarのバージョン", StringComparison.Ordinal),
            "Caller-supplied translation applies to the unknown MA version format");
        Check(DependencyPolicy.ModularAvatarAppearanceError("1.17.1", text => "[" + text + "]").StartsWith("[Modular Avatar 1.17.1", StringComparison.Ordinal),
            "Caller-supplied translation applies to the MA format");
        Check(DependencyPolicy.LilToonVersion == "2.3.4","Exports conform to the 2.3.4 catalogue");
        // Any installed lilToon exports (mk 2026-10-22). Only a missing one stops;
        // a version other than the catalogue's warns that the app draws 2.3.4.
        Check(DependencyPolicy.ClassifyLilToon("2.3.4") == DependencyPolicy.LilToonSupport.Catalogue, "Catalogue version");
        Check(DependencyPolicy.LilToonError("2.3.4") == null && DependencyPolicy.LilToonWarning("2.3.4") == null, "Catalogue version neither stops nor warns");
        var cases = new (string Version, DependencyPolicy.LilToonSupport Expected)[]
        {
            ("1.10.3", DependencyPolicy.LilToonSupport.Older), ("1.8.0", DependencyPolicy.LilToonSupport.Older),
            ("0.9.9", DependencyPolicy.LilToonSupport.Older), ("1.99.99", DependencyPolicy.LilToonSupport.Older),
            ("2.0.0", DependencyPolicy.LilToonSupport.Older), ("2.3.3", DependencyPolicy.LilToonSupport.Older),
            ("2.3.5", DependencyPolicy.LilToonSupport.Newer), ("2.4.0", DependencyPolicy.LilToonSupport.Newer),
            ("2.4.1", DependencyPolicy.LilToonSupport.Newer), ("2.10.0", DependencyPolicy.LilToonSupport.Newer),
            ("3.0.0", DependencyPolicy.LilToonSupport.Newer),
            ("2.3.4-beta", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4-preview.1", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.0.0-rc.1", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4+local", DependencyPolicy.LilToonSupport.Unreadable),
            (" 2.3.4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4 ", DependencyPolicy.LilToonSupport.Unreadable),
            ("v2.3.4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.3.4.0", DependencyPolicy.LilToonSupport.Unreadable), ("2.03.4", DependencyPolicy.LilToonSupport.Unreadable),
            ("2..4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.x", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.3.\u0664", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.99999999999", DependencyPolicy.LilToonSupport.Unreadable),
        };
        foreach (var item in cases)
        {
            Check(DependencyPolicy.ClassifyLilToon(item.Version) == item.Expected, "Classify " + item.Version);
            Check(DependencyPolicy.AcceptsLilToon(item.Version), "Exports " + item.Version);
            Check(DependencyPolicy.LilToonError(item.Version) == null, "Does not stop " + item.Version);
            var warning = DependencyPolicy.LilToonWarning(item.Version);
            Check(warning != null && warning.Contains(item.Version) && warning.Contains("2.3.4"), "Warning names both versions " + item.Version);
        }
        foreach (var version in new[] { null, "" })
        {
            Check(DependencyPolicy.ClassifyLilToon(version) == DependencyPolicy.LilToonSupport.Missing, "Classify missing " + (version ?? "null"));
            Check(!DependencyPolicy.AcceptsLilToon(version), "Stops without lilToon " + (version ?? "null"));
            Check(DependencyPolicy.LilToonError(version).Contains("VCC／ALCOM"), "Missing lilToon tells how to add it");
            Check(DependencyPolicy.LilToonWarning(version) == null, "Missing lilToon stops instead of warning");
        }
        Check(DependencyPolicy.LilToonError(null, text => "[" + text + "]").StartsWith("[lilToon", StringComparison.Ordinal),
            "Caller-supplied translation applies to the error");
        Check(DependencyPolicy.LilToonWarning("2.4.1", text => "[" + text + "]").StartsWith("[", StringComparison.Ordinal),
            "Caller-supplied translation applies to the warning");
        Console.WriteLine("Dependency compatibility: " + count + " assertions passed.");
    }
}
