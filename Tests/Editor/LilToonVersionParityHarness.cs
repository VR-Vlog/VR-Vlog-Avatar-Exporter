using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Batch-mode entry points for "export with lilToon X == upgrade to 2.3.4, then export" (#149, WBS 1-8).
    // This is not a test fixture, so the runner profiles keep their case counts. Run it on a copy of a
    // version project:
    //   Unity -batchmode -nographics -projectPath <copy> -executeMethod
    //     VRVlog.LilToonExporter.Tests.LilToonVersionParityHarness.CreateAndExport -parityOutput <dir>
    //   (replace Packages/jp.lilxyzw.liltoon with 2.3.4)
    //   Unity ... -executeMethod VRVlog.LilToonExporter.Tests.LilToonVersionParityHarness.ExportSaved -parityOutput <dir>
    // Tools/compare-liltoon-version-exports.py then compares <dir>/A with <dir>/B. Unity exits by itself.
    [InitializeOnLoad]
    public static class LilToonVersionParityHarness
    {
        internal const string Root = "Assets/VRVlogLilToonParity";
        const string PendingKey = "VRVlog.LilToonVersionParity.Pending";
        const string ExporterVersion = "0.10.0-preview.1";
        const int SettleSeconds = 20;

        static double readySince = -1;

        // lilToon's own startup (and its material migration) runs from delayCall after a domain
        // reload. A pending run survives reloads and waits until the editor has settled.
        static LilToonVersionParityHarness()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) EditorApplication.update += Tick;
        }

        public static void CreateAndExport() => Schedule("A|" + Argument("-parityOutput"));
        public static void ExportSaved() => Schedule("B|" + Argument("-parityOutput"));

        static void Schedule(string pending)
        {
            if (pending.EndsWith("|", StringComparison.Ordinal)) { Debug.LogError("[parity] -parityOutput is required"); EditorApplication.Exit(2); return; }
            SessionState.SetString(PendingKey, pending);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { readySince = -1; return; }
            if (readySince < 0) { readySince = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - readySince < SettleSeconds) return;
            EditorApplication.update -= Tick;
            var pending = SessionState.GetString(PendingKey, "").Split(new[] { '|' }, 2);
            SessionState.EraseString(PendingKey);
            var code = 1;
            try { code = Execute(pending[0], pending[1]); }
            catch (Exception error)
            {
                Debug.LogException(error);
                try { File.WriteAllText(Path.Combine(pending[1], "error-" + pending[0] + ".txt"), error.ToString()); } catch { }
            }
            Debug.Log("[parity] exit " + code);
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : "";
        }

        static int Execute(string side, string output)
        {
            var create = side == "A";
            if (create && AssetDatabase.IsValidFolder(Root)) throw new InvalidOperationException(Root + " already exists; start from a fresh copy of the version project.");
            if (!create && !AssetDatabase.IsValidFolder(Root)) throw new InvalidOperationException(Root + " is missing; run CreateAndExport first.");
            var specs = Specs();
            var manifest = new Dictionary<string, object>
            {
                { "side", side }, { "unity", Application.unityVersion },
                { "lilToonPackage", FindLilToon() }, { "groups", new List<object>() },
            };
            var groups = (List<object>)manifest["groups"];
            if (create) CreateAssets(specs, groups);
            var failures = 0;
            foreach (var spec in specs)
            {
                var path = Root + "/Materials/" + spec.Name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                var record = groups.OfType<Dictionary<string, object>>().FirstOrDefault(g => (string)g["name"] == spec.Name);
                if (record == null) groups.Add(record = new Dictionary<string, object> { { "name", spec.Name } });
                record["material"] = path;
                record["shader"] = material != null ? material.shader.name : null;
                record["lilToonVersionProperty"] = material != null && material.HasProperty("_lilToonVersion") ? (object)material.GetFloat("_lilToonVersion") : null;
                if (material == null) { record["error"] = "material not found"; failures++; continue; }
                var targets = create ? new[] { side, side + "-again" } : new[] { side };
                foreach (var target in targets)
                {
                    try
                    {
                        var warnings = new List<string>();
                        var bytes = Export(material, spec.Name, warnings, out var installed);
                        Directory.CreateDirectory(Path.Combine(output, target));
                        File.WriteAllBytes(Path.Combine(output, target, spec.Name + ".glb"), bytes);
                        record["installedLilToon"] = installed;
                        record["warnings"] = warnings.Cast<object>().ToList();
                    }
                    catch (Exception error)
                    {
                        record["error"] = error.GetType().Name + ": " + error.Message;
                        failures++;
                        break;
                    }
                }
            }
            manifest["failures"] = failures;
            File.WriteAllText(Path.Combine(output, "manifest-" + side + ".json"), JsonDom.Serialize(manifest));
            Debug.Log("[parity] " + side + " exported " + (specs.Count - failures) + "/" + specs.Count);
            return failures == 0 ? 0 : 1;
        }

        static byte[] Export(Material material, string name, List<string> warnings, out string installed)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            // The same preflight as the export window: range check, structure check, installed version.
            installed = LilToonExporterWindow.RequireSupportedLilToon(fixture.Source);
            return UniVrmOneClickExporter.Export(fixture.Source, "Parity " + name, "Tests", warnings,
                exporterVersion: ExporterVersion, lilToonVersion: installed, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
        }

        static object FindLilToon()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == "jp.lilxyzw.liltoon");
            return package == null ? null : new Dictionary<string, object> { { "version", package.version }, { "source", package.source.ToString() } };
        }

        internal sealed class Spec
        {
            internal string Name, Shader;
            internal Action<Setter> Setup;
        }

        // Records which properties exist in this lilToon version, so a property that is missing in an
        // old version shows up in the manifest instead of silently not being set.
        internal sealed class Setter
        {
            internal Material Material;
            internal Dictionary<string, Texture2D> Textures;
            internal readonly List<object> Set = new List<object>(), Missing = new List<object>();
            internal void F(string name, float value) { if (Has(name)) Material.SetFloat(name, value); }
            internal void C(string name, Color value) { if (Has(name)) Material.SetColor(name, value); }
            internal void V(string name, Vector4 value) { if (Has(name)) Material.SetVector(name, value); }
            internal void T(string name, string texture) { if (Has(name)) Material.SetTexture(name, Textures[texture]); }
            bool Has(string name)
            {
                if (Material.HasProperty(name)) { Set.Add(name); return true; }
                Missing.Add(name); return false;
            }
        }

        // Synthetic materials on the parts whose rendering changed between lilToon releases
        // (2.1.0 shadow colour and Outline Lit, 2.3.0 backlight alpha) and on the default
        // material of every catalogue shader.
        static List<Spec> Specs()
        {
            var specs = new List<Spec>
            {
                new Spec { Name = "backlight-color-a050", Shader = "lilToon", Setup = s => { s.F("_UseBacklight", 1); s.C("_BacklightColor", new Color(1f, .8f, .6f, .5f)); s.F("_BacklightMainStrength", .25f); } },
                new Spec { Name = "backlight-color-a100", Shader = "lilToon", Setup = s => { s.F("_UseBacklight", 1); s.C("_BacklightColor", new Color(1f, .8f, .6f, 1f)); } },
                new Spec { Name = "backlight-texture-alpha", Shader = "lilToon", Setup = s => { s.F("_UseBacklight", 1); s.C("_BacklightColor", new Color(.9f, .9f, 1f, .75f)); s.T("_BacklightColorTex", "alpha-ramp"); } },
                new Spec { Name = "fur", Shader = "Hidden/lilToonFur", Setup = s => { s.F("_FurLayerNum", 3); s.V("_FurVector", new Vector4(0f, 0f, 1f, .04f)); s.T("_FurMask", "alpha-ramp"); s.T("_MainTex", "color"); } },
                new Spec { Name = "outline-lit", Shader = "Hidden/lilToonOutline", Setup = s => { s.C("_OutlineColor", new Color(.2f, .1f, .3f, 1f)); s.F("_OutlineWidth", .3f); s.C("_OutlineLitColor", new Color(1f, .3f, .2f, .6f)); s.F("_OutlineLitApplyTex", 1); s.F("_OutlineLitScale", 4); s.F("_OutlineLitOffset", -3); s.F("_OutlineLitShadowReceive", 1); } },
                new Spec { Name = "matcap-uv1", Shader = "lilToon", Setup = s => { s.F("_UseMatCap", 1); s.T("_MatCapTex", "color"); s.V("_MatCapBlendUV1", new Vector4(.5f, .25f, 0f, 0f)); s.F("_UseMatCap2nd", 1); s.T("_MatCap2ndTex", "alpha-ramp"); s.V("_MatCap2ndBlendUV1", new Vector4(0f, .75f, 0f, 0f)); } },
                new Spec { Name = "shadow-colors", Shader = "lilToon", Setup = s => { s.F("_UseShadow", 1); s.C("_ShadowColor", new Color(.6f, .5f, .7f, 1f)); s.C("_Shadow2ndColor", new Color(.4f, .3f, .5f, .5f)); s.C("_Shadow3rdColor", new Color(.2f, .2f, .3f, .25f)); s.F("_ShadowBorder", .4f); s.F("_ShadowBlur", .2f); s.T("_ShadowColorTex", "color"); s.F("_ShadowMainStrength", .3f); s.C("_ShadowBorderColor", new Color(1f, .2f, .1f, 1f)); } },
                new Spec { Name = "rim-default-env-border", Shader = "lilToon", Setup = s => { s.F("_UseRim", 1); s.C("_RimColor", new Color(.7f, .9f, 1f, .8f)); s.F("_RimBorder", .6f); s.F("_UseReflection", 1); } },
                new Spec { Name = "rim-shade", Shader = "lilToon", Setup = s => { s.F("_UseRimShade", 1); s.C("_RimShadeColor", new Color(.3f, .2f, .4f, .7f)); } },
                new Spec { Name = "main-texture-alpha", Shader = "Hidden/lilToonTransparent", Setup = s => { s.T("_MainTex", "alpha-ramp"); s.C("_Color", new Color(1f, .95f, .9f, .6f)); } },
                new Spec { Name = "cutout", Shader = "Hidden/lilToonCutout", Setup = s => { s.T("_MainTex", "alpha-ramp"); s.F("_Cutoff", .3f); } },
            };
            foreach (var shader in VRVlog.LilToon.LilToon234Catalogue.Shaders.Keys.OrderBy(k => k, StringComparer.Ordinal))
                specs.Add(new Spec { Name = "default-" + new string(shader.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-'), Shader = shader });
            return specs;
        }

        static void CreateAssets(List<Spec> specs, List<object> groups)
        {
            AssetDatabase.CreateFolder("Assets", "VRVlogLilToonParity");
            AssetDatabase.CreateFolder(Root, "Textures");
            AssetDatabase.CreateFolder(Root, "Materials");
            var textures = new Dictionary<string, Texture2D>
            {
                { "color", CreateTexture("color", (x, y) => new Color32((byte)(40 + x * 50), (byte)(200 - y * 40), 120, 255)) },
                // Alpha below 1 is what the 2.3.0 backlight change reads.
                { "alpha-ramp", CreateTexture("alpha-ramp", (x, y) => new Color32(230, 180, 90, (byte)(32 + (x + y * 4) * 14))) },
            };
            foreach (var spec in specs)
            {
                var shader = Shader.Find(spec.Shader);
                var record = new Dictionary<string, object> { { "name", spec.Name }, { "requestedShader", spec.Shader } };
                groups.Add(record);
                if (shader == null) { record["error"] = "shader not found"; continue; }
                var material = new Material(shader) { name = spec.Name };
                if (spec.Shader.Contains("Multi")) SetupMulti(material, spec.Shader);
                var setter = new Setter { Material = material, Textures = textures };
                spec.Setup?.Invoke(setter);
                record["set"] = setter.Set; record["missing"] = setter.Missing;
                AssetDatabase.CreateAsset(material, Root + "/Materials/" + spec.Name + ".mat");
            }
            AssetDatabase.SaveAssets();
        }

        // Same setup as LilToonFullExportTests for the multi shader family.
        static void SetupMulti(Material material, string shader)
        {
            material.SetFloat("_TransparentMode", shader.Contains("Fur") ? 5 : shader.Contains("Gem") ? 6 : shader.Contains("Refraction") ? 3 : 2);
            material.SetFloat("_UseOutline", shader.Contains("Outline") ? 1 : 0);
            var utility = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("lilToon.lilMaterialUtils")).FirstOrDefault(t => t != null);
            utility?.GetMethod("SetupMultiMaterial", new[] { typeof(Material) })?.Invoke(null, new object[] { material });
        }

        static Texture2D CreateTexture(string name, Func<int, int, Color32> pixel)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(Enumerable.Range(0, 16).Select(i => pixel(i % 4, i / 4)).ToArray());
                texture.Apply(false);
                var path = Root + "/Textures/" + name + ".png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
