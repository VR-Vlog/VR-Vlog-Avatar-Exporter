using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    public sealed class ExportGimmickOptions
    {
        public bool AutoExclude = true;
        public IEnumerable<GameObject> IncludedObjects = Array.Empty<GameObject>();
        // Components of Component findings to keep. Including their object
        // instead would keep everything below it (for the root, every gimmick).
        public IEnumerable<Component> IncludedComponents = Array.Empty<Component>();
    }

    // Component removes only the listed components from the export copy.
    internal enum GimmickExclusionUnit { Review, Renderer, Hierarchy, Component }

    internal sealed class ExportGimmickFinding
    {
        internal GameObject Target;
        internal Renderer Renderer;
        internal GimmickExclusionUnit Unit;
        internal string Reason;
        // Set for findings of a table rule (ExportGimmickRules).
        internal string RuleId;
        // "prefab" when the object comes from a listed prefab, else "structure".
        internal string Evidence;
        // Findings of one rule match share it. Keeping one member keeps all.
        internal GameObject Group;
        internal Component[] Components = Array.Empty<Component>();
    }

    // Author-declared fallback behavior and the rules in ExportGimmickRules
    // (component types, configured values, render texture loops) are evidence;
    // names, colors, primitive shapes and arbitrary scripts are not.
    internal static class ExportGimmickDetection
    {
        const string NadeControllerGuid = "e285f7b13023d4744948a36807877425";
        static readonly HashSet<string> PortableShaders = new HashSet<string>(StringComparer.Ordinal)
        {
            "Standard", "Standard (Specular setup)", "VRM/MToon", "VRM10/MToon10",
            "UniGLTF/UniUnlit", "Unlit/Color", "Unlit/Texture", "Unlit/Transparent",
            "Unlit/Transparent Cutout", "VRM/UnlitTexture", "VRM/UnlitCutout",
            "VRM/UnlitTransparent", "VRM/UnlitTransparentZWrite"
        };

        internal static bool Portable(Material material) => material != null && material.shader != null &&
            (LilToonMaterialReader.IsLilToon(material) || PortableShaders.Contains(material.shader.name));

        internal static bool HiddenFallback(Material material) => material != null && material.shader != null &&
            !Portable(material) && string.Equals(material.GetTag("VRCFallback", true, "").Trim(), "Hidden", StringComparison.OrdinalIgnoreCase);

        // avatarRoot: activity is judged inside the avatar (see ExportRendererSelection.ActiveUnder).
        internal static ExportGimmickFinding Inspect(Renderer renderer, Transform avatarRoot = null)
        {
            if (renderer == null || !renderer.enabled || !ExportRendererSelection.ActiveUnder(renderer.gameObject, avatarRoot)) return null;
            // Other renderer types have their own export semantics.
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null && !(renderer is MeshRenderer)) return null;
            var mesh = skin != null ? skin.sharedMesh : renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
            var materials = renderer.sharedMaterials;
            var finding = new ExportGimmickFinding { Target = renderer.gameObject, Renderer = renderer };
            if (mesh == null || mesh.subMeshCount == 0 || materials.Length < mesh.subMeshCount ||
                materials.Length == 0 || materials.Any(m => m == null || m.shader == null))
                finding.Reason = "メッシュまたは材質が不足しているため自動判定できません。";
            else if (materials.All(Portable)) return null;
            else if (materials.All(HiddenFallback))
            {
                finding.Unit = GimmickExclusionUnit.Renderer;
                finding.Reason = "未対応シェーダーに、代替表示では隠す指定があります（VRCFallback=Hidden）。";
            }
            else finding.Reason = materials.Any(HiddenFallback)
                ? "通常の材質と補助演出が混在しているため、そのまま含めます。"
                : "未対応シェーダーですが、非表示の指定がないため、そのまま含めます。";
            return finding;
        }

        internal static List<ExportGimmickFinding> Analyze(GameObject source, Func<Transform, bool> manuallyExcluded = null) =>
            Analyze(source, ExportGimmickRules.All, manuallyExcluded);

        internal static List<ExportGimmickFinding> Analyze(GameObject source, IEnumerable<ExportGimmickRule> rules, Func<Transform, bool> manuallyExcluded)
        {
            var result = new List<ExportGimmickFinding>();
            if (source == null) return result;
            bool Excluded(Transform t) => manuallyExcluded?.Invoke(t) == true;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (Excluded(renderer.transform)) continue;
                var finding = Inspect(renderer, source.transform);
                if (finding != null) result.Add(finding);
            }
            var matched = new List<ExportGimmickFinding>();
            var claimed = new List<Transform>();
            bool Claimed(Transform t) => claimed.Any(root => t == root || t.IsChildOf(root));
            foreach (var rule in rules)
                foreach (var candidate in Candidates(source, rule, Excluded))
                {
                    // A structure around a claimed gimmick is that gimmick's
                    // container (an outfit or prop group), not a second gimmick.
                    if (Claimed(candidate) || !rule.Anchored && claimed.Any(root => root.IsChildOf(candidate))) continue;
                    var findings = Findings(source, rule, candidate, Excluded, Claimed);
                    claimed.AddRange(findings.Where(f => f.Unit != GimmickExclusionUnit.Component).Select(f => f.Target.transform));
                    matched.AddRange(findings);
                }
            result.InsertRange(0, matched);
            return result;
        }

        internal static bool Kept(Transform target, IEnumerable<GameObject> included) => target != null &&
            (included ?? Array.Empty<GameObject>()).Any(go => go != null && (target == go.transform || target.IsChildOf(go.transform)));

        internal static IEnumerable<GameObject> AutomaticRoots(IEnumerable<ExportGimmickFinding> findings, ExportGimmickOptions options) =>
            Automatic(findings, options).Where(f => f.Unit == GimmickExclusionUnit.Hierarchy).Select(f => f.Target).ToArray();

        // Components removed from the export copy before the build tools run.
        internal static IEnumerable<ExportGimmickFinding> AutomaticComponents(IEnumerable<ExportGimmickFinding> findings, ExportGimmickOptions options) =>
            Automatic(findings, options).Where(f => f.Unit == GimmickExclusionUnit.Component).ToArray();

        static IEnumerable<ExportGimmickFinding> Automatic(IEnumerable<ExportGimmickFinding> findings, ExportGimmickOptions options)
        {
            if (options?.AutoExclude == false) return Array.Empty<ExportGimmickFinding>();
            var all = findings.Where(f => f.Target != null).ToArray();
            var kept = (options?.IncludedObjects ?? Array.Empty<GameObject>()).Where(go => go != null).ToArray();
            // A kept child also keeps the hierarchy around it. Components stay
            // only for their own object or a kept parent of it.
            var keptComponents = new HashSet<Component>((options?.IncludedComponents ?? Array.Empty<Component>()).Where(c => c != null));
            bool KeptFinding(ExportGimmickFinding f) => Kept(f.Target.transform, kept) ||
                (f.Unit == GimmickExclusionUnit.Component
                    ? f.Components.Any(keptComponents.Contains)
                    : kept.Any(go => go.transform.IsChildOf(f.Target.transform)));
            var keptGroups = new HashSet<GameObject>(all.Where(f => f.Group != null && KeptFinding(f)).Select(f => f.Group));
            return all.Where(f => (f.Unit == GimmickExclusionUnit.Hierarchy || f.Unit == GimmickExclusionUnit.Component) &&
                !KeptFinding(f) && (f.Group == null || !keptGroups.Contains(f.Group))).ToArray();
        }

        internal static bool KnownNadeRoot(GameObject target) => target != null &&
            Matches(ExportGimmickRules.All.First(rule => rule.Id == ExportGimmickRules.PettingSystem), target.transform);

        internal static bool IsType(Component component, IEnumerable<string> names)
        {
            if (component == null) return false;
            var type = component.GetType();
            return names.Any(name => name.IndexOf('.') >= 0 ? type.FullName == name : type.Name == name);
        }

        static IEnumerable<Transform> Candidates(GameObject source, ExportGimmickRule rule, Func<Transform, bool> excluded)
        {
            // Build tools read anchored settings wherever they are, so they are
            // found on the avatar root and on inactive objects too. Activity of
            // the rest is judged inside the avatar, whose root may be inactive.
            var candidates = source.GetComponentsInChildren<Transform>(true).Where(t => !excluded(t) &&
                (rule.Anchored || t != source.transform && ExportRendererSelection.ActiveUnder(t.gameObject, source.transform)) && Matches(rule, t)).ToList();
            // A structure found again inside a match belongs to the inner gimmick:
            // an outfit that holds one is not the gimmick itself.
            if (!rule.Anchored) candidates.RemoveAll(c => candidates.Any(other => other != c && other.IsChildOf(c)));
            return candidates;
        }

        internal static bool Matches(ExportGimmickRule rule, Transform target)
        {
            if (rule == null || target == null || !rule.HasEvidence) return false;
            var components = target.GetComponents<Component>();
            if (rule.Anchored && !components.Any(c => IsType(c, rule.AnchorTypes))) return false;
            if (rule.EntryTypes.Length > 0 && !components.Any(c => IsType(c, rule.EntryTypes))) return false;
            if (rule.RequiredChildren.Any(path => target.Find(path) == null)) return false;
            if (!rule.RequiredValues.All(value => HasValue(target.Find(value.Path), value))) return false;
            if (rule.ShaderNames.Length > 0 && !target.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                renderer.sharedMaterials.Any(m => m != null && m.shader != null && rule.ShaderNames.Contains(m.shader.name)))) return false;
            if (!rule.RenderTextureLoop) return true;
            // The camera and its display belong to the nearest entry above them,
            // so a group holding an entried gimmick is not that gimmick.
            if (rule.EntryTypes.Length == 0) return HasRenderTextureLoop(target);
            bool Owned(Transform t)
            {
                for (var at = t; at != null; at = at.parent)
                {
                    if (at.GetComponents<Component>().Any(c => IsType(c, rule.EntryTypes))) return at == target;
                    if (at == target) return false;
                }
                return false;
            }
            return HasRenderTextureLoop(target, Owned);
        }

        static bool HasValue(Transform target, ExportGimmickValue value)
        {
            if (target == null) return false;
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != value.Type) continue;
                using var serialized = new SerializedObject(component);
                var property = serialized.FindProperty(value.Property);
                if (property != null && property.propertyType == SerializedPropertyType.String && property.stringValue == value.Value) return true;
            }
            return false;
        }

        internal static bool HasRenderTextureLoop(Transform root, Func<Transform, bool> owned = null)
        {
            var rendered = new HashSet<Texture>(root.GetComponentsInChildren<Camera>(true)
                .Where(camera => camera.targetTexture != null && owned?.Invoke(camera.transform) != false).Select(camera => (Texture)camera.targetTexture));
            if (rendered.Count == 0) return false;
            return root.GetComponentsInChildren<Renderer>(true).Where(renderer => owned?.Invoke(renderer.transform) != false).Any(renderer => renderer.sharedMaterials.Any(material =>
                material != null && material.GetTexturePropertyNameIDs().Any(id => rendered.Contains(material.GetTexture(id)))));
        }

        static bool FromPrefab(Transform target, string[] guids)
        {
            if (guids.Length == 0) return false;
            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(target.gameObject);
            return original != null && guids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)));
        }

        static List<ExportGimmickFinding> Findings(GameObject source, ExportGimmickRule rule, Transform candidate,
            Func<Transform, bool> excluded, Func<Transform, bool> claimed)
        {
            var roots = new List<Transform>();
            var removed = new List<Component>();
            if (rule.Anchored)
            {
                var components = candidate.GetComponents<Component>();
                var anchors = components.Where(c => IsType(c, rule.AnchorTypes)).ToArray();
                if (candidate != source.transform && candidate.childCount == 0 &&
                    components.All(c => c is Transform || IsType(c, rule.AnchorTypes) || IsType(c, rule.DedicatedTypes)))
                    roots.Add(candidate);
                else removed.AddRange(anchors);
                foreach (var anchor in anchors) roots.AddRange(LinkedObjects(source, anchor, rule.LinkedObjectProperties));
                foreach (var path in rule.GeneratedRootChildren)
                    for (var i = 0; i < source.transform.childCount; i++)
                        if (source.transform.GetChild(i).name == path) roots.Add(source.transform.GetChild(i));
            }
            else roots.Add(candidate);
            roots = roots.Where(t => t != null && t != source.transform && t.IsChildOf(source.transform) && !excluded(t) && !claimed(t))
                .Distinct().ToList();
            roots.RemoveAll(t => roots.Any(other => other != t && t.IsChildOf(other)));
            // References from anywhere in the same match are the gimmick's own.
            bool Own(Component component) => removed.Contains(component) ||
                roots.Any(root => component.transform == root || component.transform.IsChildOf(root));
            var evidence = FromPrefab(candidate, rule.PrefabGuids) ? "prefab" : "structure";
            var findings = new List<ExportGimmickFinding>();
            foreach (var root in roots)
            {
                var reason = rule.UnsafeReason?.Invoke(source, root, excluded) ?? SharedUnsafeReason(source, root, excluded, Own);
                findings.Add(new ExportGimmickFinding
                {
                    Target = root.gameObject,
                    Unit = reason == null ? GimmickExclusionUnit.Hierarchy : GimmickExclusionUnit.Review,
                    Reason = reason ?? rule.Reason,
                    RuleId = rule.Id,
                    Evidence = evidence,
                    Group = candidate.gameObject
                });
                // A settings object kept for review still loses its settings:
                // removing only the component keeps what refers to the object.
                if (reason != null && root == candidate && rule.Anchored)
                    removed.AddRange(candidate.GetComponents<Component>().Where(c => IsType(c, rule.AnchorTypes)));
            }
            if (removed.Count > 0)
                findings.Add(new ExportGimmickFinding
                {
                    Target = candidate.gameObject,
                    Unit = GimmickExclusionUnit.Component,
                    Reason = rule.Reason,
                    RuleId = rule.Id,
                    Evidence = evidence,
                    Group = candidate.gameObject,
                    Components = removed.ToArray()
                });
            return findings;
        }

        static IEnumerable<Transform> LinkedObjects(GameObject source, Component anchor, string[] properties)
        {
            if (properties.Length == 0) return Array.Empty<Transform>();
            var found = new List<Transform>();
            void Add(SerializedProperty property)
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) return;
                var value = property.objectReferenceValue;
                var target = value is GameObject go ? go.transform : (value as Component)?.transform;
                // Prefab assets and objects of other avatars are not part of this copy.
                if (target != null && target.IsChildOf(source.transform)) found.Add(target);
            }
            using var serialized = new SerializedObject(anchor);
            foreach (var name in properties)
            {
                var property = serialized.FindProperty(name);
                if (property == null) continue;
                if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
                    for (var i = 0; i < property.arraySize; i++) Add(property.GetArrayElementAtIndex(i));
                else Add(property);
            }
            return found;
        }

        internal static string UnsafeRootReason(GameObject source, Transform root, Func<Transform, bool> manuallyExcluded) =>
            PettingSystemUnsafeReason(source, root, manuallyExcluded) ?? SharedUnsafeReason(source, root, manuallyExcluded, _ => false);

        // Rules for the petting system only: it is removed as a whole only when
        // every part is one of its known, invisible pieces.
        internal static string PettingSystemUnsafeReason(GameObject source, Transform root, Func<Transform, bool> manuallyExcluded)
        {
            bool Within(Transform t) => t != null && (t == root || t.IsChildOf(root));
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (manuallyExcluded(renderer.transform)) continue;
                // Inactive ordinary wardrobe parts are also evidence against
                // deleting the entire system, even though they aren't exported.
                var materials = renderer.sharedMaterials;
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (mesh == null || mesh.subMeshCount == 0 || materials.Length < mesh.subMeshCount || materials.Any(m => m == null || m.shader == null))
                    return "メッシュまたは材質が不足しているため、システム全体は残します。";
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || materials.Length == 0 || !materials.All(HiddenFallback))
                    return "通常の表示物を含むため、システム全体は残して補助Rendererだけを判定します。";
            }
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) return "不明なコンポーネントを含むため、システム全体は残します。";
                if (manuallyExcluded(component.transform)) continue;
                var name = component.GetType().Name;
                if (AppearanceChangers.Contains(name)) return "見た目を変更する設定を含むため、システム全体は残します。";
                if (!KnownNadeComponent(component)) return "未確認の設定が追加されているため、システム全体は残します。";
                if (name == "ModularAvatarMergeAnimator")
                {
                    using var serialized = new SerializedObject(component);
                    var mode = serialized.FindProperty("pathMode");
                    var reference = serialized.FindProperty("relativePathRoot.targetObject")?.objectReferenceValue as GameObject;
                    var path = serialized.FindProperty("relativePathRoot.referencePath")?.stringValue;
                    var controller = serialized.FindProperty("animator")?.objectReferenceValue as RuntimeAnimatorController;
                    if (mode == null || mode.intValue != 0 || reference != null && !Within(reference.transform) || !string.IsNullOrEmpty(path) || !KnownNadeController(controller))
                        return "システム外の見た目を変更する可能性があるため、システム全体は残します。";
                }
            }
            return null;
        }

        static readonly HashSet<string> AppearanceChangers = new HashSet<string>(StringComparer.Ordinal)
        {
            "ModularAvatarShapeChanger", "ModularAvatarObjectToggle", "ModularAvatarMaterialSetter",
            "ModularAvatarMaterialSwap", "ModularAvatarMeshCutter"
        };

        // Checks for every rule. ownReference tells which outside components
        // belong to the same gimmick and may point into the root.
        static string SharedUnsafeReason(GameObject source, Transform root, Func<Transform, bool> manuallyExcluded, Func<Component, bool> ownReference)
        {
            bool Within(Transform t) => t != null && (t == root || t.IsChildOf(root));
            foreach (var animator in source.GetComponentsInChildren<Animator>(true))
                if (animator.isHuman)
                    for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        if (Within(HumanoidBones.Resolve(animator, (HumanBodyBones)i)))
                            return "Humanoidの骨を含むため、システム全体は残します。";
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || manuallyExcluded(component.transform)) continue;
                // An outfit merged into the armature is never part of a gimmick.
                if (component.GetType().Name == "ModularAvatarMergeArmature") return "衣装の統合設定を含むため、システム全体は残します。";
                if (!AppearanceChangers.Contains(component.GetType().Name)) continue;
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    var target = value is GameObject go ? go.transform : (value as Component)?.transform;
                    if (target != null && !Within(target)) return "システム外の見た目を変更する設定を含むため、システム全体は残します。";
                }
            }
            // Transform parent/children references express hierarchy ownership;
            // all other incoming component references must survive the export.
            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || Within(component.transform) || manuallyExcluded(component.transform) || ownReference(component)) continue;
                // MA also stores connections as avatar-relative paths or
                // humanoid bone/subpath pairs, without an ObjectReference.
                try
                {
                    if (Within(NdmfExportPreparation.FollowingTarget(component)))
                        return "残す衣装のMA接続先を含むため、システム全体は残します。";
                }
                catch (Exception)
                {
                    return "MAの接続先を確認できないため、システム全体は残します。";
                }
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    var target = value is GameObject go ? go.transform : (value as Component)?.transform;
                    if (Within(target)) return "残すオブジェクトから参照されているため、システム全体は残します。";
                }
            }
            return null;
        }

        static bool KnownNadeComponent(Component component)
        {
            var type = component.GetType();
            if (component is Animator animator) return animator.avatar == null && KnownNadeController(animator.runtimeAnimatorController);
            if (component is Transform || component is MeshFilter || component is MeshRenderer || component is SkinnedMeshRenderer ||
                component is AudioSource || component is Light || component is UnityEngine.Animations.IConstraint) return true;
            var fullName = type.FullName;
            if (fullName == "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver" ||
                fullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone" ||
                fullName == "VRC.SDK3.Avatars.Components.VRCSpatialAudioSource") return true;
            return type.Namespace == "nadena.dev.modular_avatar.core" &&
                (type.Name == "ModularAvatarMergeAnimator" || type.Name == "ModularAvatarMenuInstaller" ||
                 type.Name == "ModularAvatarParameters" || type.Name == "ModularAvatarBoneProxy" || type.Name == "ModularAvatarMenuItem" ||
                 type.Name == "ModularAvatarConvertConstraints");
        }

        static bool KnownNadeController(RuntimeAnimatorController controller) => controller == null ||
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(controller)) == NadeControllerGuid;
    }
}
