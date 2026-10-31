using System;
using System.Collections.Generic;
using System.Linq;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.Expressions
{
    public sealed class ExpressionAnimationData
    {
        public const string Extension = "VRVLOG_expression_animations";
        public const string TargetPrefix = "__VRVlog_Anim_";
        public string Expression;
        public double Duration;
        public bool Loop;
        public readonly List<Channel> Channels = new List<Channel>();

        // Schema 1 points name a dedicated morph (Target); schema 2 points refer
        // to a glTF node and morph target index (Morph). Null moves no morph.
        public sealed class Point { public double Value; public string Target; public MorphReference Morph; }
        public sealed class MorphReference { public int Node; public int Index; }
        public sealed class Channel
        {
            public Curve Curve;
            public readonly List<Point> Points = new List<Point>();

            public void Weights(double time, out int left, out int right, out float blend)
            {
                var value = Curve.Evaluate(time);
                right = 1;
                while (right < Points.Count - 1 && value > Points[right].Value) right++;
                left = right - 1;
                blend = (float)Math.Max(0, Math.Min(1,
                    (value - Points[left].Value) / (Points[right].Value - Points[left].Value)));
            }
        }

        public double TimeAt(double elapsed)
        {
            elapsed = Math.Max(0, elapsed);
            return Loop ? elapsed % Duration : Math.Min(elapsed, Duration);
        }

        public sealed class Key
        {
            public double Time, Value, InWeight = 1.0 / 3, OutWeight = 1.0 / 3;
            // null denotes a stepped tangent; JSON never contains Infinity.
            public double? InTangent, OutTangent;
        }

        public sealed class Curve
        {
            public string PreWrap = "clamp", PostWrap = "clamp";
            public readonly List<Key> Keys = new List<Key>();

            public double Evaluate(double time)
            {
                var first = Keys[0];
                var last = Keys[Keys.Count - 1];
                if (Keys.Count == 1) return first.Value;
                if (time < first.Time || time > last.Time)
                {
                    var wrap = time < first.Time ? PreWrap : PostWrap;
                    if (wrap == "clamp") return time < first.Time ? first.Value : last.Value;
                    var length = last.Time - first.Time;
                    var period = wrap == "pingPong" ? length * 2 : length;
                    var phase = (time - first.Time) % period;
                    if (phase < 0) phase += period;
                    if (phase > length) phase = period - phase;
                    time = first.Time + phase;
                }
                if (time <= first.Time) return first.Value;
                if (time >= last.Time) return last.Value;
                var lo = 0;
                var hi = Keys.Count - 1;
                while (hi - lo > 1)
                {
                    var mid = (lo + hi) / 2;
                    if (Keys[mid].Time <= time) lo = mid; else hi = mid;
                }
                var a = Keys[lo]; var b = Keys[hi];
                if (time == a.Time || !a.OutTangent.HasValue || !b.InTangent.HasValue) return a.Value;
                var duration = b.Time - a.Time;
                var x = (time - a.Time) / duration;
                // Weighted Unity tangents are cubic Bezier handles in normalized
                // time. Solve x first, then evaluate y with the same parameter.
                double lower = 0, upper = 1;
                for (var iteration = 0; iteration < 40; iteration++)
                {
                    var t = (lower + upper) * .5;
                    if (Bezier(0, a.OutWeight, 1 - b.InWeight, 1, t) < x) lower = t;
                    else upper = t;
                }
                return Bezier(a.Value, a.Value + duration * a.OutWeight * a.OutTangent.Value,
                    b.Value - duration * b.InWeight * b.InTangent.Value, b.Value, (lower + upper) * .5);
            }

            static double Bezier(double a, double b, double c, double d, double t)
            {
                var u = 1 - t;
                return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
            }

            public void Range(out double minimum, out double maximum)
            {
                minimum = Keys.Min(k => k.Value); maximum = Keys.Max(k => k.Value);
                for (var i = 1; i < Keys.Count; i++)
                {
                    var a = Keys[i - 1]; var b = Keys[i]; var dt = b.Time - a.Time;
                    if (!a.OutTangent.HasValue || !b.InTangent.HasValue) continue;
                    var y1 = a.Value + dt * a.OutWeight * a.OutTangent.Value;
                    var y2 = b.Value - dt * b.InWeight * b.InTangent.Value;
                    minimum = Math.Min(minimum, Math.Min(y1, y2));
                    maximum = Math.Max(maximum, Math.Max(y1, y2));
                }
            }

            public void Validate()
            {
                if (!Wrap(PreWrap) || !Wrap(PostWrap) || Keys.Count == 0 || Keys.Count > 16384) Bad();
                var previous = double.NegativeInfinity;
                foreach (var k in Keys)
                {
                    if (!Finite(k.Time) || Math.Abs(k.Time) > 3600 || k.Time <= previous ||
                        !Finite(k.Value) || !Finite(k.InWeight) || !Finite(k.OutWeight) ||
                        k.InWeight < 0 || k.InWeight > 1 || k.OutWeight < 0 || k.OutWeight > 1 ||
                        k.InTangent.HasValue && !Finite(k.InTangent.Value) ||
                        k.OutTangent.HasValue && !Finite(k.OutTangent.Value)) Bad();
                    previous = k.Time;
                }
                Range(out var minimum, out var maximum);
                if (!Finite(minimum) || !Finite(maximum) || minimum < -10000 || maximum > 10000) Bad();
            }

            static bool Wrap(string value) => value == "clamp" || value == "loop" || value == "pingPong";
        }

        public static Dictionary<string, object> Write(IList<ExpressionAnimationData> animations)
        {
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L,
                ["animations"] = WriteAnimations(animations, WriteName)
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        // Writes schema 2 only. Schema 1 files are still read by Read and ReadDocument.
        public static Dictionary<string, object> WriteDocument(ExpressionAnimationDocument document)
        {
            if (document.SchemaVersion != 2) Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = 2L,
                ["expressions"] = document.Expressions.Select(e => (object)new Dictionary<string, object>
                    { ["expression"] = e.Expression, ["source"] = e.Source }).ToList(),
                ["animations"] = WriteAnimations(document.Animations, WriteMorph)
            };
            // Omitted when empty, so avatars without bone or visibility keys
            // keep the bytes they had before transforms existed.
            if (document.Transforms.Count > 0) result["transforms"] = WriteTransforms(document.Transforms);
            ReadTransforms(result, ReadDocument(result)); // The exporter and runtime enforce the same contract.
            return result;
        }

        static List<object> WriteAnimations(IEnumerable<ExpressionAnimationData> animations, Func<Point, object> target) =>
            animations.Select(a => (object)new Dictionary<string, object>
            {
                ["expression"] = a.Expression, ["duration"] = a.Duration, ["loop"] = a.Loop,
                ["channels"] = a.Channels.Select(c =>
                {
                    var channel = WriteCurve(c.Curve);
                    channel["points"] = c.Points.Select(p => (object)new Dictionary<string, object>
                        { ["value"] = p.Value, ["target"] = target(p) }).ToList();
                    return (object)channel;
                }).ToList()
            }).ToList();

        static Dictionary<string, object> WriteCurve(Curve curve) => new Dictionary<string, object>
        {
            ["preWrap"] = curve.PreWrap, ["postWrap"] = curve.PostWrap,
            ["keys"] = curve.Keys.Select(k => (object)new List<object>
                { k.Time, k.Value, k.InTangent.HasValue ? (object)k.InTangent.Value : null,
                    k.OutTangent.HasValue ? (object)k.OutTangent.Value : null, k.InWeight, k.OutWeight }).ToList()
        };

        static List<object> WriteTransforms(IEnumerable<ExpressionTransformAnimation> transforms) =>
            transforms.Select(a => (object)new Dictionary<string, object>
            {
                ["expression"] = a.Expression, ["duration"] = a.Duration, ["loop"] = a.Loop,
                ["tracks"] = a.Tracks.Select(t => (object)new Dictionary<string, object>
                {
                    ["node"] = (long)t.Node, ["property"] = t.Property,
                    ["curves"] = t.Curves.Select(c => (object)WriteCurve(c)).ToList()
                }).ToList()
            }).ToList();

        static object WriteName(Point point)
        {
            // Schema 1 has no node/index references; dropping one would leave the morph unmoved.
            if (point.Morph != null) Bad();
            return point.Target;
        }

        static object WriteMorph(Point point)
        {
            // Schema 2 has no morph names; a named point belongs to schema 1.
            if (point.Target != null) Bad();
            return point.Morph == null ? null : new Dictionary<string, object>
                { ["node"] = (long)point.Morph.Node, ["index"] = (long)point.Morph.Index };
        }

        public static List<ExpressionAnimationData> Read(object value)
        {
            var root = Obj(value);
            if (Number(root["schemaVersion"]) != 1) Bad();
            var array = Arr(root["animations"]);
            if (array.Count == 0 || array.Count > 512) Bad();
            return ReadAnimations(array, 1);
        }

        // Reads schema 1 and 2. A schema 1 document has no expression sources,
        // and its points use Target instead of Morph.
        public static ExpressionAnimationDocument ReadDocument(object value)
        {
            var root = Obj(value);
            var version = Number(root["schemaVersion"]);
            var document = new ExpressionAnimationDocument();
            if (version == 1)
            {
                document.SchemaVersion = 1;
                document.Animations.AddRange(Read(value));
                return document;
            }
            if (version != 2) Bad();
            var expressions = Arr(root["expressions"]);
            var array = Arr(root["animations"]);
            if (expressions.Count > 1024 || array.Count > 512 || expressions.Count == 0 && array.Count == 0) Bad();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in expressions)
            {
                var obj = Obj(raw);
                var expression = new ExpressionSource { Expression = Text(obj["expression"]), Source = Text(obj["source"]) };
                if (!names.Add(expression.Expression) || !ExpressionSource.IsKnown(expression.Source)) Bad();
                document.Expressions.Add(expression);
            }
            document.Animations.AddRange(ReadAnimations(array, 2));
            if (document.Animations.Any(a => !names.Contains(a.Expression))) Bad();
            return document;
        }

        static List<ExpressionAnimationData> ReadAnimations(List<object> array, int version)
        {
            var result = new List<ExpressionAnimationData>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var channelCount = 0; var keyCount = 0; var pointCount = 0;
            foreach (var raw in array)
            {
                var obj = Obj(raw);
                var animation = new ExpressionAnimationData { Expression = Text(obj["expression"]), Duration = Number(obj["duration"]) };
                var targets = new HashSet<string>(StringComparer.Ordinal);
                var morphs = new HashSet<(int, int)>();
                if (!(obj["loop"] is bool)) Bad();
                animation.Loop = (bool)obj["loop"];
                if (!names.Add(animation.Expression) || animation.Duration <= 0 || animation.Duration > 600) Bad();
                foreach (var rawChannel in Arr(obj["channels"]))
                {
                    if (++channelCount > 1024) Bad();
                    var c = Obj(rawChannel);
                    var channel = new Channel { Curve = ReadCurve(c, ref keyCount) };
                    foreach (var rawPoint in Arr(c["points"]))
                    {
                        if (++pointCount > 4096) Bad();
                        var p = Obj(rawPoint);
                        var point = new Point { Value = Number(p["value"]) };
                        if (version == 1)
                        {
                            point.Target = p["target"] == null ? null : Text(p["target"]);
                            // Null is the zero residual at the animation's first
                            // pose. Identical basis morphs may be shared between
                            // mutually exclusive animations, never within one face.
                            if (point.Target == null ? point.Value != channel.Curve.Evaluate(0) :
                                !point.Target.StartsWith(TargetPrefix, StringComparison.Ordinal) || !targets.Add(point.Target)) Bad();
                        }
                        else if (p["target"] != null)
                        {
                            // Null moves no morph at this value. A morph is written
                            // by at most one point of one animation.
                            var t = Obj(p["target"]);
                            point.Morph = new MorphReference { Node = NonNegative(t["node"]), Index = NonNegative(t["index"]) };
                            if (!morphs.Add((point.Morph.Node, point.Morph.Index))) Bad();
                        }
                        if (point.Value < -10000 || point.Value > 10000 ||
                            channel.Points.Count > 0 && point.Value <= channel.Points[channel.Points.Count - 1].Value) Bad();
                        channel.Points.Add(point);
                    }
                    channel.Curve.Range(out var minimum, out var maximum);
                    if (channel.Points.Count < 2 || channel.Points[0].Value > minimum ||
                        channel.Points[channel.Points.Count - 1].Value < maximum) Bad();
                    animation.Channels.Add(channel);
                }
                if (animation.Channels.Count == 0) Bad();
                result.Add(animation);
            }
            return result;
        }

        static Curve ReadCurve(Dictionary<string, object> c, ref int keyCount)
        {
            var curve = new Curve { PreWrap = Text(c["preWrap"]), PostWrap = Text(c["postWrap"]) };
            foreach (var rawKey in Arr(c["keys"]))
            {
                if (++keyCount > 16384) Bad();
                var k = Arr(rawKey);
                if (k.Count != 6) Bad();
                curve.Keys.Add(new Key { Time = Number(k[0]), Value = Number(k[1]),
                    InTangent = k[2] == null ? (double?)null : Number(k[2]), OutTangent = k[3] == null ? (double?)null : Number(k[3]),
                    InWeight = Number(k[4]), OutWeight = Number(k[5]) });
            }
            curve.Validate();
            return curve;
        }

        // Reads the optional "transforms" of a schema 2 document that
        // ReadDocument already accepted. ReadDocument does not look at this
        // key, so readers older than it and readers that skip it keep loading
        // the BlendShape part. A caller that plays transforms catches the
        // exception here and keeps the document.
        public static List<ExpressionTransformAnimation> ReadTransforms(object value, ExpressionAnimationDocument document)
        {
            var root = Obj(value);
            var result = new List<ExpressionTransformAnimation>();
            if (!root.TryGetValue("transforms", out var rawTransforms)) return result;
            if (document == null || document.SchemaVersion != 2) Bad();
            var array = Arr(rawTransforms);
            if (array.Count == 0 || array.Count > 512) Bad();
            var listed = new HashSet<string>(document.Expressions.Select(e => e.Expression), StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var trackCount = 0; var keyCount = 0;
            foreach (var raw in array)
            {
                var obj = Obj(raw);
                var animation = new ExpressionTransformAnimation { Expression = Text(obj["expression"]), Duration = Number(obj["duration"]) };
                if (!(obj["loop"] is bool)) Bad();
                animation.Loop = (bool)obj["loop"];
                if (!listed.Contains(animation.Expression) || !names.Add(animation.Expression) ||
                    animation.Duration < 0 || animation.Duration > 600 || animation.Duration == 0 && animation.Loop) Bad();
                // One expression has one clock: the BlendShape animation of
                // the same expression has the same length and looping.
                var blendShapes = document.Animations.FirstOrDefault(a => a.Expression == animation.Expression);
                if (blendShapes != null && (blendShapes.Duration != animation.Duration || blendShapes.Loop != animation.Loop)) Bad();
                var owners = new HashSet<(int, string)>();
                foreach (var rawTrack in Arr(obj["tracks"]))
                {
                    if (++trackCount > 1024) Bad();
                    var t = Obj(rawTrack);
                    var track = new ExpressionTransformAnimation.Track { Node = NonNegative(t["node"]), Property = Text(t["property"]) };
                    var components = ExpressionTransformAnimation.Components(track.Property);
                    // euler and rotation both write the node's rotation.
                    var owner = track.Property == ExpressionTransformAnimation.Euler ? ExpressionTransformAnimation.Rotation : track.Property;
                    if (components == 0 || !owners.Add((track.Node, owner))) Bad();
                    var curves = Arr(t["curves"]);
                    if (curves.Count != components) Bad();
                    foreach (var rawCurve in curves)
                    {
                        var curve = ReadCurve(Obj(rawCurve), ref keyCount);
                        curve.Range(out var minimum, out var maximum);
                        if (track.Property == ExpressionTransformAnimation.Active && (minimum < 0 || maximum > 1)) Bad();
                        track.Curves.Add(curve);
                    }
                    animation.Tracks.Add(track);
                }
                if (animation.Tracks.Count == 0) Bad();
                result.Add(animation);
            }
            return result;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static Dictionary<string, object> Obj(object value) => value as Dictionary<string, object> ?? throw new InvalidOperationException("Invalid expression animation object.");
        static List<object> Arr(object value) => value as List<object> ?? throw new InvalidOperationException("Invalid expression animation array.");
        static string Text(object value) => value is string s && s.Length > 0 && s.Length <= 1024 ? s : throw new InvalidOperationException("Invalid expression animation name.");
        static double Number(object value)
        {
            if (!(value is double) && !(value is float) && !(value is long) && !(value is int)) Bad();
            var number = Convert.ToDouble(value);
            if (!Finite(number)) Bad();
            return number;
        }
        static int NonNegative(object value)
        {
            var number = Number(value);
            if (number < 0 || number > int.MaxValue || number != Math.Floor(number)) Bad();
            return (int)number;
        }
        static void Bad() => throw new InvalidOperationException("Expression animation data is invalid or exceeds the supported limits.");
    }

    // A whole VRVLOG_expression_animations extension. Schema 2 lists every custom
    // expression built from the avatar's VRChat setup, animated or not.
    public sealed class ExpressionAnimationDocument
    {
        public int SchemaVersion = 2;
        public readonly List<ExpressionSource> Expressions = new List<ExpressionSource>();
        public readonly List<ExpressionAnimationData> Animations = new List<ExpressionAnimationData>();
        // Written under "transforms" and read only by ExpressionAnimationData.ReadTransforms.
        public readonly List<ExpressionTransformAnimation> Transforms = new List<ExpressionTransformAnimation>();
    }

    // Bone and visibility keys of one custom expression. Values are in the
    // local space of the glTF node: translation in meters, euler in degrees
    // applied like Unity's Quaternion.Euler (Z, then X, then Y), rotation as
    // an (x, y, z, w) quaternion normalized after evaluation, scale, and
    // active (shown at 0.5 or more). The player blends from the node's pose
    // at load time with the expression's weight and restores it on release.
    public sealed class ExpressionTransformAnimation
    {
        public const string Translation = "translation", Euler = "euler", Rotation = "rotation", Scale = "scale", Active = "active";
        public string Expression;
        // Zero holds the pose at time 0 and does not loop.
        public double Duration;
        public bool Loop;
        public readonly List<Track> Tracks = new List<Track>();

        public sealed class Track
        {
            public int Node;
            public string Property;
            // One curve per component in the order x, y, z (w).
            public readonly List<ExpressionAnimationData.Curve> Curves = new List<ExpressionAnimationData.Curve>();

            public void Evaluate(double time, double[] values)
            {
                for (var i = 0; i < Curves.Count; i++) values[i] = Curves[i].Evaluate(time);
            }
        }

        public double TimeAt(double elapsed)
        {
            if (Duration <= 0) return 0;
            elapsed = Math.Max(0, elapsed);
            return Loop ? elapsed % Duration : Math.Min(elapsed, Duration);
        }

        public static int Components(string property) =>
            property == Translation || property == Euler || property == Scale ? 3 :
            property == Rotation ? 4 : property == Active ? 1 : 0;
    }

    // Where a custom expression came from. Expression names carry no source
    // prefix, so the app tells them apart from tracking expressions here.
    public sealed class ExpressionSource
    {
        public const string Menu = "menu";
        public const string Gesture = "gesture";
        public const string FaceEmo = "faceEmo";
        public string Expression;
        public string Source;

        public static bool IsKnown(string source) => source == Menu || source == Gesture || source == FaceEmo;
    }
}
