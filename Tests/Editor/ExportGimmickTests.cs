using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportGimmickTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject avatar;
        Material hidden, normal, unspecified;
        string controllerFolder;

        [SetUp] public void SetUp()
        {
            avatar = Own(new GameObject("avatar"));
            hidden = Own(new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback")));
            normal = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToon")));
            unspecified = Own(new Material(Shader.Find("Hidden/VRVlogTests/ShadowSphere")));
        }
        [TearDown] public void TearDown()
        {
            for (var i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (controllerFolder != null) AssetDatabase.DeleteAsset(controllerFolder);
            controllerFolder = null;
        }
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent ?? avatar.transform, false); return go;
        }
        Mesh Mesh()
        {
            var mesh = Own(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
            mesh.RecalculateNormals();
            mesh.AddBlendShapeFrame("Smile", 100, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
            return mesh;
        }
        MeshRenderer Render(string name, Material material, Transform parent = null)
        {
            var go = Child(name, parent); go.AddComponent<MeshFilter>().sharedMesh = Mesh();
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        SkinnedMeshRenderer Skin(string name, Material material)
        {
            var renderer = Child(name).AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = Mesh(); renderer.sharedMaterial = material; return renderer;
        }

        [Test] public void EffectiveHiddenTagRemovesOnlyUnsupportedMaterials()
        {
            Assert.That(ExportGimmickDetection.Inspect(Render("effect", hidden)).Unit, Is.EqualTo(GimmickExclusionUnit.Renderer));
            normal.SetOverrideTag("VRCFallback", "Hidden");
            Assert.That(ExportGimmickDetection.Inspect(Render("clothing", normal)), Is.Null);
            hidden.SetOverrideTag("VRCFallback", "Toon");
            Assert.That(ExportGimmickDetection.Inspect(Render("authored override", hidden)).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
        }

        [Test] public void NamesBlackColorAndShapeDoNotIdentifyAGimmick()
        {
            Assert.That(ExportGimmickDetection.Inspect(Render("NadeShadow", unspecified)).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            var fake = Child("NadeSystem"); Child("HeadSystem", fake.transform); Child("Proxies", fake.transform);
            Assert.That(ExportGimmickDetection.KnownNadeRoot(fake), Is.False);
            Assert.That(ExportGimmickDetection.Analyze(avatar).Any(f => f.Unit == GimmickExclusionUnit.Hierarchy), Is.False);
        }

        [Test] public void MixedMissingAndUndersuppliedSlotsRemainIncluded()
        {
            var renderer = Render("mixed", hidden);
            renderer.sharedMaterials = new[] { hidden, normal };
            Assert.That(ExportGimmickDetection.Inspect(renderer).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            renderer.sharedMaterials = new Material[] { hidden, null };
            Assert.That(ExportGimmickDetection.Inspect(renderer).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            renderer.sharedMaterials = new[] { hidden };
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh; mesh.subMeshCount = 2;
            Assert.That(ExportGimmickDetection.Inspect(renderer).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            renderer.sharedMaterials = new[] { normal };
            Assert.That(ExportGimmickDetection.Inspect(renderer).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
        }

        [Test] public void MeshRendererWithoutMeshFilterIsReviewedAsMissingMesh()
        {
            // In the Editor, GetComponent returns a fake null for the absent
            // MeshFilter; ?. on it threw MissingComponentException.
            var renderer = Child("MeshRendererOnly").AddComponent<MeshRenderer>(); renderer.sharedMaterial = hidden;
            List<ExportGimmickFinding> findings = null;
            Assert.DoesNotThrow(() => findings = ExportGimmickDetection.Analyze(avatar));
            var finding = findings.Single(f => f.Renderer == renderer);
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(finding.Reason, Is.EqualTo("メッシュまたは材質が不足しているため自動判定できません。"));
            Assert.That(ExportGimmickDetection.PettingSystemUnsafeReason(avatar, renderer.transform, _ => false),
                Is.EqualTo("メッシュまたは材質が不足しているため、システム全体は残します。"));
        }

        [Test] public void InactiveAndDisabledRenderersAreNotCandidates()
        {
            var renderer = Render("effect", hidden); renderer.enabled = false;
            Assert.That(ExportGimmickDetection.Inspect(renderer), Is.Null);
            renderer.enabled = true; renderer.gameObject.SetActive(false);
            Assert.That(ExportGimmickDetection.Inspect(renderer), Is.Null);
        }

        [Test] public void RemovingRendererKeepsItsTransformChildAndReferencedBone()
        {
            var effect = Render("effect", hidden);
            Render("accessory", normal, effect.transform);
            var skin = Skin("body", normal); skin.bones = new[] { effect.transform }; skin.rootBone = effect.transform;
            var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, null);
            session.Apply(null, null, new List<string>());
            var kept = clone.transform.Find("effect");
            Assert.That(kept, Is.Not.Null);
            Assert.That(kept.GetComponent<Renderer>(), Is.Null);
            Assert.That(kept.Find("accessory").GetComponent<Renderer>(), Is.Not.Null);
            Assert.That(clone.transform.Find("body").GetComponent<SkinnedMeshRenderer>().bones[0], Is.SameAs(kept));
            Assert.That(effect.GetComponent<Renderer>(), Is.SameAs(effect));
            Assert.That(effect.sharedMaterial, Is.SameAs(hidden));
        }

        [TestCase(true)]
        public void RemovedAuxiliaryRendererDoesNotExcludeRetainedOrMovedFaceMorphs(bool moveToOldPath)
        {
            avatar.AddComponent<Animator>();
            var face = Skin(moveToOldPath ? "originalFace" : "Face", normal);
            // Keep all animation paths unique. The moved case reuses the
            // auxiliary's old path after renaming its retained Transform.
            var auxiliary = Render(moveToOldPath ? "Face" : "auxiliary", hidden);
            var folderName = "__GimmickDefaults_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); controllerFolder = "Assets/" + folderName;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerFolder + "/FX.controller");
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Smile"),
                AnimationCurve.Constant(0, 1, 60));
            AssetDatabase.AddObjectToAsset(clip, controller);
            var state = controller.layers[0].stateMachine.AddState("Always"); state.motion = clip; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, null);
            session.Apply(null, null, null);
            if (moveToOldPath)
            {
                clone.transform.Find("Face").name = "removedAuxiliary";
                clone.transform.Find("originalFace").name = "Face";
            }
            Assert.That(session.ContainsPath("Face"), Is.False);
            var defaults = VrChatExpressionSampler.SampleDefaults(clone, controller, new Dictionary<string, float>(), session.ContainsPath);
            Assert.That(defaults.Single(value => value.Shape == "Smile").Weight, Is.EqualTo(60).Within(.01));
            Assert.That(face.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(auxiliary, Is.Not.Null);
            Assert.That(face.sharedMesh.blendShapeCount, Is.EqualTo(1));
        }

        [Test] public void DuplicateNamesUseIdentityAndManualExclusionWins()
        {
            var first = Render("same", hidden); var second = Render("same", hidden);
            var clone = Own(Object.Instantiate(avatar));
            var options = new ExportGimmickOptions { IncludedObjects = new[] { second.gameObject } };
            using var session = new ExportGimmickSession(avatar, clone, options);
            using var exclusions = new ExportObjectExclusions(avatar, new[] { second.gameObject });
            exclusions.Apply(clone, null); session.Apply(null, null, null);
            Assert.That(clone.transform.childCount, Is.EqualTo(1));
            Assert.That(clone.GetComponentsInChildren<Renderer>(), Is.Empty);
            Assert.That(avatar.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(2));
        }

        [Test] public void IncludeSurvivesRenameReparentAndSecondPassFindsGeneratedRenderer()
        {
            var first = Render("same", hidden); var second = Render("same", hidden);
            var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, new ExportGimmickOptions { IncludedObjects = new[] { second.gameObject } });
            var kept = clone.transform.GetChild(1); kept.name = "moved"; kept.SetParent(clone.transform.GetChild(0), false);
            session.Apply(null, null, null);
            Assert.That(kept.GetComponent<Renderer>(), Is.Not.Null);
            var generated = Render("generated", hidden, clone.transform);
            session.Apply(null, null, null);
            Assert.That(generated == null, Is.True);
            Assert.That(kept.GetComponent<Renderer>(), Is.Not.Null);
        }

        [Test] public void DisabledAutomaticOptionPreservesAllRenderers()
        {
            Render("effect", hidden); var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, new ExportGimmickOptions { AutoExclude = false });
            session.Apply(null, null, null);
            Assert.That(clone.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(1));
        }

        [Test] public void UniVrmDoesNotSerializeRemovedGeometryOrItsMaterial()
        {
            Render("effect", hidden); Render("body", normal);
            var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, null);
            session.Apply(null, null, null);
            using var arrays = new VRVlog.LilToonExporter.Vendor.UniGLTF.NativeArrayManager();
            var exporter = new ModelExporter();
            var model = exporter.Export(new VRVlog.LilToonExporter.Vendor.UniGLTF.GltfExportSettings(), arrays, clone);
            Assert.That(model.MeshGroups.Count, Is.EqualTo(1));
            Assert.That(exporter.Materials, Is.EquivalentTo(new[] { normal }));
            Assert.That(clone.transform.Find("effect"), Is.Not.Null);
        }

        [Test] public void PreparedMaterialChangeIsReevaluatedInsteadOfTrustingPreview()
        {
            Render("effect", hidden); var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, null);
            clone.GetComponentInChildren<Renderer>().sharedMaterial = normal;
            session.Apply(null, null, null);
            Assert.That(clone.GetComponentInChildren<Renderer>(), Is.Not.Null);
        }

        [Test] public void RootWithOrdinaryClothingOrIncomingReferenceIsNotRemoved()
        {
            var root = Child("system"); var effect = Render("effect", hidden, root.transform);
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Null);
            var clothing = Render("clothing", normal, root.transform); clothing.gameObject.SetActive(false);
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
            Object.DestroyImmediate(clothing.gameObject);
            var skin = Skin("body", normal); skin.bones = new[] { effect.transform };
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, t => t == skin.transform), Is.Null);
        }

        [TestCase("ModularAvatarMergeArmature", "mergeTarget.referencePath")]
        [TestCase("ModularAvatarBoneProxy", "subPath")]
        public void MaPathBasedConnectionsProtectTheHierarchy(string componentName, string pathProperty)
        {
            var type = FindType("nadena.dev.modular_avatar.core." + componentName);
            var descriptor = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (type == null || descriptor == null) Assert.Ignore("Installed MA and VRChat SDK are required for this connection regression.");
            avatar.AddComponent(descriptor);
            var root = Child("system"); var effect = Render("effect", hidden, root.transform);
            var wardrobe = Render("wardrobe", normal).gameObject;
            var component = wardrobe.AddComponent(type);
            using (var serialized = new SerializedObject(component))
            {
                serialized.FindProperty(pathProperty).stringValue = "system/effect";
                var direct = serialized.FindProperty("mergeTarget.targetObject");
                if (direct != null) direct.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(NdmfExportPreparation.FollowingTarget(component), Is.SameAs(effect.transform));
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, t => t == wardrobe.transform), Is.Null);
        }

        [Test] public void MissingMeshOrMaterialSlotsProtectTheHierarchy()
        {
            var root = Child("system"); var renderer = Render("effect", hidden, root.transform);
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            mesh.subMeshCount = 2;
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
            renderer.GetComponent<MeshFilter>().sharedMesh = null;
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
        }

        [Test] public void UnrecognizedAnimatorControllerProtectsTheHierarchy()
        {
            var root = Child("system"); Render("effect", hidden, root.transform);
            var animator = root.AddComponent<Animator>();
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Null);
            animator.runtimeAnimatorController = Own(new UnityEditor.Animations.AnimatorController());
            Assert.That(ExportGimmickDetection.UnsafeRootReason(avatar, root.transform, _ => false), Is.Not.Null);
        }

        [Test] public void MixedFacialExpressionDropsOnlyRemovedRendererBindings()
        {
            Skin("body", normal); Skin("effect", hidden);
            var menu = new VrChatExpressionMenu.Source();
            var mixed = new VrChatExpressionMenu.Entry { Name = "mixed" };
            mixed.Values.Add(new VrChatExpressionMenu.MorphValue { Path = "body", Shape = "Smile", Weight = 50 });
            mixed.Values.Add(new VrChatExpressionMenu.MorphValue { Path = "effect", Shape = "Smile", Weight = 50 });
            menu.Entries.Add(mixed);
            var only = new VrChatExpressionMenu.Entry { Name = "only effect" };
            only.Values.Add(new VrChatExpressionMenu.MorphValue { Path = "effect", Shape = "Smile", Weight = 50 }); menu.Entries.Add(only);
            var clone = Own(Object.Instantiate(avatar));
            var bindings = new PreparedExpressionBindings(clone, menu);
            using var session = new ExportGimmickSession(avatar, clone, null);
            session.Apply(bindings, menu, null); bindings.Capture(menu);
            Assert.That(mixed.Error, Is.Null);
            Assert.That(mixed.Values.Single().Path, Is.EqualTo("body"));
            Assert.That(only.Error, Is.Not.Null);
        }

        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        static ExportGimmickRule AnchorRule(params string[] linked) => new ExportGimmickRule
        {
            Id = "test-anchor", Reason = "test anchor", AnchorTypes = new[] { typeof(GimmickSettingsProbe).FullName },
            DedicatedTypes = new[] { "BoxCollider" }, LinkedObjectProperties = linked
        };

        List<ExportGimmickFinding> Matched(params ExportGimmickRule[] rules) =>
            ExportGimmickDetection.Analyze(avatar, rules, null).Where(f => f.RuleId != null).ToList();

        [Test] public void EveryTableRuleNeedsEvidenceBeyondNames()
        {
            var rules = ExportGimmickRules.All;
            Assert.That(rules.Select(rule => rule.Id).Distinct().Count(), Is.EqualTo(rules.Count));
            Assert.That(rules.All(rule => rule.HasEvidence && !string.IsNullOrEmpty(rule.Reason)), Is.True);
            var namesOnly = new ExportGimmickRule { Id = "names", Reason = "names", RequiredChildren = new[] { "HeadSystem" } };
            Child("HeadSystem", Child("system").transform);
            Assert.That(Matched(namesOnly), Is.Empty);
        }

        [Test] public void DedicatedSettingsObjectLeavesWholeOtherwiseOnlyTheComponent()
        {
            var settings = Child("settings"); var probe = settings.AddComponent<GimmickSettingsProbe>();
            settings.AddComponent<BoxCollider>();
            settings.SetActive(false);
            var finding = Matched(AnchorRule()).Single();
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            Assert.That(finding.Target, Is.SameAs(settings));
            Assert.That(finding.RuleId, Is.EqualTo("test-anchor"));
            // Kept for review, the object still loses its settings component.
            var skin = Skin("body", normal); skin.bones = new[] { settings.transform };
            var kept = Matched(AnchorRule());
            Assert.That(kept.Select(f => f.Unit), Is.EquivalentTo(new[] { GimmickExclusionUnit.Review, GimmickExclusionUnit.Component }));
            Assert.That(kept.Single(f => f.Unit == GimmickExclusionUnit.Component).Components, Is.EqualTo(new Component[] { probe }));
            Object.DestroyImmediate(skin.gameObject);
            Child("part", settings.transform);
            finding = Matched(AnchorRule()).Single();
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Component));
            Assert.That(finding.Components, Is.EqualTo(new Component[] { probe }));
            Object.DestroyImmediate(settings);
            var rootProbe = avatar.AddComponent<GimmickSettingsProbe>();
            finding = Matched(AnchorRule()).Single();
            Assert.That(finding.Target, Is.SameAs(avatar));
            Assert.That(finding.Components, Is.EqualTo(new Component[] { rootProbe }));
        }

        [Test] public void SettingsComponentIsRemovedFromTheCopyOnly()
        {
            var probe = avatar.AddComponent<GimmickSettingsProbe>(); Render("clothing", normal);
            var other = Child("other settings"); other.AddComponent<GimmickSettingsProbe>(); other.AddComponent<BoxCollider>();
            var findings = Matched(AnchorRule());
            Assert.That(findings.Count, Is.EqualTo(2));
            var clone = Own(Object.Instantiate(avatar));
            using (var session = new ExportGimmickSession(avatar, clone, null, findings)) session.Apply(null, null, null);
            Assert.That(clone.GetComponent<GimmickSettingsProbe>(), Is.Null);
            Assert.That(clone.GetComponentInChildren<MeshRenderer>(), Is.Not.Null);
            Assert.That(avatar.GetComponent<GimmickSettingsProbe>(), Is.Not.Null);
            // Keeping the root's component keeps that component only; the
            // other gimmick still leaves.
            var keep = new ExportGimmickOptions { IncludedComponents = new Component[] { probe } };
            Assert.That(ExportGimmickDetection.AutomaticRoots(findings, keep), Is.EqualTo(new[] { other }));
            Assert.That(ExportGimmickDetection.AutomaticComponents(findings, keep), Is.Empty);
            var kept = Own(Object.Instantiate(avatar));
            using (var session = new ExportGimmickSession(avatar, kept, keep, findings))
                session.Apply(null, null, null);
            Assert.That(kept.GetComponent<GimmickSettingsProbe>(), Is.Not.Null);
            var disabled = Own(Object.Instantiate(avatar));
            using (var session = new ExportGimmickSession(avatar, disabled, new ExportGimmickOptions { AutoExclude = false }, findings))
                session.Apply(null, null, null);
            Assert.That(disabled.GetComponent<GimmickSettingsProbe>(), Is.Not.Null);
        }

        [Test] public void SettingsComponentIsFoundWhenTheCopyDroppedAnEarlierComponent()
        {
            // The export copy drops missing scripts and edit-mode behaviours,
            // which moves every later component up by one.
            avatar.AddComponent<BoxCollider>();
            avatar.AddComponent<GimmickSettingsProbe>();
            var findings = Matched(AnchorRule());
            var clone = Own(Object.Instantiate(avatar));
            Object.DestroyImmediate(clone.GetComponent<BoxCollider>());
            using (var session = new ExportGimmickSession(avatar, clone, null, findings)) session.Apply(null, null, null);
            Assert.That(clone.GetComponent<GimmickSettingsProbe>(), Is.Null);
            Assert.That(avatar.GetComponent<GimmickSettingsProbe>(), Is.Not.Null);
        }

        [Test] public void LinkedGimmickObjectsLeaveTogetherAndAvatarPartsStay()
        {
            var probe = Child("settings").AddComponent<GimmickSettingsProbe>();
            var camera = Child("camera"); Child("lens", camera.transform);
            var toucher = Child("toucher", Child("hand").transform);
            var clothing = Render("clothing", normal).gameObject;
            probe.Linked = camera; probe.LinkedList = new[] { toucher, camera.transform.GetChild(0).gameObject }; probe.AvatarPart = clothing;
            var findings = Matched(AnchorRule("Linked", "LinkedList"));
            var roots = ExportGimmickDetection.AutomaticRoots(findings, null).ToArray();
            Assert.That(roots, Is.EquivalentTo(new[] { probe.gameObject, camera, toucher }));
            Assert.That(findings.All(f => f.Group == probe.gameObject), Is.True);
            // Keeping one part keeps the whole gimmick, so nothing half-works.
            Assert.That(ExportGimmickDetection.AutomaticRoots(findings, new ExportGimmickOptions { IncludedObjects = new[] { toucher } }), Is.Empty);
            // A linked object that an avatar part still uses stays.
            var skin = Skin("body", normal); skin.bones = new[] { toucher.transform };
            findings = Matched(AnchorRule("Linked", "LinkedList"));
            Assert.That(findings.Single(f => f.Target == toucher).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(ExportGimmickDetection.AutomaticRoots(findings, null), Is.EquivalentTo(new[] { probe.gameObject, camera }));
        }

        GameObject RenderTextureLens(Transform parent = null)
        {
            var lens = Child("lens", parent); lens.AddComponent<Animator>();
            var texture = Own(new RenderTexture(16, 16, 0));
            // Owned after the texture so TearDown destroys the camera first.
            Own(Child("capture", lens.transform)).AddComponent<Camera>().targetTexture = texture;
            var display = Own(new Material(Shader.Find("Unlit/Texture"))); display.mainTexture = texture;
            Render("display", display, lens.transform);
            return lens;
        }

        [Test] public void CameraShownOnItsOwnMeshIsFoundByStructure()
        {
            var rule = new ExportGimmickRule { Id = "test-loop", Reason = "loop", EntryTypes = new[] { "Animator" }, RenderTextureLoop = true };
            var outfit = Child("outfit"); outfit.AddComponent<Animator>(); Render("dress", normal, outfit.transform);
            var lens = RenderTextureLens(outfit.transform);
            var finding = Matched(rule).Single();
            Assert.That(finding.Target, Is.SameAs(lens));
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            Assert.That(finding.Evidence, Is.EqualTo("structure"));
            var display = lens.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var capture = lens.GetComponentInChildren<Camera>();
            display.mainTexture = Own(new Texture2D(2, 2));
            Assert.That(Matched(rule), Is.Empty);
            display.mainTexture = capture.targetTexture;
            capture.targetTexture = null;
            Assert.That(Matched(rule), Is.Empty);
        }

        [Test] public void InactiveAvatarRootStillHasItsGimmicksFound()
        {
            var rule = new ExportGimmickRule { Id = "test-loop", Reason = "loop", EntryTypes = new[] { "Animator" }, RenderTextureLoop = true };
            var lens = RenderTextureLens(avatar.transform);
            avatar.SetActive(false);
            Assert.That(Matched(rule).Single().Target, Is.SameAs(lens));
            avatar.SetActive(true);
            lens.SetActive(false);
            Assert.That(Matched(rule), Is.Empty);
        }

        [Test] public void GroupAroundAGimmickIsNotTakenForIt()
        {
            var rule = new ExportGimmickRule { Id = "test-loop", Reason = "loop", EntryTypes = new[] { "Animator" }, RenderTextureLoop = true };
            var outfit = Child("outfit"); outfit.AddComponent<Animator>(); Render("dress", normal, outfit.transform);
            var lens = RenderTextureLens(outfit.transform);
            // A rig without its own entry: only the outfit carries the structure.
            Object.DestroyImmediate(lens.GetComponent<Animator>());
            var settings = Child("settings"); settings.AddComponent<BoxCollider>();
            settings.AddComponent<GimmickSettingsProbe>().Linked = lens;
            // The rig is claimed by its settings; the outfit around it stays.
            var findings = Matched(AnchorRule("Linked"), rule);
            Assert.That(findings.Select(f => f.Target), Is.EquivalentTo(new[] { settings, lens }));
            Assert.That(findings.All(f => f.RuleId == "test-anchor"), Is.True);
            Object.DestroyImmediate(settings);
            lens.AddComponent<Animator>();
            // A camera and a display under different entries are not one loop.
            lens.GetComponentInChildren<MeshRenderer>().transform.SetParent(outfit.transform, false);
            Assert.That(Matched(rule), Is.Empty);
        }

        [Test] public void UnpackedPettingSystemIsFoundByItsContacts()
        {
            var receiver = FindType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver");
            if (receiver == null) Assert.Ignore("Installed VRChat SDK is required for contact receivers.");
            var root = Child("renamed"); Child("HeadSystem", root.transform); Child("Proxies", root.transform);
            void Contact(string path, string parameter)
            {
                var parent = root.transform;
                foreach (var name in path.Split('/')) parent = parent.Find(name) ?? Child(name, parent).transform;
                using var serialized = new SerializedObject(parent.gameObject.AddComponent(receiver));
                serialized.FindProperty("parameter").stringValue = parameter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Contact("RxHeadMain", "NadeRxHeadMain");
            Contact("RightHandSystem/RxRight", "NadeRxRightHand");
            Assert.That(ExportGimmickDetection.KnownNadeRoot(root), Is.False);
            Contact("LeftHandSystem/RxLeft", "NadeRxLeftHand");
            Assert.That(ExportGimmickDetection.KnownNadeRoot(root), Is.True);
            var finding = ExportGimmickDetection.Analyze(avatar).Single(f => f.RuleId != null);
            Assert.That(finding.RuleId, Is.EqualTo(ExportGimmickRules.PettingSystem));
            Assert.That(finding.Evidence, Is.EqualTo("structure"));
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
        }

        [Test] public void OmittedGimmickIsAResultLineWithItsRule()
        {
            var settings = Child("settings", Child("accessories").transform);
            var report = new ExportRecoveryReport();
            var warnings = new List<string>();
            UniVrmOneClickExporter.RecordOmittedGimmick(avatar, new ExportGimmickFinding
            {
                Target = settings, Unit = GimmickExclusionUnit.Hierarchy, Reason = "test", RuleId = "test-anchor"
            }, warnings, report);
            var entry = report.Fallbacks.Results.Single();
            Assert.That(entry.Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(entry.RuleId, Is.EqualTo("test-anchor"));
            Assert.That(entry.Name, Is.EqualTo("accessories/settings"));
            Assert.That(entry.Path, Is.EqualTo("accessories/settings"));
            // Not a confirmation item: the dialog is for features that failed.
            Assert.That(report.Fallbacks.Count, Is.Zero);
            Assert.That(warnings, Is.Empty);
            var list = ExportResultList.Build(report.Fallbacks, warnings);
            Assert.That(list.Rows.Single().Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(list.NoticeCount, Is.Zero);
        }

        [Test] public void MeshReferencePruningKeepsBoneSpringAndOriginalVrmSettings()
        {
            var renderer = Skin("effect", hidden);
            var instance = avatar.AddComponent<Vrm10Instance>();
            var settings = Own(ScriptableObject.CreateInstance<VRM10Object>());
            var expression = Own(ScriptableObject.CreateInstance<VRM10Expression>());
            expression.MorphTargetBindings = new[] { new MorphTargetBinding { RelativePath = "effect", Index = 0, Weight = 1 } };
            settings.Expression.AddClip(ExpressionPreset.happy, expression);
            settings.FirstPerson.Renderers.Add(new RendererFirstPersonFlags { Renderer = "effect" });
            instance.Vrm = settings;
            var joint = renderer.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var spring = new Vrm10InstanceSpringBone.Spring("keep") { Joints = { joint } };
            instance.SpringBone.Springs.Add(spring);
            var clone = Own(Object.Instantiate(avatar));
            using var session = new ExportGimmickSession(avatar, clone, null); session.Apply(null, null, null);
            var copy = clone.GetComponent<Vrm10Instance>();
            Assert.That(copy.SpringBone.Springs[0].Joints[0], Is.Not.Null);
            Assert.That(copy.Vrm.FirstPerson.Renderers, Is.Empty);
            Assert.That(copy.Vrm.Expression.Clips.First().Clip.MorphTargetBindings, Is.Empty);
            Assert.That(settings.FirstPerson.Renderers.Count, Is.EqualTo(1));
            Assert.That(expression.MorphTargetBindings.Length, Is.EqualTo(1));
        }

        static Type MaType(string fullName)
        {
            var type = FindType(fullName);
            if (type == null) Assert.Ignore("Installed MA is required for the structural gimmick rules.");
            return type;
        }
        Material Plain() => Own(new Material(Shader.Find("Unlit/Texture")));

        // A menu or animator entry with a prop that is not drawn like the
        // avatar and, unless told otherwise, parts that only work in VRChat.
        GameObject StructuralGimmick(string entry, Material prop, bool runtime = true, Transform parent = null)
        {
            var group = Child("gimmick", parent); group.AddComponent(MaType(entry));
            if (prop != null) Render("prop", prop, group.transform);
            if (!runtime) return group;
            Child("particles", group.transform).AddComponent<ParticleSystem>();
            Child("viewer", group.transform).AddComponent<Camera>();
            Child("speaker", group.transform).AddComponent<AudioSource>();
            return group;
        }

        [Test] public void VrChatOnlyGimmickWithAMenuLeavesAsAWhole()
        {
            Render("clothing", normal);
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, Plain());
            var finding = Matched().Single();
            Assert.That(finding.RuleId, Is.EqualTo(ExportGimmickRules.VrChatOnlyStructure));
            Assert.That(finding.Target, Is.SameAs(gimmick));
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            Assert.That(finding.Evidence, Is.EqualTo("structure"));
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, null), Is.EqualTo(new[] { gimmick }));
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, new ExportGimmickOptions { AutoExclude = false }), Is.Empty);
            // Kept by the user, as a whole or through one of its parts.
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, new ExportGimmickOptions { IncludedObjects = new[] { gimmick } }), Is.Empty);
            var prop = gimmick.transform.Find("prop").gameObject;
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, new ExportGimmickOptions { IncludedObjects = new[] { prop } }), Is.Empty);
        }

        [Test] public void PropDrawnLikeTheAvatarStays()
        {
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, normal);
            Assert.That(Matched(), Is.Empty);
            // A toon renderer counts even while it is hidden until the menu shows it.
            gimmick.transform.Find("prop").GetComponent<Renderer>().sharedMaterial = Plain();
            Render("plush", normal, gimmick.transform).gameObject.SetActive(false);
            Assert.That(Matched(), Is.Empty);
            Object.DestroyImmediate(gimmick.transform.Find("plush").gameObject);
            Assert.That(Matched().Single().Target, Is.SameAs(gimmick));
        }

        [Test] public void PropSharingAShaderWithTheBodyStays()
        {
            var body = Skin("body", unspecified); body.bones = new[] { Child("hips").transform };
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, Own(new Material(unspecified.shader)));
            Assert.That(Matched(), Is.Empty);
            // Without bones the body skin says nothing about how the avatar is drawn.
            body.bones = Array.Empty<Transform>();
            Assert.That(Matched().Single().Target, Is.SameAs(gimmick));
        }

        [Test] public void OutfitBoundToTheBodyIsNotAGimmick()
        {
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, Plain());
            var skin = Skin("dress", Plain()); skin.transform.SetParent(gimmick.transform, false);
            skin.bones = new[] { Child("hips").transform };
            Assert.That(Matched(), Is.Empty);
            Object.DestroyImmediate(skin.gameObject);
            Assert.That(Matched().Count, Is.EqualTo(1));
            gimmick.AddComponent(MaType(ExportGimmickRules.MergeArmature));
            Assert.That(Matched(), Is.Empty);
        }

        [Test] public void GimmickWithoutVrChatOnlyPartsIsOnlyReviewed()
        {
            var gimmick = StructuralGimmick(ExportGimmickRules.MergeAnimator, Plain(), runtime: false);
            var finding = Matched().Single();
            Assert.That(finding.Target, Is.SameAs(gimmick));
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(finding.Reason, Is.EqualTo(ExportGimmickDetection.VrChatOnlyNoEvidenceReason));
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, null), Is.Empty);
            Child("particles", gimmick.transform).AddComponent<ParticleSystem>();
            Assert.That(Matched().Single().Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            // A missing material leaves the drawing unknown, whatever else it holds.
            gimmick.transform.Find("prop").GetComponent<Renderer>().sharedMaterial = null;
            finding = Matched().Single();
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(finding.Reason, Is.EqualTo(ExportGimmickDetection.VrChatOnlyUncertainReason));
        }

        [Test] public void StructuralGimmickUsedFromOutsideIsOnlyReviewed()
        {
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, Plain());
            var skin = Skin("body", normal); skin.bones = new[] { gimmick.transform.Find("prop") };
            var finding = Matched().Single();
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(finding.Reason, Is.Not.EqualTo(ExportGimmickDetection.VrChatOnlyReason));
            Assert.That(ExportGimmickDetection.AutomaticRoots(new[] { finding }, null), Is.Empty);
        }

        [Test] public void StructuralGimmickReplacesTheTableFindingInsideIt()
        {
            var merge = MaType(ExportGimmickRules.MergeAnimator);
            var gimmick = StructuralGimmick(ExportGimmickRules.MenuInstaller, Plain(), runtime: false);
            var lens = RenderTextureLens(gimmick.transform); Object.DestroyImmediate(lens.GetComponent<Animator>()); lens.AddComponent(merge);
            var findings = ExportGimmickDetection.Analyze(avatar).Where(f => f.RuleId != null).ToList();
            var finding = findings.Single();
            Assert.That(finding.RuleId, Is.EqualTo(ExportGimmickRules.VrChatOnlyStructure));
            Assert.That(finding.Target, Is.SameAs(gimmick));
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            // Kept for review, the group leaves the claimed lens to its rule.
            var skin = Skin("body", normal); skin.bones = new[] { gimmick.transform.Find("prop") };
            findings = ExportGimmickDetection.Analyze(avatar).Where(f => f.RuleId != null).ToList();
            Assert.That(findings.Select(f => f.RuleId), Is.EquivalentTo(new[] { ExportGimmickRules.VrChatOnlyStructure, ExportGimmickRules.RenderTextureCamera }));
            Assert.That(findings.Single(f => f.RuleId == ExportGimmickRules.VrChatOnlyStructure).Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(findings.Single(f => f.RuleId == ExportGimmickRules.RenderTextureCamera).Target, Is.SameAs(lens));
        }

        [Test] public void MeshlessModularAvatarGimmickIsNotStructural()
        {
            StructuralGimmick(ExportGimmickRules.MenuInstaller, null);
            Assert.That(Matched(), Is.Empty);
            // A mesh that is never shown is not one either.
            var gimmick = StructuralGimmick(ExportGimmickRules.MergeAnimator, Plain());
            gimmick.transform.Find("prop").GetComponent<Renderer>().enabled = false;
            Assert.That(Matched(), Is.Empty);
        }

        const string VrChatPlatform = "nadena.dev.ndmf.vrchat.avatar3";
        const string GenericPlatform = "nadena.dev.ndmf.generic";

        GameObject Filtered(string name, params (string platform, bool exclude)[] filters)
        {
            var type = MaType(ExportGimmickRules.PlatformFilterType);
            var go = Child(name);
            foreach (var (platform, exclude) in filters)
            {
                using var serialized = new SerializedObject(go.AddComponent(type));
                serialized.FindProperty("m_platform").stringValue = platform;
                serialized.FindProperty("m_excludePlatform").boolValue = exclude;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }

        [Test] public void PlatformFilterIsAppliedForTheVrmPlatform()
        {
            var vrchatOnly = Filtered("vrchat only", (VrChatPlatform, false));
            Filtered("not on vrchat", (VrChatPlatform, true));
            Filtered("generic", (GenericPlatform, false), (VrChatPlatform, false));
            var mixed = Filtered("mixed", (VrChatPlatform, false), (GenericPlatform, true));
            // A gimmick inside a removed object leaves with it, as one line.
            StructuralGimmick(ExportGimmickRules.MenuInstaller, Plain(), parent: vrchatOnly.transform);
            var findings = Matched();
            Assert.That(findings.Select(f => f.Target), Is.EquivalentTo(new[] { vrchatOnly, mixed }));
            Assert.That(findings.All(f => f.RuleId == ExportGimmickRules.PlatformFilter), Is.True);
            Assert.That(findings.Single(f => f.Target == vrchatOnly).Unit, Is.EqualTo(GimmickExclusionUnit.Hierarchy));
            var review = findings.Single(f => f.Target == mixed);
            Assert.That(review.Unit, Is.EqualTo(GimmickExclusionUnit.Review));
            Assert.That(review.Reason, Is.EqualTo(ExportGimmickDetection.PlatformFilterMixedReason));
            Assert.That(ExportGimmickDetection.AutomaticRoots(findings, null), Is.EqualTo(new[] { vrchatOnly }));
        }

        [Test] public void EmptyObjectsLeaveTheVrmOnlyWithTheAutomaticOption()
        {
            var shader = Shader.Find("lilToon"); if (shader == null) Assert.Ignore("Install lilToon for the real exporter entry point.");
            using var f = new AttachmentConnectionTests.Fixture();
            foreach (var skin in f.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = shader;
            Child("empty holder", Child("empty branch", f.Source.transform).transform);
            string[] Nodes(bool automatic) => ((List<object>)GlbDocument.Read(UniVrmOneClickExporter.Export(f.Source, "Empty objects", "Tests",
                    exporterVersion: "0.11.5", lilToonVersion: "2.3.4", gimmickOptions: new ExportGimmickOptions { AutoExclude = automatic },
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None })).Json["nodes"])
                .Cast<Dictionary<string, object>>().Select(node => node.TryGetValue("name", out var name) ? (string)name : null).ToArray();
            var pruned = Nodes(true);
            Assert.That(pruned, Has.No.Member("empty branch").And.No.Member("empty holder"));
            Assert.That(pruned, Has.Member("Front").And.Member("Back").And.Member("Head").And.Member("Hips"));
            var kept = Nodes(false);
            Assert.That(kept, Has.Member("empty branch").And.Member("empty holder"));
            Assert.That(kept, Is.SupersetOf(pruned));
            Assert.That(f.Source.transform.Find("empty branch/empty holder"), Is.Not.Null);
        }

        [Test] public void EmptyObjectPruningKeepsEverythingTheVrmWrites()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var copy = f.Copy.transform;
            var hips = copy.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            Child("menu holder", copy); Child("leaf", Child("branch", copy).transform);
            var underBone = Child("empty under a bone", hips).transform;
            var props = Child("props", copy).transform; var group = Child("group", props).transform;
            Render("prop", normal, group);
            var chain = Child("spring", f.Head).transform;
            chain.gameObject.AddComponent<VRM10SpringBoneJoint>();
            Child("collider", hips).AddComponent<VRM10SpringBoneCollider>();
            var center = Child("center", copy).transform;
            var instance = f.Copy.AddComponent<Vrm10Instance>();
            instance.SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("keep") { Joints = { chain.GetComponent<VRM10SpringBoneJoint>() }, Center = center });
            Assert.That(ExportEmptyNodes.Prune(f.Copy), Is.EqualTo(4));
            Assert.That(copy.Find("menu holder"), Is.Null);
            Assert.That(copy.Find("branch"), Is.Null);
            Assert.That(underBone == null, Is.True);
            Assert.That(copy.Find("props/group/prop"), Is.Not.Null);
            Assert.That(copy.Find("Independent hair/Head"), Is.Not.Null);
            Assert.That(chain != null && center != null && hips.Find("collider") != null, Is.True);
            var animator = copy.GetComponent<Animator>();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                if (f.Source.GetComponent<Animator>().GetBoneTransform(bone) != null)
                    Assert.That(animator.GetBoneTransform(bone), Is.Not.Null, bone.ToString());
            Assert.Throws<ArgumentException>(() => ExportEmptyNodes.Prune(null));
        }
    }
}
