using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // MToon adds EmissiveFactor * EmissiveTexture. lilToon 2.3.4 also masks the
    // emission and tints it by the main color, so a mask that limits the glow to
    // a small area would otherwise light the whole mesh in the fallback.
    internal static class EmissionMaskBaker
    {
        // Returns null when the source emission image is already exact, or when
        // the bake cannot reproduce it (the reader then keeps its warning).
        internal static Texture2D Create(Material material, List<Texture2D> owned)
        {
            if (!LilToonEmissionPolicy.NeedsEmissionBake(material) || !LilToonEmissionPolicy.CanBakeEmission(material)) return null;
            // Without an owner list the baked image would leak; keep the source image.
            if (owned == null) return null;
            var shader = Shader.Find("Hidden/VRVlog/EmissionMaskBaker");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("lilToonのベイク用シェーダーを利用できません。");
            var main = Texture(material, "_MainTex") ?? Texture2D.whiteTexture;
            var emission = Texture(material, "_EmissionMap") ?? Texture2D.whiteTexture;
            var mask = Texture(material, "_EmissionBlendMask") ?? Texture2D.whiteTexture;
            var size = TextureResizePolicy.Size(Math.Max(main.width, Math.Max(emission.width, mask.width)),
                Math.Max(main.height, Math.Max(emission.height, mask.height)));
            var baker = new Material(shader);
            Texture2D result = null;
            try
            {
                baker.SetColor("_Color", material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white);
                foreach (var name in new[] { "_EmissionMap", "_EmissionBlendMask" })
                {
                    baker.SetTexture(name, name == "_EmissionMap" ? emission : mask);
                    if (!material.HasProperty(name)) continue;
                    baker.SetTextureScale(name, material.GetTextureScale(name));
                    baker.SetTextureOffset(name, material.GetTextureOffset(name));
                    if (material.HasProperty(name + "_ScrollRotate")) baker.SetVector(name + "_ScrollRotate", material.GetVector(name + "_ScrollRotate"));
                }
                baker.SetFloat("_EmissionMainStrength", material.HasProperty("_EmissionMainStrength") ? material.GetFloat("_EmissionMainStrength") : 0f);
                if (material.HasProperty("_MainTex"))
                {
                    var scale = material.GetTextureScale("_MainTex");
                    var offset = material.GetTextureOffset("_MainTex");
                    baker.SetVector("_VrvMainST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                }
                // MToon samples every image with the main texture transform.
                result = GpuReadback.BakeRgba32(size.Item1, size.Item2, material.name + "__VRVlog_Emission_" + Guid.NewGuid().ToString("N"), main,
                    target => Graphics.Blit(main, target, baker));
                owned.Add(result);
                var baked = result;
                result = null; // ownership transferred to the export
                return baked;
            }
            finally
            {
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                UnityEngine.Object.DestroyImmediate(baker);
            }
        }

        private static Texture Texture(Material material, string property) => material.HasProperty(property) ? material.GetTexture(property) : null;
    }
}
