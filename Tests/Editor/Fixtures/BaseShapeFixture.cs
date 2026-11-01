using System;
using UnityEngine;
using VRVlog.LilToonExporter;

namespace VRVlog.LilToonExporter.Tests
{
    internal static class BaseShapeFixture
    {
        internal static Mesh Create()
        {
            var mesh = new Mesh { name = "Synthetic rest face" };
            mesh.vertices = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
            mesh.normals = new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("Face size", 100, Delta(4, 0, 0), Delta(0, 1, 0), Delta(0, 0, 1));
            mesh.AddBlendShapeFrame("Blink", 100, Delta(0, 2, 0), Delta(1, 0, 0), Delta(0, 1, 0));
            return mesh;
        }

        static Vector3[] Delta(float x, float y, float z) => new[] { new Vector3(x, y, z), Vector3.zero, Vector3.zero };
        static bool Near(float a, float b) => Math.Abs(a - b) < 0.00001f;

        internal static void Run(Action<bool, string> check)
        {
            AuthorEndpointChecks(check);
            LeadingNeutralFrameChecks(check);
            NegativeIntermediateFrameChecks(check);
            NativeNegativeBracketChecks(check);
            EndpointBeyondHundredChecks(check);
            UnsupportedFrameChecks(check);
            ExclusiveVisemeChecks(check);
            var source = Create();
            var target = Create();
            try
            {
                check(AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "A finite nonzero raw endpoint is usable tracking evidence.");
                check(AvatarBaseShape.HasUsableRawEndpoint(source, 0, 75), "A partially resting raw endpoint keeps usable residual range.");
                check(AvatarBaseShape.HasUsableRawEndpoint(source, 0, 150), "A finite raw rest beyond the final frame retains a negative residual endpoint.");
                foreach (var weight in new[] { 100f, -1f, float.NaN, float.PositiveInfinity })
                    check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, weight), "A fully resting or unsupported raw weight cannot waive blink validation.");
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 99, 0), "An invalid raw morph index is not usable evidence.");
                AvatarBaseShape.Rebase(source, target, new[] { 25f, 50f });
                check(Near(target.vertices[0].x, 2) && Near(target.vertices[0].y, 1), "Authored face and partially closed eye are stored in base geometry.");
                check(target.blendShapeCount == 2 && target.GetBlendShapeName(1) == "Blink", "Expression target indices and names are preserved.");
                var v = new Vector3[3]; var n = new Vector3[3]; var t = new Vector3[3];
                target.GetBlendShapeFrameVertices(1, 0, v, n, t);
                check(Near(target.vertices[0].y + v[0].y, 2), "Full blink reaches original endpoint without double applying the initial weight.");
                check(Near(target.vertices[0].x + v[0].x, 2), "Blink keeps the authored face-size customization.");
                check(Near(target.normals[0].x, 0.5f) && Near(target.normals[0].y, 0.25f), "Normal deltas are baked.");
                check(Near(target.tangents[0].y, 0.5f) && Near(target.tangents[0].z, 0.25f) && target.tangents[0].w == -1, "Tangent deltas and handedness are preserved.");
                check(source.vertices[0].x == 1 && source.vertices[0].y == 0, "The original shared mesh is unchanged.");
                check(target.triangles[2] == 2, "Topology is unchanged.");
                AvatarBaseShape.AppendExpression(source, target, "Menu smile", new[] { 25f, 50f }, new[] { 75f, 0f });
                target.GetBlendShapeFrameVertices(2, 0, v, n, t);
                check(Near(target.vertices[0].x + v[0].x, 4) && Near(target.vertices[0].y + v[0].y, 0),
                    "One menu expression combines partial weights and can reopen a customized half-closed eye.");
                check(target.blendShapeCount == 3 && target.GetBlendShapeName(1) == "Blink" && source.blendShapeCount == 2,
                    "A composite is appended without replacing source morphs or existing VRM indices.");
                check(Near(target.normals[0].x + n[0].x, 0) && Near(target.normals[0].y + n[0].y, .75f),
                    "Composite normals subtract the authored rest as well as adding the selected pose.");
                bool rejected = false;
                try { AvatarBaseShape.AppendExpression(source, target, "Invalid", new[] { 25f, 50f }, new[] { float.NaN, 0f }); }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected, "Invalid expression weights cannot enter geometry.");
                AvatarBaseShape.Rebase(source, target, new[] { 100f, 0f });
                target.GetBlendShapeFrameVertices(0, 0, v, n, t);
                check(Near(v[0].x, 0) && target.blendShapeCount == 2, "Fully authored shapes retain their zero residual target slot.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Multi", 50, Delta(2, 0, 0), null, null);
                source.AddBlendShapeFrame("Multi", 100, Delta(6, 0, 0), null, null);
                check(AvatarBaseShape.HasUsableRawEndpoint(source, 0, 75), "Multiframe residual evidence uses the original interpolation curve.");
                AvatarBaseShape.Rebase(source, target, new[] { 75f });
                check(Near(target.vertices[0].x, 5), "Authored values interpolate between multiple frames.");
                AvatarBaseShape.AppendExpression(source, target, "Menu multi", new[] { 75f }, new[] { 25f });
                target.GetBlendShapeFrameVertices(1, 0, v, n, t);
                check(Near(target.vertices[0].x + v[0].x, 2), "Composed expressions evaluate multi-frame curves in the original mesh, not rebased linear weights.");
                AvatarBaseShape.Rebase(source, target, new[] { 150f });
                check(Near(target.vertices[0].x, 11), "Values above the final frame extrapolate.");
                AvatarBaseShape.Rebase(source, target, new[] { -25f });
                check(Near(target.vertices[0].x, 0), "Negative authored values extrapolate from zero.");
                AvatarBaseShape.Rebase(source, target, new[] { 0f });
                check(Near(target.vertices[0].x, 1), "Zero defaults keep the original rest geometry.");
                AvatarBaseShape.AppendAnimatedShape(source, target, "Animated negative", 0, 25, -25);
                target.GetBlendShapeFrameVertices(1, 0, v, n, t);
                check(Near(v[0].x, -2), "Animation basis subtracts the first pose and supports negative source weights.");
                AvatarBaseShape.AppendAnimatedShape(source, target, "Animated second frame", 0, 25, 75);
                target.GetBlendShapeFrameVertices(2, 0, v, n, t);
                check(Near(v[0].x, 3), "Animation basis retains the authored multi-frame geometry.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Inert", 100, Delta(0, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "An inert raw morph cannot waive blink validation.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Cancelled endpoint", 50, Delta(2, 0, 0), null, null);
                source.AddBlendShapeFrame("Cancelled endpoint", 100, Delta(0, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "A meaningful intermediate frame cannot conceal a zero exported endpoint.");
                check(AvatarBaseShape.HasUsableRawEndpoint(source, 0, 50), "A nonzero authored rest can leave a usable residual at a zero final frame.");
#if EXPORTER_BEHAVIOR_TESTS
                // Array-only host fixtures can represent invalid payloads that
                // Unity's native mesh API may reject while constructing them.
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Invalid delta", 100, Delta(float.NaN, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "A nonfinite raw delta cannot waive blink validation.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Invalid frame", float.NaN, Delta(2, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "A nonfinite raw frame weight cannot waive blink validation.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Invalid normal", 100, Delta(2, 0, 0), Delta(float.PositiveInfinity, 0, 0), null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source, 0, 0), "Invalid auxiliary raw geometry cannot waive blink validation.");
#endif

            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        // A Lip Sync face that moves every vertex. Shapes: 0 sil, 1 aa, 2 ou,
        // 3 E (moves only normals and tangents), 4 Smile (outside Lip Sync).
        // Each has one frame at 100. VisemeGroup is named as VRChat visemes;
        // MouthGroup is not, so only descriptor slots make it Lip Sync.
        internal static readonly string[] VisemeGroup = { "vrc.v_sil", "vrc.v_aa", "vrc.v_ou", "vrc.v_E" };
        internal static readonly string[] MouthGroup = { "mouth_sil", "mouth_aa", "mouth_ou", "mouth_E" };

        internal static Mesh VisemeMesh(string[] group = null)
        {
            var names = group ?? VisemeGroup;
            var mesh = Create();
            mesh.ClearBlendShapes();
            mesh.AddBlendShapeFrame(names[0], 100, Vectors(0, 1, 0, .5f, -.25f, 0, 0, .25f, .125f),
                Vectors(1, 0, 0, 0, .5f, 0, .25f, 0, 0), Vectors(0, 1, 0, 0, 0, .5f, .375f, 0, 0));
            mesh.AddBlendShapeFrame(names[1], 100, Vectors(0, -.5f, .25f, 0, .75f, 0, .125f, 0, -.5f),
                Vectors(0, .25f, 0, .125f, 0, 0, 0, 0, .375f), Vectors(.25f, 0, 0, 0, .125f, 0, 0, 0, .5f));
            mesh.AddBlendShapeFrame(names[2], 100, Vectors(0, 0, 2, .25f, 0, .5f, 0, -.5f, 0),
                Vectors(0, .375f, 0, 0, 0, .25f, .125f, 0, 0), Vectors(0, 0, .25f, .5f, 0, 0, 0, .25f, 0));
            mesh.AddBlendShapeFrame(names[3], 100, new Vector3[3],
                Vectors(0, 0, .5f, .375f, 0, 0, 0, .5f, 0), Vectors(0, .75f, 0, 0, 0, .25f, .125f, 0, 0));
            mesh.AddBlendShapeFrame("Smile", 100, Vectors(4, 0, 0, 0, 0, -1, .5f, .5f, 0),
                Vectors(0, .125f, 0, 0, 0, 0, .25f, 0, 0), Vectors(0, 0, .125f, 0, 0, 0, 0, .375f, 0));
            return mesh;
        }

        // Adds a viseme whose frames Rebase cannot evaluate a rest from, so it is
        // kept as authored. Its rest at 0 is not zero.
        internal static void AddKeptViseme(Mesh mesh, string name)
        {
            mesh.AddBlendShapeFrame(name, -50, Vectors(-1, 0, 0, 0, .5f, 0, 0, 0, .25f), Vectors(0, .25f, 0, 0, 0, 0, 0, 0, .125f), new Vector3[3]);
            mesh.AddBlendShapeFrame(name, 0, Vectors(1, 0, 0, 0, -.5f, 0, .25f, 0, 0), new Vector3[3], Vectors(0, 0, .25f, 0, 0, 0, 0, 0, 0));
            mesh.AddBlendShapeFrame(name, 100, Vectors(2, 0, 0, 0, 0, .75f, 0, .5f, 0), Vectors(.125f, 0, 0, 0, .25f, 0, 0, 0, 0), new Vector3[3]);
        }

        static Vector3[] Vectors(params float[] xyz) =>
            new[] { new Vector3(xyz[0], xyz[1], xyz[2]), new Vector3(xyz[3], xyz[4], xyz[5]), new Vector3(xyz[6], xyz[7], xyz[8]) };

        static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
        static bool Same(Vector3 a, Vector3 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);

        static string SameAll(string what, Vector3[] expected, Vector3[] actual)
        {
            if (expected.Length != actual.Length) return $"{what}: {expected.Length} values, not {actual.Length}.";
            for (var i = 0; i < expected.Length; i++)
                if (!Same(expected[i], actual[i])) return $"{what}: vertex {i} differs.";
            return null;
        }

        // Bitwise, so a -0 that used to be +0 counts as a change.
        internal static string SameMesh(Mesh expected, Mesh actual)
        {
            var result = SameAll("vertices", expected.vertices, actual.vertices) ?? SameAll("normals", expected.normals, actual.normals);
            if (result != null) return result;
            var a = expected.tangents; var b = actual.tangents;
            if (a.Length != b.Length) return "tangent count differs.";
            for (var i = 0; i < a.Length; i++)
                if (Bits(a[i].x) != Bits(b[i].x) || Bits(a[i].y) != Bits(b[i].y) || Bits(a[i].z) != Bits(b[i].z) || Bits(a[i].w) != Bits(b[i].w))
                    return $"tangents: vertex {i} differs.";
            if (expected.blendShapeCount != actual.blendShapeCount) return "BlendShape count differs.";
            for (var shape = 0; shape < expected.blendShapeCount; shape++)
            {
                var name = expected.GetBlendShapeName(shape);
                if (name != actual.GetBlendShapeName(shape)) return $"BlendShape {shape} is {actual.GetBlendShapeName(shape)}, not {name}.";
                result = SameFrames(expected, actual, shape);
                if (result != null) return result;
            }
            return null;
        }

        static string SameFrames(Mesh expected, Mesh actual, int shape)
        {
            var name = expected.GetBlendShapeName(shape);
            var count = expected.GetBlendShapeFrameCount(shape);
            if (count != actual.GetBlendShapeFrameCount(shape)) return $"{name}: frame count differs.";
            var n = expected.vertexCount;
            for (var frame = 0; frame < count; frame++)
            {
                if (Bits(expected.GetBlendShapeFrameWeight(shape, frame)) != Bits(actual.GetBlendShapeFrameWeight(shape, frame)))
                    return $"{name}: frame {frame} weight differs.";
                var ev = new Vector3[n]; var en = new Vector3[n]; var et = new Vector3[n];
                var av = new Vector3[n]; var an = new Vector3[n]; var at = new Vector3[n];
                expected.GetBlendShapeFrameVertices(shape, frame, ev, en, et);
                actual.GetBlendShapeFrameVertices(shape, frame, av, an, at);
                var result = SameAll($"{name} frame {frame} positions", ev, av) ?? SameAll($"{name} frame {frame} normals", en, an) ??
                    SameAll($"{name} frame {frame} tangents", et, at);
                if (result != null) return result;
            }
            return null;
        }

        static bool Near(Vector3 a, Vector3 b) => Math.Abs(a.x - b.x) < 0.0001f && Math.Abs(a.y - b.y) < 0.0001f && Math.Abs(a.z - b.z) < 0.0001f;

        // What VRChat shows, computed independently of Rebase. Every shape but
        // the kept ones needs one frame at 100. The base is the original plus
        // every rest. A shape at full weight ends at the original base plus its
        // frame plus the rest of the other shapes, leaving out the other group
        // shapes when it is in the group (VRChat shows one viseme at a time),
        // and the mouth shapes, the default mouth a viseme takes off.
        // Kept shapes are exported exactly as authored.
        internal static string RebasedAsVrChatShows(Mesh source, float[] weights, string[] group, string[] kept, Mesh baked,
            string[] mouth = null)
        {
            var n = source.vertexCount;
            var count = source.blendShapeCount;
            var rests = new Vector3[count][][];
            for (var shape = 0; shape < count; shape++)
            {
                var name = source.GetBlendShapeName(shape);
                rests[shape] = new[] { new Vector3[n], new Vector3[n], new Vector3[n] };
                if (Array.IndexOf(kept, name) >= 0) continue;
                if (source.GetBlendShapeFrameCount(shape) != 1 || source.GetBlendShapeFrameWeight(shape, 0) != 100f)
                    return $"{name}: the expectation needs one frame at 100.";
                source.GetBlendShapeFrameVertices(shape, 0, rests[shape][0], rests[shape][1], rests[shape][2]);
                for (var part = 0; part < 3; part++)
                    for (var v = 0; v < n; v++)
                    {
                        var d = rests[shape][part][v];
                        rests[shape][part][v] = new Vector3(d.x * weights[shape] / 100f, d.y * weights[shape] / 100f, d.z * weights[shape] / 100f);
                    }
            }
            var original = new[] { source.vertices, source.normals, Xyz(source.tangents) };
            var actualBase = new[] { baked.vertices, baked.normals, Xyz(baked.tangents) };
            var parts = new[] { "positions", "normals", "tangents" };
            var sourceTangents = source.tangents; var bakedTangents = baked.tangents;
            for (var v = 0; v < n; v++)
                if (Bits(sourceTangents[v].w) != Bits(bakedTangents[v].w)) return $"base tangent w of vertex {v} changed.";
            for (var part = 0; part < 3; part++)
                for (var v = 0; v < n; v++)
                {
                    var expected = original[part][v];
                    for (var shape = 0; shape < count; shape++) expected += rests[shape][part][v];
                    if (!Near(expected, actualBase[part][v])) return $"base {parts[part]} of vertex {v}.";
                }
            if (baked.blendShapeCount != count) return "BlendShape count differs.";
            for (var shape = 0; shape < count; shape++)
            {
                var name = source.GetBlendShapeName(shape);
                if (baked.GetBlendShapeName(shape) != name) return $"BlendShape {shape} is not {name}.";
                if (Array.IndexOf(kept, name) >= 0)
                {
                    var result = SameFrames(source, baked, shape);
                    if (result != null) return result;
                    continue;
                }
                if (baked.GetBlendShapeFrameCount(shape) != 1 || baked.GetBlendShapeFrameWeight(shape, 0) != 100f) return $"{name}: not one frame at 100.";
                var inGroup = Array.IndexOf(group, name) >= 0;
                var frame = new[] { new Vector3[n], new Vector3[n], new Vector3[n] };
                var exported = new[] { new Vector3[n], new Vector3[n], new Vector3[n] };
                source.GetBlendShapeFrameVertices(shape, 0, frame[0], frame[1], frame[2]);
                baked.GetBlendShapeFrameVertices(shape, 0, exported[0], exported[1], exported[2]);
                for (var part = 0; part < 3; part++)
                    for (var v = 0; v < n; v++)
                    {
                        var expected = original[part][v] + frame[part][v];
                        for (var other = 0; other < count; other++)
                            if (other != shape && !(inGroup && (Array.IndexOf(group, source.GetBlendShapeName(other)) >= 0 ||
                                Array.IndexOf(mouth ?? Array.Empty<string>(), source.GetBlendShapeName(other)) >= 0)))
                                expected += rests[other][part][v];
                        if (!Near(expected, actualBase[part][v] + exported[part][v])) return $"{name}: {parts[part]} of vertex {v} at full weight.";
                    }
            }
            return null;
        }

        static Vector3[] Xyz(Vector4[] values)
        {
            var result = new Vector3[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new Vector3(values[i].x, values[i].y, values[i].z);
            return result;
        }

        static System.Collections.Generic.HashSet<int> Indices(Mesh mesh, params string[] names)
        {
            var result = new System.Collections.Generic.HashSet<int>();
            foreach (var name in names) if (mesh.GetBlendShapeIndex(name) >= 0) result.Add(mesh.GetBlendShapeIndex(name));
            return result;
        }

        // Lip Sync visemes show one at a time in VRChat, so each one is rebased
        // without the other visemes' rest.
        private static void ExclusiveVisemeChecks(Action<bool, string> check)
        {
            foreach (var name in new[] { "vrc.v_sil", "VRC.V_aa", "vrc.v.ou", "Vrc.V.E", "vrc.v_sil2" })
                check(AvatarBaseShape.IsVisemeChannel(name), name + " is named as a VRChat viseme.");
            foreach (var name in new[] { null, "", "vrc.vowel", "vrc_v_aa", "mouth_sil", "v_aa", "x.vrc.v_aa" })
                check(!AvatarBaseShape.IsVisemeChannel(name), (name ?? "null") + " is not named as a VRChat viseme.");
            var none = new string[0];
            var kept = new[] { "vrc.v_kk" };
            var withKept = new[] { "vrc.v_sil", "vrc.v_aa", "vrc.v_ou", "vrc.v_E", "vrc.v_kk" };
            foreach (var weights in new[]
                     {
                         new[] { 100f, 0f, 0f, 0f, 50f }, // sil rests at 100
                         new[] { 100f, 30f, 0f, 0f, 50f }, // two visemes rest at once
                         new[] { 0f, 0f, 0f, 50f, 0f }, // a viseme that rests only in normals and tangents
                     })
            {
                var source = VisemeMesh(); var target = VisemeMesh();
                try
                {
                    AvatarBaseShape.Rebase(source, target, weights, Indices(source, VisemeGroup));
                    var result = RebasedAsVrChatShows(source, weights, VisemeGroup, none, target);
                    check(result == null, "A viseme ends at the original base plus itself, without the other visemes' rest: " + result);
                }
                finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); }
            }
            {
                var source = VisemeMesh(); var target = VisemeMesh();
                try
                {
                    AddKeptViseme(source, "vrc.v_kk");
                    var weights = new[] { 100f, 0f, 0f, 0f, 50f, 25f };
                    var kept2 = AvatarBaseShape.Rebase(source, target, weights, Indices(source, withKept));
                    check(kept2.Count == 1 && kept2[0] == 5, "A viseme with unsupported frames is kept as authored.");
                    var result = RebasedAsVrChatShows(source, weights, withKept, kept, target);
                    check(result == null, "A kept viseme stays as authored and is not subtracted while sil rests: " + result);
                }
                finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); }
            }
            foreach (var weights in new[] { new[] { 100f, 30f, 0f, 0f, 50f }, new[] { 0f, 0f, 0f, 0f, 50f } })
            {
                var source = VisemeMesh(); var grouped = VisemeMesh(); var plain = VisemeMesh(); var reference = VisemeMesh();
                try
                {
                    AvatarBaseShape.Rebase(source, plain, weights);
                    // Without Lip Sync each shape only drops its own rest.
                    var result = RebasedAsVrChatShows(source, weights, none, none, plain);
                    check(result == null, "Without Lip Sync a viseme keeps its source delta: " + result);
                    if (weights[0] == 0f)
                    {
                        AvatarBaseShape.Rebase(source, grouped, weights, Indices(source, VisemeGroup));
                        result = SameMesh(plain, grouped);
                        check(result == null, "Visemes that all rest at zero export the same mesh as without Lip Sync: " + result);
                    }
                    result = SameMesh(reference, source);
                    check(result == null, "The source mesh is unchanged: " + result);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(grouped); UnityEngine.Object.DestroyImmediate(plain);
                    UnityEngine.Object.DestroyImmediate(reference);
                }
            }
        }

        private static void LeadingNeutralFrameChecks(Action<bool, string> check)
        {
            var source=Create();var target=Create();
            try
            {
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Neutral origin",0,Delta(0,0,0),null,null);
                source.AddBlendShapeFrame("Neutral origin",100,Delta(4,0,0),null,null);
                foreach(var rest in new[]{0f,25f,100f})
                {
                    check(AvatarBaseShape.HasUsableRawEndpoint(source,0,rest)==(rest<100f),"A pure leading0 frame preserves raw residual qualification at rest"+rest+".");
                    check(AvatarBaseShape.HasUsableMorphEndpoint(source,0,rest)==(rest<100f),"A pure leading0 frame preserves authored residual qualification at rest"+rest+".");
                    AvatarBaseShape.Rebase(source,target,new[]{rest});
                    var vertices=new Vector3[3];target.GetBlendShapeFrameVertices(0,0,vertices,new Vector3[3],new Vector3[3]);
                    check(Near(target.vertices[0].x,1f+4f*rest/100f) && Near(vertices[0].x,4f*(1f-rest/100f)) && Near(target.vertices[0].x+vertices[0].x,5f),
                        "Rebase evaluates a leading purezero as the implicit origin, preserves authored rest and reaches the original endpoint.");
                }
                check(source.GetBlendShapeFrameCount(0)==2 && source.GetBlendShapeFrameWeight(0,0)==0f && source.GetBlendShapeFrameWeight(0,1)==100f && source.vertices[0].x==1f,"Neutral-frame qualification and rebase never mutate the source mesh or frame intervals.");
                source.ClearBlendShapes();source.AddBlendShapeFrame("Only neutral",0,Delta(0,0,0),null,null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source,0,0) && !AvatarBaseShape.HasUsableMorphEndpoint(source,0,0),"A leading zero frame without any positive moving frame cannot establish an endpoint.");
#if EXPORTER_BEHAVIOR_TESTS
                foreach(var invalid in new[]{"nonzeroZero","unordered","nonfinite"})
                {
                    source.ClearBlendShapes();source.AddBlendShapeFrame("Invalid origin",invalid=="unordered"?200f:invalid=="nonfinite"?float.PositiveInfinity:0f,
                        Delta(invalid=="nonzeroZero"?1f:0f,0,0),null,null);
                    source.AddBlendShapeFrame("Invalid origin",100,Delta(4,0,0),null,null);
                    check(!AvatarBaseShape.HasUsableRawEndpoint(source,0,0) && !AvatarBaseShape.HasUsableMorphEndpoint(source,0,0),"Only a finite purezero leading frame is an implicit origin; malformed"+invalid+" remains excluded.");
                }
#endif
            }
            finally { UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void NegativeIntermediateFrameChecks(Action<bool, string> check)
        {
            var source=Create();var target=Create();
            try
            {
                source.ClearBlendShapes(); source.AddBlendShapeFrame("Negative intermediate",-50,Delta(-2,0,0),null,null);
                source.AddBlendShapeFrame("Negative intermediate",100,Delta(4,0,0),null,null);
                foreach(var rest in new[]{0f,25f,100f})
                {
                    check(AvatarBaseShape.HasUsableRawEndpoint(source,0,rest)==(rest<100),"Finite ordered negative intermediates retain raw positive endpoint evidence at rest"+rest+".");
                    check(AvatarBaseShape.HasUsableMorphEndpoint(source,0,rest)==(rest<100),"Authored endpoint evidence accepts negative intermediate frames at rest"+rest+".");
                    AvatarBaseShape.Rebase(source,target,new[]{rest});
                    var vertices=new Vector3[3];target.GetBlendShapeFrameVertices(0,0,vertices,new Vector3[3],new Vector3[3]);
                    check(Near(target.vertices[0].x,1+4*rest/100) && Near(vertices[0].x,4*(1-rest/100)) && Near(target.vertices[0].x+vertices[0].x,5),
                        "Rebase interpolates the native proportional bracket and preserves its final positive endpoint.");
                }
                check(!AvatarBaseShape.HasUsableRawEndpoint(source,0,-25) && AvatarBaseShape.HasUsableMorphEndpoint(source,0,-25),"The existing negative raw-rest policy stays separate from valid negative authored rest.");
                AvatarBaseShape.Rebase(source,target,new[]{-25f});
                var delta=new Vector3[3];target.GetBlendShapeFrameVertices(0,0,delta,new Vector3[3],new Vector3[3]);
                check(Near(target.vertices[0].x,0) && Near(delta[0].x,5),"Negative authored rest interpolates the native negative frame before normalization.");
                check(source.GetBlendShapeFrameWeight(0,0)==-50 && source.GetBlendShapeFrameWeight(0,1)==100 && source.vertices[0].x==1,"Negative-curve inspection and rebase preserve source data.");
                source.ClearBlendShapes();source.AddBlendShapeFrame("Negative-only endpoint",-50,Delta(-2,0,0),null,null);
                check(!AvatarBaseShape.HasUsableRawEndpoint(source,0,0) && !AvatarBaseShape.HasUsableMorphEndpoint(source,0,0),"A nonpositive final endpoint cannot establish positive tracking range.");
            }
            finally { UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void NativeNegativeBracketChecks(Action<bool, string> check)
        {
            var source=Create();var target=Create();
            try
            {
                foreach(var curve in new[]{"nonproportional","zeroEndpoint","explicitNeutral"})
                {
                    source.ClearBlendShapes();source.AddBlendShapeFrame("Native bracket",-50,Delta(3,0,0),null,null);
                    if(curve=="explicitNeutral") source.AddBlendShapeFrame("Native bracket",0,Delta(0,0,0),null,null);
                    var end=curve=="zeroEndpoint"?0f:2f;
                    source.AddBlendShapeFrame("Native bracket",100,Delta(end,0,0),null,null);
                    foreach(var rest in new[]{0f,25f,100f})
                    {
                        var neutral=curve=="explicitNeutral"?end*rest/100f:3f+(end-3f)*(rest+50f)/150f;
                        check(AvatarBaseShape.HasUsableRawEndpoint(source,0,rest)==(rest<100),"Raw negative bracket "+curve+" uses its real finite endpoint-minus-rest residual at rest"+rest+".");
                        check(AvatarBaseShape.HasUsableMorphEndpoint(source,0,rest)==(rest<100),"Authored negative bracket "+curve+" uses the real native zero/intermediate baseline.");
                        AvatarBaseShape.Rebase(source,target,new[]{rest});
                        var vertices=new Vector3[3];target.GetBlendShapeFrameVertices(0,0,vertices,new Vector3[3],new Vector3[3]);
                        check(Near(target.vertices[0].x,1f+neutral) && Near(vertices[0].x,end-neutral) && Near(target.vertices[0].x+vertices[0].x,1f+end),
                            "Negative bracket "+curve+" preserves actual native rest and endpoint without inventing an implicit zero knot.");
                    }
                    check(source.GetBlendShapeFrameCount(0)==(curve=="explicitNeutral"?3:2) && source.GetBlendShapeFrameWeight(0,0)==-50 && source.vertices[0].x==1,
                        "Negative bracket "+curve+" retains original frames and source geometry.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void EndpointBeyondHundredChecks(Action<bool, string> check)
        {
            var source=Create();var target=Create();
            try
            {
                source.ClearBlendShapes();source.AddBlendShapeFrame("Extended endpoint",200,Delta(4,0,0),null,null);
                foreach(var rest in new[]{0f,100f,150f,200f})
                {
                    var hasResidual=rest!=200f;
                    check(AvatarBaseShape.HasUsableRawEndpoint(source,0,rest)==hasResidual,"Raw capability uses the actual final frame200 residual at rest"+rest+".");
                    check(AvatarBaseShape.HasUsableMorphEndpoint(source,0,rest)==hasResidual,"Authored and raw routes agree about the final frame200 residual at rest"+rest+".");
                    AvatarBaseShape.Rebase(source,target,new[]{rest});
                    var vertices=new Vector3[3];target.GetBlendShapeFrameVertices(0,0,vertices,new Vector3[3],new Vector3[3]);
                    check(Near(target.vertices[0].x,1f+4f*rest/200f) && Near(vertices[0].x,4f*(1f-rest/200f)) && Near(target.vertices[0].x+vertices[0].x,5f),
                        "Rebase preserves rest"+rest+" and exports the actual frame200 endpoint without a hardcoded100 limit.");
                }
                check(source.GetBlendShapeFrameCount(0)==1 && source.GetBlendShapeFrameWeight(0,0)==200f && source.vertices[0].x==1f,"Extended endpoint qualification and rebase preserve the source mesh and final frame weight.");
            }
            finally { UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void UnsupportedFrameChecks(Action<bool, string> check)
        {
            var source=Create();var target=Create();
            try
            {
                // A moving frame at weight 0 after a negative one has no rest this class can evaluate.
                source.AddBlendShapeFrame("Unusual frames",-50,Delta(-1,0,0),null,null);
                source.AddBlendShapeFrame("Unusual frames",0,Delta(1,0,0),null,null);
                source.AddBlendShapeFrame("Unusual frames",100,Delta(2,0,0),null,null);
                var kept=AvatarBaseShape.Rebase(source,target,new[]{25f,0f,30f});
                var vertices=new Vector3[3];target.GetBlendShapeFrameVertices(2,1,vertices,new Vector3[3],new Vector3[3]);
                check(kept.Count==1 && kept[0]==2 && Near(target.vertices[0].x,2f),"A shape with unsupported frames stays out of the rest while the other shapes are stored.");
                check(target.blendShapeCount==3 && target.GetBlendShapeName(2)=="Unusual frames" && target.GetBlendShapeFrameCount(2)==3 &&
                    target.GetBlendShapeFrameWeight(2,0)==-50f && target.GetBlendShapeFrameWeight(2,1)==0f && Near(vertices[0].x,1f),
                    "A shape with unsupported frames keeps its authored frames and its index.");
            }
            finally { UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void AuthorEndpointChecks(Action<bool, string> check)
        {
            var source = Create();
            try
            {
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "A positive authored morph route needs a finite nonzero residual endpoint.");
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 75), "A partially resting authored route preserves its remaining endpoint range.");
                check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 100), "An authored morph already at its exported endpoint cannot establish tracking support.");
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, -25), "A finite negative authored rest remains usable when its exported endpoint differs.");
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 150), "A finite authored rest beyond the final frame can retain a nonzero residual endpoint.");
                foreach (var rest in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, rest), "Nonfinite authored rest cannot qualify an endpoint.");
                check(!AvatarBaseShape.HasUsableMorphEndpoint(source, -1, 0) && !AvatarBaseShape.HasUsableMorphEndpoint(source, 99, 0), "Invalid authored target indices cannot qualify geometry.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Inert author", 100, Delta(0, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "A named authored zero-delta shape has no usable endpoint.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Normal-only author", 100, Delta(0, 0, 0), Delta(1, 0, 0), null);
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "Finite authored normal movement qualifies even without positional delta.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Tangent-only author", 100, Delta(0, 0, 0), null, Delta(0, 1, 0));
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "Finite authored tangent movement qualifies even without positional delta.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Rest cancels endpoint", 50, Delta(3, 0, 0), null, null);
                source.AddBlendShapeFrame("Rest cancels endpoint", 100, Delta(3, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 50), "An intermediate rest equal to the final endpoint has no exported residual despite nonzero frames.");
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "The same multiframe authored morph can qualify from a different rest.");
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Final cancellation", 50, Delta(2, 0, 0), null, null);
                source.AddBlendShapeFrame("Final cancellation", 100, Delta(0, 0, 0), null, null);
                check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "An intermediate moving frame cannot make a zero final/rest residual usable.");
                check(AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 50), "A zero final frame still moves away from a nonzero authored rest.");
#if EXPORTER_BEHAVIOR_TESTS
                foreach (var invalid in new[] { "delta", "normal", "tangent", "frame" })
                {
                    source.ClearBlendShapes();
                    source.AddBlendShapeFrame("Malformed author", invalid == "frame" ? float.NaN : 100,
                        Delta(invalid == "delta" ? float.NaN : 2, 0, 0),
                        invalid == "normal" ? Delta(float.PositiveInfinity, 0, 0) : null,
                        invalid == "tangent" ? Delta(0, float.NegativeInfinity, 0) : null);
                    check(!AvatarBaseShape.HasUsableMorphEndpoint(source, 0, 0), "Malformed authored " + invalid + " data cannot qualify an endpoint.");
                }
#endif
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
