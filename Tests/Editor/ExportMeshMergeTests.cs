using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Which renderers the automatic merge for AAO takes: the ones no
    // animation switches and that hold no shape the app drives by name.
    public sealed class ExportMeshMergeTests
    {
        sealed class Avatar : System.IDisposable
        {
            internal readonly GameObject Root = new GameObject("Avatar");
            readonly List<Object> owned = new List<Object>();
            internal AnimationClip Clip;

            internal SkinnedMeshRenderer Skin(string path, params string[] shapes)
            {
                var parent = Root.transform;
                var names = path.Split('/');
                foreach (var name in names)
                {
                    var child = parent.Find(name);
                    if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
                    parent = child;
                }
                var mesh = new Mesh { name = path, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
                owned.Add(mesh);
                var skin = parent.gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                return skin;
            }

            // Plays one clip from the root's Animator, as a VRChat playable
            // layer does, so paths start at the avatar root.
            internal void Curve(string path, System.Type type, string property)
            {
                if (Clip == null)
                {
                    Clip = new AnimationClip { name = "Toggles" };
                    var controller = new AnimatorController { name = "FX" };
                    controller.AddLayer("Base");
                    controller.layers[0].stateMachine.AddState("On").motion = Clip;
                    owned.Add(Clip); owned.Add(controller); owned.Add(controller.layers[0].stateMachine);
                    Root.AddComponent<Animator>().runtimeAnimatorController = controller;
                }
                if (property == "m_Materials.Array.data[0]")
                    AnimationUtility.SetObjectReferenceCurve(Clip, EditorCurveBinding.PPtrCurve(path, type, property),
                        new[] { new ObjectReferenceKeyframe { time = 0, value = null } });
                else AnimationUtility.SetEditorCurve(Clip, EditorCurveBinding.FloatCurve(path, type, property), AnimationCurve.Constant(0, 1, 1));
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            }
        }

        static string[] Names(IEnumerable<Renderer> renderers) => renderers.Select(renderer => renderer.name).ToArray();

        [Test]
        public void TheFaceAndEveryRendererAnAnimationSwitchesStaySeparate()
        {
            using var avatar = new Avatar();
            avatar.Skin("Body", "Hair detail");
            avatar.Skin("Hair");
            avatar.Skin("Face", "Blink", "vrc.v_aa");
            avatar.Skin("Hat");
            avatar.Skin("Outfit/Skirt");
            avatar.Skin("Gloves");
            avatar.Skin("Shoes");
            avatar.Skin("Ribbon", "Ribbon size");
            avatar.Curve("Hat", typeof(GameObject), "m_IsActive");
            // An ancestor switched on or off takes every renderer below it.
            avatar.Curve("Outfit", typeof(GameObject), "m_IsActive");
            avatar.Curve("Gloves", typeof(SkinnedMeshRenderer), "m_Enabled");
            avatar.Curve("Shoes", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]");
            // A shape the avatar animates still merges: AAO follows the rename.
            avatar.Curve("Ribbon", typeof(SkinnedMeshRenderer), "blendShape.Ribbon size");

            Assert.That(Names(ExportMeshMerge.Sources(avatar.Root, null)), Is.EqualTo(new[] { "Body", "Hair", "Ribbon" }));
        }

        [Test]
        public void ShapesTheExporterGeneratedAndRoutedRenderersStaySeparate()
        {
            using var avatar = new Avatar();
            avatar.Skin("Body");
            avatar.Skin("Hair");
            avatar.Skin("Tail");
            avatar.Skin("Mouth", "__VRVlog_Blink_0");
            var routed = avatar.Skin("Eyes", "Eye detail");
            Assert.That(Names(ExportMeshMerge.Sources(avatar.Root, new Renderer[] { routed })), Is.EqualTo(new[] { "Body", "Hair", "Tail" }));
        }

        [Test]
        public void HiddenClothAndEmptyRenderersAreNotMerged()
        {
            using var avatar = new Avatar();
            avatar.Skin("Body");
            avatar.Skin("Hair");
            avatar.Skin("Hidden").gameObject.SetActive(false);
            avatar.Skin("Disabled").enabled = false;
            avatar.Skin("Empty").sharedMesh = null;
            avatar.Skin("Cape").gameObject.AddComponent<Cloth>();
            Assert.That(Names(ExportMeshMerge.Sources(avatar.Root, null)), Is.EqualTo(new[] { "Body", "Hair" }));
        }

        [Test]
        public void OneRendererLeftIsNotAMerge()
        {
            using var avatar = new Avatar();
            avatar.Skin("Body");
            avatar.Skin("Face", "Blink");
            Assert.That(ExportMeshMerge.Sources(avatar.Root, null), Is.Empty);
        }

        [Test]
        public void AnAvatarWithoutAvatarOptimizerIsNotMerged()
        {
            using var avatar = new Avatar();
            avatar.Skin("Body");
            avatar.Skin("Hair");
            Assert.That(ExportMeshMerge.UsesAvatarOptimizer(avatar.Root), Is.False);
            Assert.That(ExportMeshMerge.Apply(avatar.Root, null), Is.False);
            Assert.That(avatar.Root.transform.Find(ExportMeshMerge.MergedName), Is.Null);
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var texts = new[] { ExperimentalExpressionCaptureWindow.MergeMeshesLabel, ExperimentalExpressionCaptureWindow.MergeMeshesNote, "メッシュの自動結合" };
            foreach (var text in texts)
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S|[ぁ-んァ-ヶ一-龠] [A-Za-z]|[A-Za-z] [ぁ-んァ-ヶ一-龠]"), Is.False, text);
            Assert.That(ExportStageGuard.Label(ExportStage.MeshMerge), Is.EqualTo("メッシュの自動結合"));
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var table = File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var text in texts) Assert.That(table, Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale + ": " + text);
            }
        }
    }
}
