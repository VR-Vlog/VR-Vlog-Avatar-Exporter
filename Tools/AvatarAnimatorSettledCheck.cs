using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VRVlog.AvatarAnimator;
using VRVlog.AvatarAnimator.Comparison;
using VRVlog.LilToonExporter;

// #3664: an evaluator that skips settled frames (AvatarAnimatorEvaluator.Advance)
// must play every frame as one that evaluates them all. This plays the two side
// by side with the same inputs and compares, after every frame, the values,
// whether each target was written, the parameters and every layer's state,
// times and weight. The inputs are the comparison cases' own, then each
// parameter changed alone, held and set back (as AvatarAnimatorCrossCheck
// does), then random changes at random frame times.
public static class AvatarAnimatorSettledCheck
{
    public sealed class Tally
    {
        public int Frames, Skipped;
        public void Add(Tally other) { Frames += other.Frames; Skipped += other.Skipped; }
    }

    sealed class Pair
    {
        readonly AvatarAnimatorEvaluator skipping, full;
        readonly Tally tally;
        readonly string where;
        internal Pair(AvatarAnimatorData data, int seed, Tally tally, string where)
        {
            // The drivers' randoms draw the same numbers in the same frames.
            var a = new Random(seed);
            var b = new Random(seed);
            skipping = new AvatarAnimatorEvaluator(data, () => a.NextDouble());
            full = new AvatarAnimatorEvaluator(data, () => b.NextDouble()) { SkipsSettledFrames = false };
            this.tally = tally;
            this.where = where;
        }

        internal AvatarAnimatorEvaluator Skipping => skipping;

        internal void Set(int parameter, double value) { skipping.Set(parameter, value); full.Set(parameter, value); }
        internal void SetLayerWeight(int layer, double weight) { skipping.SetLayerWeight(layer, weight); full.SetLayerWeight(layer, weight); }

        internal void Advance(double dt)
        {
            skipping.Advance(dt);
            full.Advance(dt);
            tally.Frames++;
            if (!skipping.Changed) tally.Skipped++;
            if (!full.Changed) throw new Exception(where + ": the evaluator that does not skip skipped frame " + tally.Frames);
            var mismatch = Mismatch(skipping, full);
            if (mismatch != null) throw new Exception(where + ", frame " + tally.Frames + ": " + mismatch);
        }

        internal void Run(double seconds, double dt)
        {
            var frames = (int)Math.Round(seconds / dt);
            for (var i = 0; i < frames; i++) Advance(dt);
        }
    }

    static bool Same(double a, double b) => a.Equals(b);

    // Null when the two are in the same state.
    public static string Mismatch(AvatarAnimatorEvaluator a, AvatarAnimatorEvaluator b)
    {
        for (var t = 0; t < a.Values.Count; t++)
            if (!Same(a.Values[t], b.Values[t]) || a.Written[t] != b.Written[t])
                return "target " + t + " skipping " + a.Values[t] + " (" + a.Written[t] + ") every frame " + b.Values[t] + " (" + b.Written[t] + ")";
        for (var p = 0; p < a.Data.Parameters.Count; p++)
            if (!Same(a.Get(p), b.Get(p))) return "parameter " + p + " skipping " + a.Get(p) + " every frame " + b.Get(p);
        for (var l = 0; l < a.LayerCount; l++)
        {
            if (a.State(l) != b.State(l) || a.NextState(l) != b.NextState(l)) return "layer " + l + " state";
            if (!Same(a.TransitionProgress(l), b.TransitionProgress(l))) return "layer " + l + " transition progress";
            if (!Same(a.NormalizedTime(l), b.NormalizedTime(l)) || !Same(a.NextNormalizedTime(l), b.NextNormalizedTime(l)))
                return "layer " + l + " normalized time skipping " + a.NormalizedTime(l) + " every frame " + b.NormalizedTime(l);
            if (!Same(a.LayerWeight(l), b.LayerWeight(l))) return "layer " + l + " weight";
        }
        return Same(a.FxWeight, b.FxWeight) ? null : "FX weight";
    }

