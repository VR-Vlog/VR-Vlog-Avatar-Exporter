using System;
using UnityEditor;

namespace VRVlog.LilToonExporter.Tests
{
    // A uniquely named folder under Assets for the assets one test creates.
    // Fixtures keep the path from Create and delete it in TearDown; a single
    // test can hold an instance in a using and have it deleted on Dispose.
    sealed class TempAssetFolder : IDisposable
    {
        public readonly string Path;

        public TempAssetFolder(string prefix) => Path = Create(prefix);

        // Creates "Assets/<prefix><guid>" and returns its path.
        public static string Create(string prefix)
        {
            var name = prefix + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            return "Assets/" + name;
        }

        public void Dispose() => AssetDatabase.DeleteAsset(Path);
    }
}
