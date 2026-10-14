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
        // Stops before the export copy is prepared. Later the snapshot and the
        // shared contract still validate what is actually written.
        internal static void Require(GameObject avatar, string installedVersion)
        {
            var differences = Check(avatar);
            if (differences.Count > 0) throw new InvalidOperationException(Message(differences, installedVersion, ExporterLocalization.T));
        }

        // Checks the official shaders that the exported renderers use, and the
        // baker when any of them is lilToon. An avatar without lilToon is not
        // affected by the installed lilToon's structure.
        internal static List<Difference> Check(GameObject avatar)
        {
            var shaders = new List<Shader>();
            foreach (var renderer in ExportRendererSelection.Enumerate(avatar))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.shader != null && LilToon234Catalogue.Shaders.ContainsKey(material.shader.name) && !shaders.Contains(material.shader))
                        shaders.Add(material.shader);
            var differences = new List<Difference>();
            if (shaders.Count == 0) return differences;
            foreach (var shader in shaders.OrderBy(s => s.name, StringComparer.Ordinal))
                differences.AddRange(Compare(Read(shader), shader.name));
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

        static string TypeName(ShaderPropertyType type)
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

        static string DimensionName(TextureDimension dimension)
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
