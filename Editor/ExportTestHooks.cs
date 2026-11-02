using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Set only by tests; null in every export.
    internal static class ExportTestHooks
    {
        // Tests throw here to prove that each stage can fail without stopping.
        internal static Action<ExportStage> FaultInjection;

        // Tests throw here to prove that a failure after the build passes,
        // outside every stage, does not stop a lenient export.
        internal static Action<GameObject> ConversionFaultInjection;

        // The packages that export an NDMF plugin, in place of the installed ones.
        internal static ISet<string> PluginPackagesOverride;

        // Tests pin whether an export continues past a failed stage, in place
        // of the editor preference.
        internal static bool? ContinueByDefault;
    }
}
