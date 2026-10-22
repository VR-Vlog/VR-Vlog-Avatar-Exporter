using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter.Compatibility
{
    // Where lilToon is: the registered VPM/UPM package jp.lilxyzw.liltoon, or
    // lilToon imported from its unitypackage under Assets, which registers no
    // package. lilToon itself reads its version from its package.json by
    // this GUID (lilDirectoryManager.GetPackageJsonPath); every release ships
    // that file with the same GUID in the package and under Assets/lilToon.
    public sealed class LilToonInstallation
    {
        public const string PackageName = "jp.lilxyzw.liltoon";
        internal const string PackageJsonGuid = "397d2fa9e93fb5d44a9540d5f01437fc";
        internal const string ShaderName = "lilToon";

        // The package version, or DependencyPolicy.LilToonUnknownVersion when
        // the Assets copy gives none. AssetsFolder is null for a registered package.
        public readonly string Version;
        public readonly string AssetsFolder;

        LilToonInstallation(string version, string assetsFolder)
        {
            Version = version;
            AssetsFolder = assetsFolder;
        }

        [Serializable]
        sealed class Manifest
        {
            public string name;
            public string version;
        }

        // A registered package wins over an Assets copy of the same lilToon.
        public static LilToonInstallation Find()
        {
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                if (string.Equals(package.name, PackageName, StringComparison.Ordinal)) return new LilToonInstallation(package.version, null);
            return FindUnderAssets();
        }

        public static LilToonInstallation FindUnderAssets() =>
            FromAssetPaths(AssetDatabase.GUIDToAssetPath(PackageJsonGuid), () =>
            {
                var shader = Shader.Find(ShaderName);
                return shader == null ? null : AssetDatabase.GetAssetPath(shader);
            }, ReadText);

        // Decides from the asset paths alone, so tests cover the Assets install
        // in projects where lilToon is a registered package. readText returns
        // null for a missing file.
        public static LilToonInstallation FromAssetPaths(string packageJsonPath, Func<string> shaderPath, Func<string, string> readText)
        {
            if (UnderAssets(packageJsonPath))
            {
                var text = readText(packageJsonPath);
                var found = text == null ? null : FromPackageJson(packageJsonPath, text);
                if (found != null) return found;
            }
            // package.json was deleted: lilToon's main shader still shows it is there.
            var shader = shaderPath();
            return UnderAssets(shader) ? new LilToonInstallation(DependencyPolicy.LilToonUnknownVersion, ShaderRoot(shader)) : null;
        }

        // A file with this GUID that names another package is not lilToon.
        // An unreadable one still is: only the version is unknown.
        public static LilToonInstallation FromPackageJson(string assetPath, string text)
        {
            Manifest manifest = null;
            try { manifest = JsonUtility.FromJson<Manifest>(text); }
            catch (ArgumentException) { }
            if (!string.IsNullOrEmpty(manifest?.name) && !string.Equals(manifest.name, PackageName, StringComparison.Ordinal)) return null;
            var version = string.IsNullOrEmpty(manifest?.version) ? DependencyPolicy.LilToonUnknownVersion : manifest.version;
            return new LilToonInstallation(version, Folder(assetPath));
        }

        // Unreadable counts as present with no version, so it reads as empty.
        static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return ""; }
        }

        public string Describe(Func<string, string> translate) => DependencyPolicy.DescribeLilToon(Version, AssetsFolder, translate);

        // The diagnostics copy is read by developers; it is not translated.
        public string Report() => AssetsFolder == null ? Version : Version + " (Assets: " + AssetsFolder + ")";

        static bool UnderAssets(string path) => !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);

        static string Folder(string assetPath) => Path.GetDirectoryName(assetPath).Replace('\\', '/');

        // lilToon keeps its shaders in <root>/Shader; report the root, as package.json does.
        static string ShaderRoot(string shaderPath)
        {
            var folder = Folder(shaderPath);
            return folder != "Assets/Shader" && folder.EndsWith("/Shader", StringComparison.Ordinal)
                ? folder.Substring(0, folder.Length - "/Shader".Length) : folder;
        }
    }
}
