using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Objects the tests make, read and clean up.
    static class TestObjects
    {
        // The skin's current pose in world space, as it is drawn.
        public static Vector3[] WorldVertices(SkinnedMeshRenderer skin)
        {
            var baked = new Mesh();
            try { skin.BakeMesh(baked, false); return baked.vertices.Select(skin.transform.TransformPoint).ToArray(); }
            finally { Object.DestroyImmediate(baked); }
        }

        // Substitutes an unsupported shader through the full entry point.
        // shader names the VRChat row (null reads the source's shader); masks
        // are baked only when textures is given. The output and the baked
        // masks join owned.
        public static Material Substitute(List<Object> owned, Material source, string shader, out bool colorSkipped,
            out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped, List<Texture2D> textures = null)
        {
            var output = UnsupportedShaderSubstitution.Create(source, shader ?? (source.shader == null ? null : source.shader.name),
                out colorSkipped, out route, out dropped, textures);
            if (textures != null) owned.AddRange(textures);
            Assert.That(output, Is.Not.Null);
            owned.Add(output);
            return output;
        }

        // Cleans up a material bake: the source, what the bake created and the
        // images the test made.
        public static void Destroy(Material material, List<Material> created, List<Texture2D> textures, params Texture2D[] images)
        {
            foreach (var item in created) Object.DestroyImmediate(item);
            foreach (var texture in textures) Object.DestroyImmediate(texture);
            foreach (var image in images) Object.DestroyImmediate(image);
            Object.DestroyImmediate(material);
        }
    }
}
