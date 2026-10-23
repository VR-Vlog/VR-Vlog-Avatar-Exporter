using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using VRVlog.LilToonExporter.Compatibility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#if UNITY_5_3_OR_NEWER
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace VRVlog.LilToonExporter
{
    // Optional bridge: no NDMF/MA assembly dependency. Use NDMF's ordered passes
    // on an owned export copy, preserving authored merge/proxy semantics.
    internal sealed class NdmfExportPreparation : IDisposable
    {
        private readonly HashSet<Object> generated = new HashSet<Object>();
        // Provenance only for copies made by our own isolation pass. Assets
        // created or replaced by NDMF plugins never enter this identity map.
        private readonly Dictionary<Object, Object> isolatedAssets = new Dictionary<Object, Object>();
        private readonly Dictionary<Renderer, Renderer> rendererReplacements =
            new Dictionary<Renderer, Renderer>();
        private readonly HashSet<Renderer> ambiguousRendererReplacements = new HashSet<Renderer>();
        // Shape names each renderer of the copy held before the build passes.
        // A lost shape is followed only to a renderer that newly holds it.
        private readonly Dictionary<SkinnedMeshRenderer, HashSet<string>> shapesBeforeProcessing =
            new Dictionary<SkinnedMeshRenderer, HashSet<string>>();
        private GameObject preparedRoot;
        // Generated shapes of an earlier export that a build pass removed.
        // Filled only when the caller tolerates lost shapes.
        internal readonly List<string> LostMorphs = new List<string>();
        internal const string LostGeneratedMorphs = "Modular Avatar / NDMF の処理で、以前の書き出しで作られた表情用のBlendShapeが失われたため省略しました: ";
        private string temporaryAssetPath, temporaryAssetGuid;
        // NDMF tracks asset replacements made by authoring/optimization passes.
        // Keep the optional public registry available to the preparation callback.
        internal object ObjectRegistry { get; private set; }
        private const string MaNamespace = "nadena.dev.modular_avatar.core.";
        // Weak keys keep reference-context markers scoped to owned copies.
        // Authored NDMF markers retain their existing processing semantics.
        private static readonly ConditionalWeakTable<GameObject, CopyAvatarRoot> copyAvatarRoots =
            new ConditionalWeakTable<GameObject, CopyAvatarRoot>();
        private sealed class CopyAvatarRoot { internal Component Marker; }
        internal const string UnknownRendererRelocation = "Modular Avatar / NDMF の処理で表情の対象Rendererが変わりましたが、元の変形との対応を一意に確定できません。統合・分割ツールの設定を確認してください。";
        private const string CompatibilityMessage =
            "アバターの準備に必要な NDMF API を利用できません。NDMF " + DependencyPolicy.NdmfMinimum + " 以降の 1.x（プレリリースは " + DependencyPolicy.NdmfPrereleases + "）が必要です。確認済み構成: MA " + DependencyPolicy.ModularAvatarReference + " / NDMF " + DependencyPolicy.NdmfReference + "。" + DependencyPolicy.Recovery;

#if UNITY_5_3_OR_NEWER
        private static readonly Dictionary<Scene, List<WeakReference>> copyScenes =
            new Dictionary<Scene, List<WeakReference>>();

        static NdmfExportPreparation()
        {
            EditorApplication.update += ReleaseUnusedCopyScene;
            AssemblyReloadEvents.beforeAssemblyReload += CloseCopyScenes;
            EditorApplication.quitting += CloseCopyScenes;
        }

        private static Scene CopyScene()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            copyScenes.Add(scene, new List<WeakReference>());
            return scene;
        }

        private static void RetainCopySceneForReport(GameObject clone, object context)
        {
            if (!copyScenes.TryGetValue(clone.scene, out var reports)) return;
            var report = ReadDiagnosticMember(context, "ErrorReport") ?? ReadDiagnosticMember(context, "_report");
            // NDMF's console retains the scene even after its avatar copy is
            // destroyed. Keep that empty scene valid while the report lives;
            // weak references neither erase other reports nor retain them.
            if (report != null) reports.Add(new WeakReference(report));
        }

        internal static void ReleaseUnusedCopyScene()
        {
            foreach (var entry in copyScenes.ToArray())
            {
                entry.Value.RemoveAll(reference => !reference.IsAlive);
                if (!entry.Key.IsValid() || (entry.Key.rootCount == 0 && entry.Value.Count == 0))
                {
                    if (entry.Key.IsValid()) EditorSceneManager.ClosePreviewScene(entry.Key);
                    copyScenes.Remove(entry.Key);
                }
            }
        }

        private static void CloseCopyScenes()
        {
            foreach (var scene in copyScenes.Keys)
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            copyScenes.Clear();
        }

        internal static void ShowOwnedCopyInPreview(GameObject copy, PreviewRenderUtility preview)
        {
            if (copy == null || !copyScenes.ContainsKey(copy.scene))
                throw new InvalidOperationException("Preview requires an owned copy scene.");
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            // AddSingleGO moves the avatar away from the scene captured by NDMF
            // and lets utility cleanup destroy that scene. Render our scene and
            // the utility's lights together, without moving or owning the avatar.
            preview.camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(copy.scene) |
                EditorSceneManager.GetSceneCullingMask(preview.camera.scene);
        }

        // The host protocol shim cannot clone Unity hierarchies; native tests
        // exercise staging, callback suppression and authored enabled states.
        // VRChat constraints run in edit mode but are read by ConstraintExport
        // on the copy; they only pose the copy as the source already is.
        internal static bool PreservesExportBehaviour(Behaviour behaviour) => behaviour != null && (
            behaviour.GetType().GetInterfaces().Any(contract => contract.FullName == "UniVRM10.IVrm10Constraint" || contract.FullName == "VRVlog.LilToonExporter.Vendor.UniVRM10.IVrm10Constraint") ||
            behaviour.GetType().FullName?.StartsWith("VRC.SDK3.Dynamics.Constraint.Components.", StringComparison.Ordinal) == true ||
            behaviour.GetType().FullName == PhysBoneSpringExport.PhysBoneType ||
            behaviour.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");

        internal static GameObject InstantiateOwnedCopy(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var host = new GameObject("VRVlog inactive copy staging");
            host.SetActive(false);
            GameObject copy = null;
            try
            {
                SceneManager.MoveGameObjectToScene(host, CopyScene());
                copy = Object.Instantiate(source, host.transform, true);
                // HideAndDontSave removes GameObjects from their native scene.
                // A preview scene provides isolation without invalidating the
                // scene captured by NDMF diagnostics.
                copy.name = source.name; copy.hideFlags = HideFlags.None;
                // A Missing Script has no type and appears as a null component.
                // Remove it from the owned copy only; the source keeps it.
                foreach (var transform in copy.GetComponentsInChildren<Transform>(true))
                    UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                // The project's own UniVRM components and MToon materials become
                // the exporter's vendored ones before any script runs on the copy.
                ForeignUniVrm.Adopt(copy);
                foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null) continue;
                    var type = behaviour.GetType();
                    var authoring = IsAuthoringTag(type) || type.GetInterfaces().Any(contract => contract.FullName == "VRC.SDKBase.IEditorOnly");
                    if (authoring || PreservesExportBehaviour(behaviour)) continue;
                    var editorCallbacks = false;
                    for (var current = type; current != null; current = current.BaseType)
                        editorCallbacks |= current.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == "UnityEngine.ExecuteAlways" ||
                            attribute.AttributeType.FullName == "UnityEngine.ExecuteInEditMode");
                    // Disabled editor scripts can still receive Awake on activation.
                    // Remove editor scripts outside export/authoring contracts
                    // before activation. Preserve inert runtime/metadata enabled
                    // states: PhysBones and constraints use them during export.
                    if (behaviour is MonoBehaviour && editorCallbacks) Object.DestroyImmediate(behaviour);
                    else if (behaviour is Animator) behaviour.enabled = false;
                }
                // A hidden avatar (or one under a hidden parent) exports too: only
                // the owned copy's root is shown, still under the inactive host,
                // so nothing runs before the cleanup above. The source is untouched.
                if (!copy.activeSelf) copy.SetActive(true);
                copy.transform.SetParent(null, true);
                return copy;
            }
            catch { if (copy != null) Object.DestroyImmediate(copy); throw; }
            finally { Object.DestroyImmediate(host); ReleaseUnusedCopyScene(); }
        }
