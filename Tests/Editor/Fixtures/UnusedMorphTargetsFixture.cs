using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter.Tests
{
    // Shared by UnusedMorphTargetsTests and the host behaviour tests. Every
    // accessor has its own bufferView filled with one byte value, recorded in
    // the accessor's name, so a moved view can be checked by its bytes.
    public static class UnusedMorphTargetsFixture
    {
        static Dictionary<string, object> D(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (var i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }

        sealed class Builder
        {
            readonly List<object> accessors = new List<object>();
            readonly List<object> views = new List<object>();
            readonly List<byte> binary = new List<byte>();
            readonly List<object> meshes = new List<object>();
            readonly List<object> nodes = new List<object>();
            internal readonly Dictionary<string, object> Root = D("asset", D("version", "2.0"), "extensionsUsed", new List<object>());

            internal long View(int length)
            {
                while (binary.Count % 4 != 0) binary.Add(0);
                views.Add(D("buffer", 0L, "byteOffset", (long)binary.Count, "byteLength", (long)length));
                binary.AddRange(Enumerable.Repeat(Fill(views.Count - 1), length));
                return views.Count - 1;
            }

            internal long Accessor(int length = 12)
            {
                var view = View(length);
                accessors.Add(D("bufferView", view, "componentType", 5126L, "count", 1L, "type", "VEC3", "name", "fill" + Fill((int)view)));
                return accessors.Count - 1;
            }

            static byte Fill(int view) => (byte)(view + 1);

            // One node per mesh, so the node index is the mesh index. Each
            // primitive has its own targets; counts default to one primitive
            // with a target per name.
            internal int Mesh(string name, IList<string> names, IList<double> weights = null, params int[] counts)
            {
                if (counts.Length == 0) counts = new[] { names.Count };
                var primitives = counts.Select(count => (object)D("attributes", D("POSITION", Accessor()), "indices", Accessor(6),
                    "targets", Enumerable.Range(0, count).Select(t => (object)D("POSITION", Accessor())).ToList())).ToList();
                var mesh = D("name", name, "primitives", primitives, "extras", D("targetNames", names.Cast<object>().ToList()));
                if (weights != null) mesh["weights"] = weights.Cast<object>().ToList();
                meshes.Add(mesh);
                nodes.Add(D("name", name, "mesh", (long)(meshes.Count - 1)));
                return nodes.Count - 1;
            }

            internal Dictionary<string, object> MeshAt(int index) => (Dictionary<string, object>)meshes[index];

            internal void Use(string extension)
            {
                var used = (List<object>)Root["extensionsUsed"];
                if (!used.Contains(extension)) used.Add(extension);
            }

            internal Dictionary<string, object> Extension(string name)
            {
                if (!Root.TryGetValue("extensions", out var raw)) Root["extensions"] = raw = new Dictionary<string, object>();
                var extensions = (Dictionary<string, object>)raw;
                if (!extensions.TryGetValue(name, out var value)) extensions[name] = value = new Dictionary<string, object>();
                Use(name);
                return (Dictionary<string, object>)value;
            }

            internal void Bind(string expression, int node, int index, string group = "preset")
            {
                var vrm = Extension("VRMC_vrm");
                if (!vrm.ContainsKey("expressions")) vrm["expressions"] = D("preset", new Dictionary<string, object>(), "custom", new Dictionary<string, object>());
                var expressions = (Dictionary<string, object>)((Dictionary<string, object>)vrm["expressions"])[group];
                if (!expressions.ContainsKey(expression)) expressions[expression] = D("morphTargetBinds", new List<object>());
                ((List<object>)((Dictionary<string, object>)expressions[expression])["morphTargetBinds"])
                    .Add(D("node", (long)node, "index", (long)index, "weight", 1.0));
            }

            internal byte[] Build()
            {
                Root["meshes"] = meshes; Root["nodes"] = nodes; Root["accessors"] = accessors; Root["bufferViews"] = views;
                Root["buffers"] = new List<object> { D("byteLength", (long)binary.Count) };
                if (((List<object>)Root["extensionsUsed"]).Count == 0) Root.Remove("extensionsUsed");
                return GlbDocument.Create(Root, binary.ToArray()).Write();
            }
        }

        static List<object> L(Dictionary<string, object> owner, string key) => owner.TryGetValue(key, out var value) ? value as List<object> : null;
        static Dictionary<string, object> O(Dictionary<string, object> owner, string key) => owner.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        static Dictionary<string, object> Mesh(GlbDocument doc, int index) => (Dictionary<string, object>)L(doc.Json, "meshes")[index];
        static List<string> Names(GlbDocument doc, int mesh) =>
            (L(O(Mesh(doc, mesh), "extras") ?? new Dictionary<string, object>(), "targetNames") ?? new List<object>()).Cast<string>().ToList();
        static int Targets(GlbDocument doc, int mesh) =>
            L((Dictionary<string, object>)L(Mesh(doc, mesh), "primitives")[0], "targets")?.Count ?? 0;
        static List<Dictionary<string, object>> Binds(GlbDocument doc, string group, string expression) =>
            L((Dictionary<string, object>)O(O(O(O(doc.Json, "extensions"), "VRMC_vrm"), "expressions"), group)[expression], "morphTargetBinds")
                .Cast<Dictionary<string, object>>().ToList();
        static int Int(object value) => Convert.ToInt32(value);

        // Every index points inside its list, every view lies in the binary
        // chunk at a 4-byte offset and still holds its own bytes, and the
        // per-target lists of each mesh agree.
        static GlbDocument Valid(byte[] bytes, Action<bool, string> check)
        {
            var doc = GlbDocument.Read(bytes);
            var accessors = L(doc.Json, "accessors");
            var views = L(doc.Json, "bufferViews");
            check(Int(((Dictionary<string, object>)L(doc.Json, "buffers")[0])["byteLength"]) == doc.Binary.Length, "The buffer length matches the binary chunk");
            foreach (Dictionary<string, object> accessor in accessors)
            {
                var index = Int(accessor["bufferView"]);
                check(index < views.Count, "Every accessor points at an existing bufferView");
                if (index >= views.Count) continue;
                var view = (Dictionary<string, object>)views[index];
                var offset = view.TryGetValue("byteOffset", out var raw) ? Int(raw) : 0;
                var length = Int(view["byteLength"]);
                check(offset % 4 == 0 && offset + length <= doc.Binary.Length, "Every bufferView lies aligned inside the binary chunk");
                var fill = byte.Parse(((string)accessor["name"]).Substring(4));
                check(doc.Binary.Skip(offset).Take(length).All(b => b == fill), "Every accessor still reads its own bytes: " + accessor["name"]);
            }
            var meshes = L(doc.Json, "meshes");
            for (var m = 0; m < meshes.Count; m++)
            {
                var mesh = (Dictionary<string, object>)meshes[m];
                var counts = new HashSet<int>();
                foreach (Dictionary<string, object> primitive in L(mesh, "primitives"))
                {
                    var targets = L(primitive, "targets");
                    check(targets == null || targets.Count > 0, "A mesh without targets has no empty targets list");
                    counts.Add(targets?.Count ?? 0);
                    var references = O(primitive, "attributes").Values.Append(primitive["indices"])
                        .Concat((targets ?? new List<object>()).Cast<Dictionary<string, object>>().SelectMany(target => target.Values));
                    check(references.All(value => Int(value) < accessors.Count), "Every primitive points at existing accessors");
                }
                if (counts.Count != 1) continue;
                var count = counts.Single();
                var weights = L(mesh, "weights");
                check(weights == null ? true : weights.Count == count && count > 0, "Weights match the targets");
                var names = L(O(mesh, "extras") ?? new Dictionary<string, object>(), "targetNames");
                check(names == null ? true : names.Count == count && count > 0, "Names match the targets");
            }
            foreach (var bind in AllBinds(O(doc.Json, "extensions")))
            {
                var mesh = Int(((Dictionary<string, object>)L(doc.Json, "nodes")[Int(bind["node"])])["mesh"]);
                check(Int(bind["index"]) < Targets(doc, mesh), "Every bind points at an existing target");
            }
            return doc;
        }

        static IEnumerable<Dictionary<string, object>> AllBinds(object value)
        {
            if (value is List<object> list) return list.SelectMany(AllBinds);
            if (!(value is Dictionary<string, object> obj)) return Enumerable.Empty<Dictionary<string, object>>();
            var self = obj.ContainsKey("node") && obj.ContainsKey("index") ? new[] { obj } : new Dictionary<string, object>[0];
            return self.Concat(obj.Values.SelectMany(AllBinds));
        }

        public static void KeepsTargetsBoundByVrmExpressions(Action<bool, string> check)
        {
            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "unusedA", "smile", "unusedB", "wink" });
            builder.Bind("happy", face, 1);
            builder.Bind("Wink", face, 3, "custom");
            var removed = new List<string>();
            var doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>(), removed), check);
            check(Names(doc, face).SequenceEqual(new[] { "smile", "wink" }), "Bound targets stay in order");
            check(Int(Binds(doc, "preset", "happy")[0]["index"]) == 0 && Int(Binds(doc, "custom", "Wink")[0]["index"]) == 1, "Binds follow their targets");
            check(removed.SequenceEqual(new[] { "Face/unusedA", "Face/unusedB" }), "Removed targets are reported by mesh and name");
        }

        public static void KeepsTargetsOfExpressionAnimations(Action<bool, string> check)
        {
            var keys = new List<object> { new List<object> { 0.0, 0.0, null, null, 0.0, 0.0 }, new List<object> { 1.0, 1.0, null, null, 0.0, 0.0 } };
            Dictionary<string, object> Channel(object target) => D("preWrap", "clamp", "postWrap", "clamp", "keys", keys,
                "points", new List<object> { D("value", 0.0, "target", null), D("value", 1.0, "target", target) });

            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "u0", "u1", "animated" });
            var point = D("node", (long)face, "index", 2L);
            builder.Extension("VRVLOG_expression_animations")["schemaVersion"] = 2L;
            var animations = builder.Extension("VRVLOG_expression_animations");
            animations["expressions"] = new List<object> { D("expression", "Wave", "source", "menu") };
            animations["animations"] = new List<object> { D("expression", "Wave", "duration", 1.0, "loop", false, "channels", new List<object> { Channel(point) }) };
            var doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>()), check);
            check(Names(doc, face).SequenceEqual(new[] { "animated" }), "A target animated by schema 2 stays");
            var written = AllBinds(O(O(doc.Json, "extensions"), "VRVLOG_expression_animations")).Single();
            check(Int(written["index"]) == 0, "The animation point follows its target");

            // Schema 1 names its dedicated targets.
            builder = new Builder();
            face = builder.Mesh("Face", new[] { "u0", "__VRVlog_Anim_Wave_0" });
            animations = builder.Extension("VRVLOG_expression_animations");
            animations["schemaVersion"] = 1L;
            animations["animations"] = new List<object> { D("expression", "Wave", "duration", 1.0, "loop", false, "channels", new List<object> { Channel("__VRVlog_Anim_Wave_0") }) };
            doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>()), check);
            check(Names(doc, face).SequenceEqual(new[] { "__VRVlog_Anim_Wave_0" }), "A target named by schema 1 stays");
        }

        public static void KeepsNamesTheAppDrives(Action<bool, string> check)
        {
            var driven = new[] { "Face.mouthSmileLeft", "LipSuckUpper", "UE/MouthSmileLeft", "Blink_L", "_neutral", "Body.Joy", "JawOpen", "eyeBlinkLeft" };
            var unused = new[] { "unused", "VRChat/JawOpen", "VRChat / Blink", "vrc.v_aa" };
            foreach (var name in driven) check(UnusedMorphTargets.AppDrives(name), "The app drives " + name);
            foreach (var name in unused) check(!UnusedMorphTargets.AppDrives(name), "The app does not drive " + name);
            var builder = new Builder();
            var face = builder.Mesh("Face", driven.Concat(unused).ToArray());
            var removed = new List<string>();
            var doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>(), removed), check);
            check(Names(doc, face).SequenceEqual(driven), "Driven names stay");
            check(removed.SequenceEqual(unused.Select(name => "Face/" + name)), "Menu names and other names go");
        }

        public static void KeepsTargetsWithAWeight(Action<bool, string> check)
        {
            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "u0", "weighted", "u2" }, new[] { 0.0, 0.25, 0.0 });
            var doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>()), check);
            check(Names(doc, face).SequenceEqual(new[] { "weighted" }), "A target with a default weight stays");
            check(L(Mesh(doc, face), "weights").Select(weight => Convert.ToDouble(weight)).SequenceEqual(new[] { 0.25 }), "Its weight stays with it");
        }

        public static void RemovesUnusedAccessorsAndViews(Action<bool, string> check)
        {
            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "u0", "kept", "u2" });
            builder.Bind("happy", face, 1);
            // Things after the removed data are renumbered: a second mesh,
            // a skin and an image.
            var body = builder.Mesh("Body", new[] { "u0", "Blink" });
            builder.Root["skins"] = new List<object> { D("joints", new List<object> { 0L }, "inverseBindMatrices", builder.Accessor(64)) };
            var image = builder.View(16);
            builder.Root["images"] = new List<object> { D("bufferView", image, "mimeType", "image/png") };
            var input = builder.Build();
            var before = GlbDocument.Read(input);
            var removed = new List<string>();
            var output = UnusedMorphTargets.Strip(input, new List<string>(), removed);
            var doc = Valid(output, check);
            check(removed.Count == 3, "Three targets go");
            check(L(doc.Json, "accessors").Count == L(before.Json, "accessors").Count - 3, "Their accessors go");
            check(L(doc.Json, "bufferViews").Count == L(before.Json, "bufferViews").Count - 3, "Their bufferViews go");
            check(doc.Binary.Length == before.Binary.Length - 36 && output.Length < input.Length, "The binary chunk shrinks by their bytes");
            check(Names(doc, body).SequenceEqual(new[] { "Blink" }), "The second mesh keeps its driven target");
            var skin = (Dictionary<string, object>)L(doc.Json, "skins")[0];
            var matrices = (Dictionary<string, object>)L(doc.Json, "accessors")[Int(skin["inverseBindMatrices"])];
            check(Int(((Dictionary<string, object>)L(doc.Json, "bufferViews")[Int(matrices["bufferView"])])["byteLength"]) == 64, "The skin keeps its matrices");
            var imageView = (Dictionary<string, object>)L(doc.Json, "bufferViews")[Int(((Dictionary<string, object>)L(doc.Json, "images")[0])["bufferView"])];
            var imageFill = (byte)(image + 1);
            check(doc.Binary.Skip(Int(imageView["byteOffset"])).Take(16).All(b => b == imageFill), "The image keeps its bytes");
        }

        public static void RemovesTheListsOfAMeshLeftWithoutTargets(Action<bool, string> check)
        {
            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "u0", "u1" }, new[] { 0.0, 0.0 });
            var doc = Valid(UnusedMorphTargets.Strip(builder.Build(), new List<string>()), check);
            var mesh = Mesh(doc, face);
            check(!((Dictionary<string, object>)L(mesh, "primitives")[0]).ContainsKey("targets"), "The primitive has no targets key");
            check(!mesh.ContainsKey("weights") && !mesh.ContainsKey("extras"), "The mesh has no weights and no emptied extras");
        }

        public static void KeepsAMeshWhosePrimitivesDisagree(Action<bool, string> check)
        {
            var builder = new Builder();
            builder.Mesh("Face", new[] { "u0", "u1" }, null, 2, 3);
            var input = builder.Build();
            var removed = new List<string>();
            check(ReferenceEquals(UnusedMorphTargets.Strip(input, new List<string>(), removed), input) && removed.Count == 0,
                "Primitives with different target counts keep the file unchanged");
        }

        public static void KeepsTheFileWithAnUnknownExtension(Action<bool, string> check)
        {
            var builder = new Builder();
            builder.Mesh("Face", new[] { "u0", "u1" });
            builder.Use("EXT_mesh_gpu_instancing");
            var input = builder.Build();
            var warnings = new List<string>();
            check(ReferenceEquals(UnusedMorphTargets.Strip(input, warnings), input), "An unknown extension keeps the file unchanged");
            check(warnings.Count == 1 && warnings[0].Contains("EXT_mesh_gpu_instancing"), "The warning names the extension");

            builder = new Builder();
            builder.Mesh("Face", new[] { "u0" });
            builder.MeshAt(0)["extensions"] = D("KHR_draco_mesh_compression", D("bufferView", 0L));
            input = builder.Build();
            warnings.Clear();
            check(ReferenceEquals(UnusedMorphTargets.Strip(input, warnings), input) && warnings.Count == 1, "An unknown extension anywhere keeps the file unchanged");
        }

        public static void KeepsAnAccessorAKeptTargetShares(Action<bool, string> check)
        {
            var builder = new Builder();
            var face = builder.Mesh("Face", new[] { "kept", "u1" });
            builder.Bind("happy", face, 0);
            var targets = (List<object>)((Dictionary<string, object>)((List<object>)builder.MeshAt(0)["primitives"])[0])["targets"];
            // The removed target also reads the kept target's POSITION.
            var shared = ((Dictionary<string, object>)targets[0])["POSITION"];
            ((Dictionary<string, object>)targets[1])["NORMAL"] = shared;
            var input = builder.Build();
            var original = L(GlbDocument.Read(input).Json, "accessors");
            var doc = Valid(UnusedMorphTargets.Strip(input, new List<string>()), check);
            var accessors = L(doc.Json, "accessors");
            check(accessors.Count == original.Count - 1, "Only the accessor no kept target uses goes");
            var position = (Dictionary<string, object>)accessors[Int(((Dictionary<string, object>)L((Dictionary<string, object>)L(Mesh(doc, face), "primitives")[0], "targets")[0])["POSITION"])];
            check(Equals(position["name"], ((Dictionary<string, object>)original[Int(shared)])["name"]), "The kept target still reads the shared accessor");
        }

        public static void KeepsTheFileWhenNothingIsUnused(Action<bool, string> check)
        {
            var builder = new Builder();
            builder.Mesh("Face", new[] { "Blink", "JawOpen" });
            var input = builder.Build();
            check(ReferenceEquals(UnusedMorphTargets.Strip(input, new List<string>()), input), "Nothing to remove returns the same bytes");
        }

        public static void Run(Action<bool, string> check)
        {
            KeepsTargetsBoundByVrmExpressions(check);
            KeepsTargetsOfExpressionAnimations(check);
            KeepsNamesTheAppDrives(check);
            KeepsTargetsWithAWeight(check);
            RemovesUnusedAccessorsAndViews(check);
            RemovesTheListsOfAMeshLeftWithoutTargets(check);
            KeepsAMeshWhosePrimitivesDisagree(check);
            KeepsTheFileWithAnUnknownExtension(check);
            KeepsAnAccessorAKeptTargetShares(check);
            KeepsTheFileWhenNothingIsUnused(check);
        }
    }
}
