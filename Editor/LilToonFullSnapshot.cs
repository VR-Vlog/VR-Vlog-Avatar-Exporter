using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    // Snapshot before fallback baking. Values and texture references belong to
    // the prepared authoring result, never to its MToon approximation.
    internal sealed class LilToonFullSnapshot
    {
        // asset.extras key for the lilToon version installed in the authoring
        // project. sourceLilToonVersion stays the catalogue version (2.3.4).
        internal const string InstalledLilToonVersionKey = "VRVLOG_lilToonInstalledVersion";
        readonly Dictionary<Renderer, Material[]> sources = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Dictionary<string, object>> records = new Dictionary<Material, Dictionary<string, object>>();
        internal bool IsEmpty => records.Count == 0;
        // Official lilToon materials whose installed shader lacks a 2.3.4
        // runtime property, or declares one with another type or texture
        // dimension. They cannot satisfy the catalogue, so the app shows their
        // MToon approximation (LilToonStructureCheck lists the properties).
        readonly List<Material> approximated = new List<Material>();
        internal IReadOnlyList<Material> Approximated => approximated;
        readonly List<object> textures = new List<object>();
        readonly Dictionary<(Texture, bool), int> textureIds = new Dictionary<(Texture, bool), int>();
        // Strongest effective scale among the references to each 8-bit normal
        // texture, by texture index. Infinity marks a reference without one.
        readonly Dictionary<int, float> normalScales = new Dictionary<int, float>();
        readonly List<DeflatePayload> payloads = new List<DeflatePayload>();
        readonly List<object> bindings = new List<object>();
        readonly Dictionary<int, Dictionary<string, object>> indexedRecords = new Dictionary<int, Dictionary<string, object>>();
        readonly HashSet<Texture> resizedWarnings = new HashSet<Texture>();
        ICollection<string> warnings;
        bool suppressSharedTextureEmission, suppressHdrTextureEmission;
        LilToonInactiveTextureProof inactiveTextures;
        readonly string sourceColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear ? "linear" : "gamma";

        internal static LilToonFullSnapshot Capture(GameObject avatar, bool suppressSharedTextureEmission=false, bool suppressHdrTextureEmission=false, ICollection<string> warnings=null, GameObject animationSource=null)
        {
            var result = new LilToonFullSnapshot {suppressSharedTextureEmission=suppressSharedTextureEmission, suppressHdrTextureEmission=suppressHdrTextureEmission, warnings=warnings,
                inactiveTextures=new LilToonInactiveTextureProof(avatar, animationSource)};
            foreach (var renderer in ExportRendererSelection.Enumerate(avatar))
            {
                // UniVRM exports mesh primitives, not particle/trail/line systems.
                // Their materials must not create bindings to absent glTF meshes.
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
                // The pinned ModelExporter omits meshes without vertices or
                // indices. They cannot have a full binding to an output mesh.
                // Keep their transforms/bones and every nonempty mesh intact.
                if(!ExportRendererSelection.HasGeometry(mesh))continue;
                var materials = renderer.sharedMaterials;
                result.sources.Add(renderer, materials);
                foreach (var material in materials)
                    if (material != null && LilToon234Catalogue.Shaders.ContainsKey(material.shader.name) &&
                        !result.records.ContainsKey(material) && !result.approximated.Contains(material))
                    {
                        var record = result.ReadMaterial(material);
                        if (record != null) result.records.Add(material, record);
                        else result.approximated.Add(material);
                    }
            }
            // An avatar without lilToon still exports: its materials become MToon
            // defaults and the extension carries no lilToon records.
            return result;
        }

        // Null when the installed shader cannot give every 2.3.4 runtime
        // property of this shader.
        Dictionary<string, object> ReadMaterial(Material source)
        {
            bool Suppress(bool second) => LilToonEmissionPolicy.IsSuppressed(source,suppressSharedTextureEmission,second) ||
                suppressHdrTextureEmission && LilToonEmissionPolicy.HasHdrTextureEmission(source,second);
            var suppressEmission=Suppress(false);var suppressEmission2nd=Suppress(true);
            var values = new List<object>();
            var textureProperties = new List<object>();
            var shader = source.shader;
            var required = new HashSet<string>(LilToon234Catalogue.RequiredProperties[shader.name], StringComparer.Ordinal);
            var written = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                if (name == "_DummyProperty") continue;
                // Another lilToon version may add properties. The 2.3.4 shader in
                // the app has no use for them; the structure check lists them.
                if (!LilToon234Catalogue.Properties.TryGetValue(name, out var declaration)) continue;
                if (declaration.EndsWith(":editor", StringComparison.Ordinal)) continue;
                if (declaration.EndsWith(":external", StringComparison.Ordinal))
                {
                    if (name == "_UseAudioLink" && source.GetFloat(name) != 0)
                        throw ExportRecoveryFailure.At(new NotSupportedException(source.name + ": AudioLinkは外部連携のため今回の再現対象外です。"), "audio-link", source);
                    continue;
                }
                var type = shader.GetPropertyType(i);
                // Only this shader's 2.3.4 runtime properties, with their 2.3.4
                // type and texture dimension, fit the catalogue record.
                if (!required.Contains(name) || LilToonStructureCheck.TypeName(type) != declaration.Split(':')[0] ||
                    type == ShaderPropertyType.Texture && LilToon234Catalogue.TextureFaces.TryGetValue(name, out var faces) &&
                    LilToonStructureCheck.DimensionName(shader.GetPropertyTextureDimension(i)) != (faces == 6 ? "Cube" : "2D") ||
                    !written.Add(name)) continue;
                if (type == ShaderPropertyType.Texture)
                {
                    var texture = source.GetTexture(name);
                    var normal = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.Normal) != 0;
                    // Keep ordinary authored textures, including disabled
                    // ones. Only an unsupported reference proven unreachable
                    // by the current official shader can become unassigned.
                    if (texture != null && !(texture is Texture2D) && !(texture is Cubemap) &&
                        inactiveTextures?.CanOmit(source, name) == true)
                    {
                        texture = null;
                        warnings?.Add(source.name + ": 無効なlilToonレイヤーで使われない動的画像の参照を省略しました: " + name);
                    }
                    // Unassigned normal slots use Unity's encoded bump default.
                    // Export that default too, so the canonical decoder never
                    // interprets an app-platform-specific bump texture as RG.
                    if(texture==null && normal)texture=Texture2D.normalTexture;
                    int id;
                    try { id = texture == null ? -1 : Texture(texture, normal, normal ? NormalScale(source, name) : float.PositiveInfinity); }
                    catch (NotSupportedException error) when (texture != null && !(texture is Texture2D) && !(texture is Cubemap) &&
                        (name.StartsWith("_Main2nd", StringComparison.Ordinal) || name.StartsWith("_Main3rd", StringComparison.Ordinal)))
                    {
                        // Keep the original error type and attribute only an
                        // actual failure in a known optional main-color layer.
                        ExportRecoveryFailure.At(error, "material-layer", source);
                        error.Data[ExportRecoveryFailure.LayerKey] = name.StartsWith("_Main2nd", StringComparison.Ordinal) ? "2nd" : "3rd";
                        throw;
                    }
                    var scale = source.GetTextureScale(name); var offset = source.GetTextureOffset(name);
                    textureProperties.Add(new Dictionary<string, object> { {"name", name}, {"texture", id}, {"st", new List<object> {scale.x, scale.y, offset.x, offset.y}} });
                }
                else
                {
                    var value = type == ShaderPropertyType.Color ? (Vector4)source.GetColor(name) :
                        type == ShaderPropertyType.Vector ? source.GetVector(name) : new Vector4(source.GetFloat(name), 0, 0, 0);
                    if(suppressEmission && (name=="_UseEmission" || name=="_EmissionBlend") ||
                        suppressEmission2nd && (name=="_UseEmission2nd" || name=="_Emission2ndBlend")) value=Vector4.zero;
                    if(suppressEmission && name=="_EmissionColor" || suppressEmission2nd && name=="_Emission2ndColor") value=(Vector4)Color.black;
                    if (!Finite(value)) throw new InvalidDataException(source.name + ": 非有限の値: " + name);
                    values.Add(new Dictionary<string, object> { {"name", name}, {"type", declaration.Split(':')[0]}, {"value", new List<object> {value.x, value.y, value.z, value.w}} });
                }
            }
            if (!written.SetEquals(required)) return null;
            // The app draws the 2.3.4 passes. Passes another lilToon version
            // adds are left out, a changed LightMode takes the 2.3.4 one, and a
            // missing pass is written with the 2.3.4 LightMode and this
            // material's choice for it. With the catalogue version this is the
            // installed pass list unchanged.
            var modes = LilToon234Catalogue.PassLightModes[shader.name];
            var passes = new List<object>();
            void AddPass(string name, string lightMode) =>
                passes.Add(new Dictionary<string, object> { {"name", name}, {"lightMode",lightMode}, {"enabled", source.GetShaderPassEnabled(lightMode)} });
            bool HasPass(string name) => passes.Cast<Dictionary<string, object>>().Any(p => (string)p["name"] == name);
            for (var i = 0; i < source.passCount; i++)
            {
                var name = source.GetPassName(i);
                var lightMode=shader.FindPassTagValue(i,new ShaderTagId("LightMode")).name;
                if(lightMode=="Never")continue;
                if (string.IsNullOrEmpty(name) || HasPass(name) || !modes.TryGetValue(name, out var mode)) continue;
                // Unity may expose interned ShaderTagId names in uppercase.
                AddPass(name, string.Equals(lightMode, mode, StringComparison.OrdinalIgnoreCase) ? lightMode : mode);
            }
            foreach (var name in LilToon234Catalogue.Passes[shader.name])
                if (!HasPass(name)) AddPass(name, modes[name]);
            var settingPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../ProjectSettings/lilToonSetting.json"));
            var clipping=false;
            if(File.Exists(settingPath))
            {
                var settings=JsonDom.Parse(File.ReadAllText(settingPath)) as Dictionary<string,object>;
                clipping=settings!=null && settings.TryGetValue("LIL_FEATURE_CLIPPING_CANCELLER",out var enabled) && enabled is bool on && on;
            }
            if(LilToon234Catalogue.Shaders[shader.name].StartsWith("ltsmulti",StringComparison.Ordinal))clipping=source.GetFloat("_UseClippingCanceller")!=0;
            var keywords=source.shaderKeywords.Where(k=>!(suppressEmission && k=="_EMISSION") && !(suppressEmission2nd && k=="GEOM_TYPE_BRANCH")).Cast<object>().ToList();
            return new Dictionary<string, object> { {"sourceShader", shader.name}, {"name", source.name}, {"renderQueue", source.renderQueue}, {"values", values}, {"textures", textureProperties}, {"passes", passes}, {"keywords", keywords}, {"clippingCanceller",clipping} };
        }

        int Texture(Texture source, bool normal, float normalScale)
        {
            if (textureIds.TryGetValue((source, normal), out var existing))
            {
                // The strongest reference decides an 8-bit normal. Read it
                // again only when a later material raises that scale.
                if (normalScales.TryGetValue(existing, out var strongest) && normalScale > strongest)
                {
                    normalScales[existing] = normalScale;
                    var record = (Dictionary<string, object>)textures[existing];
                    // Half holds the canonical values for any scale, so only an
                    // image stored as rgba32 needs another decision.
                    if ((string)record["format"] == "rgba32")
                    {
                        var stored = (List<object>)record["chunks"];
                        record["format"] = StoreNormal(Pixels(source, true, false, true, true).ToList(), normalScale,
                            (chunk, bytes) => payloads[(int)stored[chunk]] = new DeflatePayload(bytes));
                    }
                }
                return existing;
            }
            if (!(source is Texture2D) && !(source is Cubemap)) throw new NotSupportedException("動的テクスチャは保存できません: " + source.name);
            var chunks = new List<object>();
            var (width, height, count, faces) = Layout(source);
            var resized = width != source.width || height != source.height;
            // Gamma rendering can expose an UNorm GPU view of sRGB pixels.
            // Preserve the stored pixel encoding, independent of this Editor's
            // sampling mode, so a Linear-space consumer decodes it correctly.
            var srgb = !normal && source.isDataSRGB;
            var storage = StorageFormat(source.graphicsFormat, normal);
            var hdr = storage != "rgba32";
            var half = storage == "rgbaHalf";
            var format = half ? "rgbaHalf" : hdr ? "rgbaFloat" : "rgba32";
            void Add(byte[] bytes) { chunks.Add(payloads.Count); payloads.Add(new DeflatePayload(bytes)); }
            // StorageFormat chose Half for an 8-bit or block-compressed normal
            // only because of its decode; the source itself is not Half.
            var quantizable = normal && half && StorageFormat(source.graphicsFormat, false) != "rgbaHalf";
            if (quantizable) format = StoreNormal(Pixels(source, normal, srgb, hdr, half).ToList(), normalScale, (_, bytes) => Add(bytes));
            else foreach (var bytes in Pixels(source, normal, srgb, hdr, half)) Add(bytes);
            if (resized && resizedWarnings.Add(source))
                warnings?.Add($"lilToon画像「{source.name}」を{source.width}×{source.height}から{width}×{height}に縮小しました（読み込み時のメモリ使用量を軽減）。");
            var index = textures.Count;
            textures.Add(new Dictionary<string, object> {
                {"name", source.name}, {"width", width}, {"height", height}, {"mips", count}, {"faces", faces},
                {"format", format}, {"srgb", srgb}, {"normal", normal},
                {"filter", (int)source.filterMode}, {"wrapU", (int)source.wrapModeU}, {"wrapV", (int)source.wrapModeV}, {"wrapW", (int)source.wrapModeW},
                {"aniso", source.anisoLevel}, {"mipBias", source.mipMapBias}, {"chunks", chunks}
            });
            textureIds.Add((source, normal), index);
            if (quantizable) normalScales.Add(index, normalScale);
            return index;
        }

        static (int width, int height, int mips, int faces) Layout(Texture source)
        {
            var size = TextureResizePolicy.Size(source.width, source.height);
            var resized = size.Item1 != source.width || size.Item2 != source.height;
            var sourceMipCount = source is Texture2D two ? two.mipmapCount : ((Cubemap)source).mipmapCount;
            // Keep intentionally mipless inputs mipless. A resized mipmapped
            // texture needs the chain for its new dimensions, not the old chain.
            var count = resized && sourceMipCount > 1 ? LilToonFullTexture.MipCount(size.Item1, size.Item2) : sourceMipCount;
            return (size.Item1, size.Item2, count, source is Cubemap ? 6 : 1);
        }

        // Every face and mip in chunk order, read one at a time.
        static IEnumerable<byte[]> Pixels(Texture source, bool normal, bool srgb, bool hdr, bool half)
        {
            var (width, height, count, faces) = Layout(source);
            var resized = width != source.width || height != source.height;
            for (var face = 0; face < faces; face++)
                for (var mip = 0; mip < count; mip++)
                    yield return resized
                        ? LilToonFullTexture.ReadResized(source, Math.Max(1, width >> mip), Math.Max(1, height >> mip), face, normal, srgb, hdr, half)
                        : LilToonFullTexture.Read(source, mip, face, normal, srgb, hdr, half);
        }

        // Holds one texture's Half chain until the whole chain is measured,
        // then stores each chunk and drops it.
        static string StoreNormal(List<byte[]> halfPixels, float scale, Action<int, byte[]> store)
        {
            var quantize = NormalFitsRgba32(halfPixels, scale);
            var format = quantize ? "rgba32" : "rgbaHalf";
            for (var i = 0; i < halfPixels.Count; i++)
            {
                var bytes = quantize ? NormalToRgba32(halfPixels[i]) : halfPixels[i];
                halfPixels[i] = null;
                LilToonFullContract.ValidatePixels(bytes, format);
                store(i, bytes);
            }
            return format;
        }

        // The scale lilToon 2.3.4 applies to each normal property's decoded RG.
        // Other Normal properties (outline and fur vectors, anisotropy tangents)
        // are not decoded this way, so their textures stay Half.
        static readonly Dictionary<string, string> NormalScaleProperties = new Dictionary<string, string> {
            {"_BumpMap", "_BumpScale"}, {"_Bump2ndMap", "_Bump2ndScale"}, {"_MatCapBumpMap", "_MatCapBumpScale"}, {"_MatCap2ndBumpMap", "_MatCap2ndBumpScale"}
        };

        // Effective scale of one reference, also when its feature toggle is off.
        // Infinity means unknown and keeps the texture Half.
        static float NormalScale(Material source, string property)
        {
            if (!NormalScaleProperties.TryGetValue(property, out var scale) || !source.HasProperty(scale)) return float.PositiveInfinity;
            var value = Math.Abs(source.GetFloat(scale));
            return float.IsNaN(value) || float.IsInfinity(value) ? float.PositiveInfinity : Math.Max(1f, value);
        }

        // Fixed before measuring exported avatars (Documentation~/NormalTextureFiltering.md).
        // An 8-bit normal is stored as rgba32 only when, over every face and
        // mip, the app's RG decode at the strongest material scale turns the
        // quantized direction from the Half one by at most these angles:
        // the nearest-rank 99th percentile and the maximum, in degrees.
        internal const double NormalRgba32Percentile99Degrees = 0.5;
        internal const double NormalRgba32MaximumDegrees = 2.0;

        internal static bool NormalFitsRgba32(IReadOnlyList<byte[]> halfPixels, float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale)) return false;
            var effective = Math.Max(1.0, Math.Abs((double)scale));
            long total = 0, over = 0;
            foreach (var bytes in halfPixels)
                for (var i = 0; i + 7 < bytes.Length; i += 8)
                {
                    var r = Half(bytes, i); var g = Half(bytes, i + 2);
                    var error = NormalAngle(r, g, Quantize(r) / 255.0, Quantize(g) / 255.0, effective);
                    if (error > NormalRgba32MaximumDegrees) return false;
                    if (error > NormalRgba32Percentile99Degrees) over++;
                    total++;
                }
            // The percentile is the ceil(0.99 N)-th smallest error.
            return over <= total - (total * 99 + 99) / 100;
        }

        // The bytes an RGBA32 readback of the same canonical normal holds.
        internal static byte[] NormalToRgba32(byte[] halfPixels)
        {
            var bytes = new byte[halfPixels.Length / 2];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)Quantize(Half(halfPixels, i * 2));
            return bytes;
        }

        static int Quantize(float value) => (int)Math.Round(Math.Min(1f, Math.Max(0f, value)) * 255.0, MidpointRounding.AwayFromZero);

        static float[] halfValues;
        static float Half(byte[] bytes, int offset)
            => (halfValues ??= Enumerable.Range(0, 65536).Select(bits => Mathf.HalfToFloat((ushort)bits)).ToArray())[bytes[offset] | bytes[offset + 1] << 8];

        // Same as the app: xy = (rg * 2 - 1) * scale, z = sqrt(1 - saturate(dot(xy, xy))).
        // atan2(|a x b|, a . b) does not need the normalized vectors.
        static double NormalAngle(double r0, double g0, double r1, double g1, double scale)
        {
            double x0 = (r0 * 2 - 1) * scale, y0 = (g0 * 2 - 1) * scale, z0 = Math.Sqrt(1 - Math.Min(1, x0 * x0 + y0 * y0));
            double x1 = (r1 * 2 - 1) * scale, y1 = (g1 * 2 - 1) * scale, z1 = Math.Sqrt(1 - Math.Min(1, x1 * x1 + y1 * y1));
            double cx = y0 * z1 - z0 * y1, cy = z0 * x1 - x0 * z1, cz = x0 * y1 - y0 * x1;
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), x0 * x1 + y0 * y1 + z0 * z1) * 180 / Math.PI;
        }

        internal static string StorageFormat(UnityEngine.Experimental.Rendering.GraphicsFormat format, bool normal)
        {
            // Canonical normals can need more precision than an RGBA8 copy, but
            // ordinary imported maps do not require full 32-bit components.
            // Half retains finer precision than their 8-bit source samples and
            // supports linear filtering on mobile GPUs without Float32 filtering.
            // Texture() then stores an 8-bit or block-compressed normal as
            // rgba32 when its measured RG-decode error stays within
            // NormalRgba32Percentile99Degrees / NormalRgba32MaximumDegrees.
            // Keep authored HDR and >8-bit normalized source precision intact.
            var name = format.ToString();
            if (name.StartsWith("R16", StringComparison.Ordinal) && name.EndsWith("_SFloat", StringComparison.Ordinal)) return "rgbaHalf";
            if (UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsHDRFormat(format)) return "rgbaFloat";
            foreach (System.Text.RegularExpressions.Match component in System.Text.RegularExpressions.Regex.Matches(name, "[RGBA]([0-9]+)"))
                if (int.Parse(component.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 8) return "rgbaFloat";
            // EAC decodes 11-bit components although Unity's names omit that width.
            if (normal && (format == UnityEngine.Experimental.Rendering.GraphicsFormat.R_EAC_UNorm
                || format == UnityEngine.Experimental.Rendering.GraphicsFormat.RG_EAC_UNorm
                || format == UnityEngine.Experimental.Rendering.GraphicsFormat.R_EAC_SNorm
                || format == UnityEngine.Experimental.Rendering.GraphicsFormat.RG_EAC_SNorm)) return "rgbaFloat";
            if (normal) return "rgbaHalf";
            if (name.EndsWith("_SNorm", StringComparison.Ordinal)) return "rgbaFloat";
            return "rgba32";
        }

        // Called while UniVRM's exact Unity-object/node/mesh mapping is alive.
        internal void Bind(UniVRM10.ModelExporter converter, VrmLib.Model model, UniGLTF.ExportingGltfData storage)
        {
            foreach (var entry in sources)
            {
                var renderer = entry.Key;
                var node = model.Nodes.IndexOf(converter.Nodes[renderer.gameObject]);
                if (node < 0 || node >= storage.Gltf.nodes.Count) throw new InvalidOperationException("出力nodeの対応が一致しません。");
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) throw new InvalidOperationException("描画メッシュがありません。");
                // Nodes retain the exact group for this renderer, including
                // shared static meshes with different additional streams.
                var group = converter.Nodes[renderer.gameObject].MeshGroup;
                var meshIndex = model.MeshGroups.IndexOf(group);
                var gltfMesh = storage.Gltf.meshes[meshIndex];
                if (gltfMesh.primitives.Count != entry.Value.Length) throw new InvalidOperationException("材質スロットとprimitiveの対応が一致しません。");
                var materialIndices = new List<object>();
                var originalIds = new List<int>();
                for (var slot = 0; slot < entry.Value.Length; slot++)
                {
                    var index = gltfMesh.primitives[slot].material ?? throw new InvalidDataException("出力primitiveに材質番号がありません。");
                    materialIndices.Add(index);
                    if (entry.Value[slot] != null && records.TryGetValue(entry.Value[slot], out var record))
                    {
                        if (!indexedRecords.ContainsKey(index))
                        {
                            var indexed = new Dictionary<string, object>(record);
                            indexed["materialIndex"] = index;
                            indexedRecords.Add(index, indexed);
                        }
                    }
                    var used = new SortedSet<int>(mesh.GetIndices(slot));
                    if (used.Count != storage.Gltf.accessors[gltfMesh.primitives[slot].attributes.POSITION].count)
                        throw new InvalidOperationException("元頂点と出力頂点の対応が一致しません。");
                    originalIds.AddRange(used);
                }
                var attributes = new List<object>();
                for (var channel = 0; channel < 8; channel++)
                {
                    var uv = new List<Vector4>(); mesh.GetUVs(channel, uv);
                    if (uv.Count == 0) continue;
                    if (uv.Count != mesh.vertexCount) throw new InvalidDataException("UV数が一致しません。");
                    attributes.Add(new Dictionary<string, object> { {"channel", channel}, {"payload", AddVectors(originalIds.Select(id => uv[id]))} });
                }
                var ids = AddVertexIds(originalIds);
                var bounds=renderer.localBounds;
                // A negative extent is an authoring mistake Unity accepts; abs() is the same box and the contract rejects negatives.
                var extents=bounds.extents;
                if(extents.x<0||extents.y<0||extents.z<0)warnings?.Add(renderer.name+": 描画範囲（Bounds）の大きさが負の値だったため、絶対値にして保存しました。");
                extents=new Vector3(Mathf.Abs(extents.x),Mathf.Abs(extents.y),Mathf.Abs(extents.z));
                List<object> Vec(Vector3 v)=>new List<object>{v.x,v.y,v.z,0};
                var anchor=renderer.probeAnchor;
                var anchorNode=anchor!=null && converter.Nodes.TryGetValue(anchor.gameObject,out var anchorModel)?model.Nodes.IndexOf(anchorModel):-1;
                if(anchor!=null && anchorNode<0)throw new NotSupportedException("アバター外のライトプローブ参照は環境データを必要とします。");
                var state=new Dictionary<string,object>{{"receiveShadows",renderer.receiveShadows},{"shadowCasting",(int)renderer.shadowCastingMode},
                    {"lightProbes",(int)renderer.lightProbeUsage},{"reflectionProbes",(int)renderer.reflectionProbeUsage},{"probeAnchor",anchorNode},
                    {"sortingLayer",SortingLayer.GetLayerValueFromID(renderer.sortingLayerID)},{"sortingOrder",renderer.sortingOrder},
                    {"boundsCenter",Vec(bounds.center)},{"boundsExtents",Vec(extents)}};
                bindings.Add(new Dictionary<string, object> { {"node", node}, {"mesh", meshIndex}, {"vertexCount", originalIds.Count}, {"materials", materialIndices}, {"uv", attributes}, {"vertexIds", ids}, {"renderer",state} });
            }
        }

        internal int AddVertexIds(IEnumerable<int> values)
        {
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            foreach (var id in values)
            {
                if (id < 0) throw new InvalidDataException("負の元頂点ID。");
                writer.Write((uint)id);
            }
            var index = payloads.Count; payloads.Add(new DeflatePayload(bytes.ToArray())); return index;
        }

        int AddVectors(IEnumerable<Vector4> values)
        {
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            foreach (var v in values) { if (!Finite(v)) throw new InvalidDataException("非有限の頂点データ。"); writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w); }
            var index = payloads.Count; payloads.Add(new DeflatePayload(bytes.ToArray())); return index;
        }

        internal byte[] Inject(byte[] bytes, string exporterVersion, string installedLilToonVersion)
        {
            // The window stops earlier without lilToon; this guards direct callers.
            // Any installed version is recorded as it is.
            var error = Compatibility.DependencyPolicy.LilToonError(installedLilToonVersion, ExporterLocalization.T);
            if (error != null) throw new NotSupportedException(error);
            var glb = GlbDocument.Read(bytes);
            var views = List(glb.Json, "bufferViews");
            var chunks = new List<object>();
            var offsets = glb.AppendBinaryBatch(payloads.Select(p => p.Encoded).ToArray());
            for (var i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                chunks.Add(new Dictionary<string, object> { {"bufferView", views.Count}, {"decodedBytes", payload.DecodedBytes}, {"codec", "deflate"} });
                views.Add(new Dictionary<string, object> { {"buffer", 0}, {"byteOffset", offsets[i]}, {"byteLength", payload.Encoded.Length} });
            }
            var extension = new Dictionary<string, object> {
                {"schemaMajor", 2}, {"schemaMinor", 0}, {"sourceLilToonVersion", LilToon234Catalogue.Version}, {"sourceCommit", LilToon234Catalogue.Commit},
                {"exporterVersion", exporterVersion}, {"materials", indexedRecords.OrderBy(p => p.Key).Select(p => (object)p.Value).ToList()},
                {"textures", textures}, {"bindings", bindings}, {"chunks", chunks}
            };
            if (!glb.Json.TryGetValue("extensions", out var raw)) glb.Json["extensions"] = raw = new Dictionary<string, object>();
            ((Dictionary<string, object>)raw)[LilToonMobileProfile.ExtensionName] = extension;
            var asset = LilToonFullContract.Object(glb.Json["asset"]);
            if (!asset.TryGetValue("extras", out var extras)) asset["extras"] = extras = new Dictionary<string, object>();
            LilToonFullContract.Object(extras)[LilToonFullContract.SourceColorSpaceKey] = sourceColorSpace;
            LilToonFullContract.Object(extras)[InstalledLilToonVersionKey] = installedLilToonVersion;
            var used = List(glb.Json, "extensionsUsed"); if (!used.Contains(LilToonMobileProfile.ExtensionName)) used.Add(LilToonMobileProfile.ExtensionName);
            GlbBinaryOptimizer.Compact(glb);
            if (System.Text.Encoding.UTF8.GetByteCount(JsonDom.Serialize(glb.Json)) > 8 * 1024 * 1024) throw new InvalidDataException("JSONが8MiB上限を超えています。");
            var output = glb.Write();
            LilToonFullContract.Validate(glb.Json, output.LongLength, glb.Binary.Length);
            return output;
        }

        static List<object> List(Dictionary<string, object> root, string key)
        { if (!root.TryGetValue(key, out var raw)) root[key] = raw = new List<object>(); return (List<object>)raw; }
        static bool Finite(Vector4 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z) && !float.IsNaN(v.w) && !float.IsInfinity(v.w);
    }
}
