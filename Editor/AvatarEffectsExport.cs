using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The root extension VRVLOG_avatar_effects (Schema/VRVLOG_avatar_effects.1.schema.json):
    // the avatar's ParticleSystems, which the VRM itself cannot carry, for
    // the app to build again under the output nodes. Each system keeps the
    // modules it can play in Unity's serialized names and values; the other
    // modules it uses are counted, not written. Its material becomes a small
    // unlit record (color and blend), not a lilToon record.
    //
    // The export removes the objects that hold particles before the build
    // (ExportGimmickDetection) or prunes them as empty, so a system hangs
    // from the nearest ancestor that is written, with its pose relative to
    // that node, or from the avatar root (node -1) in the model space.
    // Capture reads the copy before those objects go; Rebind measures the
    // pose again against the ancestors the build left, before the empty
    // objects are pruned; Resolve binds them to output nodes once they are
    // known.
    internal sealed class AvatarEffectsExport
    {
        internal const string Extension = "VRVLOG_avatar_effects";
        internal const int SchemaVersion = 1;
        // Per avatar, as the app reads it: more systems or particles are left
        // out in hierarchy order and counted as effectLimit.
        internal const int MaximumParticleSystems = 16;
        internal const int MaximumParticles = 2000;
        // ParticleSystem module properties the extension writes, in this order.
        internal static readonly string[] WrittenModules =
            { "InitialModule", "EmissionModule", "ShapeModule", "ColorModule", "SizeModule", "VelocityModule" };
        internal static readonly string[] OmittedKinds = { "particleModule", "effectLimit", "effectTexture", "missingNode" };

        internal const string ResultNote = "アプリに渡すパーティクルで省いたもの: {0}";
        internal const string ModuleCount = "未対応のモジュール{0}個";
        internal const string LimitCount = "書き出せなかったパーティクル{0}個";
        internal const string TextureCount = "テクスチャ{0}枚";

        // Root properties of the serialized ParticleSystem that are not part of
        // the object itself.
        static readonly HashSet<string> ObjectFields = new HashSet<string>(StringComparer.Ordinal)
            { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier" };
        // Renderer properties that are scene or lighting state the app does
        // not take from here; the render mode and particle size settings stay.
        static readonly HashSet<string> RendererSkipped = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_Enabled", "m_CastShadows", "m_ReceiveShadows", "m_DynamicOccludee", "m_StaticShadowCaster", "m_MotionVectors",
            "m_LightProbeUsage", "m_ReflectionProbeUsage", "m_RayTracingMode", "m_RayTraceProcedural", "m_RenderingLayerMask",
            "m_RendererPriority", "m_StaticBatchInfo", "m_StaticBatchRoot", "m_ProbeAnchor", "m_LightProbeVolumeOverride",
            "m_ScaleInLightmap", "m_ReceiveGI", "m_PreserveUVs", "m_IgnoreNormalsForChartDetection", "m_ImportantGI",
            "m_StitchLightmapSeams", "m_SelectedEditorRenderState", "m_MinimumChartSize", "m_AutoUVMaxDistance",
            "m_AutoUVMaxAngle", "m_LightmapParameters", "m_SortingLayerID", "m_SortingLayer", "m_SortingOrder",
            "m_AdditionalVertexStreamsInternal", "m_LightmapIndex", "m_LightmapIndexDynamic", "m_LightmapTilingOffset",
            "m_LightmapTilingOffsetDynamic", "m_Materials", "m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3", "m_MeshDistribution",
            "m_MeshWeighting", "m_MeshWeighting1", "m_MeshWeighting2", "m_MeshWeighting3", "m_MaskInteraction", "m_SmallMeshCulling"
        };

        sealed class Particle
        {
            // The system's own object, then each ancestor up to the root.
            internal Transform[] Ancestors;
            // Its pose against each of them (Rebind); against the root in the model space.
            internal Matrix4x4[] Offsets;
            internal Matrix4x4 World;
            internal bool Active;
            // Modules in use that are not written.
            internal int Modules;
            internal Dictionary<string, object> Values;
            internal int Material = -1;
        }

        Transform root;
        readonly List<Particle> particles = new List<Particle>();
        readonly List<Dictionary<string, object>> materials = new List<Dictionary<string, object>>();
        readonly List<bool> materialTextures = new List<bool>();
        readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();
        readonly Dictionary<string, int> omitted = new Dictionary<string, int>(StringComparer.Ordinal);

        internal int Count => particles.Count;
        // The result line of the last Resolve, also when it wrote nothing; null when nothing was left out.
        internal string LeftOut { get; private set; }

        // Reads the ParticleSystems under root (the export copy), skipping those
        // excluded (objects the user left out of the export). Null when there are none.
        internal static AvatarEffectsExport Capture(GameObject root, Func<Transform, bool> excluded = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var capture = new AvatarEffectsExport { root = root.transform };
            var total = 0;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (excluded != null && excluded(system.transform)) continue;
                var max = Math.Max(0, system.main.maxParticles);
                if (capture.particles.Count >= MaximumParticleSystems || total + max > MaximumParticles)
                {
                    capture.Omit("effectLimit");
                    continue;
                }
                total += max;
                capture.particles.Add(capture.Read(system, root.transform));
            }
            capture.Rebind();
            return capture.particles.Count == 0 && capture.omitted.Count == 0 ? null : capture;
        }

        Particle Read(ParticleSystem system, Transform root)
        {
            // The build may remove the system's object or any ancestor, and
            // Resolve takes the nearest one written. Whether it plays is its
            // object's state in the copy; the FX that turns it on is not read yet.
            var ancestors = new List<Transform>();
            for (var ancestor = system.transform; ancestor != null; ancestor = ancestor.parent)
            {
                ancestors.Add(ancestor);
                if (ancestor == root) break;
            }
            var particle = new Particle
            {
                Ancestors = ancestors.ToArray(),
                World = system.transform.localToWorldMatrix,
                Active = system.gameObject.activeInHierarchy
            };
            using var serialized = new SerializedObject(system);
            var values = new Dictionary<string, object>();
            var main = new Dictionary<string, object>();
            var field0 = serialized.GetIterator();
            if (field0.Next(true))
                do
                {
                    if (ObjectFields.Contains(field0.name) || field0.name.EndsWith("Module", StringComparison.Ordinal)) continue;
                    if (Value(field0, 0) is object value) main[field0.name] = value;
                } while (field0.Next(false));
            values["system"] = main;
            foreach (var name in WrittenModules)
            {
                var module = serialized.FindProperty(name);
                if (module != null && Value(module, 0) is Dictionary<string, object> written) values[name] = written;
            }
            // Modules in use that the app does not play.
            var other = serialized.GetIterator();
            if (other.Next(true))
                do
                {
                    if (!other.name.EndsWith("Module", StringComparison.Ordinal) || WrittenModules.Contains(other.name)) continue;
                    var enabled = other.FindPropertyRelative("enabled");
                    if (enabled != null && enabled.propertyType == SerializedPropertyType.Boolean && enabled.boolValue) particle.Modules++;
                } while (other.Next(false));
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                using var serializedRenderer = new SerializedObject(renderer);
                var rendered = new Dictionary<string, object>();
                var field = serializedRenderer.GetIterator();
                if (field.Next(true))
                    do
                    {
                        if (ObjectFields.Contains(field.name) || RendererSkipped.Contains(field.name)) continue;
                        if (Value(field, 0) is object value) rendered[field.name] = value;
                    } while (field.Next(false));
                rendered["m_Enabled"] = renderer.enabled;
                values["ParticleSystemRenderer"] = rendered;
                particle.Material = AddMaterial(renderer.sharedMaterial);
            }
            particle.Values = values;
            return particle;
        }

        // The approximation of a particle material: its tint and how it
        // blends. Shared materials share one record.
        int AddMaterial(Material material)
        {
            if (material == null) return -1;
            if (materialIndex.TryGetValue(material, out var index)) return index;
            var color = Tint(material);
            var record = new Dictionary<string, object>
            {
                { "color", new List<object> { (double)color.r, (double)color.g, (double)color.b, (double)color.a } },
                { "blend", Blend(material) },
                { "softParticles", false }
            };
            index = materials.Count;
            materials.Add(record);
            // The texture is not written yet: see Documentation~/decisions/2026-11-15-avatar-effects-particles.md.
            materialTextures.Add(HasMainTexture(material));
            materialIndex.Add(material, index);
            return index;
        }

        internal static Color Tint(Material material)
        {
            // Legacy Particles/* shaders double _TintColor; the others take _Color or _BaseColor as it is.
            if (material.HasProperty("_TintColor"))
            {
                var tint = material.GetColor("_TintColor");
                return new Color(Mathf.Clamp01(tint.r * 2), Mathf.Clamp01(tint.g * 2), Mathf.Clamp01(tint.b * 2), Mathf.Clamp01(tint.a * 2));
            }
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            return Color.white;
        }

        // alpha, additive or opaque, from the blend factors the shader uses
        // (lilToon, Standard Particles and URP particles set _SrcBlend and
        // _DstBlend), else from the shader name and render queue.
        internal static string Blend(Material material)
        {
            if (material.HasProperty("_DstBlend"))
            {
                var destination = (UnityEngine.Rendering.BlendMode)Mathf.RoundToInt(material.GetFloat("_DstBlend"));
                if (destination == UnityEngine.Rendering.BlendMode.One) return "additive";
                if (destination == UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha || destination == UnityEngine.Rendering.BlendMode.OneMinusSrcColor) return "alpha";
                if (destination == UnityEngine.Rendering.BlendMode.Zero) return "opaque";
            }
            var name = material.shader != null ? material.shader.name : "";
            if (name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) return "additive";
            return material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent ? "alpha" : "opaque";
        }

        static bool HasMainTexture(Material material)
        {
            foreach (var name in new[] { "_MainTex", "_BaseMap" })
                if (material.HasProperty(name) && material.GetTexture(name) != null) return true;
            return false;
        }

        void Omit(string kind) => omitted[kind] = (omitted.TryGetValue(kind, out var count) ? count : 0) + 1;

        // Measures each system's pose, as captured, against the ancestors that
        // are still there. The export calls it again once the build has moved
        // and removed objects, before the empty ones are pruned and before
        // RootScaleBake moves the root scale into the root's children; the
        // pose against the root takes that scale, as the model space does
        // (the avatar is written at the origin).
        internal void Rebind()
        {
            if (root == null) return;
            foreach (var particle in particles)
            {
                // The build may move an object under another parent (a bone it
                // merges into): from the nearest object still there, walk the
                // parents as they are now.
                var chain = new List<Transform>();
                for (var ancestor = particle.Ancestors.FirstOrDefault(a => a != null); ancestor != null; ancestor = ancestor.parent)
                {
                    chain.Add(ancestor);
                    if (ancestor == root) break;
                }
                if (chain.Count == 0 || chain[chain.Count - 1] != root) chain.Add(root);
                particle.Ancestors = chain.ToArray();
                particle.Offsets = new Matrix4x4[particle.Ancestors.Length];
                for (var i = 0; i < particle.Ancestors.Length; i++)
                {
                    var ancestor = particle.Ancestors[i];
                    if (ancestor == null) continue;
                    particle.Offsets[i] = ancestor == root
                        ? Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix * particle.World
                        : ancestor.worldToLocalMatrix * particle.World;
                }
            }
        }

        // The extension once the output nodes are known: node gives the output
        // node of an object of the copy, -1 when it is not written. A system
        // with no written ancestor hangs from the root (node -1); one whose
        // ancestors are all gone is counted as missingNode. Only the written
        // systems count their modules and textures. Null when no system is
        // written; LeftOut still tells what was left out.
        internal Dictionary<string, object> Resolve(Func<GameObject, int> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            var written = new List<object>();
            var counts = new Dictionary<string, int>(omitted, StringComparer.Ordinal);
            void Add(string kind, int count) { if (count > 0) counts[kind] = (counts.TryGetValue(kind, out var c) ? c : 0) + count; }
            var used = new HashSet<int>();
            foreach (var particle in particles)
            {
                var at = -1;
                var output = -1;
                for (var i = 0; i < particle.Ancestors.Length && at < 0; i++)
                {
                    var ancestor = particle.Ancestors[i];
                    if (ancestor == null) continue;
                    if (ancestor == root) { at = i; output = -1; }
                    else if ((output = node(ancestor.gameObject)) >= 0) at = i;
                }
                if (at < 0) { Add("missingNode", 1); continue; }
                var offset = particle.Offsets[at];
                var entry = new Dictionary<string, object>
                {
                    { "node", output },
                    { "position", Vector(offset.GetColumn(3)) },
                    { "rotation", Quaternion(offset.rotation) },
                    { "scale", Vector(offset.lossyScale) },
                    { "active", particle.Active },
                    { "material", particle.Material }
                };
                foreach (var pair in particle.Values) entry[pair.Key] = pair.Value;
                Add("particleModule", particle.Modules);
                if (particle.Material >= 0) used.Add(particle.Material);
                written.Add(entry);
            }
            // Materials only the left-out systems used are dropped and the rest renumbered.
            var renumber = new Dictionary<int, int>();
            var kept = new List<object>();
            foreach (var index in used.OrderBy(i => i)) { renumber[index] = kept.Count; kept.Add(materials[index]); }
            foreach (Dictionary<string, object> entry in written)
                if ((int)entry["material"] >= 0) entry["material"] = renumber[(int)entry["material"]];
            Add("effectTexture", used.Count(i => materialTextures[i]));
            var omissions = OmittedKinds.Where(kind => counts.ContainsKey(kind))
                .Select(kind => (object)new Dictionary<string, object> { { "kind", kind }, { "count", counts[kind] } }).ToList();
            var extension = new Dictionary<string, object>
            {
                { "schemaVersion", SchemaVersion },
                { "particles", written },
                { "materials", kept },
                { "omitted", omissions }
            };
            LeftOut = ResultLine(extension);
            return written.Count == 0 ? null : extension;
        }

        // Adds the extension to the root of the finished GLB. Null keeps the bytes.
        internal static byte[] Inject(byte[] bytes, Dictionary<string, object> extension)
        {
            if (extension == null) return bytes;
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list.Count : 0;
            var count = ((List<object>)extension["materials"]).Count;
            foreach (Dictionary<string, object> particle in (List<object>)extension["particles"])
            {
                if ((int)particle["node"] < -1 || (int)particle["node"] >= nodes) throw new InvalidOperationException("Avatar effects node is out of range.");
                if ((int)particle["material"] >= count) throw new InvalidOperationException("Avatar effects material is out of range.");
            }
            document.PlaceExtension(Extension, extension);
            return document.Write();
        }

        // The result line for what the written extension left out; null when nothing was.
        internal static string ResultLine(Dictionary<string, object> extension)
        {
            if (extension == null) return null;
            int Of(string kind) => ((List<object>)extension["omitted"]).Cast<Dictionary<string, object>>()
                .Where(o => (string)o["kind"] == kind).Select(o => (int)o["count"]).FirstOrDefault();
            var parts = new List<string>();
            if (Of("particleModule") is var modules && modules > 0) parts.Add(ExporterLocalization.Format(ModuleCount, modules));
            if (Of("effectLimit") + Of("missingNode") is var limit && limit > 0) parts.Add(ExporterLocalization.Format(LimitCount, limit));
            if (Of("effectTexture") is var textures && textures > 0) parts.Add(ExporterLocalization.Format(TextureCount, textures));
            return parts.Count == 0 ? null : ExporterLocalization.Format(ResultNote, string.Join(ExporterLocalization.T("、"), parts));
        }

        static List<object> Vector(Vector3 v) => new List<object> { Number(v.x), Number(v.y), Number(v.z) };
        static List<object> Quaternion(Quaternion q) => new List<object> { Number(q.x), Number(q.y), Number(q.z), Number(q.w) };
        static double Number(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;

        // A serialized value as JSON: numbers, booleans, strings, colors and
        // vectors as arrays, curves and gradients as keys, structs as objects
        // keyed by their serialized names, arrays as lists. Object references
        // (meshes, textures, other objects) are left out.
        internal static object Value(SerializedProperty property, int depth)
        {
            if (depth > 16) return null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return property.longValue;
                case SerializedPropertyType.Enum:
                    return (long)property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return float.IsNaN(property.floatValue) || float.IsInfinity(property.floatValue) ? null : (object)(double)property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    var c = property.colorValue;
                    return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                case SerializedPropertyType.Vector2:
                    return new List<object> { Number(property.vector2Value.x), Number(property.vector2Value.y) };
                case SerializedPropertyType.Vector3:
                    return Vector(property.vector3Value);
                case SerializedPropertyType.Vector4:
                    var v = property.vector4Value;
                    return new List<object> { Number(v.x), Number(v.y), Number(v.z), Number(v.w) };
                case SerializedPropertyType.Quaternion:
                    return Quaternion(property.quaternionValue);
                case SerializedPropertyType.AnimationCurve:
                    return Curve(property.animationCurveValue);
                case SerializedPropertyType.Gradient:
                    return Gradient(property.gradientValue);
                case SerializedPropertyType.Generic:
                    if (property.isArray)
                    {
                        var list = new List<object>();
                        for (var i = 0; i < property.arraySize; i++)
                            if (Value(property.GetArrayElementAtIndex(i), depth + 1) is object item) list.Add(item);
                        return list;
                    }
                    var fields = new Dictionary<string, object>();
                    var child = property.Copy();
                    var end = property.GetEndProperty();
                    if (child.Next(true))
                        while (!SerializedProperty.EqualContents(child, end))
                        {
                            if (Value(child, depth + 1) is object value) fields[child.name] = value;
                            if (!child.Next(false)) break;
                        }
                    return fields;
                default:
                    return null;
            }
        }

        // Keys as in VRVLOG_expression_animations: [time, value, inTangent,
        // outTangent, inWeight, outWeight], a null tangent stepped.
        static Dictionary<string, object> Curve(AnimationCurve curve)
        {
            if (curve == null) return null;
            var converted = VrChatGestureExpressions.ConvertCurve(curve);
            return new Dictionary<string, object>
            {
                { "preWrap", converted.PreWrap },
                { "postWrap", converted.PostWrap },
                { "keys", converted.Keys.Select(k => (object)new List<object> { k.Time, k.Value, k.InTangent, k.OutTangent, k.InWeight, k.OutWeight }).ToList() }
            };
        }

        static Dictionary<string, object> Gradient(Gradient gradient)
        {
            if (gradient == null) return null;
            return new Dictionary<string, object>
            {
                { "mode", gradient.mode == GradientMode.Fixed ? "fixed" : gradient.mode == GradientMode.PerceptualBlend ? "perceptualBlend" : "blend" },
                { "colorKeys", gradient.colorKeys.Select(k => (object)new List<object> { (double)k.time, (double)k.color.r, (double)k.color.g, (double)k.color.b }).ToList() },
                { "alphaKeys", gradient.alphaKeys.Select(k => (object)new List<object> { (double)k.time, (double)k.alpha }).ToList() }
            };
        }
    }
}
