using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // Full names of plugin types found by reflection, so that no plugin
    // assembly is referenced.
    internal static class ExportTypeNames
    {
        internal const string MaNamespace = "nadena.dev.modular_avatar.core.";
        internal const string MaAvatarTag = MaNamespace + "AvatarTagComponent";
        internal const string AaoAvatarTag = "Anatawa12.AvatarOptimizer.AvatarTagComponent";
        internal const string NdmfObjectRegistry = "nadena.dev.ndmf.IObjectRegistry";

        // type, then its base types up to object.
        internal static IEnumerable<Type> SelfAndBaseTypes(Type type)
        {
            for (var current = type; current != null; current = current.BaseType) yield return current;
        }

        internal static bool InheritsFrom(Type type, string fullName) =>
            SelfAndBaseTypes(type).Any(current => current.FullName == fullName);
    }
}
