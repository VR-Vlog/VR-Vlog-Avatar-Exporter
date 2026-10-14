using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToon;
using Check = VRVlog.LilToonExporter.LilToonStructureCheck;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class LilToonStructureCheckTests
    {
        const string Probe = "_lil/[Optional] lilToonFakeShadow";

        [Test]
        public void InstalledOfficialShadersMatchTheCatalogue()
        {
            var names = LilToon234Catalogue.Shaders.Keys.Append(Check.BakerShader).ToArray();
            var find = Stopwatch.StartNew();
            var shaders = names.Select(Shader.Find).ToArray();
            find.Stop();
            var timings = new StringBuilder();
            for (var round = 0; round < 2; round++)
            {
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < names.Length; i++)
                {
                    Assert.That(shaders[i], Is.Not.Null, names[i]);
                    var differences = Check.Compare(Check.Read(shaders[i]), names[i]);
                    Assert.That(differences, Is.Empty, names[i] + ": " + string.Join("; ", differences));
                }
                timings.Append(" read+compare round " + round + ": " + timer.ElapsedMilliseconds + " ms");
            }
            UnityEngine.Debug.Log("lilToon structure check of " + names.Length + " shaders: find " + find.ElapsedMilliseconds + " ms" + timings);
        }

        [Test]
        public void ExportEntryChecksTheLilToonShadersTheAvatarUses()
        {
            var avatar = new GameObject("avatar");
            var lilToon = new Material(Shader.Find("lilToon"));
            var other = new Material(Shader.Find("Hidden/VRVlogTests/lilToon"));
            try
            {
                var child = new GameObject("body");
                child.transform.SetParent(avatar.transform);
                child.AddComponent<MeshFilter>();
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { other, null };
                Assert.That(Check.Check(avatar), Is.Empty);
                renderer.sharedMaterials = new[] { other, lilToon };
                Assert.That(Check.Check(avatar), Is.Empty);
                Assert.DoesNotThrow(() => Check.Require(avatar, "2.3.4"));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(lilToon);
                Object.DestroyImmediate(other);
            }
        }

        // A compiled stand-in for the FakeShadow shader with one change.
        [TestCase("unchanged", null, "")]
        [TestCase("grab-pass", null, "")]
        [TestCase("never-pass", null, "")]
        [TestCase("unnamed-pass", "UnknownPass", "<Unnamed Pass 1>")]
        [TestCase("type", "PropertyType", "_FakeShadowVector")]
        [TestCase("dimension", "TextureDimension", "_MainTex")]
        [TestCase("missing", "MissingProperty", "_Cull")]
        [TestCase("unknown", "UnknownProperty", "_VrvStructureProbe")]
        [TestCase("light-mode", "PassLightMode", "FORWARD")]
        public void SyntheticShaderWithOneChangedDeclarationStops(string change, string kind, string name)
        {
            var shader = ShaderUtil.CreateShaderAsset(Source(change));
            try
            {
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, change);
                var differences = Check.Compare(Check.Read(shader), Probe);
                if (kind == null) { Assert.That(differences, Is.Empty, string.Join("; ", differences)); return; }
                Assert.That(differences.Count, Is.EqualTo(1), string.Join("; ", differences));
                Assert.That(differences[0].Kind.ToString(), Is.EqualTo(kind));
                Assert.That(differences[0].Name, Is.EqualTo(name));
                var message = Check.Message(differences, "2.3.4", text => text);
                StringAssert.Contains(Probe, message);
                StringAssert.Contains(name, message);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        static string Source(string change)
        {
            var text = new StringBuilder("Shader \"Hidden/VRVlogTests/StructureProbe\"\n{\n    Properties\n    {\n");
            foreach (var property in LilToon234Catalogue.RequiredProperties[Probe])
            {
                if (change == "missing" && property == "_Cull") continue;
                var type = LilToon234Catalogue.Properties[property].Split(':')[0];
                if (change == "type" && property == "_FakeShadowVector") type = "float";
                var dimension = change == "dimension" && property == "_MainTex" ? "Cube" : "2D";
                text.Append("        ").Append(property).Append(type == "texture" ? " (\"t\", " + dimension + ") = \"white\" {}" :
                    type == "color" ? " (\"c\", Color) = (1,1,1,1)" : type == "vector" ? " (\"v\", Vector) = (0,0,0,0)" : " (\"f\", Float) = 0").Append('\n');
            }
            if (change == "unknown") text.Append("        _VrvStructureProbe (\"f\", Float) = 0\n");
            text.Append("    }\n    SubShader\n    {\n");
            if (change == "grab-pass") text.Append("        GrabPass {}\n");
            var lightMode = change == "light-mode" ? "ForwardAdd" : "ForwardBase";
            text.Append("        Pass\n        {\n            Name \"FORWARD\"\n            Tags {\"LightMode\" = \"" + lightMode + "\"}\n" + Program + "        }\n");
            // lilToon's fake pass has no name and LightMode Never; Unity names
            // an ordinary unnamed pass "<Unnamed Pass N>", which the snapshot records.
            if (change == "never-pass") text.Append("        Pass\n        {\n            Tags {\"LightMode\" = \"Never\"}\n" + Program + "        }\n");
            if (change == "unnamed-pass") text.Append("        Pass\n        {\n" + Program + "        }\n");
            return text.Append("    }\n}\n").ToString();
        }

        const string Program = @"            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
";
    }
}
