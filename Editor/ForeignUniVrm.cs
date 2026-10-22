using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF.UniUnlit;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using VRVlog.LilToonExporter.Vendor.VRM10.MToon10;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // The exporter writes VRM with its own copy of UniVRM, renamed under
    // VRVlog.LilToonExporter.Vendor. The project may also have UniVRM itself
    // (any version, installed in any way, or none), and an avatar set up with
    // it carries that UniVRM's components, settings assets and MToon
    // materials. Those types are only known here by name. The owned export
    // copy gets the vendored counterparts holding the same serialized values;
    // the source avatar and the user's assets are never changed.
    internal static class ForeignUniVrm
    {
        const string VendorPrefix = "VRVlog.LilToonExporter.Vendor.";
        static readonly string[] Namespaces = { "UniVRM10", "UniGLTF", "UniHumanoid" };

        internal static readonly IReadOnlyDictionary<string, string> Shaders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "VRM10/MToon10", MToon10Meta.UnityShaderName },
            { "VRM10/Universal Render Pipeline/MToon10", MToon10Meta.UnityUrpShaderName },
            { "UniGLTF/UniUnlit", UniUnlitUtil.ShaderName },
        };

        static readonly Dictionary<Type, Type> counterparts = new Dictionary<Type, Type>();
        // Assets made for copies, released once their copy is gone.
        static readonly List<KeyValuePair<GameObject, Object[]>> adopted = new List<KeyValuePair<GameObject, Object[]>>();
        // Settings read from a source avatar, rebuilt when the user edits them.
        // The source is kept to tell a reused instance ID from the same asset.
        static readonly Dictionary<int, (Object Source, long Stamp, Object[] Created)> settingsCache = new Dictionary<int, (Object, long, Object[])>();

        // The lists above are lost on a domain reload, but the DontSave
        // objects they track would stay in memory until the editor quits.
        // The copies are gone by then (NdmfExportPreparation closes their
        // scenes), and a later read converts the settings again.
        static ForeignUniVrm() => AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;

        internal static void ReleaseAll()
        {
            foreach (var pair in adopted) Release(pair.Value);
            adopted.Clear();
            foreach (var cached in settingsCache.Values) Release(cached.Created);
            settingsCache.Clear();
        }

        internal static bool IsForeign(Type type)
        {
            var space = type?.Namespace;
            return space != null && Namespaces.Any(name => space == name || space.StartsWith(name + ".", StringComparison.Ordinal));
        }

        internal static Type Counterpart(Type foreign)
        {
            if (!IsForeign(foreign)) return null;
            lock (counterparts)
            {
                if (counterparts.TryGetValue(foreign, out var known)) return known;
                var name = VendorPrefix + foreign.FullName;
                var found = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => assembly.GetName().Name.StartsWith(VendorPrefix, StringComparison.Ordinal))
                    .Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);
                counterparts.Add(foreign, found);
                return found;
            }
        }

        // The VRM settings authored on an avatar, from the exporter's own
        // Vrm10Instance or from the project's UniVRM one. Read only: the
        // converted copy of a project UniVRM asset must not be edited.
        internal static VRM10Object AuthoredSettings(GameObject avatar)
        {
            if (avatar == null) return null;
            if (avatar.TryGetComponent<Vrm10Instance>(out var own)) return own.Vrm;
            var instance = avatar.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component != null && IsForeign(component.GetType()) && Counterpart(component.GetType()) == typeof(Vrm10Instance));
            if (instance == null) return null;
            using (var serialized = new SerializedObject(instance))
            {
                var settings = serialized.FindProperty("Vrm")?.objectReferenceValue;
                if (settings == null || Counterpart(settings.GetType()) != typeof(VRM10Object)) return null;
                var stamp = Stamp(settings);
                if (settingsCache.TryGetValue(settings.GetInstanceID(), out var cached))
                {
                    if (ReferenceEquals(cached.Source, settings) && cached.Stamp == stamp && cached.Created.Length != 0 && cached.Created[0] != null)
                        return (VRM10Object)cached.Created[0];
                    Release(cached.Created);
                }
                var map = new Dictionary<Object, Object>();
                var created = new List<Object>();
                Convert(settings, map, created);
                CopyAll(map);
                settingsCache[settings.GetInstanceID()] = (settings, stamp, created.ToArray());
                return (VRM10Object)map[settings];
            }
        }

        // Replaces the project UniVRM components and MToon/UniUnlit materials
        // of an owned copy with the vendored ones. Call before anything runs
        // on the copy (it is still under an inactive host).
        internal static void Adopt(GameObject copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            Sweep();
            var map = new Dictionary<Object, Object>();
            var created = new List<Object>();
            var foreign = copy.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component != null && Counterpart(component.GetType()) != null).ToList();
            foreach (var group in foreign.GroupBy(component => component.gameObject))
                foreach (var byType in group.GroupBy(component => component.GetType()))
                {
                    var type = Counterpart(byType.Key);
                    var existing = group.Key.GetComponents(type);
                    var index = 0;
                    foreach (var component in byType)
                    {
                        // A RequireComponent of an earlier counterpart (Humanoid
                        // for Vrm10Instance) may have added this one already.
                        var target = index < existing.Length ? existing[index] : group.Key.AddComponent(type);
                        index++;
                        if (target == null) continue;
                        map.Add(component, target);
                        if (component is Behaviour from && target is Behaviour to) to.enabled = from.enabled;
                    }
                }
            foreach (var component in map.Keys.ToArray()) Convert(component, map, created);
            CopyAll(map);
            Remove(foreign);
            AdoptMaterials(copy, created);
            if (created.Count != 0) adopted.Add(new KeyValuePair<GameObject, Object[]>(copy, created.ToArray()));
        }

        // Creates vendored settings assets for the project UniVRM ones a
        // converted object refers to (VRM10Object and its expressions).
        static void Convert(Object source, Dictionary<Object, Object> map, List<Object> created)
        {
            if (!map.ContainsKey(source))
            {
                var type = Counterpart(source.GetType());
                if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type)) return;
                var value = ScriptableObject.CreateInstance(type);
                value.name = source.name; value.hideFlags = HideFlags.DontSave;
                map.Add(source, value); created.Add(value);
            }
            using (var serialized = new SerializedObject(source))
            {
                var property = serialized.GetIterator();
                var enter = true;
                while (property.Next(enter))
                {
                    enter = property.propertyType == SerializedPropertyType.Generic;
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_Script") continue;
                    var value = property.objectReferenceValue;
                    if (value is ScriptableObject && !map.ContainsKey(value) && Counterpart(value.GetType()) != null)
                        Convert(value, map, created);
                }
            }
        }

        static void CopyAll(Dictionary<Object, Object> map)
        {
            foreach (var pair in map) Copy(pair.Key, pair.Value, map);
        }

        // Copies every serialized value whose path and kind match. Fields an
        // older or newer UniVRM does not have keep the vendored defaults.
        static void Copy(Object source, Object target, Dictionary<Object, Object> map)
        {
            using (var from = new SerializedObject(source))
            using (var to = new SerializedObject(target))
            {
                var property = from.GetIterator();
                var enter = true;
                while (property.Next(enter))
                {
                    enter = false;
                    // Unity's own fields stay the target's; UniVRM's m_ fields (Humanoid's bones) are copied.
                    if (property.depth == 0 && UnityFields.Contains(property.name)) continue;
                    var other = to.FindProperty(property.propertyPath);
                    if (other == null || other.propertyType != property.propertyType) continue;
                    enter = property.propertyType == SerializedPropertyType.Generic;
                    switch (property.propertyType)
                    {
                        case SerializedPropertyType.ArraySize:
                        case SerializedPropertyType.Integer:
                        case SerializedPropertyType.Enum:
                        case SerializedPropertyType.LayerMask:
                        case SerializedPropertyType.Character:
                            if (other.propertyType == SerializedPropertyType.Integer && other.type == "long") other.longValue = property.longValue;
                            else other.intValue = property.intValue;
                            break;
                        case SerializedPropertyType.Boolean: other.boolValue = property.boolValue; break;
                        case SerializedPropertyType.Float: other.doubleValue = property.doubleValue; break;
                        case SerializedPropertyType.String: other.stringValue = property.stringValue; break;
                        case SerializedPropertyType.Color: other.colorValue = property.colorValue; break;
                        case SerializedPropertyType.Vector2: other.vector2Value = property.vector2Value; break;
                        case SerializedPropertyType.Vector3: other.vector3Value = property.vector3Value; break;
                        case SerializedPropertyType.Vector4: other.vector4Value = property.vector4Value; break;
                        case SerializedPropertyType.Quaternion: other.quaternionValue = property.quaternionValue; break;
                        case SerializedPropertyType.Rect: other.rectValue = property.rectValue; break;
                        case SerializedPropertyType.Bounds: other.boundsValue = property.boundsValue; break;
                        case SerializedPropertyType.Vector2Int: other.vector2IntValue = property.vector2IntValue; break;
                        case SerializedPropertyType.Vector3Int: other.vector3IntValue = property.vector3IntValue; break;
                        case SerializedPropertyType.AnimationCurve: other.animationCurveValue = property.animationCurveValue; break;
                        case SerializedPropertyType.ObjectReference:
                            var value = property.objectReferenceValue;
                            if (value != null && map.TryGetValue(value, out var replacement)) value = replacement;
                            // A project UniVRM object without a vendored counterpart cannot be held.
                            other.objectReferenceValue = value != null && IsForeign(value.GetType()) ? null : value;
                            break;
                    }
                }
                to.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static readonly HashSet<string> UnityFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
        };

        // Dependents first: Vrm10Instance requires Humanoid.
        static void Remove(List<MonoBehaviour> foreign)
        {
            var left = foreign.Where(component => component != null).ToList();
            while (left.Count != 0)
            {
                var free = left.Where(component => !left.Any(other => other != component && other.gameObject == component.gameObject &&
                    Requires(other.GetType(), component.GetType()))).ToList();
                if (free.Count == 0) free = left;
                foreach (var component in free) Object.DestroyImmediate(component);
                var remaining = left.Where(component => component != null).ToList();
                // Unity keeps a component another one requires; the rest is left to the copy's cleanup.
                if (remaining.Count == left.Count)
                {
                    Debug.LogWarning("VR Vlog: 他のコンポーネントが必要としているため、書き出し用の複製からUniVRMのコンポーネントを外せませんでした: " +
                        string.Join(", ", remaining.Select(component => component.GetType().FullName).Distinct()));
                    break;
                }
                left = remaining;
            }
        }

        static bool Requires(Type type, Type required) =>
            type.GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                .Any(attribute => new[] { attribute.m_Type0, attribute.m_Type1, attribute.m_Type2 }.Any(value => value != null && value.IsAssignableFrom(required)));

        static void AdoptMaterials(GameObject copy, List<Object> created)
        {
            var replacements = new Dictionary<Material, Material>();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || material.shader == null || !Shaders.TryGetValue(material.shader.name, out var name)) continue;
                    if (!replacements.TryGetValue(material, out var replacement))
                    {
                        var shader = Shader.Find(name);
                        if (shader == null) continue;
                        // Same property names: switching the shader keeps the values.
                        replacement = new Material(material) { name = material.name, hideFlags = HideFlags.DontSave };
                        var queue = material.renderQueue;
                        replacement.shader = shader;
                        replacement.renderQueue = queue;
                        replacements.Add(material, replacement);
                        created.Add(replacement);
                    }
                    materials[i] = replacement;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        static long Stamp(Object settings)
        {
            long stamp = EditorUtility.GetDirtyCount(settings);
            using (var serialized = new SerializedObject(settings))
            {
                var property = serialized.GetIterator();
                var enter = true;
                while (property.Next(enter))
                {
                    enter = property.propertyType == SerializedPropertyType.Generic;
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is ScriptableObject value)
                        stamp = stamp * 31 + EditorUtility.GetDirtyCount(value) + value.GetInstanceID();
                }
            }
            return stamp;
        }

        static void Sweep()
        {
            for (var i = adopted.Count - 1; i >= 0; i--)
            {
                if (adopted[i].Key != null) continue;
                Release(adopted[i].Value);
                adopted.RemoveAt(i);
            }
        }

        static void Release(IEnumerable<Object> values)
        {
            foreach (var value in values)
                if (value != null) Object.DestroyImmediate(value);
        }
    }
}
