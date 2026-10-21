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
// temporary assembly needs no InternalsVisibleTo entry and no reference that a
// project with the repository added from disk would already define.
//
// Unity -batchmode -projectPath <project> -executeMethod
//   VRVlog.LilToonExporter.ExportRegression.ExportRegressionRunner.Main
//   -regressionOut <dir>     results directory (created when missing)
//   -regressionJobs <file>   job list, UTF-8, one job per line: ID|prefab path|runs
//                            (runs defaults to 2; extra columns, blank lines and
//                            lines starting with # are ignored)
//   -regressionLabel <text>  free label copied into every result (default "x")
//
// Output in <dir>, one set per job:
//   <ID>_open.json   board opening: seconds, error, scene statistics
//   <ID>_<run>.json  ok, failure, warnings (export details entries), console
//                    warnings and errors, export_seconds, bytes
//   <ID>_<run>.vrm   the exported file
//   progress.txt     one line per step; DONE.txt when every job ran
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
using Debug = UnityEngine.Debug;

namespace VRVlog.LilToonExporter.ExportRegression
{
    public static class ExportRegressionRunner
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string Author = "regression";
        const double OpenTimeoutSeconds = 900, SettleTimeoutSeconds = 300;
        // "VR Vlog <export details>\n<middle dot>" as the exporter logs it; escaped to keep this file ASCII.
        const string DetailHeader = "VR Vlog \u66f8\u304d\u51fa\u3057\u8a73\u7d30\n\u30fb";
        const string DetailSeparator = "\n\u30fb";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly List<string> detailLogs = new List<string>();
        static readonly List<string> consoleLogs = new List<string>();
        static string outDir, label, detailHeader;
        static IEnumerator routine;
        static bool capturing, failed;

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
            Application.logMessageReceived += OnLog;
            routine = Run(jobs);
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
            Progress($"BEGIN {label} unity={Application.unityVersion} jobs={jobs.Length}");
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
                    // Let the board run what it queued before the next export.
                    for (var k = 0; k < 3; k++) yield return null;
                    var settle = Stopwatch.StartNew();
                    while (Busy(window) && settle.Elapsed.TotalSeconds < SettleTimeoutSeconds) yield return null;
                }
                CloseBoards();
                releaseCopyScene?.Invoke(null, null);
                UnityEngine.Object.DestroyImmediate(avatar);
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
