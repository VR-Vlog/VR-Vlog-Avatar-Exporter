using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The root extension VRVLOG_avatar_effects (Schema/VRVLOG_avatar_effects.1.schema.json):
    // the avatar's ParticleSystems, TrailRenderers, LineRenderers and Lights,
    // which the VRM itself cannot carry, for the app to build again under the
    // output nodes. Particle systems, trails and lines keep their settings in
    // Unity's serialized names and values; a particle system's other modules
    // in use are counted, not written. Their materials become a small unlit
    // record (color, blend and the main texture as a PNG in the extension's
    // own image table), not a lilToon record. A light keeps its type, color,
    // intensity, range and spot angle; shadows are not written.
    //
    // The export removes the objects that hold effects before the build
    // (ExportGimmickDetection) or prunes them as empty, so an effect hangs
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
        // Per avatar, as the app reads it: more effects of a kind are left
        // out in hierarchy order and counted as effectLimit.
        internal const int MaximumParticleSystems = 16;
        internal const int MaximumParticles = 2000;
        // Trails and lines together.
        internal const int MaximumTrailsAndLines = 16;
        internal const int MaximumLights = 2;
        // The long side of an effect image; larger ones are scaled down.
        internal const int MaximumTextureSize = 512;
        // ParticleSystem module properties the extension writes, in this order.
        internal static readonly string[] WrittenModules =
            { "InitialModule", "EmissionModule", "ShapeModule", "ColorModule", "SizeModule", "VelocityModule" };
        internal static readonly string[] OmittedKinds = { "particleModule", "effectLimit", "effectTexture", "missingNode" };
        // The lists of the extension, in this order.
        internal static readonly string[] Kinds = { "particles", "trails", "lines", "lights" };

        internal const string ResultNote = "アプリに渡すエフェクトで省いたもの: {0}";
        internal const string ModuleCount = "未対応のモジュール{0}個";
        internal const string LimitCount = "書き出せなかったエフェクト{0}個";
        internal const string TextureCount = "テクスチャ{0}枚";

        // Root properties of the serialized components that are not part of
        // the object itself.
        static readonly HashSet<string> ObjectFields = new HashSet<string>(StringComparer.Ordinal)
            { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier" };
        // Renderer properties that are scene or lighting state the app does
        // not take from here; the render mode, size, width and color settings stay.
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

        sealed class Effect
        {
            // One of Kinds.
            internal string Kind;
            // The effect's own object, then each ancestor up to the root.
            internal Transform[] Ancestors;
            // Its pose against each of them (Rebind); against the root in the model space.
            internal Matrix4x4[] Offsets;
            internal Matrix4x4 World;
            internal bool Active;
            // Modules in use that are not written (particle systems only).
            internal int Modules;
            internal Dictionary<string, object> Values;
            // Index into materials; lights have none.
            internal int Material = -1;
            internal bool HasMaterial = true;
            // The part FX's enabled targets name (AvatarAnimatorParts.cs).
            internal AvatarAnimatorCapture.Part Part;
        }

        sealed class Image
        {
            internal byte[] Png;
        }

        Transform root;
        readonly List<Effect> effects = new List<Effect>();
        // Each written effect's index in its table and node, by its component (Resolve).
        readonly Dictionary<int, (int Index, int Node)> tableIndex = new Dictionary<int, (int, int)>();
        readonly List<Dictionary<string, object>> materials = new List<Dictionary<string, object>>();
        // Per material: the image index, -1 for none, -2 for a texture that could not be read.
        readonly List<int> materialImages = new List<int>();
        readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();
        readonly List<Image> images = new List<Image>();
        readonly Dictionary<Texture, int> imageIndex = new Dictionary<Texture, int>();
        readonly Dictionary<string, int> omitted = new Dictionary<string, int>(StringComparer.Ordinal);

        internal int Count => effects.Count;
        // The effects read, as the parts FX may turn (AvatarAnimatorParts.cs).
        internal IEnumerable<AvatarAnimatorCapture.Part> Parts => effects.Select(effect => effect.Part);
        // The index in its table and the node of an effect the last Resolve wrote; (-1, -1) for another.
        internal (int Index, int Node) IndexOf(Component component) =>
            component is object && tableIndex.TryGetValue(component.GetInstanceID(), out var at) ? at : (-1, -1);
        // The result line of the last Resolve, also when it wrote nothing; null when nothing was left out.
        internal string LeftOut { get; private set; }

        // Reads the effects under root (the export copy), skipping those
        // excluded (objects the user left out of the export). Null when there are none.
        internal static AvatarEffectsExport Capture(GameObject root, Func<Transform, bool> excluded = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var capture = new AvatarEffectsExport { root = root.transform };
            // VRChat removes objects tagged EditorOnly at upload; their effects never play.
            bool Excluded(Component component) => EditorOnly(component.transform, root.transform) || excluded != null && excluded(component.transform);

            var systems = 0;
            var total = 0;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (Excluded(system)) continue;
                var max = Math.Max(0, system.main.maxParticles);
                if (systems >= MaximumParticleSystems || total + max > MaximumParticles)
                {
                    capture.Omit("effectLimit");
                    continue;
                }
                systems++;
                total += max;
                capture.effects.Add(capture.ReadParticle(system, root.transform));
            }
            // Trails and lines share one limit, in hierarchy order.
            var strokes = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is TrailRenderer) && !(renderer is LineRenderer) || Excluded(renderer)) continue;
                if (strokes >= MaximumTrailsAndLines)
                {
                    capture.Omit("effectLimit");
                    continue;
                }
                strokes++;
                capture.effects.Add(capture.ReadStroke(renderer, root.transform));
            }
            var lights = 0;
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                // Area lights and baked lights light nothing on an avatar at run time.
                if (Excluded(light) || LightType(light) == null || light.lightmapBakeType == LightmapBakeType.Baked) continue;
                if (lights >= MaximumLights)
                {
                    capture.Omit("effectLimit");
                    continue;
                }
                lights++;
                capture.effects.Add(capture.ReadLight(light, root.transform));
            }
            capture.Rebind();
            return capture.effects.Count == 0 && capture.omitted.Count == 0 ? null : capture;
        }

        static bool EditorOnly(Transform transform, Transform root)
        {
            for (var current = transform; current != null; current = current == root ? null : current.parent)
                if (current.CompareTag("EditorOnly")) return true;
            return false;
        }

        // The build may remove the effect's object or any ancestor, and
        // Resolve takes the nearest one written. Whether it plays is its
        // object's state in the copy; the FX that turns it on is not read here.
        static Effect Place(string kind, Component component, Transform root)
        {
            var ancestors = new List<Transform>();
            for (var ancestor = component.transform; ancestor != null; ancestor = ancestor.parent)
            {
                ancestors.Add(ancestor);
                if (ancestor == root) break;
            }
            return new Effect
            {
                Kind = kind,
                Part = AvatarAnimatorCapture.ReadPart(kind.Substring(0, kind.Length - 1), component, root),
                Ancestors = ancestors.ToArray(),
                World = component.transform.localToWorldMatrix,
                Active = component.gameObject.activeInHierarchy
            };
        }

        Effect ReadParticle(ParticleSystem system, Transform root)
        {
            var particle = Place("particles", system, root);
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
                values["ParticleSystemRenderer"] = RendererValues(renderer);
                particle.Material = AddMaterial(renderer.sharedMaterial);
            }
            particle.Values = values;
            return particle;
        }

        // A trail or a line: the renderer's own settings (time, width curve,
        // color gradient, positions and the rest) under its type name.
        Effect ReadStroke(Renderer renderer, Transform root)
        {
            var stroke = Place(renderer is TrailRenderer ? "trails" : "lines", renderer, root);
            var values = RendererValues(renderer);
            // A line in world space keeps its points where they are in the copy:
            // they are written in its own space, which the app places.
            if (renderer is LineRenderer line && line.useWorldSpace)
            {
                var points = new Vector3[line.positionCount];
                line.GetPositions(points);
                values["m_Positions"] = points.Select(p => (object)Vector(renderer.transform.InverseTransformPoint(p))).ToList();
                values["m_UseWorldSpace"] = false;
            }
            stroke.Values = new Dictionary<string, object> { { renderer.GetType().Name, values } };
            stroke.Material = AddMaterial(renderer.sharedMaterial);
            return stroke;
        }

        Effect ReadLight(Light light, Transform root)
        {
            var effect = Place("lights", light, root);
            var color = light.color;
            effect.HasMaterial = false;
            effect.Values = new Dictionary<string, object>
            {
                { "enabled", light.enabled },
                { "type", LightType(light) },
                { "color", new List<object> { (double)color.r, (double)color.g, (double)color.b, (double)color.a } },
                { "intensity", Number(light.intensity) },
                { "range", Number(light.range) },
                { "spotAngle", Number(light.spotAngle) }
            };
            return effect;
        }

        // The run-time light types; null for the area lights, which are baked only.
        static string LightType(Light light)
        {
            switch (light.type)
            {
                case UnityEngine.LightType.Point: return "point";
                case UnityEngine.LightType.Spot: return "spot";
                case UnityEngine.LightType.Directional: return "directional";
                default: return null;
            }
        }

        static Dictionary<string, object> RendererValues(Renderer renderer)
        {
            using var serialized = new SerializedObject(renderer);
            var values = new Dictionary<string, object>();
            var field = serialized.GetIterator();
            if (field.Next(true))
                do
                {
                    if (ObjectFields.Contains(field.name) || RendererSkipped.Contains(field.name)) continue;
                    if (Value(field, 0) is object value) values[field.name] = value;
                } while (field.Next(false));
            values["m_Enabled"] = renderer.enabled;
            return values;
        }

        // The approximation of an effect material: its tint, how it blends
        // and its main texture. Shared materials share one record.
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
            materialImages.Add(MainTexture(material) is Texture texture ? AddImage(texture) : -1);
            materialIndex.Add(material, index);
            return index;
        }

        // The image index of a texture, -2 when it cannot be read.
        int AddImage(Texture texture)
        {
            if (imageIndex.TryGetValue(texture, out var index)) return index;
            byte[] png = null;
            try { png = Png(texture); }
            catch (Exception error) when (!(error is OperationCanceledException)) { Debug.LogWarning("VR Vlog: " + texture.name + ": " + error.Message); }
            index = png == null ? -2 : images.Count;
            if (png != null) images.Add(new Image { Png = png });
            imageIndex.Add(texture, index);
            return index;
        }

        // The texture as an sRGB PNG at most MaximumTextureSize on its long
        // side: a readable 8-bit image from its pixels, else drawn and read
        // back on the GPU. Null without a graphics device for the others.
        internal static byte[] Png(Texture texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            var size = TextureResizePolicy.Size(texture.width, texture.height, MaximumTextureSize);
            if (texture is Texture2D readable && PngWriter.Supports(readable))
            {
                var pixels = readable.GetPixels32();
                if (size.Item1 == readable.width && size.Item2 == readable.height) return PngWriter.Encode(pixels, size.Item1, size.Item2);
                var rgba = new byte[checked(pixels.Length * 4)];
                for (var i = 0; i < pixels.Length; i++)
                {
                    rgba[i * 4] = pixels[i].r; rgba[i * 4 + 1] = pixels[i].g;
                    rgba[i * 4 + 2] = pixels[i].b; rgba[i * 4 + 3] = pixels[i].a;
                }
                var resized = TextureResizePolicy.Resize(rgba, readable.width, readable.height, true, MaximumTextureSize);
                var colors = new Color32[resized.Length / 4];
                for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(resized[i * 4], resized[i * 4 + 1], resized[i * 4 + 2], resized[i * 4 + 3]);
                return PngWriter.Encode(colors, size.Item1, size.Item2);
            }
            if (!GpuReadback.Available) return null;
            var baked = GpuReadback.BakeRgba32(size.Item1, size.Item2, texture.name, texture, target => Graphics.Blit(texture, target));
            try { return PngWriter.Encode(baked); }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
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

        static Texture MainTexture(Material material)
        {
            foreach (var name in new[] { "_MainTex", "_BaseMap" })
                if (material.HasProperty(name) && material.GetTexture(name) is Texture texture && texture != null) return texture;
            return null;
        }

        void Omit(string kind) => omitted[kind] = (omitted.TryGetValue(kind, out var count) ? count : 0) + 1;

        // Measures each effect's pose, as captured, against the ancestors that
        // are still there. The export calls it again once the build has moved
        // and removed objects, before the empty ones are pruned and before
        // RootScaleBake moves the root scale into the root's children; the
        // pose against the root takes that scale, as the model space does
        // (the avatar is written at the origin).
        internal void Rebind()
        {
            if (root == null) return;
            foreach (var effect in effects)
            {
                // The build may move an object under another parent (a bone it
                // merges into): from the nearest object still there, walk the
                // parents as they are now.
                var chain = new List<Transform>();
                for (var ancestor = effect.Ancestors.FirstOrDefault(a => a != null); ancestor != null; ancestor = ancestor.parent)
                {
                    chain.Add(ancestor);
                    if (ancestor == root) break;
                }
                if (chain.Count == 0 || chain[chain.Count - 1] != root) chain.Add(root);
                effect.Ancestors = chain.ToArray();
                effect.Offsets = new Matrix4x4[effect.Ancestors.Length];
                for (var i = 0; i < effect.Ancestors.Length; i++)
                {
                    var ancestor = effect.Ancestors[i];
                    if (ancestor == null) continue;
                    effect.Offsets[i] = ancestor == root
                        ? Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix * effect.World
                        : ancestor.worldToLocalMatrix * effect.World;
                }
            }
        }

        // The extension once the output nodes are known: node gives the output
        // node of an object of the copy, -1 when it is not written. An effect
        // with no written ancestor hangs from the root (node -1); one whose
        // ancestors are all gone is counted as missingNode. Only the written
        // effects count their modules and textures. Null when no effect is
        // written; LeftOut still tells what was left out. The images carry
        // their PNG bytes until Inject places them in the binary chunk.
        internal Dictionary<string, object> Resolve(Func<GameObject, int> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            var lists = Kinds.ToDictionary(kind => kind, _ => new List<object>(), StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(omitted, StringComparer.Ordinal);
            void Add(string kind, int count) { if (count > 0) counts[kind] = (counts.TryGetValue(kind, out var c) ? c : 0) + count; }
            var used = new HashSet<int>();
            tableIndex.Clear();
            var written = new List<Dictionary<string, object>>();
            foreach (var effect in effects)
            {
                var at = -1;
                var output = -1;
                for (var i = 0; i < effect.Ancestors.Length && at < 0; i++)
                {
                    var ancestor = effect.Ancestors[i];
                    if (ancestor == null) continue;
                    if (ancestor == root) { at = i; output = -1; }
                    else if ((output = node(ancestor.gameObject)) >= 0) at = i;
                }
                if (at < 0) { Add("missingNode", 1); continue; }
                var offset = effect.Offsets[at];
                var entry = new Dictionary<string, object>
                {
                    { "node", output },
                    { "position", Vector(offset.GetColumn(3)) },
                    { "rotation", Quaternion(offset.rotation) },
                    { "scale", Vector(offset.lossyScale) },
                    { "active", effect.Active }
                };
                if (effect.HasMaterial) entry["material"] = effect.Material;
                foreach (var pair in effect.Values) entry[pair.Key] = pair.Value;
                Add("particleModule", effect.Modules);
                if (effect.Material >= 0) used.Add(effect.Material);
                lists[effect.Kind].Add(entry);
                if (effect.Part?.Source is object) tableIndex[effect.Part.Source.GetInstanceID()] = (lists[effect.Kind].Count - 1, output);
                written.Add(entry);
            }
            // Materials and images only the left-out effects used are dropped and the rest renumbered.
            var renumber = new Dictionary<int, int>();
            var keptImages = new Dictionary<int, int>();
            var kept = new List<object>();
            var imageList = new List<object>();
            foreach (var index in used.OrderBy(i => i))
            {
                renumber[index] = kept.Count;
                var record = new Dictionary<string, object>(materials[index]);
                var image = materialImages[index];
                if (image >= 0)
                {
                    if (!keptImages.TryGetValue(image, out var keptImage))
                    {
                        keptImages[image] = keptImage = imageList.Count;
                        imageList.Add(new Dictionary<string, object> { { "mimeType", "image/png" }, { "bytes", images[image].Png } });
                    }
                    record["texture"] = keptImage;
                }
                kept.Add(record);
            }
            foreach (var entry in written)
                if (entry.TryGetValue("material", out var material) && (int)material >= 0) entry["material"] = renumber[(int)material];
            Add("effectTexture", used.Count(i => materialImages[i] == -2));
            var omissions = OmittedKinds.Where(kind => counts.ContainsKey(kind))
                .Select(kind => (object)new Dictionary<string, object> { { "kind", kind }, { "count", counts[kind] } }).ToList();
            var extension = new Dictionary<string, object> { { "schemaVersion", SchemaVersion } };
            foreach (var kind in Kinds) extension[kind] = lists[kind];
            extension["materials"] = kept;
            extension["images"] = imageList;
            extension["omitted"] = omissions;
            LeftOut = ResultLine(extension);
            return written.Count == 0 ? null : extension;
        }

        // Adds the extension to the root of the finished GLB, its images
        // appended to the binary chunk as bufferViews. Null keeps the bytes.
        // The extension given is not changed.
        internal static byte[] Inject(byte[] bytes, Dictionary<string, object> extension)
        {
            if (extension == null) return bytes;
            ExportTestHooks.TableFaultInjection?.Invoke(Extension);
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list.Count : 0;
            var materialCount = ((List<object>)extension["materials"]).Count;
            var sourceImages = ((List<object>)extension["images"]).Cast<Dictionary<string, object>>().ToArray();
            foreach (var kind in Kinds)
                foreach (Dictionary<string, object> effect in (List<object>)extension[kind])
                {
                    if ((int)effect["node"] < -1 || (int)effect["node"] >= nodes) throw new InvalidOperationException("Avatar effects node is out of range.");
                    if (effect.TryGetValue("material", out var material) && (int)material >= materialCount) throw new InvalidOperationException("Avatar effects material is out of range.");
                }
            foreach (Dictionary<string, object> material in (List<object>)extension["materials"])
                if (material.TryGetValue("texture", out var texture) && (int)texture >= sourceImages.Length) throw new InvalidOperationException("Avatar effects texture is out of range.");
            var written = new Dictionary<string, object>(extension);
            var imageList = new List<object>();
            if (sourceImages.Length > 0)
            {
                if (!document.Json.TryGetValue("bufferViews", out var rawViews)) document.Json["bufferViews"] = rawViews = new List<object>();
                var views = (List<object>)rawViews;
                var payloads = sourceImages.Select(image => (byte[])image["bytes"]).ToArray();
                var offsets = document.AppendBinaryBatch(payloads);
                for (var i = 0; i < payloads.Length; i++)
                {
                    imageList.Add(new Dictionary<string, object> { { "bufferView", views.Count }, { "mimeType", sourceImages[i]["mimeType"] } });
                    views.Add(new Dictionary<string, object> { { "buffer", 0 }, { "byteOffset", offsets[i] }, { "byteLength", payloads[i].Length } });
                }
            }
            written["images"] = imageList;
            document.PlaceExtension(Extension, written);
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
