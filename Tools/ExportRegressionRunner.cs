#if VRVLOG_EXPORT_REGRESSION_RUNNER
// Exports avatars through the detailed transfer board (the expression board's
// "transfer to app" export) to local .vrm files, so that two package versions
// can be compared with Tools/compare-exports.py. It is not part of the package:
// Tools/run-export-regression.ps1 copies this file into a temporary folder of a
// Unity project together with an asmdef whose versionDefines define
// VRVLOG_EXPORT_REGRESSION_RUNNER, and removes the folder afterwards.
// Everything else compiles this file to nothing.
//
// The exporter's types are internal, so they are reached by reflection; the
// temporary assembly needs no InternalsVisibleTo entry. The screenshots use the
// UniVRM copy that ships inside the package (VRVlog.LilToonExporter.Vendor.*), so
// the asmdef references those vendor assemblies.
//
// Unity -batchmode -projectPath <project> -executeMethod
//   VRVlog.LilToonExporter.ExportRegression.ExportRegressionRunner.Main
//   -regressionOut <dir>     results directory (created when missing)
//   -regressionJobs <file>   job list, UTF-8, one job per line: ID|prefab path|runs
//                            (runs defaults to 2; extra columns, blank lines and
//                            lines starting with # are ignored)
//   -regressionLabel <text>  free label copied into every result (default "x")
//   -regressionShots <mode>  first (default): screenshots of run 1 only;
//                            all: every run; none: no screenshots
//   -regressionTransforms on|off  sets ExpressionTransformCurves.Enabled for the
//                            whole run (bones and visibility of expressions written
//                            or not), so one package compares both; default: as built
//   -regressionShotsOnly 1   no export: redo the screenshots of run 1 for every
//                            job whose <ID>_1.vrm already exists in the directory
//                            (resumes via shots_progress.txt; SHOTS_DONE.txt at the end)
//
// Output in <dir>, one set per job:
//   <ID>_open.json        board opening: seconds, error, scene statistics
//   <ID>_<run>.json       ok, failure, warnings (export details entries), console
//                         warnings and errors, export_seconds, bytes
//   <ID>_<run>.vrm        the exported file
//   <ID>_<run>_shots.json screenshot list: bounds, head, expression names
//   <ID>_<run>_body_zpos.png / _body_zneg.png  whole model seen from +Z (front) and -Z (back)
//   <ID>_<run>_face.png   face close-up, no expression
//   <ID>_<run>_expr_NN.png face close-up with expression NN at weight 1
//   progress.txt          one line per step; DONE.txt when every job ran
// A relaunch into the same directory skips every job that progress.txt shows as
// started, so a crash or timeout does not repeat earlier jobs. Use a new
// directory for a new run. Exit code 0 when every job ran (export failures are
// recorded in the results), 1 on an error of the runner itself, 2 on bad arguments.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.ExportRegression
{
    public static class ExportRegressionRunner
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string Author = "regression";
        const double OpenTimeoutSeconds = 900, SettleTimeoutSeconds = 300;
        const int BodySize = 768, FaceSize = 512, MaxExpressionShots = 120;
        // "VR Vlog <export details>\n<middle dot>" as the exporter logs it; escaped to keep this file ASCII.
        const string DetailHeader = "VR Vlog 書き出し詳細\n・";
        const string DetailSeparator = "\n・";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly List<string> detailLogs = new List<string>();
        static readonly List<string> consoleLogs = new List<string>();
        static readonly Vector3 StageOffset = new Vector3(0f, 1000f, 0f);
        static string outDir, label, detailHeader, shotsMode;
        static IEnumerator routine;
        static bool capturing, failed;
        static Camera camera;
        static Light light;
        static RenderTexture renderTexture;

        static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        public static void Main()
        {
            outDir = Arg("-regressionOut");
            label = Arg("-regressionLabel", "x");
            shotsMode = Arg("-regressionShots", "first");
            var jobsPath = Arg("-regressionJobs");
            if (string.IsNullOrEmpty(outDir) || string.IsNullOrEmpty(jobsPath) || !File.Exists(jobsPath))
            {
                Debug.LogError("ExportRegressionRunner: -regressionOut and an existing -regressionJobs file are required.");
                EditorApplication.Exit(2);
                return;
            }
            Directory.CreateDirectory(outDir);
            var jobs = File.ReadAllLines(jobsPath, Encoding.UTF8)
                .Where(line => line.Contains("|") && !line.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToArray();
            var transforms = Arg("-regressionTransforms");
            if (transforms != null)
            {
                var enabled = RequireType("VRVlog.LilToonExporter.ExpressionTransformCurves").GetField("Enabled", Any);
                if (enabled == null || (transforms != "on" && transforms != "off"))
                {
                    Debug.LogError("ExportRegressionRunner: -regressionTransforms needs on or off and a package with ExpressionTransformCurves.Enabled.");
                    EditorApplication.Exit(2);
                    return;
                }
                enabled.SetValue(null, transforms == "on");
                label += " transforms=" + transforms;
            }
            Application.logMessageReceived += OnLog;
            routine = Arg("-regressionShotsOnly") != null ? ShotsOnly(jobs) : Run(jobs);
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Progress("FATAL " + e.ToString().Replace("\n", " | ")); failed = true; more = false; }
            if (more) return;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(failed ? 1 : 0);
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (!capturing || message == null) return;
            if (type == LogType.Log && detailHeader != null && message.StartsWith(detailHeader, StringComparison.Ordinal))
            {
                detailLogs.Add(message.Substring(detailHeader.Length));
                return;
            }
            if (type == LogType.Log) return;
            if (consoleLogs.Count < 80) consoleLogs.Add(type + ": " + message.Split('\n')[0]);
        }

        static void Progress(string line) =>
            File.AppendAllText(Path.Combine(outDir, "progress.txt"), DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + " " + line + "\n", Utf8);

        static void WriteJson(string name, string body) => File.WriteAllText(Path.Combine(outDir, name), "{" + body + "}", Utf8);

        static string Q(string s)
        {
            if (s == null) return "null";
            var b = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"') b.Append("\\\""); else if (c == '\\') b.Append("\\\\");
                else if (c == '\n') b.Append("\\n"); else if (c == '\r') b.Append("\\r"); else if (c == '\t') b.Append("\\t");
                else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        static string N(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "true" : "false";
        static string Arr(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";
        static string V(Vector3 v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        static Type RequireType(string fullName) =>
            FindType(fullName) ?? throw new InvalidOperationException("The package has no " + fullName + ".");

        static MethodInfo RequireMethod(Type type, string name) =>
            type.GetMethod(name, Any) ?? throw new InvalidOperationException("The package has no " + type.Name + "." + name + ".");

        static object Get(object target, string field)
        {
            var info = target.GetType().GetField(field, Any)
                ?? throw new InvalidOperationException("The board has no field " + field + ".");
            return info.GetValue(target);
        }

        static string Stats(GameObject avatar)
        {
            var transforms = avatar.GetComponentsInChildren<Transform>(true).Length;
            var skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var blendShapes = skinned.Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.blendShapeCount);
            var components = avatar.GetComponentsInChildren<Component>(true).Length;
            return "{" + $"\"transforms\":{transforms},\"components\":{components},\"skinned\":{skinned.Length},\"blendshapes\":{blendShapes}" + "}";
        }

        static void CloseBoards()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w != null && w.GetType().Namespace != null
                         && w.GetType().Namespace.StartsWith("VRVlog", StringComparison.Ordinal)).ToArray())
                w.Close();
        }

        static bool Busy(object window) =>
            window != null && (Get(window, "pending") != null || (bool)Get(window, "candidateValidationPending"));

        // ---- screenshots -------------------------------------------------------

        static void EnsureStage()
        {
            if (camera == null)
            {
                var go = new GameObject("__regression_camera");
                camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 50f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.enabled = false;
            }
            if (light == null)
            {
                var go = new GameObject("__regression_light");
                light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                go.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f, 1f);
        }

        static void Shoot(Vector3 position, Vector3 target, float fov, int size, string file)
        {
            EnsureStage();
            if (renderTexture == null || renderTexture.width != size)
            {
                if (renderTexture != null) { renderTexture.Release(); Object.DestroyImmediate(renderTexture); }
                renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            }
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            camera.fieldOfView = fov;
            camera.targetTexture = renderTexture;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static string ColorProperty(string bindType)
        {
            switch (bindType)
            {
                case "color": return "_Color";
                case "emissionColor": return "_EmissionColor";
                case "shadeColor": return "_ShadeColor";
                case "matcapColor": return "_MatcapColor";
                case "rimColor": return "_RimColor";
                case "outlineColor": return "_OutlineColor";
                default: return null;
            }
        }

        // Applies one expression at weight 1 (morph targets and material colors) and returns the undo action.
        static Action Apply(Transform root, Dictionary<string, Material> materials, VRM10Expression clip, out int morphs, out int colors)
        {
            var undo = new List<Action>();
            morphs = 0;
            colors = 0;
            foreach (var binding in clip.MorphTargetBindings)
            {
                var t = string.IsNullOrEmpty(binding.RelativePath) ? root : root.Find(binding.RelativePath);
                var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null || smr.sharedMesh == null || binding.Index < 0 || binding.Index >= smr.sharedMesh.blendShapeCount) continue;
                var index = binding.Index;
                var before = smr.GetBlendShapeWeight(index);
                smr.SetBlendShapeWeight(index, binding.Weight * MorphTargetBinding.VRM_TO_UNITY);
                undo.Add(() => { if (smr != null) smr.SetBlendShapeWeight(index, before); });
                morphs++;
            }
            foreach (var binding in clip.MaterialColorBindings)
            {
                var property = ColorProperty(binding.BindType.ToString());
                if (property == null || binding.MaterialName == null || !materials.TryGetValue(binding.MaterialName, out var material) || !material.HasProperty(property)) continue;
                var before = material.GetColor(property);
                material.SetColor(property, new Color(binding.TargetValue.x, binding.TargetValue.y, binding.TargetValue.z, binding.TargetValue.w));
                undo.Add(() => { if (material != null) material.SetColor(property, before); });
                colors++;
            }
            return () => { foreach (var u in undo) u(); };
        }

        sealed class ShotState
        {
            public Vrm10Instance instance;
            public Transform root;
            public Renderer[] renderers;
            public Dictionary<string, Material> materials = new Dictionary<string, Material>();
            public Vector3 faceTarget, facePosition;
            public Bounds bounds;
            public bool headFound;
            public byte[] neutral;
        }

        const float FaceFov = 25f;

        // Unity widens a skinned renderer's bounds by the reach of every BlendShape, so the same
        // body with one more shape in the file would be framed differently. Frame the vertices
        // as they are drawn at load instead.
        static Bounds DrawnBounds(Renderer[] renderers)
        {
            var bounds = new Bounds();
            var any = false;
            var baked = new Mesh();
            try
            {
                foreach (var r in renderers)
                {
                    Vector3[] vertices;
                    Matrix4x4 matrix;
                    if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    {
                        skinned.BakeMesh(baked, true);
                        vertices = baked.vertices;
                        matrix = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                    }
                    else if (r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                    {
                        vertices = filter.sharedMesh.vertices;
                        matrix = r.transform.localToWorldMatrix;
                    }
                    else
                    {
                        if (any) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
                        any = true;
                        continue;
                    }
                    foreach (var v in vertices)
                    {
                        var p = matrix.MultiplyPoint3x4(v);
                        if (any) bounds.Encapsulate(p); else bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            return any ? bounds : renderers[0].bounds;
        }

        // Loads the exported VRM beside the scene and shoots body (+Z/-Z) and neutral face. Throws on failure.
        static ShotState LoadForShots(string vrmPath, string prefix, List<string> shots)
        {
            var s = new ShotState();
            s.instance = Vrm10.LoadPathAsync(vrmPath, canLoadVrm0X: false, controlRigGenerationOption: ControlRigGenerationOption.None,
                showMeshes: true, awaitCaller: new ImmediateCaller()).Result;
            if (s.instance == null) throw new InvalidOperationException("the VRM did not load");
            s.root = s.instance.transform;
            s.root.position = StageOffset; // away from the source avatar that is still in the scene
            s.renderers = s.instance.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
            if (s.renderers.Length == 0) throw new InvalidOperationException("the VRM has no enabled renderer");
            s.bounds = DrawnBounds(s.renderers);
            foreach (var r in s.renderers)
                foreach (var m in r.sharedMaterials)
                    if (m != null && !s.materials.ContainsKey(m.name)) s.materials[m.name] = m;

            const float bodyFov = 30f;
            var half = Mathf.Max(s.bounds.extents.x, s.bounds.extents.y) * 1.15f;
            var distance = half / Mathf.Tan(bodyFov * 0.5f * Mathf.Deg2Rad) + s.bounds.extents.z;
            Shoot(s.bounds.center + new Vector3(0f, 0f, distance), s.bounds.center, bodyFov, BodySize, prefix + "_body_zpos.png");
            shots.Add("{\"file\":" + Q(prefix + "_body_zpos.png") + ",\"kind\":\"body\",\"side\":\"front\"}");
            Shoot(s.bounds.center - new Vector3(0f, 0f, distance), s.bounds.center, bodyFov, BodySize, prefix + "_body_zneg.png");
            shots.Add("{\"file\":" + Q(prefix + "_body_zneg.png") + ",\"kind\":\"body\",\"side\":\"back\"}");

            var humanoid = s.instance.Humanoid;
            var head = humanoid != null ? humanoid.Head : null;
            s.headFound = head != null;
            s.faceTarget = s.headFound ? head.position + Vector3.up * 0.07f : s.bounds.center + Vector3.up * s.bounds.extents.y * 0.8f;
            s.facePosition = s.faceTarget + new Vector3(0f, 0f, 0.55f);
            Shoot(s.facePosition, s.faceTarget, FaceFov, FaceSize, prefix + "_face.png");
            s.neutral = File.ReadAllBytes(Path.Combine(outDir, prefix + "_face.png"));
            shots.Add("{\"file\":" + Q(prefix + "_face.png") + ",\"kind\":\"face\"}");
            return s;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Screenshots of one exported VRM. A frame passes between applying an expression and rendering it,
        // because skinning (blend shapes included) is updated by the player loop, not by Camera.Render().
        static IEnumerator Screenshots(string vrmPath, string id, int run)
        {
            var sw = Stopwatch.StartNew();
            var prefix = $"{id}_{run}";
            var shots = new List<string>();
            string error = null;
            ShotState s = null;
            var count = 0;
            var expressions = 0;
            var sameAsNeutral = 0;
            try
            {
                s = LoadForShots(vrmPath, prefix, shots);
                count = 3;
            }
            catch (Exception e)
            {
                var inner = e is AggregateException && e.InnerException != null ? e.InnerException : e;
                error = inner.GetType().Name + ": " + inner.Message;
            }
            if (error == null)
            {
                var expression = s.instance.Vrm != null ? s.instance.Vrm.Expression : null;
                var clips = expression != null ? expression.Clips.ToArray() : new (ExpressionPreset Preset, VRM10Expression Clip)[0];
                var index = 0;
                foreach (var (preset, clip) in clips)
                {
                    var name = expression.CreateKey(clip).ToString();
                    if (index >= MaxExpressionShots)
                    {
                        shots.Add("{\"kind\":\"skipped\",\"index\":" + index + ",\"preset\":" + Q(preset.ToString()) + ",\"name\":" + Q(name) + "}");
                        index++;
                        continue;
                    }
                    Action undo = null;
                    int morphs = 0, colors = 0;
                    string shotError = null;
                    try { undo = Apply(s.root, s.materials, clip, out morphs, out colors); }
                    catch (Exception e) { shotError = "apply: " + e.Message; }
                    yield return null; // skinning picks up the new weights
                    var file = prefix + "_expr_" + index.ToString("D2", CultureInfo.InvariantCulture) + ".png";
                    var same = false;
                    if (shotError == null)
                    {
                        try
                        {
                            Shoot(s.facePosition, s.faceTarget, FaceFov, FaceSize, file);
                            same = SameBytes(s.neutral, File.ReadAllBytes(Path.Combine(outDir, file)));
                        }
                        catch (Exception e) { shotError = "shoot: " + e.Message; }
                    }
                    try { undo?.Invoke(); } catch (Exception e) { shotError = (shotError ?? "") + " undo: " + e.Message; }
                    if (same && morphs + colors > 0) sameAsNeutral++;
                    shots.Add("{\"file\":" + Q(file) + ",\"kind\":\"expression\",\"index\":" + index + ",\"preset\":" + Q(preset.ToString()) + ",\"name\":" + Q(name) +
                              ",\"binary\":" + B(clip.IsBinary) + ",\"morphs\":" + morphs + ",\"colors\":" + colors + ",\"transforms\":" + clip.NodeTransformBindings.Length +
                              ",\"same_as_neutral\":" + B(same) + ",\"error\":" + Q(shotError) + "}");
                    count++;
                    expressions++;
                    index++;
                }
                yield return null; // weights are back to neutral before the instance goes away
            }
            if (s != null && s.instance != null) Object.DestroyImmediate(s.instance.gameObject);
            var center = s != null ? s.bounds.center - StageOffset : Vector3.zero;
            var size = s != null ? s.bounds.size : Vector3.zero;
            WriteJson(prefix + "_shots.json", $"\"label\":{Q(label)},\"id\":{Q(id)},\"run\":{run},\"ok\":{B(error == null)},\"error\":{Q(error)},\"seconds\":{N(sw.Elapsed.TotalSeconds)}," +
                      $"\"count\":{count},\"expressions\":{expressions},\"same_as_neutral\":{sameAsNeutral},\"head_found\":{B(s != null && s.headFound)},\"bounds_center\":{V(center)},\"bounds_size\":{V(size)},\"shots\":{Arr(shots)}");
            Progress($"SHOTS {id} {run} {count} same={sameAsNeutral} {sw.Elapsed.TotalSeconds:F1}s {(error == null ? "ok" : "ERR " + error.Replace("\n", " "))}");
        }

        // -regressionShotsOnly: redo the screenshots of run 1 for every job whose VRM exists (no export).
        static IEnumerator ShotsOnly(string[] jobs)
        {
            var progressPath = Path.Combine(outDir, "shots_progress.txt");
            var done = new HashSet<string>(File.Exists(progressPath) ? File.ReadAllLines(progressPath).Select(l => l.Trim()).Where(l => l.Length > 0) : Enumerable.Empty<string>());
            Progress($"BEGIN-SHOTS {label} unity={Application.unityVersion} jobs={jobs.Length}");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return null;
            var n = 0;
            foreach (var job in jobs)
            {
                var id = job.Split('|')[0].Trim();
                if (done.Contains(id)) continue;
                var vrm = Path.Combine(outDir, id + "_1.vrm");
                if (!File.Exists(vrm)) { Progress($"SHOTS-SKIP {id} no vrm"); File.AppendAllText(progressPath, id + "\n", Utf8); continue; }
                var e = Screenshots(vrm, id, 1);
                while (e.MoveNext()) yield return null;
                File.AppendAllText(progressPath, id + "\n", Utf8);
                if (++n % 20 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); }
                yield return null;
            }
            Progress("DONE-SHOTS " + label);
            File.WriteAllText(Path.Combine(outDir, "SHOTS_DONE.txt"), DateTime.Now.ToString("s", CultureInfo.InvariantCulture), Utf8);
        }

        static IEnumerator Run(string[] jobs)
        {
            var boardType = RequireType("VRVlog.LilToonExporter.ExperimentalExpressionCaptureWindow");
            var licenseType = RequireType("VRVlog.LilToonExporter.AvatarLicenseOptions");
            var transferExport = RequireMethod(boardType, "TransferExport");
            var openDetailed = RequireMethod(RequireType("VRVlog.LilToonExporter.LanTransfer.AppTransferWindow"), "OpenDetailedFromMenu");
            var localize = RequireMethod(RequireType("VRVlog.LilToonExporter.ExporterLocalization"), "T");
            var releaseCopyScene = FindType("VRVlog.LilToonExporter.NdmfExportPreparation")?.GetMethod("ReleaseUnusedCopyScene", Any);
            detailHeader = (string)localize.Invoke(null, new object[] { DetailHeader });

            var progressPath = Path.Combine(outDir, "progress.txt");
            var started = new HashSet<string>(File.Exists(progressPath)
                ? File.ReadAllLines(progressPath).Select(l => l.Split(' ')).Where(a => a.Length >= 3 && a[1] == "JOB").Select(a => a[2])
                : Enumerable.Empty<string>());
            Progress($"BEGIN {label} unity={Application.unityVersion} jobs={jobs.Length} shots={shotsMode}");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return null;
            var processFirst = true;
            foreach (var job in jobs)
            {
                var parts = job.Split('|');
                var id = parts[0].Trim();
                var prefabPath = parts[1].Trim();
                var runs = parts.Length > 2 && parts[2].Trim().Length > 0 ? int.Parse(parts[2].Trim(), CultureInfo.InvariantCulture) : 2;
                if (started.Contains(id)) { Progress($"SKIP {id}"); continue; }
                Progress($"JOB {id}");
                var head = $"\"label\":{Q(label)},\"id\":{Q(id)},\"prefab\":{Q(prefabPath)}";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    for (var run = 1; run <= runs; run++)
                        WriteJson($"{id}_{run}.json", head + $",\"run\":{run},\"ok\":false,\"failure\":\"runner: missing prefab\",\"warnings\":[]");
                    Progress($"MISSING {id}");
                    continue;
                }
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var stats = Stats(avatar);

                // Open the detailed board as the menu item does, then wait until it has
                // nothing queued and no candidate validation waiting, two updates in a row.
                var sw = Stopwatch.StartNew();
                string openError = null;
                object window = null;
                var quiet = 0;
                try { openDetailed.Invoke(null, new object[] { new MenuCommand(avatar) }); }
                catch (Exception e) { openError = "open threw: " + (e.InnerException ?? e).Message; }
                while (openError == null)
                {
                    window = Resources.FindObjectsOfTypeAll(boardType).FirstOrDefault();
                    if (window != null && !Busy(window) && (Get(window, "session") != null || Get(window, "error") != null))
                    {
                        if (++quiet >= 2) break;
                    }
                    else quiet = 0;
                    if (sw.Elapsed.TotalSeconds > OpenTimeoutSeconds) { openError = "open timeout"; break; }
                    yield return null;
                }
                var openSeconds = sw.Elapsed.TotalSeconds;
                if (openError == null && window == null) openError = "no window";
                if (openError == null && Get(window, "session") == null) openError = "open failed: " + (Get(window, "error") as string);
                var recommendations = window == null ? null : Get(window, "recommendations") as IList;
                WriteJson($"{id}_open.json", head + $",\"stats\":{stats},\"open_seconds\":{N(openSeconds)},\"open_error\":{Q(openError)},\"recommendations\":{recommendations?.Count ?? -1}");
                Progress($"OPEN {id} {openSeconds:F1}s recs={recommendations?.Count ?? -1} {(openError ?? "ok")}");

                for (var run = 1; run <= runs; run++)
                {
                    var dest = Path.GetFullPath(Path.Combine(outDir, $"{id}_{run}.vrm"));
                    if (File.Exists(dest)) File.Delete(dest);
                    Progress($"START {id} {run}");
                    detailLogs.Clear();
                    consoleLogs.Clear();
                    var saved = false;
                    var processFirstRun = false;
                    var failure = openError;
                    sw.Restart();
                    if (failure == null)
                    {
                        processFirstRun = processFirst;
                        processFirst = false;
                        capturing = true;
                        try
                        {
                            Action onSaved = () => saved = true;
                            var license = Activator.CreateInstance(licenseType, true);
                            failure = (string)transferExport.Invoke(window, new object[] { avatar.name, Author, license, dest, onSaved, true });
                        }
                        catch (Exception e)
                        {
                            var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                            failure = "runner: " + inner.GetType().Name + ": " + inner.Message;
                        }
                        finally { capturing = false; EditorUtility.ClearProgressBar(); }
                        if (failure == null && !saved) failure = "runner: not saved (canceled)";
                        if (failure == null && !File.Exists(dest)) failure = "runner: no output file";
                    }
                    var exportSeconds = sw.Elapsed.TotalSeconds;
                    var bytes = File.Exists(dest) ? new FileInfo(dest).Length : -1;
                    var warnings = detailLogs.SelectMany(d => d.Split(new[] { DetailSeparator }, StringSplitOptions.None)).Select(Q);
                    WriteJson($"{id}_{run}.json", head + $",\"run\":{run},\"process_first\":{B(processFirstRun)},\"ok\":{B(failure == null)},\"failure\":{Q(failure)}," +
                              $"\"detail_logs\":{detailLogs.Count},\"warnings\":{Arr(warnings)},\"console\":{Arr(consoleLogs.Select(Q))}," +
                              $"\"export_seconds\":{N(exportSeconds)},\"bytes\":{bytes},\"stats\":{stats},\"recommendations\":{recommendations?.Count ?? -1}");
                    Progress($"END {id} {run} {exportSeconds:F1}s {(failure == null ? "ok" : "ERR " + failure.Replace("\n", " "))}");
                    if (failure == null && (shotsMode == "all" || (shotsMode == "first" && run == 1)))
                    {
                        var shotsRoutine = Screenshots(dest, id, run);
                        while (shotsRoutine.MoveNext()) yield return null;
                    }
                    // Let the board run what it queued before the next export.
                    for (var k = 0; k < 3; k++) yield return null;
                    var settle = Stopwatch.StartNew();
                    while (Busy(window) && settle.Elapsed.TotalSeconds < SettleTimeoutSeconds) yield return null;
                }
                CloseBoards();
                releaseCopyScene?.Invoke(null, null);
                Object.DestroyImmediate(avatar);
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();
                yield return null;
                yield return null;
            }
            Progress("DONE " + label);
            File.WriteAllText(Path.Combine(outDir, "DONE.txt"), DateTime.Now.ToString("s", CultureInfo.InvariantCulture), Utf8);
        }
    }
}
#endif
