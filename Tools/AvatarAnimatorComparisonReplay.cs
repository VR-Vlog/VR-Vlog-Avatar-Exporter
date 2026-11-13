using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRVlog.AvatarAnimator;
using VRVlog.AvatarAnimator.Comparison;
using VRVlog.LilToonExporter;

// Replays Unity's recordings of AvatarAnimatorComparison's cases
// (AvatarAnimatorEvaluatorComparisonTests) against the evaluator. Every case
// and timing has a recording, and a recording made from other data is stale.
public static class AvatarAnimatorComparisonReplay
{
    public static void Run(string folder)
    {
        var expected = AvatarAnimatorComparison.Cases().SelectMany(c => AvatarAnimatorComparison.Timings.Select(t => c.Name + "." + t + ".json")).ToList();
        var found = Directory.GetFiles(folder, "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();
        if (missing.Count > 0 || extra.Count > 0)
            throw new Exception("Avatar animator recordings do not match the cases. Missing: " + string.Join(", ", missing) + "; not a case: " + string.Join(", ", extra));
        var failures = new List<string>();
        foreach (var file in expected)
        {
            var parts = file.Split('.');
            var c = AvatarAnimatorComparison.Find(parts[0]);
            var recording = (Dictionary<string, object>)JsonDom.Parse(File.ReadAllText(Path.Combine(folder, file)));
            if (AvatarAnimatorComparison.Serialize(recording["data"]) != AvatarAnimatorComparison.Serialize(AvatarAnimatorData.Write(c.Data)))
                throw new Exception(file + " was recorded from other data: run AvatarAnimatorEvaluatorComparisonTests in Unity again.");
            var mismatch = AvatarAnimatorComparison.Mismatch(c, parts[1], Rows(recording["values"]), Rows(recording["parameters"]));
            if (mismatch != null) failures.Add(mismatch);
        }
        if (failures.Count > 0) throw new Exception("The evaluator differs from Unity's Animator:\n" + string.Join("\n", failures));
        Console.WriteLine("Avatar animator evaluator: " + expected.Count + " Unity recordings replayed.");
    }

    static double[][] Rows(object value) =>
        ((List<object>)value).Select(row => ((List<object>)row).Select(Convert.ToDouble).ToArray()).ToArray();

    // Every case plays without an error; used before the recordings exist.
    public static void Smoke()
    {
        foreach (var c in AvatarAnimatorComparison.Cases())
            foreach (var timing in AvatarAnimatorComparison.Timings)
                AvatarAnimatorComparison.Play(c, timing, out _, out _);
    }
}