#endif

        internal static bool NeedsProcessing(GameObject avatar) => avatar != null && RelevantAuthoring(avatar).Count != 0;

        internal Object IsolatedCopyOf(Object original) => original != null && isolatedAssets.TryGetValue(original, out var copy)
            ? copy : original;

        internal SkinnedMeshRenderer PreparedRendererFor(SkinnedMeshRenderer original) => PreparedRendererFor((Renderer)original) as SkinnedMeshRenderer;

        internal Renderer PreparedRendererFor(Renderer original)
        {
            if (ReferenceEquals(original, null)) return null;
            if (ambiguousRendererReplacements.Contains(original)) return null;
            if (rendererReplacements.TryGetValue(original, out var current)) return current;
            return original != null ? original : null;
        }

        // Find the prepared renderer that carries these shapes of a renderer
        // captured before the build passes. The shapes are looked up again by
        // name: first on the renderer itself (or its registered replacement),
        // then on the one renderer that gained all of them, such as the mesh a
        // pass merged it into. Renderers that already had a shape of that name
        // never count, and the one that gained them must also carry every other
        // authored shape of the original, so a lost shape cannot land on an
        // unrelated mesh that happens to use the same name.
        internal SkinnedMeshRenderer FollowShapes(SkinnedMeshRenderer original, IEnumerable<string> shapes)
        {
            if (ReferenceEquals(original, null)) return null;
            var names = (shapes ?? Enumerable.Empty<string>()).Where(name => name != null).Distinct(StringComparer.Ordinal).ToArray();
            var direct = PreparedRendererFor(original);
            if (names.Length == 0) return direct;
            bool Holds(SkinnedMeshRenderer skin) => skin != null && skin.sharedMesh != null && preparedRoot != null &&
                (skin.transform == preparedRoot.transform || skin.transform.IsChildOf(preparedRoot.transform)) &&
                names.All(name => Enumerable.Range(0, skin.sharedMesh.blendShapeCount)
                    .Count(index => string.Equals(skin.sharedMesh.GetBlendShapeName(index), name, StringComparison.Ordinal)) == 1);
            bool Shown(SkinnedMeshRenderer skin) => skin.enabled && skin.gameObject.activeInHierarchy;
            // A renderer that kept its shapes is still the authored one, even hidden.
            if (Holds(direct)) return direct;
            if (preparedRoot == null) return null;
            var authored = shapesBeforeProcessing.TryGetValue(original, out var originalShapes)
                ? originalShapes.Where(name => !name.StartsWith("__VRVlog_", StringComparison.Ordinal)).ToArray() : new string[0];
            bool CarriesAuthored(SkinnedMeshRenderer skin)
            {
                var present = new HashSet<string>(Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Select(skin.sharedMesh.GetBlendShapeName), StringComparer.Ordinal);
                return authored.All(present.Contains);
            }
            var gained = preparedRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(skin => skin != direct && Holds(skin) && Shown(skin) &&
                !(shapesBeforeProcessing.TryGetValue(skin, out var before) && names.Any(before.Contains)) && CarriesAuthored(skin)).ToArray();
            return gained.Length == 1 ? gained[0] : null;
        }

        internal SkinnedMeshRenderer FollowShape(SkinnedMeshRenderer original, string shape) => FollowShapes(original, new[] { shape });

        // Whether the renderer held the shape before the build passes. Still
        // answers after a pass destroyed or merged that renderer.
        internal bool HadShape(SkinnedMeshRenderer original, string shape) => !ReferenceEquals(original, null) && shape != null &&
            shapesBeforeProcessing.TryGetValue(original, out var before) && before.Contains(shape);

        private void CaptureShapes(GameObject root)
        {
            preparedRoot = root;
            shapesBeforeProcessing.Clear();
            if (root == null) return;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                shapesBeforeProcessing[skin] = new HashSet<string>(skin.sharedMesh == null ? Enumerable.Empty<string>() :
                    Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Select(skin.sharedMesh.GetBlendShapeName), StringComparer.Ordinal);
        }

        // Strict callers stop on a lost generated shape. Lenient callers keep
        // exporting and the bindings that used the shape are left out later.
        private void RequireMorphs(HashSet<string> required, GameObject clone, bool tolerate, ICollection<string> warnings)
        {
            var lost = required.Except(ExportMorphs(clone)).Except(LostMorphs).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (lost.Length == 0) return;
            if (!tolerate)
                throw new InvalidOperationException("Modular Avatar / NDMF の処理で書き出し用の表情が失われました。メッシュや BlendShape を変更する追加ツールの設定を確認してください。");
            LostMorphs.AddRange(lost);
            warnings?.Add(ExporterLocalization.T(LostGeneratedMorphs) + string.Join(", ", lost));
        }

        // NDMF's registry records provenance for diagnostic references. Read its
        // existing references without creating entries or guessing by shape/name.
        // Only one-to-one registered component replacement can carry a channel.
        private void CaptureRendererReplacements(GameObject clone, object context)
        {
            var registry = context?.GetType().GetProperty("ObjectRegistry", BindingFlags.Public | BindingFlags.Instance)?.GetValue(context);
            var contract = registry?.GetType().GetInterfaces().FirstOrDefault(type => type.FullName == "nadena.dev.ndmf.IObjectRegistry");
            var getReference = contract?.GetMethod("GetReference", new[] { typeof(Object), typeof(bool) });
            if (getReference == null) return;
            foreach (var current in clone.GetComponentsInChildren<Renderer>(true))
            {
                var reference = getReference.Invoke(registry, new object[] { current, false });
                var original = reference?.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance)?.GetValue(reference)
                    as Renderer;
                if (ReferenceEquals(original, null)) continue;
                // A build can retain the original disabled component. Its own
                // diagnostic reference is not a competing replacement.
                if (ReferenceEquals(original, current)) continue;
                if (rendererReplacements.TryGetValue(original, out var previous) && !ReferenceEquals(previous, current))
                {
                    ambiguousRendererReplacements.Add(original);
                    rendererReplacements.Remove(original);
                }
                else if (!ambiguousRendererReplacements.Contains(original)) rendererReplacements[original] = current;
            }
        }

        private static bool IsAuthoringTag(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.FullName == MaNamespace + "AvatarTagComponent" ||
                    current.FullName == "Anatawa12.AvatarOptimizer.AvatarTagComponent") return true;
            return type.GetInterfaces().Any(i => i.FullName == "nadena.dev.ndmf.INDMFEditorOnly" ||
                i.FullName == "nadena.dev.ndmf.runtime.INDMFEditorOnly");
        }

        private static HashSet<Component> RelevantAuthoring(GameObject avatar, Func<Transform, bool> excluded = null)
        {
            // Activation controls the avatar's initial appearance, not whether
            // authoring participates in a build. MA intentionally traverses
            // inactive components: an inactive Merge Animator can supply FX,
            // menu installers and reactive controls can activate clothing, and
            // another NDMF pass can generate their eventual dependencies. Only
            // the user's explicit export exclusions may omit authoring.
            return new HashSet<Component>(avatar.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && excluded?.Invoke(component.transform) != true &&
                    IsAuthoringTag(component.GetType()) && !IsCopyAvatarRoot(avatar, component)));
        }

        private static bool IsCopyAvatarRoot(GameObject clone, Component component) =>
            copyAvatarRoots.TryGetValue(clone, out var root) && ReferenceEquals(root.Marker, component);

        private static string FollowingProperty(Component component)
        {
            for (var type = component.GetType(); type != null; type = type.BaseType)
            {
                if (type.FullName == MaNamespace + "ModularAvatarMergeArmature") return "mergeTargetObject";
                if (type.FullName == MaNamespace + "ModularAvatarBoneProxy") return "target";
            }
            return null;
        }

        internal static Transform FollowingTarget(Component component)
        {
            var property = FollowingProperty(component);
            return property == null ? null : ReadFollowingTarget(component, property);
        }

        private static Transform ReadFollowingTarget(Component component, string property)
        {
            var getter = component.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            if (getter == null) throw new InvalidOperationException(CompatibilityMessage);
            var value = Invoke(() => getter.GetValue(component));
            // MA's runtime reference getter rejects an empty path before
            // checking the serialized direct target. Its inspector can still
            // resolve that exact authored target. Read it without editing the
            // source; the owned copy will restore MA's path before processing.
            if (value == null && property == "mergeTargetObject")
                value = ReadMaDirectTargetWithoutPath(component);
            return value is GameObject gameObject ? gameObject.transform : value as Transform;
        }

        private static GameObject ReadMaDirectTargetWithoutPath(Component component)
        {
            var type = FindType(MaNamespace + "AvatarObjectReference");
            var resolve = type?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(SerializedProperty) }, null);
            if (resolve?.ReturnType != typeof(GameObject)) return null;
            using (var serialized = new SerializedObject(component))
            {
                var reference = serialized.FindProperty("mergeTarget");
                if (reference == null) return null;
                var path = reference.FindPropertyRelative("referencePath");
                var direct = reference.FindPropertyRelative("targetObject");
                if (path?.propertyType != SerializedPropertyType.String || !string.IsNullOrEmpty(path.stringValue) ||
                    direct?.propertyType != SerializedPropertyType.ObjectReference || !(direct.objectReferenceValue is GameObject target)) return null;
                var resolved = Invoke(() => resolve.Invoke(null, new object[] { reference })) as GameObject;
                return resolved == target ? resolved : null;
            }
        }

        internal static void ValidateSource(GameObject source, Func<Transform, bool> excluded = null, bool deferUnresolvedTargets = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (var component in RelevantAuthoring(source, excluded))
            {
                var property = FollowingProperty(component);
                if (property == null) continue;
                var target = ReadFollowingTarget(component, property);
                if (target == null)
                {
                    if (deferUnresolvedTargets) continue; // Validate against the explicit root on the owned copy.
                    throw new InvalidOperationException(ScenePath(component.transform) + " (" + component.GetType().Name +
                        "): Modular Avatar の追従先を取得できません。Merge Armature / Bone Proxy の対象を設定してから書き出してください。");
                }
                if (target != source.transform && !target.IsChildOf(source.transform))
                    throw new InvalidOperationException(component.name + ": Modular Avatar の追従先が選択したアバターの外にあります。アバター全体を選ぶか、追従先を同じアバター内に設定してください。");
                if (excluded?.Invoke(target) == true)
                    throw new InvalidOperationException(component.name + ": Modular Avatar の追従先が書き出しの除外対象です。追従するオブジェクトも除外するか、除外設定を見直してください。");
            }
        }

        internal static void ValidateCopy(GameObject source, GameObject clone, Func<Transform, bool> excluded = null)
        {
            RequireOwnedCopy(source, clone);
            EnsureCopyAvatarRoot(clone);
            ResolveMaSceneReferences(clone);
            // Plugins resolve references, apply platform filters and generate
            // authoring during Resolving/Generating. Validate null targets only
            // after those canonical phases have supplied their final inputs.
            ValidateSource(clone, excluded, deferUnresolvedTargets: true);
        }

        private static void EnsureCopyAvatarRoot(GameObject clone)
        {
            // BuildContext adds this marker too, but MA's getters need the
            // explicit export root before our preflight and pruning run.
            if (!clone.GetComponentsInChildren<Component>(true).Any(component => component != null && IsMaTag(component.GetType()))) return;
            var marker = FindType("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot");
            if (marker == null) return; // The bridge reports a missing NDMF installation.
            if (!typeof(Component).IsAssignableFrom(marker)) throw new InvalidOperationException(CompatibilityMessage);
            if (clone.GetComponent(marker) == null)
                copyAvatarRoots.GetValue(clone, _ => new CopyAvatarRoot()).Marker = clone.AddComponent(marker);
        }

        internal static NdmfExportPreparation Prepare(GameObject source, GameObject clone, ICollection<string> warnings = null,
            Action<Material, Material> materialCopyObserver = null, Action<NdmfExportPreparation> afterTransforming = null,
            bool skipProcessing = false, bool tolerateLostMorphs = false)
        {
            RequireOwnedCopy(source, clone);
            // The build tools failed on an earlier attempt: export the copy as
            // authored, with their components left unprocessed.
            if (skipProcessing)
            {
                var unprocessed = new NdmfExportPreparation(); unprocessed.CaptureShapes(clone);
                try { afterTransforming?.Invoke(unprocessed); return unprocessed; }
                catch { unprocessed.Dispose(); throw; }
            }
            EnsureCopyAvatarRoot(clone);
            // Canonical resolution runs with the selected avatar root. Normalize
            // supported stale direct references only on this owned hierarchy.
            ResolveMaSceneReferences(clone);
            if (!NeedsProcessing(clone))
            {
                var lease = new NdmfExportPreparation(); lease.CaptureShapes(clone);
                try { afterTransforming?.Invoke(lease); return lease; }
                catch { lease.Dispose(); throw; }
            }
            ValidateSource(clone, deferUnresolvedTargets: true);
            var processor = FindType("nadena.dev.ndmf.AvatarProcessor");
            var package = processor != null ? PackageInfo.FindForAssembly(processor.Assembly) : null;
            var bridge = Bridge.Resolve(FindType, package?.version);
            return ProcessClone(source, clone, bridge, warnings, materialCopyObserver, afterTransforming, tolerateLostMorphs);
        }

        internal static NdmfExportPreparation ProcessClone(GameObject source, GameObject clone, Bridge bridge,
            ICollection<string> warnings = null, Action<Material, Material> materialCopyObserver = null,
            Action<NdmfExportPreparation> afterTransforming = null, bool tolerateLostMorphs = false)
        {
            RequireOwnedCopy(source, clone);
            EnsureCopyAvatarRoot(clone);
            // Preserve all included authoring, including disabled GameObjects;
            // NDMF/MA owns the build semantics and pass ordering for those tags.
            ResolveMaSceneReferences(clone);
            var lease = new NdmfExportPreparation();
            var sourceAssets = Dependencies(source);
            var cloneAssets = Dependencies(clone);
            var existing = new HashSet<Object>(sourceAssets);
            existing.UnionWith(cloneAssets);
            var requiredMorphs = ExportMorphs(clone);
            lease.CaptureShapes(clone);
            object context = null;
            IDisposable directoryScope = null;
            Exception processError = null;
            try
            {
                try
                {
                    RequireCopySceneReferences(clone, cloneAssets);
                    lease.IsolateSharedAssets(clone, sourceAssets, cloneAssets, materialCopyObserver);
                    // Legacy NDMF plugins (including LightLimitChanger) add
                    // subassets directly to context.AssetContainer. A null
                    // root selects NullAssetSaver and breaks every such pass.
                    // Use an exclusively owned folder for this export lease.
                    lease.CreateTemporaryAssetDirectory();
                    directoryScope = (IDisposable)Invoke(() => bridge.DirectoryScope.Invoke(new object[] { lease.temporaryAssetPath }));
                    if (bridge.PrimaryPlatform == null)
                        context = Invoke(() => bridge.Context.Invoke(new object[] { clone, lease.temporaryAssetPath, true }));
                    else
                    {
                        var platform = Invoke(() => bridge.PrimaryPlatform.Invoke(null, new object[] { clone })) ??
                            Invoke(() => bridge.GenericPlatform.GetValue(null));
                        if (platform == null) throw new InvalidOperationException(CompatibilityMessage);
                        context = Invoke(() => bridge.Context.Invoke(new object[] { clone, lease.temporaryAssetPath, platform, true }));
                    }
#if UNITY_5_3_OR_NEWER
                    RetainCopySceneForReport(clone, context);
#endif
                    lease.ObjectRegistry = context.GetType().GetProperty("ObjectRegistry", BindingFlags.Public | BindingFlags.Instance)?.GetValue(context);
                    try
                    {
                        Invoke(() => bridge.Process.Invoke(null, new[] { context, bridge.First, bridge.Generating }));
                        if (IsSuccessful(bridge, context))
                        {
                            // MA treats missing attachment targets as NonFatal,
                            // which NDMF.Successful does not reject. After the
                            // official resolvers and generators have run, ensure
                            // included attachments are safe and fully resolved
                            // before MA can silently omit a merge or proxy.
                            ResolveMaSceneReferences(clone);
                            ValidateSource(clone);
                            RequireCopySceneReferences(clone, Dependencies(clone));
                            Invoke(() => bridge.Process.Invoke(null, new[] { context, bridge.Transforming, bridge.Transforming }));
                        }
                        if (IsSuccessful(bridge, context))
                        {
                            // Prepared identities must be available while export
                            // evaluates the committed controller, before AAO.
                            lease.CaptureRendererReplacements(clone, context);
                            lease.RequireMorphs(requiredMorphs, clone, tolerateLostMorphs, warnings);
                            // Phase-end cleanup commits MA's virtual controller.
                            // Only export callers with this preparation callback
                            // can declare/remap morph dependencies for Optimizing.
                            // Preview callers retain their captured renderer IDs.
                            if (afterTransforming != null)
                            {
                                afterTransforming(lease);
                                // Every Optimizing pass sees the root as a normal NDMF build
                                // does. The one exception stands in for the com.vrmc.vrm
                                // Vrm10Instance, for which AAO skips the root controller: it
                                // does not know the bundled one, and without a platform
                                // component it parses the root controller and throws on null.
                                var animator = clone.GetComponent<Animator>();
                                UnityEditor.Animations.AnimatorStateMachine placeholderLayer = null;
                                var placeholder = animator != null && animator.runtimeAnimatorController == null
                                    && clone.GetComponent<VRVlog.LilToonExporter.Vendor.UniVRM10.Vrm10Instance>() != null
                                    && AvatarOptimizerParsesRootController(clone)
                                    ? PlaceholderController(out placeholderLayer) : null;
                                if (placeholder != null) animator.runtimeAnimatorController = placeholder;
                                try { Invoke(() => bridge.Process.Invoke(null, new[] { context, bridge.Optimizing, bridge.Optimizing })); }
                                finally
                                {
                                    if (placeholder != null)
                                    {
                                        if (animator != null && animator.runtimeAnimatorController == placeholder) animator.runtimeAnimatorController = null;
                                        Object.DestroyImmediate(placeholderLayer);
                                        Object.DestroyImmediate(placeholder);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception error) { processError = error; }
                    try { Invoke(() => bridge.Finish.Invoke(context, null)); }
                    catch (Exception error)
                    {
                        processError = processError == null ? error : new AggregateException(processError, error);
                    }
                    if (processError != null)
                    {
                        // A user cancellation in expression sampling remains a
                        // cancellation after Finish and owned-asset cleanup.
                        if (processError is OperationCanceledException) throw processError;
                        throw BuildFailure(context, processError);
                    }
                    // NDMF records plugin exceptions instead of always rethrowing.
                    RequireSuccessful(bridge, context);
                    lease.CaptureRendererReplacements(clone, context);
                    // The exporter callback validates scoped AAO mappings;
                    // legacy callers still require exact generated names.
                    if (afterTransforming == null) lease.RequireMorphs(requiredMorphs, clone, tolerateLostMorphs, warnings);
                    warnings?.Add("Modular Avatar / NDMF の衣装・追従・メッシュ編集を一時コピーに適用しました。");
                    return lease;
                }
                finally
                {
                    try
                    {
                        if (context != null && Invoke(() => bridge.AssetSaver.GetValue(context)) is IDisposable saver) saver.Dispose();
                    }
                    finally
                    {
                        try { directoryScope?.Dispose(); }
                        finally { lease.CaptureGenerated(clone, existing); }
                    }
                }
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        // The platform components for which AAO reads the platform's animators
        // instead of the root controller (AnimatorParser, avatar root animator).
        private static readonly string[] AvatarOptimizerPlatformTypes =
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", "VRM.VRMBlendShapeProxy", "UniVRM10.Vrm10Instance",
        };

        // AAO's own conditions for parsing the root controller: it parses the
        // animators only when the avatar has an AAO component, and the root
        // controller only without a platform component. On a VRChat avatar a lent
        // controller would serve no one and only be exposed to other plugins.
        private static bool AvatarOptimizerParsesRootController(GameObject clone) =>
            !clone.GetComponents<Component>().Any(c => c != null && AvatarOptimizerPlatformTypes.Contains(c.GetType().FullName)) &&
            clone.GetComponentsInChildren<Component>(true).Any(IsAvatarOptimizerComponent);

        private static bool IsAvatarOptimizerComponent(Component component)
        {
            if (component == null) return false;
            for (var type = component.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "Anatawa12.AvatarOptimizer.AvatarTagComponent") return true;
            return false;
        }

        // The lent controller keeps one empty layer: Animator::Sample, which
        // AnimationMode.SampleAnimationClip calls (as plugins that photograph menu
        // icons do in Optimizing), crashes Unity on a controller with no layers.
        // The state machine is returned so that cleanup does not depend on a
        // plugin leaving the controller's layers in place.
        private static UnityEditor.Animations.AnimatorController PlaceholderController(out UnityEditor.Animations.AnimatorStateMachine stateMachine)
        {
            var controller = new UnityEditor.Animations.AnimatorController { hideFlags = HideFlags.HideAndDontSave };
            stateMachine = new UnityEditor.Animations.AnimatorStateMachine { name = "Base Layer", hideFlags = HideFlags.HideAndDontSave };
            controller.AddLayer(new UnityEditor.Animations.AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = stateMachine });
            return controller;
        }

        private static void RequireSuccessful(Bridge bridge, object context)
        {
            if (!IsSuccessful(bridge, context)) throw BuildFailure(context, null);
        }

        private static bool IsSuccessful(Bridge bridge, object context) =>
            Invoke(() => bridge.Successful.GetValue(context)) is bool success && success;

        private static void RequireOwnedCopy(GameObject source, GameObject clone)
        {
            if (source == null || clone == null) throw new ArgumentNullException(source == null ? nameof(source) : nameof(clone));
            if (source == clone || clone.transform.IsChildOf(source.transform) || source.transform.IsChildOf(clone.transform) || EditorUtility.IsPersistent(clone))
                throw new InvalidOperationException("NDMF の準備には元のアバターから独立した書き出し用コピーが必要です。");
        }

        private static HashSet<Object> Dependencies(GameObject root) => Dependencies(root == null
            ? Array.Empty<Object>()
            : root.GetComponentsInChildren<Component>(true).Cast<Object>().Concat(new Object[] { root }));

        private static HashSet<Object> Dependencies(IEnumerable<Object> roots)
        {
            // CollectDependencies omits unsaved references in the native Editor.
            // Traverse the live serialized graph so copy ownership also covers
            // generated meshes and nested, unsaved authoring settings.
            var result = new HashSet<Object>();
            var pending = new Queue<Object>(roots);
            while (pending.Count > 0)
            {
                var value = pending.Dequeue();
                if (value == null || !result.Add(value) || value is Shader || value is ComputeShader || value is MonoScript) continue;
                foreach (var reference in References(value))
                {
                    // Every component of our hierarchy is already a root. An
                    // external scene edge must be diagnosed on its holder,
                    // not pull another avatar's assets into this ownership set.
                    if (!(reference is GameObject) && !(reference is Component)) pending.Enqueue(reference);
                }
            }
            return result;
        }

        private static bool IsMutableAsset(Object value) => value != null &&
            !(value is GameObject) && !(value is Component) && !(value is Shader) &&
            !(value is ComputeShader) && !(value is MonoScript);

        private static IEnumerable<Object> References(Object value)
        {
            // Mesh/texture payloads can contain millions of scalar elements,
            // but do not contain asset references that need remapping here.
            if (value is Mesh || value is Texture) yield break;
            using (var serialized = new SerializedObject(value))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        yield return property.objectReferenceValue;
            }
        }

        private static void RequireCopySceneReferences(GameObject clone, HashSet<Object> cloneAssets)
        {
            // Instantiate remaps scene references on cloned components, but a
            // shared ScriptableObject can still point into the original rig.
            // Do not guess a matching transform after exclusions or mesh edits.
            var holders = clone.GetComponentsInChildren<Component>(true).Where(value => value != null).Cast<Object>()
                .Concat(cloneAssets.Where(IsMutableAsset)).Distinct();
            foreach (var holder in holders)
            {
                if (holder is Mesh || holder is Texture) continue;
                using (var serialized = new SerializedObject(holder))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var reference = property.objectReferenceValue;
                        var target = reference is GameObject gameObject ? gameObject.transform :
                            (reference as Component)?.transform;
                        if (target != null && !EditorUtility.IsPersistent(reference) && !IsWithinCopy(target, clone))
                            throw new InvalidOperationException(holder.name + " (" + holder.GetType().Name + ") / " + property.propertyPath +
                                ": 設定に書き出し用コピーの外部を指すシーン参照が残っています。参照先: " + ScenePath(target) +
                                "。元のアバターやシーンを保護するため NDMF の処理を中止しました。" +
                                "対象コンポーネントの参照先を同じアバター内に設定し、衣装だけでなくアバター全体を選んで書き出してください。");
                    }
                }
            }
        }

        private static bool IsWithinCopy(Transform target, GameObject clone) =>
            target == clone.transform || target.IsChildOf(clone.transform);

        private static string ScenePath(Transform target)
        {
            var names = new Stack<string>();
            for (var node = target; node != null; node = node.parent) names.Push(node.name);
            return string.Join("/", names);
        }

        private static void ResolveMaSceneReferences(GameObject clone)
        {
            // MA deliberately falls back to the avatar-relative path when a
            // serialized targetObject belongs to a previous avatar. Resolve
            // that supported reference on our copy before inspecting raw edges.
            // Shared assets and ordinary component references remain untouched.
            var type = FindType(MaNamespace + "AvatarObjectReference");
            var resolve = type?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(SerializedProperty) }, null);
            if (resolve?.ReturnType != typeof(GameObject)) return;
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !IsMaTag(component.GetType())) continue;
                if (FollowingProperty(component) == "mergeTargetObject")
                {
                    var target = ReadMaDirectTargetWithoutPath(component);
                    if (target != null && IsWithinCopy(target.transform, clone))
                    {
                        // Use the same public authoring API as MA's inspector,
                        // including its avatar-root sentinel and cache update.
                        var reference = component.GetType().GetField("mergeTarget", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
                        var set = reference?.GetType().GetMethod("Set", new[] { typeof(GameObject) });
                        if (set == null) throw new InvalidOperationException(CompatibilityMessage);
                        Invoke(() => { set.Invoke(reference, new object[] { target }); return null; });
                    }
                }
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    var changed = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.Generic || property.type != type.Name) continue;
                        var direct = property.FindPropertyRelative("targetObject");
                        var path = property.FindPropertyRelative("referencePath");
                        if (direct?.propertyType != SerializedPropertyType.ObjectReference ||
                            path?.propertyType != SerializedPropertyType.String || string.IsNullOrEmpty(path.stringValue)) continue;
                        var stale = direct.objectReferenceValue as GameObject;
                        if (stale == null || EditorUtility.IsPersistent(stale) || IsWithinCopy(stale.transform, clone)) continue;
                        var resolved = Invoke(() => resolve.Invoke(null, new object[] { property })) as GameObject;
                        // Merge Armature's runtime getter includes MA's own
                        // special handling for duplicated Armature transforms.
                        // Preserve that effective target instead of choosing one.
                        if (property.propertyPath == "mergeTarget" && FollowingProperty(component) == "mergeTargetObject")
                            resolved = ReadFollowingTarget(component, "mergeTargetObject")?.gameObject;
                        if (resolved == null || !IsWithinCopy(resolved.transform, clone)) continue;
                        direct.objectReferenceValue = resolved;
                        changed = true;
                    }
                    if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static bool IsMaTag(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.FullName == MaNamespace + "AvatarTagComponent") return true;
            return false;
        }

        private void IsolateSharedAssets(GameObject clone, HashSet<Object> sourceAssets, HashSet<Object> cloneAssets,
            Action<Material, Material> materialCopyObserver)
        {
            // NullAssetSaver treats every nonpersistent asset as mutable. A
            // scene avatar can legitimately own unsaved meshes, clips, menus,
            // materials or textures; Instantiate(GameObject) shares these.
            var candidates = cloneAssets.Where(IsMutableAsset).ToArray();
            var toCopy = new HashSet<Object>(candidates.Where(value => sourceAssets.Contains(value) && !EditorUtility.IsPersistent(value)));
            if (toCopy.Count == 0) return;
            var references = candidates.ToDictionary(value => value, value => References(value).ToArray());
            // A persistent container may currently reference an unsaved child.
            // Copy that container too, so redirecting the child never edits it.
            bool added;
            do
            {
                added = false;
                foreach (var value in candidates)
                    if (EditorUtility.IsPersistent(value) && !toCopy.Contains(value) && references[value].Any(toCopy.Contains))
                        added |= toCopy.Add(value);
            } while (added);
            var replacements = new Dictionary<Object, Object>();
            foreach (var value in toCopy)
            {
                Object copy;
                try
                {
                    // Unity's native Instantiate remapper asserts on an
                    // Animator graph's strong state/behaviour pointers.
                    // Copy serialized fields, then redirect every reference in
                    // the same owned replacement map used below.
                    if (value is UnityEditor.Animations.AnimatorStateMachine)
                    {
                        copy = new UnityEditor.Animations.AnimatorStateMachine();
                        EditorUtility.CopySerialized(value, copy);
                    }
                    else if (value is UnityEditor.Animations.AnimatorState)
                    {
                        copy = new UnityEditor.Animations.AnimatorState();
                        EditorUtility.CopySerialized(value, copy);
                    }
                    else copy = Object.Instantiate(value);
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(value.name + ": 未保存のアバター素材を一時コピーに分離できません。素材をプロジェクト内のアセットとして保存してから書き出してください。", error);
                }
                if (copy == null || copy == value) throw new InvalidOperationException("アバター素材の一時コピーを作成できませんでした。");
                generated.Add(copy);
                copy.name = value.name;
                replacements.Add(value, copy);
                isolatedAssets.Add(value, copy);
                if (copy is Material material && value is Material original) materialCopyObserver?.Invoke(material, original);
            }
            var copiedDependencies = Dependencies(replacements.Values)
                .Where(value => IsMutableAsset(value) && !EditorUtility.IsPersistent(value) &&
                    !sourceAssets.Contains(value) && !cloneAssets.Contains(value)).ToArray();
            generated.UnionWith(copiedDependencies);
            var targets = clone.GetComponentsInChildren<Component>(true).Where(value => value != null).Cast<Object>()
                .Concat(replacements.Values).Concat(copiedDependencies)
                .Concat(cloneAssets.Where(value => IsMutableAsset(value) && !sourceAssets.Contains(value) && !EditorUtility.IsPersistent(value)))
                .Distinct();
            foreach (var target in targets)
            {
                if (target is Mesh || target is Texture) continue;
                using (var serialized = new SerializedObject(target))
                {
                    var property = serialized.GetIterator();
                    var changed = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var original = property.objectReferenceValue;
                        if (original == null || !replacements.TryGetValue(original, out var replacement)) continue;
                        property.objectReferenceValue = replacement;
                        changed = true;
                    }
                    if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private void CaptureGenerated(GameObject clone, HashSet<Object> existing)
        {
            foreach (var value in Dependencies(clone))
                if (value != null && !(value is GameObject) && !(value is Component) &&
                    !EditorUtility.IsPersistent(value) && !existing.Contains(value)) generated.Add(value);
        }

        private static HashSet<string> ExportMorphs(GameObject root)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (root == null) return result;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var name = mesh.GetBlendShapeName(index);
                    if (name.StartsWith("__VRVlog_Menu_", StringComparison.Ordinal) ||
                        name.StartsWith("__VRVlog_Anim_", StringComparison.Ordinal) ||
                        name.StartsWith("__VRVlog_Blink_", StringComparison.Ordinal) ||
                        name.StartsWith("__VRVlog_BlinkNone_", StringComparison.Ordinal)) result.Add(name);
                }
            }
            return result;
        }

        public void Dispose()
        {
            try
            {
                foreach (var value in generated)
                    if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                generated.Clear();
                isolatedAssets.Clear();
                rendererReplacements.Clear();
                ambiguousRendererReplacements.Clear();
                shapesBeforeProcessing.Clear();
                preparedRoot = null;
            }
            finally
            {
                if (temporaryAssetPath != null)
                {
                    var path = temporaryAssetPath;
                    var guid = temporaryAssetGuid;
                    temporaryAssetPath = temporaryAssetGuid = null;
                    // Do not remove a replacement folder or a moved/user asset.
                    if (AssetDatabase.IsValidFolder(path) && AssetDatabase.AssetPathToGUID(path) == guid)
                    {
                        if (!AssetDatabase.DeleteAsset(path))
                            Debug.LogWarning("VR Vlog: 書き出しの一時データを削除できませんでした: " + path);
                    }
                }
            }
        }

        private void CreateTemporaryAssetDirectory()
        {
            var name = "VRVlogExportTemp-" + Guid.NewGuid().ToString("N");
            var guid = AssetDatabase.CreateFolder("Assets", name);
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(guid) || path != "Assets/" + name || !AssetDatabase.IsValidFolder(path))
                throw new InvalidOperationException("書き出し用の一時保存先を作成できませんでした。UnityプロジェクトのAssetsフォルダーへの書き込み権限と空き容量を確認してください。");
            temporaryAssetPath = path;
            temporaryAssetGuid = guid;
        }

        private static InvalidOperationException BuildFailure(object context, Exception cause)
        {
            var details = new List<string>();
            var exceptions = new List<Exception>();
            if (cause != null) exceptions.Add(cause);
            try
            {
                var report = ReadDiagnosticMember(context, "ErrorReport") ?? ReadDiagnosticMember(context, "_report");
                if (ReadDiagnosticMember(report, "Errors") is System.Collections.IEnumerable errors)
                    foreach (var entry in errors)
                    {
                        var error = ReadDiagnosticMember(entry, "TheError");
                        var severity = ReadDiagnosticMember(error, "Severity")?.ToString();
                        if (severity != "Error" && severity != "InternalError") continue;
                        var plugin = ReadDiagnosticMember(entry, "Plugin");
                        var pluginName = ReadDiagnosticMember(plugin, "DisplayName") as string ?? "NDMF";
                        var pass = ReadDiagnosticMember(entry, "PassName") as string;
                        var exception = ReadDiagnosticMember(error, "Exception") as Exception;
                        if (exception != null) exceptions.Add(exception);
                        var message = exception?.Message ?? error?.GetType().GetMethod("ToMessage", Type.EmptyTypes)?.Invoke(error, null) as string;
                        if (string.IsNullOrWhiteSpace(message)) message = "処理中にエラーが発生しました。";
                        var detail = pluginName + (string.IsNullOrWhiteSpace(pass) ? "" : " / " + pass) + ": " + message.Trim();
                        if (detail.Length > 900) detail = detail.Substring(0, 900) + "…";
                        if (!details.Contains(detail)) details.Add(detail);
                        if (details.Count >= 8) break;
                    }
            }
            catch { /* Diagnostics must not replace the original processing failure. */ }
            var summary = "書き出し用コピーの処理を完了できませんでした。元のアバターは変更していません。";
            if (details.Count > 0) summary += "\n\n" + string.Join("\n", details);
            else if (cause != null) summary += "\n\n" + cause.Message;
            summary += "\n\nNDMFコンソールの該当ツールを開くと、対象オブジェクトを確認できます。「詳細をコピー」で原因の情報もコピーできます。";
            var inner = exceptions.Count == 1 ? exceptions[0] : exceptions.Count > 1 ? new AggregateException(exceptions) : null;
            return new InvalidOperationException(summary, inner);
        }

        private static object ReadDiagnosticMember(object owner, string name)
        {
            if (owner == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = owner.GetType();
            return type.GetProperty(name, flags)?.GetValue(owner) ?? type.GetField(name, flags)?.GetValue(owner);
        }

        private static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        private static object Invoke(Func<object> operation)
        {
            try { return operation(); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        // The phase-limited entry point and Finish are internal in the official
        // 1.6.0, 1.7.10, 1.8.3, 1.13.0 and 1.14.8 sources. Validate the whole
        // bridge before any processing. The same context must span every range
        // so plugins run once and the final extension cleanup can publish their
        // mappings. Before 1.8.0 NDMF has no platforms: the context takes no
        // platform, and Resolving is the first phase (1.8.0 adds FirstChance and
        // PlatformInit before it, reached through the internal First).
        internal sealed class Bridge
        {
            internal ConstructorInfo Context, DirectoryScope;
            internal MethodInfo Process, Finish, PrimaryPlatform;
            internal PropertyInfo Successful, AssetSaver, GenericPlatform;
            internal object First, Generating, Transforming, Optimizing;

            internal static Bridge Resolve(Func<string, Type> find, string version)
            {
                const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
                const BindingFlags staticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                if (!DependencyPolicy.SupportsNdmf(version)) throw new InvalidOperationException(CompatibilityMessage);
                var processor = find("nadena.dev.ndmf.AvatarProcessor");
                var context = find("nadena.dev.ndmf.BuildContext");
                var phase = find("nadena.dev.ndmf.BuildPhase");
                var provider = find("nadena.dev.ndmf.platform.INDMFPlatformProvider");
                var registry = find("nadena.dev.ndmf.platform.PlatformRegistry");
                var generic = find("nadena.dev.ndmf.platform.GenericPlatform");
                var directory = find("nadena.dev.ndmf.OverrideTemporaryDirectoryScope");
                if (new[] { processor, context, phase, directory }.Any(t => t == null))
                    throw new InvalidOperationException(CompatibilityMessage);
                var platforms = provider != null;
                if (platforms && (registry == null || generic == null)) throw new InvalidOperationException(CompatibilityMessage);
                var result = new Bridge
                {
                    Context = context.GetConstructor(platforms
                        ? new[] { typeof(GameObject), typeof(string), provider, typeof(bool) }
                        : new[] { typeof(GameObject), typeof(string), typeof(bool) }),
                    DirectoryScope = directory.GetConstructor(new[] { typeof(string) }),
                    Process = processor.GetMethod("ProcessAvatar", staticMembers, null, new[] { context, phase, phase }, null),
                    Finish = context.GetMethod("Finish", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null),
                    Successful = context.GetProperty("Successful", publicInstance),
                    AssetSaver = context.GetProperty("AssetSaver", publicInstance),
                    PrimaryPlatform = registry?.GetMethod("GetPrimaryPlatformForAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null),
                    GenericPlatform = generic?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static),
                    First = platforms ? phase.GetProperty("First", staticMembers)?.GetValue(null) : phase.GetField("Resolving", staticMembers)?.GetValue(null),
                    Generating = phase.GetField("Generating", staticMembers)?.GetValue(null),
                    Transforming = phase.GetField("Transforming", staticMembers)?.GetValue(null),
                    Optimizing = phase.GetField("Optimizing", staticMembers)?.GetValue(null)
                };
                if (result.Context == null || result.DirectoryScope == null || !typeof(IDisposable).IsAssignableFrom(directory) ||
                    result.Process?.ReturnType != typeof(void) || result.Finish?.ReturnType != typeof(void) ||
                    result.Successful?.PropertyType != typeof(bool) || result.Successful.GetMethod == null || result.AssetSaver?.GetMethod == null ||
                    (platforms && (result.PrimaryPlatform?.ReturnType != provider || result.GenericPlatform?.PropertyType != provider)) ||
                    !phase.IsInstanceOfType(result.First) || !phase.IsInstanceOfType(result.Generating) || !phase.IsInstanceOfType(result.Transforming) ||
                    !phase.IsInstanceOfType(result.Optimizing))
                    throw new InvalidOperationException(CompatibilityMessage);
                return result;
            }
        }
    }
}
