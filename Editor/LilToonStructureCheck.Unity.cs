using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    internal static partial class LilToonStructureCheck
    {
        // Runs before the export copy is prepared. Differences of the shaders
        // the avatar uses go to warnings; a missing or changed baker stops.
        // Later the snapshot and the shared contract still validate what is
        // actually written.
        internal static void Inspect(GameObject avatar, string installedVersion, ICollection<string> warnings)
        {
            var differences = Check(avatar);
            var stops = differences.Where(Stops).ToList();
            if (stops.Count > 0) throw new InvalidOperationException(Message(stops, installedVersion, ExporterLocalization.T));
            if (warnings == null) return;
            foreach (var warning in Warnings(differences.Where(d => d.Shader != BakerShader).ToList(), ExporterLocalization.T)) warnings.Add(warning);
        }

        // Checks the official shaders of the renderers the snapshot records, and
        // lilToon's baker when one of their materials is baked with it. An
        // avatar without lilToon is not affected by the installed structure.
        internal static List<Difference> Check(GameObject avatar)
        {
            var shaders = new List<Shader>();
            var bakes = false;
            foreach (var renderer in ExportRendererSelection.Enumerate(avatar))
            {
                // Same selection as LilToonFullSnapshot.Capture: UniVRM exports
                // only mesh primitives with geometry.
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
                if (!ExportRendererSelection.HasGeometry(mesh)) continue;
                // Capture leaves a renderer with an empty slot on standard VRM materials.
                if (renderer.sharedMaterials.Any(material => material == null)) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    // A substitute can carry the source's brightness into
                    // _MainTexHSVG, which sends it to lilToon's baker too.
                    if (!bakes && material != null && UnsupportedShaderSubstitution.NeedsSubstitute(material))
                    {
                        Material substitute = null;
                        try { substitute = UnsupportedShaderSubstitution.Create(material); }
                        catch (Exception) { }
                        if (substitute != null)
                        {
                            bakes |= LilToonMainTextureBaker.NeedsBake(substitute);
                            UnityEngine.Object.DestroyImmediate(substitute);
                        }
                    }
                    if (material == null || material.shader == null || !LilToon234Catalogue.Shaders.ContainsKey(material.shader.name)) continue;
                    if (!shaders.Contains(material.shader)) shaders.Add(material.shader);
                    // Export options can only turn layers off, so this is a
                    // superset of the materials that reach lilToon's baker.
                    bakes |= LilToonMainTextureBaker.NeedsBake(material);
                }
            }
            var differences = new List<Difference>();
            foreach (var shader in shaders.OrderBy(s => s.name, StringComparer.Ordinal))
                differences.AddRange(Compare(Read(shader), shader.name));
            if (!bakes) return differences;
            var baker = Shader.Find(BakerShader);
            if (baker == null) differences.Add(new Difference(Kind.MissingShader, BakerShader));
            else differences.AddRange(Compare(Read(baker), BakerShader));
            return differences;
        }

        internal static ShaderStructure Read(Shader shader)
        {
            var result = new ShaderStructure(shader.name);
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var type = shader.GetPropertyType(i);
                var texture = type == ShaderPropertyType.Texture;
                result.Properties.Add((shader.GetPropertyName(i), TypeName(type), texture ? DimensionName(shader.GetPropertyTextureDimension(i)) : ""));
            }
            // Same pass indexing as the snapshot, which reads them from a material.
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                for (var i = 0; i < material.passCount; i++)
                    result.Passes.Add((material.GetPassName(i), shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name));
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            return result;
        }

        internal static string TypeName(ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color: return "color";
                case ShaderPropertyType.Vector: return "vector";
                case ShaderPropertyType.Texture: return "texture";
                case ShaderPropertyType.Float: case ShaderPropertyType.Range: case ShaderPropertyType.Int: return "float";
                default: return type.ToString().ToLowerInvariant();
            }
        }

        internal static string DimensionName(TextureDimension dimension)
        {
            switch (dimension)
            {
                case TextureDimension.Tex2D: return "2D";
                case TextureDimension.Cube: return "Cube";
                case TextureDimension.Tex3D: return "3D";
                case TextureDimension.Tex2DArray: return "2DArray";
                case TextureDimension.CubeArray: return "CubeArray";
                default: return dimension.ToString();
            }
        }
    }
}
