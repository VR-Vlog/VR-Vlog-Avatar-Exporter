using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // With the real NDMF: every unsaved object the copy references is saved into
    // the temporary folder, and deleting the folder destroys what is saved there.
    public sealed class NdmfSharedObjectTests
    {
        const string PluginType = "VRVlog.LilToonExporter.Tests.SharedObjectTestPlugin";

        static Type InstalledType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        static readonly HideFlags[] Flags = { HideFlags.None, HideFlags.DontSave, HideFlags.HideAndDontSave };

        [Test]
        public void ObjectsAToolKeepsAcrossBuildsSurviveEveryExport()
        {
            var plugin = InstalledType(PluginType);
            var descriptorType = InstalledType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var proxyType = InstalledType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (plugin == null || descriptorType == null || proxyType == null)
                Assert.Ignore("Install the real VRChat SDK, Modular Avatar and NDMF to run their real processing pipeline.");

            var textures = Flags.Select(flags => new Texture2D(2, 2) { name = "Shared texture " + flags, hideFlags = flags }).ToArray();
            var materials = Flags.Select((flags, index) => new Material(Shader.Find("Unlit/Texture"))
                { name = "Shared material " + flags, hideFlags = flags, mainTexture = textures[index] }).ToArray();
            var mesh = new Mesh { name = "Shared mesh", hideFlags = HideFlags.DontSave };
            var folder = "Assets/__VRVlogSharedObjectTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            plugin.GetField("Materials").SetValue(null, materials);
            plugin.GetField("Mesh").SetValue(null, mesh);
            plugin.GetField("Armed").SetValue(null, true);
            // Unity logs an internal assertion when NDMF saves a DontSave object.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                using var fixture = new AttachmentConnectionTests.Fixture();
                var source = fixture.Source;
                var descriptor = source.AddComponent(descriptorType);
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Base.controller");
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers");
                    layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0);
                    var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var proxy = source.transform.Find("Back").gameObject.AddComponent(proxyType);
                proxyType.GetField("boneReference").SetValue(proxy, HumanBodyBones.Head);
                var attachment = proxyType.GetField("attachmentMode");
                attachment.SetValue(proxy, Enum.Parse(attachment.FieldType, "AsChildKeepWorldPose"));

                for (var export = 0; export < 2; export++)
                {
                    var copy = Object.Instantiate(source);
                    try
                    {
                        using (NdmfExportPreparation.Prepare(source, copy))
                        {
                            // Without this the test would not reach the deletion it guards against.
                            Assert.That(materials.All(EditorUtility.IsPersistent), Is.True, "NDMF no longer saves the shared materials");
                            Assert.That(copy.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials, Is.EqualTo(materials));
                        }
                        Assert.That(copy.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials, Is.EqualTo(materials));
                    }
                    finally { Object.DestroyImmediate(copy); }

                    for (var index = 0; index < Flags.Length; index++)
                    {
                        Assert.That(materials[index] != null, Is.True, "Destroyed: " + Flags[index] + " material, export " + export);
                        Assert.That(textures[index] != null, Is.True, "Destroyed: " + Flags[index] + " texture, export " + export);
                        Assert.That(EditorUtility.IsPersistent(materials[index]), Is.False);
                        Assert.That(EditorUtility.IsPersistent(textures[index]), Is.False);
                        Assert.That(materials[index].hideFlags, Is.EqualTo(Flags[index]));
                        Assert.That(textures[index].hideFlags, Is.EqualTo(Flags[index]));
                        Assert.That(materials[index].mainTexture.GetInstanceID(), Is.EqualTo(textures[index].GetInstanceID()));
                    }
                    Assert.That(mesh != null, Is.True, "Destroyed: mesh, export " + export);
                    Assert.That(EditorUtility.IsPersistent(mesh), Is.False);
                }
                Assert.That(AssetDatabase.GetSubFolders("Assets").Where(path =>
                    NdmfExportPreparation.IsTemporaryAssetFolderName(path.Substring("Assets/".Length))), Is.Empty);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                plugin.GetField("Armed").SetValue(null, false);
                plugin.GetField("Materials").SetValue(null, null);
                plugin.GetField("Mesh").SetValue(null, null);
                foreach (var value in materials.Cast<Object>().Concat(textures).Append(mesh))
                    if (value != null) Object.DestroyImmediate(value, true);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
