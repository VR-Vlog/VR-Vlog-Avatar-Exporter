using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // The VRChat SDK, NDMF, Modular Avatar, Avatar Optimizer and lilToon are
    // optional, so tests find their types by name and skip when one is missing.
    // NdmfPreparationTests also compiles this file in the host shim
    // (Tools/run-ndmf-preparation-tests.ps1); keep it to what the shim stubs.
    static class OptionalTools
    {
        public static Type Find(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        public static Type Require(string fullName, string reason)
        {
            var type = Find(fullName);
            if (type == null) Assert.Ignore(reason);
            return type;
        }

        public static Shader RequireShader(string name, string reason)
        {
            var shader = Shader.Find(name);
            if (shader == null) Assert.Ignore(reason);
            return shader;
        }

        public static UnityEditor.PackageManager.PackageInfo Package(string name) =>
            UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(value => value.name == name);

        public static bool TryVersion(UnityEditor.PackageManager.PackageInfo package, out Version version) =>
            Version.TryParse(package.version.Split('-', '+')[0], out version);

        // Skips when the package is installed but older than the minimum; the
        // installed version is appended to the reason.
        public static void IgnoreBelow(string package, Version minimum, string reason)
        {
            var installed = Package(package);
            if (installed != null && TryVersion(installed, out var version) && version < minimum)
                Assert.Ignore(reason + " (installed " + installed.version + ").");
        }
    }
}
