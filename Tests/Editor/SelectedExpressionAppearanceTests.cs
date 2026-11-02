using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class SelectedExpressionAppearanceTests
    {
        private string folder;
        private GameObject avatar, dummy;
        private SkinnedMeshRenderer skin;
        private Mesh mesh;
        private Material material;
        private AnimationClip clip;
        private readonly List<Object> owned = new List<Object>();
        private static readonly Color PreparedColor = new Color(.25f, .5f, .75f, 1f);
        private static readonly Vector4 PreparedVector = new Vector4(.125f, .5f, .75f, 1.25f);

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install the real lilToon package.");
            var name = "__SelectedAppearance_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            avatar = new GameObject("Avatar", typeof(Animator));
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            dummy = new GameObject("Dummy"); dummy.transform.SetParent(avatar.transform, false);
            skin = body.GetComponent<SkinnedMeshRenderer>(); mesh = BaseShapeFixture.Create(); skin.sharedMesh = mesh;
            material = new Material(shader); material.SetColor("_Color2nd", PreparedColor); material.SetFloat("_Cutoff", .5f);
            material.SetVector("_MainTexHSVG", PreparedVector);
            skin.sharedMaterial = material;
            clip = new AnimationClip { name = "Mixed face" };
            Curve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size", AnimationCurve.Linear(0, 20, 1, 80));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); Object.DestroyImmediate(clip);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear(); if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        private EditorCurveBinding Curve(string path, Type type, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, type, property); AnimationUtility.SetEditorCurve(clip, binding, curve); return binding;
        }

        private EditorCurveBinding Appearance(string kind, float? changed = null)
        {
            switch (kind)
            {
                case "activation": return Curve("Dummy", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, changed ?? 1));
                case "enabled": return Curve("Body", typeof(SkinnedMeshRenderer), "m_Enabled", AnimationCurve.Constant(0, 1, changed ?? 1));
                case "color": return Curve("Body", typeof(SkinnedMeshRenderer), "material._Color2nd.r", AnimationCurve.Constant(0, 1, changed ?? PreparedColor.r));
                case "scalar": return Curve("Body", typeof(SkinnedMeshRenderer), "material._Cutoff", AnimationCurve.Constant(0, 1, changed ?? .5f));
                case "vector": return Curve("Body", typeof(SkinnedMeshRenderer), "material._MainTexHSVG.x", AnimationCurve.Constant(0, 1, changed ?? PreparedVector.x));
                case "absent activation": return Curve("Absent", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
                case "absent material": return Curve("Absent", typeof(SkinnedMeshRenderer), "material._Color2nd.r", AnimationCurve.Constant(0, 1, PreparedColor.r));
                case "absent renderer": return Curve("Dummy", typeof(SkinnedMeshRenderer), "m_Enabled", AnimationCurve.Constant(0, 1, 1));
                default: throw new ArgumentException(kind);
            }
        }

        [TestCase("activation")]
        [TestCase("enabled")]
        [TestCase("color")]
        [TestCase("scalar")]
        [TestCase("vector")]
        [TestCase("absent activation")]
        [TestCase("absent renderer")]
        public void UnchangedPreparedAppearanceKeepsOnlyAuthoredMorphAnimation(string kind)
        {
            var binding = Appearance(kind);
            var before = new[] { avatar, (Object)skin, material, mesh, clip }.Select(EditorJsonUtility.ToJson).ToArray();
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, clip, entry);
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True);
            Assert.That(entry.Values.Single().Shape, Is.EqualTo("Face size"));
            Assert.That(entry.Values.Single().Weight, Is.EqualTo(20));
            Assert.That(entry.Animation.Single().Shape, Is.EqualTo("Face size"));
            Assert.That(entry.Animation.Single().Curve.Evaluate(1), Is.EqualTo(80));
            Assert.That(entry.Duration, Is.EqualTo(1));
            Assert.That(new[] { avatar, (Object)skin, material, mesh, clip }.Select(EditorJsonUtility.ToJson), Is.EqualTo(before));
        }

        [TestCase("enabled")]
        [TestCase("color")]
        [TestCase("vector")]
        public void ARealAppearanceChangeStillRejectsTheWholeSelectedClip(string kind)
        {
            var binding = Appearance(kind, 0);
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadClip(avatar, clip, new VrChatExpressionMenu.Entry()));
        }

        // Hiding an object is the face's visibility track, not an appearance change.
        [Test]
        public void HidingAnObjectIsAVisibilityTrack()
        {
            var binding = Appearance("activation", 0);
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            // While the export leaves visibility out, it is unsupported as before.
            using (new ExpressionTransformsSwitch(false))
                Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadClip(avatar, clip, new VrChatExpressionMenu.Entry()));
            using var on = new ExpressionTransformsSwitch(true);
            var entry = new VrChatExpressionMenu.Entry();
            VrChatGestureExpressions.ReadClip(avatar, clip, entry);
            var track = entry.Transforms.Single();
            Assert.That(track.Path, Is.EqualTo("Dummy"));
            Assert.That(track.Property, Is.EqualTo(VRVlog.Expressions.ExpressionTransformAnimation.Active));
            Assert.That(track.Curves.Single().Evaluate(.5), Is.EqualTo(0));
        }

        [TestCase("color")]
        [TestCase("activation")]
        public void AChangingAppearanceCurveIsNotCertifiedFromItsFirstKey(string kind)
        {
            var binding = Appearance(kind);
            var first = AnimationUtility.GetEditorCurve(clip, binding).keys[0].value;
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0, first, 1, 0));
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(avatar, clip));
        }

        [TestCase(false, "color")]
        [TestCase(true, "vector")]
        public void PropertyBlockPreventsMaterialNoOpProofEvenWhenStoredMaterialMatches(bool perSlot, string kind)
        {
            var binding = Appearance(kind); var block = new MaterialPropertyBlock();
            block.SetColor("_Color2nd", Color.red);
            if (perSlot) skin.SetPropertyBlock(block, 0); else skin.SetPropertyBlock(block);
            Assert.That(skin.HasPropertyBlock(), Is.True);
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(avatar, clip));
        }

        [TestCase("equal", "color")]
        [TestCase("missing", "color")]
        [TestCase("different", "vector")]
        public void EverySharedMaterialSlotMustHaveTheSameDeclaredValue(string mode, string kind)
        {
            var binding = Appearance(kind); var second = new Material(material); owned.Add(second);
            if (mode == "different") second.SetColor("_Color2nd", Color.black);
            if (mode == "different") second.SetVector("_MainTexHSVG", Vector4.zero);
            skin.sharedMaterials = new[] { material, mode == "missing" ? null : second };
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.EqualTo(mode == "equal"));
        }

        [TestCase("NaN tangent")]
        [TestCase("overshoot")]
        [TestCase("event")]
        [TestCase("object swap")]
        public void NoOpCandidateCannotConcealCorruptOrUnsupportedClipData(string kind)
        {
            var binding = Appearance("color");
            if (kind == "event") AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { functionName = "UnexpectedCallback", time = .5f } });
            else if (kind == "object swap") AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = material } });
            else
            {
                var curve = AnimationCurve.Constant(0, 1, PreparedColor.r); var keys = curve.keys;
                keys[0].outTangent = kind == "NaN tangent" ? float.NaN : 1;
                curve.keys = keys; AnimationUtility.SetEditorCurve(clip, binding, curve);
                var stored = AnimationUtility.GetEditorCurve(clip, binding).keys;
                if (kind == "NaN tangent")
                    Assert.That(stored.Length == 2 && float.IsNaN(stored[0].outTangent), Is.True,
                        "The fixture must retain an active NaN tangent before testing the production guard.");
            }
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadClip(avatar, clip, new VrChatExpressionMenu.Entry()));
        }

        [Test]
        public void AmbiguousPathsRemainHardEvenWhenBothTargetsHaveMatchingValues()
        {
            var duplicate = new GameObject("Dummy"); duplicate.transform.SetParent(avatar.transform, false);
            var binding = Appearance("activation");
            Assert.Throws<InvalidOperationException>(() => SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding));
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(avatar, clip));
        }

        [TestCase("transform")]
        [TestCase("collider")]
        [TestCase("missing shader property")]
        [TestCase("vector as color")]
        [TestCase("color as vector")]
        [TestCase("texture")]
        public void ExistingUnknownPropertiesAreNotAuthorizedByCoincidentValues(string kind)
        {
            EditorCurveBinding binding;
            if (kind == "transform") binding = Curve("Dummy", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0, 1, 1));
            else if (kind == "collider")
            { dummy.AddComponent<BoxCollider>(); binding = Curve("Dummy", typeof(BoxCollider), "m_Enabled", AnimationCurve.Constant(0, 1, 1)); }
            else binding = Curve("Body", typeof(SkinnedMeshRenderer), "material." + (kind == "vector as color" ? "_MainTex_ScrollRotate.r" :
                kind == "color as vector" ? "_Color2nd.x" : kind == "texture" ? "_MainTex" : "_NotDeclared"), AnimationCurve.Constant(0, 1, 0));
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(avatar, clip));
        }

        [Test]
        public void DynamicsEnabledCurveDoesNotRejectTheGesturePose()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType(PhysBoneSpringExport.ColliderType)).FirstOrDefault(value => value != null);
            if (type == null) Assert.Ignore("Install the real VRChat SDK with VRCPhysBoneCollider.");
            var collider = (Behaviour)dummy.AddComponent(type); collider.enabled = false;
            Curve("Dummy", type, "m_Enabled", AnimationCurve.Constant(0, 1, 1));
            var before = new[] { avatar, (Object)skin, collider, clip }.Select(EditorJsonUtility.ToJson).ToArray();
            var pose = VrChatGestureExpressions.ReadPose(avatar, clip);
            Assert.That(pose.Single().Shape, Is.EqualTo("Face size"));
            Assert.That(pose.Single().Weight, Is.EqualTo(20));
            Assert.That(collider.enabled, Is.False);
            Assert.That(new[] { avatar, (Object)skin, collider, clip }.Select(EditorJsonUtility.ToJson), Is.EqualTo(before));
        }

        [Test]
        public void UnusedEndpointTangentsDoNotInvalidateAnOtherwiseConstantNoOp()
        {
            var binding = Appearance("color"); var curve = AnimationUtility.GetEditorCurve(clip, binding); var keys = curve.keys;
            keys[0].inTangent = float.NaN; keys[1].outTangent = float.NaN; curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True);
        }

        [TestCase("_MainTexHSVG")]
        public void MatchingVectorChannelsKeepTheirActualNativeValue(string property)
        {
            material.SetVector(property, PreparedVector);
            Assert.That(material.shader.GetPropertyType(material.shader.FindPropertyIndex(property)),
                Is.EqualTo(UnityEngine.Rendering.ShaderPropertyType.Vector));
            foreach (var component in "xyzw")
                Curve("Body", typeof(SkinnedMeshRenderer), "material." + property + "." + component,
                    AnimationCurve.Constant(0, 1, PreparedVector["xyzw".IndexOf(component)]));
            var native = Object.Instantiate(avatar); owned.Add(native);
            var nativeSkin = native.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            var nativeMaterial = new Material(material); owned.Add(nativeMaterial); nativeSkin.sharedMaterial = nativeMaterial;
            var before = EditorJsonUtility.ToJson(material);
            clip.SampleAnimation(native, .5f);
            var block = new MaterialPropertyBlock(); nativeSkin.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.False, "The native clip must actually apply its vector channels.");
            Assert.That(block.GetVector(property), Is.EqualTo(PreparedVector));
            Assert.That(nativeMaterial.GetVector(property), Is.EqualTo(PreparedVector));
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(binding => binding.propertyName.StartsWith("material.", StringComparison.Ordinal)))
                Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True, binding.propertyName);
            Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SharedFacialSetDoesNotClaimMouthMorphsForUnchangedEyeVectorResets(bool changesMaterial)
        {
            var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FacialSet.controller");
            fx.AddParameter("FacialSet", AnimatorControllerParameterType.Int); fx.AddLayer("Mouth");
            var layers = fx.layers; layers[1].defaultWeight = 1; fx.layers = layers;
            AnimatorState State(int layer, string name, string shape, float weight, bool appearance)
            {
                var motion = new AnimationClip { name = name }; AssetDatabase.AddObjectToAsset(motion, fx);
                AnimationUtility.SetEditorCurve(motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape),
                    AnimationCurve.Constant(0, 1, weight));
                if (appearance) AnimationUtility.SetEditorCurve(motion,
                    EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._MainTexHSVG.x"),
                    AnimationCurve.Constant(0, 1, changesMaterial ? 0 : PreparedVector.x));
                var state = layers[layer].stateMachine.AddState(name); state.motion = motion; state.writeDefaultValues = false; return state;
            }
            void Select(AnimatorState from, AnimatorState to)
            {
                var transition = from.AddTransition(to); transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.Equals, 1, "FacialSet");
            }
            var eye = State(0, "Eye__default", "Face size", 20, true);
            var otherEye = State(0, "Eye__selected", "Face size", 80, true); layers[0].stateMachine.defaultState = eye; Select(eye, otherEye);
            var mouth = State(1, "Mouth__default", "Blink", 10, false);
            var selected = State(1, "Mouth__selected", "Blink", 75, false); layers[1].stateMachine.defaultState = mouth; Select(mouth, selected);
            var metadata = new VrChatExpressionMenu.Source { Controller = fx };
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)selected.motion, entry);
            var before = EditorJsonUtility.ToJson(material);
            if (changesMaterial)
                Assert.Throws<InvalidOperationException>(() => VrChatExpressionSampler.ApplyPermanentOverrides(avatar, fx, entry, 1, false,
                    metadata: metadata, sourceState: selected));
            else
            {
                VrChatExpressionSampler.ApplyPermanentOverrides(avatar, fx, entry, 1, false, metadata: metadata, sourceState: selected);
                Assert.That(entry.Values.Single(value => value.Shape == "Blink").Weight, Is.EqualTo(75).Within(.01));
                Assert.That(entry.Messages.Any(value => value.Contains("共有の入力 FacialSet")), Is.False);
            }
            Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
            Assert.That(skin.GetBlendShapeWeight(1), Is.Zero);
        }

        [TestCase("color")]
        [TestCase("activation")]
        public void NoOpProofUsesThePreparedCopyRatherThanAnEarlierAuthoringValue(string kind)
        {
            var binding = Appearance(kind); Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True);
            var prepared = Object.Instantiate(avatar); owned.Add(prepared);
            if (kind == "activation") prepared.transform.Find("Dummy").gameObject.SetActive(false);
            else
            {
                var changed = new Material(material); owned.Add(changed); changed.SetColor("_Color2nd", Color.black);
                prepared.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial = changed;
            }
            Assert.That(SelectedExpressionAppearance.IsUnchanged(prepared, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(prepared, clip));
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MatchingShaderColorChannelsKeepTheirNativeAppliedValue(bool useStoredColor)
        {
            const string property = "_Color2nd";
            material.SetColor(property, PreparedColor);
            var shader = material.shader;
            var colorIndex = shader.FindPropertyIndex(property);
            var componentIndex = shader.FindPropertyIndex(property + ".r");
            string Property(int index) => index < 0 ? "absent" : shader.GetPropertyName(index) + " / " + shader.GetPropertyType(index);
            var native = Object.Instantiate(avatar); owned.Add(native);
            var nativeSkin = native.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            var nativeMaterial = new Material(material); owned.Add(nativeMaterial); nativeSkin.sharedMaterial = nativeMaterial;
            var before = EditorJsonUtility.ToJson(material);
            var stored = material.GetColor(property);
            var authored = useStoredColor ? stored : PreparedColor;
            foreach (var channel in "rgba")
                Curve("Body", typeof(SkinnedMeshRenderer), "material." + property + "." + channel,
                    AnimationCurve.Constant(0, 1, authored["rgba".IndexOf(channel)]));
            var diagnostics = "ColorSpace=" + QualitySettings.activeColorSpace + "; shader=" + shader.name +
                "; full=" + Property(colorIndex) + "; flags=" + shader.GetPropertyFlags(colorIndex) +
                "; component=" + Property(componentIndex) + "; stored=" + stored.ToString("R") +
                "; raw=" + material.GetVector(property).ToString("R") + "; declared linear=" + PreparedColor.linear.ToString("R") +
                "; declared linear-gamma=" + PreparedColor.linear.gamma.ToString("R") + "; stored linear=" + stored.linear.ToString("R") +
                "; propertyBlock=" + skin.HasPropertyBlock() + "; curves=" + string.Join("; ", AnimationUtility.GetCurveBindings(clip).Select(binding =>
                    binding.propertyName + "=" + string.Join(",", AnimationUtility.GetEditorCurve(clip, binding).keys.Select(key =>
                        key.value.ToString("R") + "/" + key.inTangent.ToString("R") + "/" + key.outTangent.ToString("R")))));
            TestContext.WriteLine(diagnostics);
            clip.SampleAnimation(native, .5f);
            var global = new MaterialPropertyBlock(); nativeSkin.GetPropertyBlock(global);
            var slot = new MaterialPropertyBlock(); nativeSkin.GetPropertyBlock(slot, 0);
            TestContext.WriteLine("native material=" + nativeSkin.sharedMaterial.GetColor(property).ToString("R") +
                "; global empty=" + global.isEmpty + "; global=" + global.GetColor(property).ToString("R") +
                "; global raw=" + global.GetVector(property).ToString("R") +
                "; slot empty=" + slot.isEmpty + "; slot=" + slot.GetColor(property).ToString("R"));
            Assert.That(nativeSkin.sharedMaterial.GetColor(property), Is.EqualTo(stored), diagnostics);
            Assert.That(global.isEmpty, Is.False, "The native clip must actually apply its material channels.");
            Assert.That(slot.isEmpty, Is.True);
            // Native material and PropertyBlock color APIs can each perform a
            // float gamma round trip. The production proof itself uses exact
            // canonical equality; this oracle measures the resulting color.
            var applied = global.GetColor(property);
            for (var channel = 0; channel < 4; channel++)
                Assert.That(applied[channel], Is.EqualTo(stored[channel]).Within(.000001f), diagnostics);
            Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(binding => binding.propertyName.StartsWith("material.", StringComparison.Ordinal)))
                Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.True, diagnostics);
        }

        [TestCase("ordinary", false, 1f)]
        [TestCase("HDR", false, 1f)]
        [TestCase("HDR", true, 4f)]
        [TestCase("Gamma", false, 1f)]
        public void MaterialColorAnimationMatchesActualRenderedPixels(string kind, bool linearBaseline, float intensity)
        {
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(UnityEngine.ColorSpace.Linear));
            Assert.That(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat), Is.True);
            var shader = Shader.Find("Hidden/VRVlogTests/AppearanceColorProbe");
            Assert.That(shader, Is.Not.Null); Assert.That(shader.isSupported, Is.True);
            var property = kind == "ordinary" ? "_Ordinary" : kind == "HDR" ? "_Hdr" : "_Gamma";
            var probeMaterial = new Material(shader); owned.Add(probeMaterial);
            probeMaterial.SetFloat("_Which", kind == "ordinary" ? 0 : kind == "HDR" ? 1 : 2);
            // Fractional alpha also proves it is never gamma-converted; 0/1
            // would be fixed points and could conceal the wrong conversion.
            var authored = new Color(PreparedColor.r * intensity, PreparedColor.g * intensity, PreparedColor.b * intensity, .25f);
            probeMaterial.SetColor(property, linearBaseline ? authored.linear : authored);
            var baseline = probeMaterial.GetVector(property);
            var beforeMaterial = EditorJsonUtility.ToJson(probeMaterial);
            skin.enabled = false;
            var probe = new GameObject("Probe", typeof(MeshFilter), typeof(MeshRenderer)); probe.transform.SetParent(avatar.transform, false);
            var quad = new Mesh(); owned.Add(quad);
            quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 }; quad.RecalculateBounds();
            probe.GetComponent<MeshFilter>().sharedMesh = quad; probe.GetComponent<MeshRenderer>().sharedMaterial = probeMaterial;
            foreach (var channel in "rgba")
                Curve("Probe", typeof(MeshRenderer), "material." + property + "." + channel,
                    AnimationCurve.Constant(0, 1, authored["rgba".IndexOf(channel)]));
            var scene = EditorSceneManager.NewPreviewScene();
            var native = Object.Instantiate(avatar); var nativeMaterial = new Material(probeMaterial); owned.Add(nativeMaterial);
            var nativeRenderer = native.transform.Find("Probe").GetComponent<MeshRenderer>(); nativeRenderer.sharedMaterial = nativeMaterial;
            nativeRenderer.gameObject.layer = 31;
            var cameraObject = new GameObject("Color oracle camera", typeof(Camera));
            var target = new RenderTexture(16, 16, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            var activeTarget = RenderTexture.active;
            try
            {
                SceneManager.MoveGameObjectToScene(native, scene); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1;
                camera.scene = scene; camera.cullingMask = 1 << 31;
                camera.nearClipPlane = .1f; camera.farClipPlane = 10; camera.allowHDR = true; camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.transform.position = new Vector3(0, 0, -2); camera.targetTexture = target; target.Create();
                Color Render()
                {
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); pixels.Apply(false, false); return pixels.GetPixel(8, 8);
                }
                var before = Render();
                Assert.That(before.a, Is.EqualTo(authored.a)); Assert.That(before.r, Is.GreaterThan(.01f), "The probe quad must actually be rendered.");
                clip.SampleAnimation(native, .5f);
                var global = new MaterialPropertyBlock(); nativeRenderer.GetPropertyBlock(global);
                var slot = new MaterialPropertyBlock(); nativeRenderer.GetPropertyBlock(slot, 0);
                Assert.That(global.isEmpty, Is.False); Assert.That(slot.isEmpty, Is.True);
                var after = Render(); var applied = global.GetVector(property);
                TestContext.WriteLine(kind + " / linearBaseline=" + linearBaseline + " / intensity=" + intensity + "; flags=" + shader.GetPropertyFlags(shader.FindPropertyIndex(property)) +
                    "; material API=" + baseline.ToString("R") + "; Animator raw=" + applied.ToString("R") +
                    "; before pixel=" + before.ToString("R") + "; after pixel=" + after.ToString("R"));
                Assert.That(EditorJsonUtility.ToJson(probeMaterial), Is.EqualTo(beforeMaterial));
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(binding => binding.propertyName.StartsWith("material.", StringComparison.Ordinal)))
                {
                    var channel = "rgba".IndexOf(binding.propertyName[binding.propertyName.Length - 1]);
                    var renderedUnchanged = Mathf.Abs(before[channel] - after[channel]) <= .000001f;
                    Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.EqualTo(renderedUnchanged), binding.propertyName);
                }
            }
            finally
            {
                RenderTexture.active = activeTarget; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(native); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase("HDR alpha")]
        [TestCase("alpha")]
        [TestCase("scalar")]
        [TestCase("vector")]
        public void ColorCanonicalizationDoesNotRelaxOtherPropertyDomains(string domain)
        {
            string property; float current;
            if (domain == "HDR alpha")
            {
                material.SetColor("_EmissionColor", new Color(.25f, .5f, .75f, .25f)); property = "_EmissionColor.a";
                var index = material.shader.FindPropertyIndex("_EmissionColor");
                Assert.That((material.shader.GetPropertyFlags(index) & UnityEngine.Rendering.ShaderPropertyFlags.HDR) != 0, Is.True);
                current = material.GetColor("_EmissionColor").a;
            }
            else if (domain == "alpha")
            {
                material.SetColor("_Color2nd", new Color(.25f, .5f, .75f, .25f)); property = "_Color2nd.a";
                current = material.GetColor("_Color2nd").a;
            }
            else if (domain == "vector")
            {
                material.SetVector("_MainTexHSVG", new Vector4(.25f, 1, 1, 1)); property = "_MainTexHSVG.x";
                current = material.GetVector("_MainTexHSVG").x;
            }
            else
            {
                material.SetFloat("_Cutoff", .25f); property = "_Cutoff"; current = material.GetFloat("_Cutoff");
            }
            var different = Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(current));
            Assert.That(different, Is.Not.EqualTo(current), "The fixture must contain a real float difference.");
            var binding = Curve("Body", typeof(SkinnedMeshRenderer), "material." + property, AnimationCurve.Constant(0, 1, different));
            Assert.That(SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding), Is.False);
            Assert.Throws<InvalidOperationException>(() => VrChatGestureExpressions.ReadPose(avatar, clip));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task MixedLilToonGesturePreservesPreparedAppearanceAndMorphEndpointsAfterExport(bool fullLilToon, bool controlledLayer)
        {
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
                .FirstOrDefault(type => type != null);
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            Vrm10Instance imported = null; GameObject reference = null;
            try
            {
                fixture.Mesh.ClearBlendShapes(); fixture.Mesh.AddBlendShapeFrame("Face", 100,
                    Enumerable.Repeat(Vector3.right * .02f, fixture.Mesh.vertexCount).ToArray(), null, null);
                foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.sharedMaterial.shader = Shader.Find("lilToon"); renderer.sharedMaterial.SetColor("_Color2nd", PreparedColor);
                    renderer.sharedMaterial.SetVector("_MainTexHSVG", PreparedVector);
                    renderer.sharedMaterial.SetFloat("_UseMain2ndTex", 1); renderer.SetBlendShapeWeight(0, 17);
                }
                var preparedColor = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()[0].sharedMaterial.GetColor("_Color2nd");
                var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Mixed.controller"); fx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                var faceLayer = controlledLayer ? 4 : 0;
                if (controlledLayer)
                {
                    var controlType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl"))
                        .FirstOrDefault(type => type != null);
                    if (controlType == null) Assert.Ignore("Install the real VRChat SDK layer controls.");
                    for (var index = 1; index <= faceLayer; index++) fx.AddLayer("Authored slot " + index);
                    var layerDefinitions = fx.layers;
                    for (var index = 0; index < faceLayer; index++)
                    {
                        var empty = new AnimationClip { name = "Empty slot " + index }; AssetDatabase.AddObjectToAsset(empty, fx);
                        var state = layerDefinitions[index].stateMachine.AddState("Prepared slot " + index);
                        state.motion = empty; state.writeDefaultValues = false; layerDefinitions[index].stateMachine.defaultState = state;
                        if (index != 0) continue;
                        var control = state.AddStateMachineBehaviour(controlType);
                        using var data = new SerializedObject(control);
                        var playable = data.FindProperty("playable"); var fxIndex = Array.IndexOf(playable.enumNames, "FX");
                        Assert.That(fxIndex, Is.GreaterThanOrEqualTo(0)); playable.enumValueIndex = fxIndex;
                        data.FindProperty("layer").intValue = faceLayer; data.FindProperty("goalWeight").floatValue = 1;
                        data.FindProperty("blendDuration").floatValue = 0; data.ApplyModifiedPropertiesWithoutUndo();
                    }
                    layerDefinitions[faceLayer].defaultWeight = 0; fx.layers = layerDefinitions;
                    Assert.That(fx.layers[faceLayer].defaultWeight, Is.Zero, "The face slot must depend on the SDK command to become active.");
                }
                AnimatorState State(string name, float weight, bool appearance)
                {
                    var motion = new AnimationClip { name = name };
                    foreach (var path in new[] { "Front", "Back" })
                    {
                        AnimationUtility.SetEditorCurve(motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.Face"), AnimationCurve.Constant(0, 1, weight));
                        if (!appearance) continue;
                        foreach (var channel in "rgba") AnimationUtility.SetEditorCurve(motion,
                            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material._Color2nd." + channel),
                            AnimationCurve.Constant(0, 1, PreparedColor["rgba".IndexOf(channel)]));
                        foreach (var component in "xyzw") AnimationUtility.SetEditorCurve(motion,
                            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material._MainTexHSVG." + component),
                            AnimationCurve.Constant(0, 1, PreparedVector["xyzw".IndexOf(component)]));
                        AnimationUtility.SetEditorCurve(motion, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
                    }
                    AssetDatabase.AddObjectToAsset(motion, fx); var state = fx.layers[faceLayer].stateMachine.AddState(name);
                    state.motion = motion; state.writeDefaultValues = false; return state;
                }
                var idle = State("Rest", 17, false); var selected = State("Mixed selected", 75, true); fx.layers[faceLayer].stateMachine.defaultState = idle;
                var transition = idle.AddTransition(selected); transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.Equals, 1, "GestureLeft");
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX"); layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = fx; data.ApplyModifiedPropertiesWithoutUndo();
                }
                var fingerprint = ExportSourceFingerprint.Compute(fixture.Source);
                var assets = AssetDatabase.LoadAllAssetsAtPath(folder + "/Mixed.controller").Concat(new Object[] { fixture.Mesh })
                    .Concat(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(renderer => renderer.sharedMaterials)).Distinct().ToArray();
                var before = assets.Select(EditorJsonUtility.ToJson).ToArray(); reference = Object.Instantiate(fixture.Source);
                foreach (var behaviour in reference.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Mixed appearance face", "Tests", warnings,
                    exporterVersion: fullLilToon ? "mixed-appearance-regression" : null, lilToonVersion: fullLilToon ? "2.3.4" : null,
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    listFaces: new[] { ("Mixed selected", (AnimationClip)selected.motion, 0f) });
                Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(1), string.Join("\n", warnings));
                if (fullLilToon)
                {
                    var glb = GlbDocument.Read(bytes); F.Validate(glb.Json, bytes.Length, glb.Binary.Length);
                    foreach (var item in F.List(F.Root(glb.Json), "materials").Select(F.Object))
                    {
                        var color = F.List(item, "values").Select(F.Object).Single(value => F.Text(value, "name") == "_Color2nd");
                        Assert.That(F.Vector(F.Get(color, "value")), Is.EqualTo(new[] { preparedColor.r, preparedColor.g, preparedColor.b, preparedColor.a }));
                        var vector = F.List(item, "values").Select(F.Object).Single(value => F.Text(value, "name") == "_MainTexHSVG");
                        Assert.That(F.Vector(F.Get(vector, "value")), Is.EqualTo(new[] { PreparedVector.x, PreparedVector.y, PreparedVector.z, PreparedVector.w }));
                    }
                }
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                var expression = imported.Vrm.Expression.CustomClips.Single(item => item.name == "Mixed selected");
                var importedMaterials = imported.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
                var colorBefore = importedMaterials.Select(item => item.color).ToArray();
                foreach (var input in new[] { 0f, 1f, 0f })
                {
                    imported.Runtime.Expression.SetWeight(ExpressionKey.CreateCustom(expression.name), input); imported.Runtime.Process();
                    foreach (var path in new[] { "Front", "Back" })
                    {
                        var expected = reference.transform.Find(path).GetComponent<SkinnedMeshRenderer>(); expected.SetBlendShapeWeight(0, input == 0 ? 17 : 75);
                        var actual = imported.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.name == path);
                        Assert.That(actual.enabled && actual.gameObject.activeInHierarchy, Is.True);
                        var a = Vertices(expected); var b = Vertices(actual); Assert.That(b.Length, Is.EqualTo(a.Length));
                        for (var i = 0; i < a.Length; i++) Assert.That(Vector3.Distance(a[i], b[i]), Is.LessThan(.0005f), path + " / " + input);
                    }
                    Assert.That(importedMaterials.Select(item => item.color), Is.EqualTo(colorBefore));
                }
                Assert.That(assets.Select(EditorJsonUtility.ToJson), Is.EqualTo(before));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(fingerprint));
                Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().All(renderer => renderer.GetBlendShapeWeight(0) == 17), Is.True);
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                if (reference != null) Object.DestroyImmediate(reference);
            }
        }

        private static Vector3[] Vertices(SkinnedMeshRenderer renderer)
        {
            var result = new Mesh();
            try { renderer.BakeMesh(result, false); return result.vertices.Select(renderer.transform.TransformPoint).ToArray(); }
            finally { Object.DestroyImmediate(result); }
        }

        // A constant rotation curve equal to the prepared pose holds it, written
        // as -q or with Euler angles a full turn apart; another angle does not.
        [TestCase("negated quaternion", true)]
        [TestCase("euler full turn", true)]
        [TestCase("other quaternion", false)]
        [TestCase("other euler", false)]
        public void HeldRotationMatchesThePreparedPoseAcrossSignAndFullTurns(string kind, bool unchanged)
        {
            dummy.transform.localRotation = Quaternion.Euler(0, 30, 0);
            var q = dummy.transform.localRotation;
            if (kind.EndsWith("quaternion"))
            {
                var r = kind == "negated quaternion" ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : Quaternion.Euler(0, 40, 0);
                foreach (var (axis, value) in new[] { ("x", r.x), ("y", r.y), ("z", r.z), ("w", r.w) })
                    Curve("Dummy", typeof(Transform), "m_LocalRotation." + axis, AnimationCurve.Constant(0, 1, value));
            }
            else
                foreach (var (axis, value) in new[] { ("x", 0f), ("y", kind == "euler full turn" ? 390f : 40f), ("z", 0f) })
                    Curve("Dummy", typeof(Transform), "localEulerAnglesRaw." + axis, AnimationCurve.Constant(0, 1, value));
            var property = kind.EndsWith("quaternion") ? "m_LocalRotation.x" : "localEulerAnglesRaw.y";
            Assert.That(SelectedExpressionAppearance.IsUnchangedTransform(dummy.transform, clip,
                EditorCurveBinding.FloatCurve("Dummy", typeof(Transform), property)), Is.EqualTo(unchanged));
        }

        // A NaN scale hides the bone's vertices. It is not the prepared pose
        // and not motion of the channel, and asking must not throw.
        [TestCase("value")]
        [TestCase("tangent")]
        public void NonFiniteTransformCurveIsNeitherHeldNorMoving(string kind)
        {
            var scale = dummy.transform.localScale;
            foreach (var (axis, value) in new[] { ("x", scale.x), ("y", scale.y), ("z", scale.z) })
                Curve("Dummy", typeof(Transform), "m_LocalScale." + axis, kind == "value"
                    ? NeutralShapeSamplerTests.NaNCurve()
                    : new AnimationCurve(new Keyframe(0, value, 0, float.NaN), new Keyframe(1, value, 0, 0)));
            var binding = EditorCurveBinding.FloatCurve("Dummy", typeof(Transform), "m_LocalScale.x");
            var stored = AnimationUtility.GetEditorCurve(clip, binding).keys[0];
            Assert.That(kind == "value" ? float.IsNaN(stored.value) : float.IsNaN(stored.outTangent), Is.True, "The fixture must keep its NaN.");
            Assert.That(SelectedExpressionAppearance.IsUnchangedTransform(dummy.transform, clip, binding), Is.False);
            Assert.That(SelectedExpressionAppearance.IsMovingTransform(clip, binding), Is.False);
        }
    }
}
