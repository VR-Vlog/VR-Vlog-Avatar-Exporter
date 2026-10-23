using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VRVlog.LilToonExporter.Compatibility;
public static class ExporterStartupProbe
{
    public static void Run()
    {
        if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("VRVlog.") && a.GetName().Name.EndsWith(".Tests")))
            throw new Exception("Production startup must run without exporter test assemblies");
        var manifest=System.IO.File.ReadAllText("Packages/manifest.json");
        if(manifest.Contains("\"testables\"")) throw new Exception("Production startup must run without testables");
        var menuEntries=TypeCache.GetMethodsWithAttribute<MenuItem>()
            .SelectMany(m=>m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
            .Where(m=>m.menuItem.StartsWith("VR Vlog/", StringComparison.Ordinal)).Select(m=>m.menuItem).ToArray();
        // No export entry; a package with the transfer module lists its two transfers.
        var transfers=new[]{"VR Vlog/アプリに転送（かんたん）...","VR Vlog/アプリに転送（通常）..."};
        if(menuEntries.Any(m=>!transfers.Contains(m)))
            throw new Exception("Exporter menu must contain nothing but the app transfers");
        Debug.Log("PROBE startup registered=" + DependencyDiagnostics.BackendReady + " errors=" + EditorUtility.scriptCompilationFailed);
        foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor).Where(a => a.name.StartsWith("VRVlog")))
            Debug.Log("PROBE graph="+assembly.name+" defines="+string.Join(",",assembly.defines.Where(d=>d.StartsWith("VRVLOG"))));
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name.StartsWith("VRVlog")))
            Debug.Log("PROBE loaded="+assembly.FullName);
        if(!DependencyDiagnostics.BackendReady || DependencyDiagnostics.StartupError(DependencyDiagnostics.Installed())!=null)
            throw new Exception("Exporter backend is not ready");
        DependencyDiagnostics.RefreshBackend();
        if(!DependencyDiagnostics.BackendReady) throw new Exception("Refresh lost backend");
        DependencyDiagnostics.ReloadBackend();
        var report=DependencyDiagnostics.CreateReport();
        if(!report.Contains("Status: Ready")) throw new Exception("Diagnostics did not become ready");
        Debug.Log("PROBE report " + report);
        Debug.Log("PROBE Passed production startup");
    }
}
