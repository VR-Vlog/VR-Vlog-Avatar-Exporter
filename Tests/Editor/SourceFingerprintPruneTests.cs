using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The expression board stops saving when the source avatar changed after
    // its copy was made. Values that never reach the VRM must not stop it;
    // values that do must still stop it.
    public sealed class SourceFingerprintPruneTests
    {
        [Test]
        public void MovingTheRootInTheSceneKeepsTheBoardCurrentButItsScaleDoesNot()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var parent = new GameObject("Folder");
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                fixture.Source.transform.position = new Vector3(3, 0, -2);
                fixture.Source.transform.rotation = Quaternion.Euler(0, 140, 0);
                Assert.That(session.HasCurrentSource, Is.True, "The copy is exported at the origin facing forward.");
                parent.transform.position = Vector3.left * 5;
                fixture.Source.transform.SetParent(parent.transform, true);
                Assert.That(session.HasCurrentSource, Is.True, "A parent with no scale only places the avatar.");
                parent.transform.localScale = Vector3.one * 1.5f;
                Assert.That(session.HasCurrentSource, Is.False, "The root's world scale is moved into the VRM.");
            }
            finally { if (fixture.Source != null) fixture.Source.transform.SetParent(null, true); Object.DestroyImmediate(parent); }
        }

        [TestCase("mesh")][TestCase("material")][TestCase("texture")][TestCase("active")][TestCase("blendShape")][TestCase("bone")][TestCase("rootScale")]
        public void ChangesThatReachTheFileStillStopTheBoard(string change)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.white, 4).ToArray()); texture.Apply();
                front.sharedMaterial.SetTexture("_MainTex", texture);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.HasCurrentSource, Is.True);
                switch (change)
                {
                    case "mesh": fixture.Mesh.vertices = fixture.Mesh.vertices.Select(v => v + Vector3.up * .001f).ToArray(); break;
                    case "material": front.sharedMaterial.SetColor("_Color", new Color(1, .5f, .5f, 1)); break;
                    case "texture": texture.SetPixel(0, 0, Color.red); texture.Apply(); break;
                    case "active": front.gameObject.SetActive(false); break;
                    case "blendShape": front.SetBlendShapeWeight(0, 80); break;
                    case "bone": fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head).localPosition += Vector3.forward * .001f; break;
                    case "rootScale": fixture.Source.transform.localScale = Vector3.one * 1.1f; break;
                }
                Assert.That(session.HasCurrentSource, Is.False, change);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void PartialVrcRotationNoiseKeepsTheBoardCurrent()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint", false)).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("The VRC constraint SDK is not installed.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var anchor = new GameObject("Authored input").transform; anchor.SetParent(fixture.Source.transform, false);
            var target = new GameObject("Partially driven target").transform; target.SetParent(fixture.Source.transform, false);
            var component = target.gameObject.AddComponent(type);
            using (var settings = new SerializedObject(component))
            {
                settings.FindProperty("IsActive").boolValue = true; settings.FindProperty("Locked").boolValue = true;
                settings.FindProperty("GlobalWeight").floatValue = .5f; settings.FindProperty("FreezeToWorld").boolValue = false;
                settings.FindProperty("AffectsRotationX").boolValue = false; settings.FindProperty("AffectsRotationY").boolValue = true; settings.FindProperty("AffectsRotationZ").boolValue = false;
                settings.FindProperty("Sources.totalLength").intValue = 1;
                settings.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = anchor;
                settings.FindProperty("Sources.source0.Weight").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            target.localRotation = Quaternion.Euler(10, 20, 30);
            var original = target.localRotation;
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var reencoded = original;
            reencoded.x = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(reencoded.x) + 1);
            target.localRotation = reencoded;
            Assert.That(session.HasCurrentSource, Is.True, "The solver re-encoding a partial rotation is not an edit.");
            target.localRotation = original * Quaternion.Euler(.001f, 0, 0);
            Assert.That(session.HasCurrentSource, Is.False, "A real rotation change still stops the board.");
        }

        [TestCase("rotation", true)][TestCase("aim", false)]
        public void RootRotationIsAnInputOnlyForAnAimWithASceneUp(string kind, bool current)
        {
            var source = new GameObject("Constraint root");
            try
            {
                var anchor = new GameObject("Authored source").transform; anchor.SetParent(source.transform, false); anchor.localPosition = Vector3.forward;
                var target = new GameObject("Driven target").transform; target.SetParent(source.transform, false);
                IConstraint constraint = kind == "aim" ? (IConstraint)target.gameObject.AddComponent<AimConstraint>() : target.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = anchor, weight = 1 });
                constraint.weight = 1; constraint.locked = true; constraint.constraintActive = true;
                if (constraint is AimConstraint aim) { aim.rotationAxis = Axis.X | Axis.Y | Axis.Z; aim.worldUpType = AimConstraint.WorldUpType.SceneUp; }
                else ((RotationConstraint)constraint).rotationAxis = Axis.X | Axis.Y | Axis.Z;
                var stamp = ExportRecoverySourceStamp.Capture(source);
                source.transform.rotation = Quaternion.Euler(20, 0, 0);
                Assert.That(stamp.Matches(source), Is.EqualTo(current), "Root rotation is hashed only when a scene-up aim reads it.");
            }
            finally { Object.DestroyImmediate(source); }
        }

        // The board exports its copy, which stays where the source was, so an
        // input outside the avatar counts in world space; an export from the
        // source counts it relative to the root. Either move stops.
        [TestCase("together")][TestCase("avatar")][TestCase("input")]
        public void ExternalConstraintInputKeepsThePlacementAsAnInput(string move)
        {
            var source = new GameObject("Constraint root");
            var world = new GameObject("World");
            var external = new GameObject("External input");
            try
            {
                source.transform.SetParent(world.transform, false); external.transform.SetParent(world.transform, false);
                external.transform.localPosition = Vector3.forward;
                var target = new GameObject("Driven target").transform; target.SetParent(source.transform, false);
                var constraint = target.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = external.transform, weight = 1 });
                constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z; constraint.weight = 1; constraint.locked = true; constraint.constraintActive = true;
                var stamp = ExportRecoverySourceStamp.Capture(source);
                switch (move)
                {
                    case "together": world.transform.position = new Vector3(4, 0, 1); world.transform.rotation = Quaternion.Euler(0, 90, 0); break;
                    case "avatar": source.transform.localPosition = Vector3.right; break;
                    case "input": external.transform.localPosition = Vector3.back; break;
                }
                Assert.That(stamp.Matches(source), Is.False, move);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(external); Object.DestroyImmediate(world); }
        }

        [Test]
        public void ShaderFileChangesAreNotInputsButMaterialValuesAre()
        {
            var folder = "Assets/VRVlogFingerprintPrune-" + Guid.NewGuid().ToString("N");
            var source = new GameObject("Shader source");
            Material material = null;
            try
            {
                Directory.CreateDirectory(folder);
                var path = folder + "/Probe.shader";
                const string text = "Shader \"Hidden/VRVlogFingerprintPruneProbe\" { Properties { _Color (\"Color\", Color) = (1,1,1,1) } SubShader { Pass { } } }\n";
                File.WriteAllText(path, text); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                source.AddComponent<MeshRenderer>().sharedMaterial = material;
                var stamp = ExportRecoverySourceStamp.Capture(source);
                var before = AssetDatabase.GetAssetDependencyHash(path);
                File.WriteAllText(path, text + "// regenerated\n"); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.Not.EqualTo(before), "The test must change the shader file.");
                Assert.That(AssetDatabase.GetAssetPath(material.shader), Is.EqualTo(path));
                Assert.That(stamp.Matches(source), Is.True, "The shader file is not written to the VRM.");
                material.SetColor("_Color", Color.red);
                Assert.That(stamp.Matches(source), Is.False, "The material value is written to the VRM.");
            }
            finally
            {
                Object.DestroyImmediate(source); if (material != null) Object.DestroyImmediate(material);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
