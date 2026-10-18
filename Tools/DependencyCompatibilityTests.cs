using System;
using VRVlog.LilToonExporter.Compatibility;

public static class DependencyCompatibilityTests
{
    static int count;
    static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        Check(DependencyPolicy.UniVrmRange == "0.131.x", "Accepted UniVRM patch line");
        Check(DependencyPolicy.UniVrmVersions == "0.131.0, 0.131.1, 0.131.2, 0.131.3", "Verified UniVRM patches");
        var line = new[] { "0.131.0", "0.131.1", "0.131.2", "0.131.3", "0.131.4", "0.131.10" };
        foreach (var version in line)
        {
            Check(DependencyPolicy.SupportsUniVrm(version), "Patch line release " + version);
            Check(DependencyPolicy.UniVrmError(version, version) == null, "Matched supported pair " + version);
            DependencyPolicy.RequireUniVrm(version, version);
            foreach (var other in line)
                if (other != version) Check(DependencyPolicy.UniVrmError(version, other) != null, "Mixed pair must fail");
        }
        foreach (var version in new[] { null, "", "0.130.1", "0.130.131", "0.132.0", "1.131.0", "0.13.1", "0.1310.0", "0.131.01",
            "0.131.1-preview.1", "0.131.4-beta.1", "0.131.1+local", " 0.131.1", "0.131.1 ", "0.131.1.0", "0.131", "0.131.x" })
        {
            Check(!DependencyPolicy.SupportsUniVrm(version), "Unregistered version cannot select a backend");
            Check(DependencyPolicy.UniVrmError(version, "0.131.1") != null, "Invalid VRM");
            Check(DependencyPolicy.UniVrmError("0.131.1", version) != null, "Invalid glTF");
            bool rejected = false;
            try { DependencyPolicy.RequireUniVrm(version, version); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Direct export also fails before conversion");
        }
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
        Check(DependencyPolicy.LilToonVersion == "2.3.4", "Exports conform to the 2.3.4 catalogue");
        Check(DependencyPolicy.LilToonMinimum == "2.0.0" && DependencyPolicy.LilToonMaximum == "2.3.4", "Supported lilToon range");
        foreach (var version in new[] { "2.0.0", "2.1.0", "2.1.10", "2.2.1", "2.3.0", "2.3.3", "2.3.4" })
        {
            Check(DependencyPolicy.ClassifyLilToon(version) == DependencyPolicy.LilToonSupport.Supported, "In range " + version);
            Check(DependencyPolicy.AcceptsLilToon(version), "Accepted " + version);
            Check(DependencyPolicy.LilToonError(version) == null, "No error " + version);
        }
        var cases = new (string Version, DependencyPolicy.LilToonSupport Expected)[]
        {
            (null, DependencyPolicy.LilToonSupport.Missing), ("", DependencyPolicy.LilToonSupport.Missing),
            ("1.10.3", DependencyPolicy.LilToonSupport.Older), ("1.8.0", DependencyPolicy.LilToonSupport.Older),
            ("0.9.9", DependencyPolicy.LilToonSupport.Older), ("1.99.99", DependencyPolicy.LilToonSupport.Older),
            ("2.3.5", DependencyPolicy.LilToonSupport.Newer), ("2.4.0", DependencyPolicy.LilToonSupport.Newer),
            ("2.10.0", DependencyPolicy.LilToonSupport.Newer), ("3.0.0", DependencyPolicy.LilToonSupport.Newer),
            ("2.3.4-beta", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4-preview.1", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.0.0-rc.1", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4+local", DependencyPolicy.LilToonSupport.Unreadable),
            (" 2.3.4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.4 ", DependencyPolicy.LilToonSupport.Unreadable),
            ("v2.3.4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.3.4.0", DependencyPolicy.LilToonSupport.Unreadable), ("2.03.4", DependencyPolicy.LilToonSupport.Unreadable),
            ("2..4", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.x", DependencyPolicy.LilToonSupport.Unreadable),
            ("2.3.٤", DependencyPolicy.LilToonSupport.Unreadable), ("2.3.99999999999", DependencyPolicy.LilToonSupport.Unreadable),
        };
        foreach (var item in cases)
        {
            Check(DependencyPolicy.ClassifyLilToon(item.Version) == item.Expected, "Classify " + (item.Version ?? "null"));
            Check(!DependencyPolicy.AcceptsLilToon(item.Version), "Stops " + (item.Version ?? "null"));
            var error = DependencyPolicy.LilToonError(item.Version);
            Check(error != null && error.Contains("2.0.0") && error.Contains("2.3.4"), "Error names the supported range");
            if (!string.IsNullOrEmpty(item.Version)) Check(error.Contains(item.Version), "Error names the installed version");
        }
        Check(DependencyPolicy.LilToonError("1.10.3").Contains("移行処理"), "Old versions are told that lilToon migrates on update");
        Check(DependencyPolicy.LilToonError("2.3.5").Contains("確かめた範囲"), "Newer versions are told the verified range");
        Check(!DependencyPolicy.AcceptsNewerLilToon, "Newer versions stay stopped until the owner decides");
        Check(DependencyPolicy.LilToonError("1.10.3", text => "[" + text + "]").StartsWith("[lilToon 1.10.3", StringComparison.Ordinal),
            "Caller-supplied translation applies to the format");
        Console.WriteLine("Dependency compatibility: " + count + " assertions passed.");
    }
}