    // Each parameter changed alone, held for 3 seconds and set back, then
    // random changes (a value at a time, now and then a layer weight) with
    // frame times from 0 to 50 ms.
    public static Tally Play(AvatarAnimatorData data, int seed, string where, int randomFrames = 3000)
    {
        var tally = new Tally();
        var pair = new Pair(data, seed, tally, where);
        const double frame = 1.0 / 60;
        pair.Run(2, frame);
        var e = pair.Skipping;
        for (var p = 0; p < data.Parameters.Count; p++)
        {
            var before = e.Get(p);
            pair.Set(p, before != 0 ? 0 : 1);
            pair.Run(3, frame);
            // Setting the value it already has changes nothing.
            pair.Set(p, e.Get(p));
            pair.Run(.5, frame);
            pair.Set(p, before);
            pair.Run(3, frame);
        }
        var random = new Random(seed);
        double[] times = { frame, frame, frame, 1.0 / 30, 1.0 / 90, 0 };
        for (var i = 0; i < randomFrames; i++)
        {
            if (data.Parameters.Count > 0 && random.NextDouble() < .03)
            {
                var p = random.Next(data.Parameters.Count);
                var type = data.Parameters[p].Type;
                double value = type == "float" ? Math.Round(random.NextDouble() * 2.5 - 1, 2)
                    : type == "int" ? random.Next(0, 9) : random.Next(2);
                pair.Set(p, value);
            }
            if (e.LayerCount > 1 && random.NextDouble() < .003)
                pair.SetLayerWeight(1 + random.Next(e.LayerCount - 1), random.Next(3) / 2.0);
            var dt = random.NextDouble() < .1 ? random.NextDouble() * .05 : times[random.Next(times.Length)];
            pair.Advance(dt);
            // Long quiet spells, as when nobody touches the menu.
            if (random.NextDouble() < .01) pair.Run(2, frame);
        }
        return tally;
    }

    // The comparison cases with their own inputs at each timing, and with the
    // random inputs above.
    public static void RunCases()
    {
        var all = new Tally();
        foreach (var c in AvatarAnimatorComparison.Cases())
        {
            var data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(c.Data));
            foreach (var timing in AvatarAnimatorComparison.Timings)
            {
                var steps = AvatarAnimatorComparison.Steps(c, timing);
                var inputs = AvatarAnimatorComparison.Inputs(c, steps);
                var weights = AvatarAnimatorComparison.LayerWeights(c, steps);
                var pair = new Pair(data, 1, all, c.Name + "." + timing);
                for (var i = 0; i < steps.Length; i++)
                {
                    foreach (var (parameter, value) in inputs[i]) pair.Set(parameter, value);
                    foreach (var (layer, weight) in weights[i]) pair.SetLayerWeight(layer, weight);
                    pair.Advance(steps[i]);
                }
                // And quiet afterwards.
                pair.Run(3, 1.0 / 60);
            }
            for (var seed = 1; seed <= 3; seed++) all.Add(Play(data, seed, c.Name + " random " + seed, 600));
        }
        Console.WriteLine("Avatar animator settled frames: " + all.Frames + " frames of the comparison cases match, " + all.Skipped + " skipped.");
    }

    // VRM files with VRVLOG_avatar_animator (exports of real avatars, kept out
    // of the repository): each with three seeds.
    public static void RunFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            var data = Read(path);
            var name = Path.GetFileName(path);
            if (data == null) { Console.WriteLine(name + ": no " + AvatarAnimatorData.Extension); continue; }
            if (!AvatarAnimatorEvaluator.Supports(data, out var reason)) { Console.WriteLine(name + ": not played: " + reason); continue; }
            var tally = new Tally();
            for (var seed = 1; seed <= 3; seed++) tally.Add(Play(data, seed, name + " seed " + seed));
            var layers = data.Controllers.Count > 0 ? data.Controllers[0].Layers.Count : 0;
            Console.WriteLine(name + ": layers " + layers + " targets " + data.Targets.Count + " parameters " + data.Parameters.Count
                + ": " + tally.Frames + " frames match, " + tally.Skipped + " skipped");
        }
    }

    public static AvatarAnimatorData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = BitConverter.ToInt32(bytes, 12);
        var root = (Dictionary<string, object>)JsonDom.Parse(Encoding.UTF8.GetString(bytes, 20, length));
        if (!root.TryGetValue("extensions", out var extensions)) return null;
        return ((Dictionary<string, object>)extensions).TryGetValue(AvatarAnimatorData.Extension, out var value) ? AvatarAnimatorData.Read(value) : null;
    }
}
