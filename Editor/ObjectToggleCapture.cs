using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using VRVlog.ObjectToggles;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_object_toggles: the GameObjects that the Expression Menu's toggles
    // switch on and off, read from the prepared copy after the build passes
    // (Modular Avatar menus and Object Toggles are plain menu entries and FX
    // layers by then). The FX is played once per value of each toggle's
    // parameter with every other parameter at its default; the nodes whose
    // activeSelf differs between the values are the group's targets. Targets
    // that are off by default are turned on in the copy so their meshes are
    // written; the extension's default state hides them again in the app.
    internal sealed class ObjectToggleCapture
    {
        internal const string NonIntegralWarning = "値が整数でないメニュー項目は、表示切り替えに含めませんでした: ";
        internal const string ConflictWarning = "複数のパラメーターが切り替えるオブジェクトは、メニューで先の項目だけに含めました: ";
        internal const string LimitWarning = "表示切り替えの上限を超えたため、次のパラメーターを含めませんでした: ";
        internal const string MissingWarning = "書き出しで無くなったオブジェクトを、表示切り替えから除きました: ";

        // Seconds of FX played per sample, in fixed steps, so transitions and
        // short default states settle before activeSelf is read.
        const int Frames = 40;
        const float FrameStep = .05f;
        const int ValueLimit = 1000000;

        sealed class Group
        {
            internal string Parameter, Name;
            internal int DefaultValue;
            internal readonly List<GameObject> Targets = new List<GameObject>();
            internal readonly List<string> Paths = new List<string>();
            internal readonly List<(int Value, bool[] Active)> States = new List<(int, bool[])>();
        }

        sealed class Item
        {
            internal int Index;
            internal string Name, Parameter;
            internal int Value;
            internal string[] Path;
        }

        readonly List<Group> groups = new List<Group>();
        readonly List<Item> items = new List<Item>();

        // Returns null when there is nothing to write (no VRChat SDK, no menu,
        // no FX or no toggle that switches an object).
        internal static ObjectToggleCapture Capture(GameObject clone, VrChatMenuImportPolicy policy, ICollection<string> warnings)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            var menu = VrChatExpressionMenu.Read(clone, policy);
            if (menu.Controller == null) return null;
            var toggles = menu.Entries.Select((entry, index) => (Entry: entry, Index: index))
                .Where(pair => pair.Entry.ControlType == "Toggle" && !string.IsNullOrEmpty(pair.Entry.ControlParameter) &&
                               pair.Entry.Parameters.ContainsKey(pair.Entry.ControlParameter)).ToList();
            if (toggles.Count == 0) return null;
            var transforms = clone.GetComponentsInChildren<Transform>(true);
            // Only nodes that hold a mesh themselves or below can be shown or
            // hidden in the app; the root is never a target.
            var withMesh = new HashSet<Transform>();
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    for (var at = renderer.transform; at != null && at != clone.transform && withMesh.Add(at); at = at.parent) { }
            var candidates = Enumerable.Range(1, transforms.Length - 1).Where(i => withMesh.Contains(transforms[i])).ToArray();
            if (candidates.Length == 0) return null;
            var result = new ObjectToggleCapture();
            using var sampler = new Sampler(clone, menu.Controller);
            var samples = new Dictionary<(string, int), bool[]>();
            bool[] atDefaults = null;
            bool[] Sample(string parameter, int value, int defaultValue)
            {
                // A parameter at its own default leaves every parameter at its
                // default, so all of them share one sample. Each sample costs a
                // new graph played for two seconds, the bulk of this stage.
                if (value == defaultValue && atDefaults != null) return atDefaults;
                if (samples.TryGetValue((parameter, value), out var active)) return active;
                var values = new Dictionary<string, float>(menu.Defaults, StringComparer.Ordinal) { [parameter] = value };
                var all = sampler.Sample(values);
                active = samples[(parameter, value)] = candidates.Select(i => all[i]).ToArray();
                if (value == defaultValue) atDefaults = active;
                return active;
            }
            var claimed = new HashSet<int>();
            var targetCount = 0;
            foreach (var parameterEntries in toggles.GroupBy(pair => pair.Entry.ControlParameter, StringComparer.Ordinal))
            {
                var parameter = parameterEntries.Key;
                if (parameter.Length > 1024)
                {
                    warnings?.Add(ExporterLocalization.T(LimitWarning) + Clip(parameter));
                    continue;
                }
                var rawDefault = menu.Defaults.TryGetValue(parameter, out var known) ? known : sampler.Default(parameter);
                if (!Integral(rawDefault, out var defaultValue))
                {
                    warnings?.Add(ExporterLocalization.T(NonIntegralWarning) + parameter);
                    continue;
                }
                var groupItems = new List<Item>();
                foreach (var (entry, index) in parameterEntries)
                {
                    if (!Integral(entry.Parameters[parameter], out var value))
                    {
                        warnings?.Add(ExporterLocalization.T(NonIntegralWarning) + entry.Name);
                        continue;
                    }
                    if (value == 0) continue;
                    var item = new Item { Index = index, Name = Clip(entry.ExportName), Parameter = parameter, Value = value, Path = PathOf(entry) };
                    // The same submenu read under other parent values repeats its entries.
                    if (!groupItems.Any(other => other.Value == item.Value && other.Name == item.Name && other.Path.SequenceEqual(item.Path)))
                        groupItems.Add(item);
                }
                if (groupItems.Count == 0) continue;
                var values = new[] { 0, defaultValue }.Concat(groupItems.Select(item => item.Value)).Distinct().OrderBy(value => value).ToList();
                var states = values.ToDictionary(value => value, value => Sample(parameter, value, defaultValue));
                // A node is a target when the values disagree on it; one that
                // an earlier group already switches stays with that group.
                var columns = Enumerable.Range(0, candidates.Length).Where(column => states.Values.Any(state => state[column] != states[0][column])).ToList();
                foreach (var column in columns.Where(claimed.Contains))
                    warnings?.Add(ExporterLocalization.T(ConflictWarning) + PathOf(clone, transforms[candidates[column]]));
                columns.RemoveAll(claimed.Contains);
                // Turning an item on or off must change something.
                groupItems.RemoveAll(item => columns.All(column => states[item.Value][column] == states[0][column]));
                if (groupItems.Count == 0) continue;
                values.RemoveAll(value => value != 0 && value != defaultValue && groupItems.All(item => item.Value != value));
                if (result.groups.Count >= 256 || columns.Count > 1024 || targetCount + columns.Count > 4096 || values.Count > 256 ||
                    result.items.Count + groupItems.Count > 512)
                {
                    warnings?.Add(ExporterLocalization.T(LimitWarning) + parameter);
                    continue;
                }
                var group = new Group { Parameter = parameter, DefaultValue = defaultValue, Name = NameOf(groupItems, parameter) };
                foreach (var column in columns)
                {
                    group.Targets.Add(transforms[candidates[column]].gameObject);
                    group.Paths.Add(PathOf(clone, transforms[candidates[column]]));
                }
                foreach (var value in values) group.States.Add((value, columns.Select(column => states[value][column]).ToArray()));
                claimed.UnionWith(columns);
                targetCount += columns.Count;
                result.groups.Add(group);
                result.items.AddRange(groupItems);
            }
            if (result.groups.Count == 0) return null;
            result.items.Sort((left, right) => left.Index.CompareTo(right.Index));
            // Meshes that are off by default are written; the default state
            // in the extension hides them. Only the targets are turned on.
            foreach (var target in result.groups.SelectMany(group => group.Targets))
                if (!target.activeSelf) target.SetActive(true);
            return result;
        }

        // Maps the targets to output nodes. A target the build passes or the
        // export removed is left out with a warning; an item that no longer
        // changes any kept target is left out, and so is a group left without
        // targets or items.
        internal ObjectToggleData Resolve(Func<GameObject, int> node, ICollection<string> warnings)
        {
            var data = new ObjectToggleData();
            var kept = new List<Item>();
            foreach (var group in groups)
            {
                var output = new ObjectToggleData.Group { Parameter = group.Parameter, Name = group.Name, DefaultValue = group.DefaultValue };
                var columns = new List<int>();
                for (var i = 0; i < group.Targets.Count; i++)
                {
                    var index = group.Targets[i] != null ? node(group.Targets[i]) : -1;
                    if (index < 0) { warnings?.Add(ExporterLocalization.T(MissingWarning) + group.Paths[i]); continue; }
                    output.Targets.Add(index);
                    columns.Add(i);
                }
                if (output.Targets.Count == 0) continue;
                bool[] ActiveAt(int value) => group.States.First(state => state.Value == value).Active;
                // Turning an item on or off must still change a kept target.
                var groupItems = items.Where(item => item.Parameter == group.Parameter &&
                                                     columns.Any(column => ActiveAt(item.Value)[column] != ActiveAt(0)[column])).ToList();
                if (groupItems.Count == 0) continue;
                foreach (var (value, active) in group.States)
                {
                    if (value != 0 && value != group.DefaultValue && groupItems.All(item => item.Value != value)) continue;
                    var state = new ObjectToggleData.State { Value = value };
                    state.Active.AddRange(columns.Select(column => active[column]));
                    output.States.Add(state);
                }
                data.Groups.Add(output);
                kept.AddRange(groupItems);
            }
            foreach (var item in items.Where(kept.Contains))
            {
                var output = new ObjectToggleData.Item { Id = "m" + item.Index, Name = item.Name, Parameter = item.Parameter, Value = item.Value };
                output.Path.AddRange(item.Path);
                data.Items.Add(output);
            }
            return data.Groups.Count == 0 ? null : data;
        }

        // Adds the extension to the root of the finished GLB. Nothing to write
        // keeps the bytes unchanged.
        internal static byte[] Inject(byte[] bytes, ObjectToggleData data)
        {
            if (data == null) return bytes;
            var extension = ObjectToggleData.Write(data);
            var document = GlbDocument.Read(bytes);
            var nodeCount = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> nodes ? nodes.Count : 0;
            if (data.Groups.Any(group => group.Targets.Any(target => target >= nodeCount)))
                throw new InvalidOperationException("Object toggle target node is out of range.");
            if (!document.Json.TryGetValue("extensions", out var rawExtensions)) document.Json["extensions"] = rawExtensions = new Dictionary<string, object>();
            ((Dictionary<string, object>)rawExtensions)[ObjectToggleData.Extension] = extension;
            if (!document.Json.TryGetValue("extensionsUsed", out var rawUsed)) document.Json["extensionsUsed"] = rawUsed = new List<object>();
            var used = (List<object>)rawUsed;
            if (!used.Contains(ObjectToggleData.Extension)) used.Add(ObjectToggleData.Extension);
            return document.Write();
        }

        static bool Integral(float raw, out int value)
        {
            value = 0;
            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw != Mathf.Round(raw) || Math.Abs(raw) > ValueLimit) return false;
            value = (int)raw;
            return true;
        }

        static string Clip(string text) => text.Length > 1024 ? text.Substring(0, 1024) : text;

        // The entry's name is its submenus and label joined by " / ".
        static string[] PathOf(VrChatExpressionMenu.Entry entry)
        {
            var suffix = " / " + entry.ExportName;
            if (!entry.Name.EndsWith(suffix, StringComparison.Ordinal)) return Array.Empty<string>();
            var parts = entry.Name.Substring(0, entry.Name.Length - suffix.Length)
                .Split(new[] { " / " }, StringSplitOptions.None).Where(part => part.Length > 0).Select(Clip).ToArray();
            return parts.Skip(Math.Max(0, parts.Length - 16)).ToArray();
        }

        // The last submenu the items share, else the parameter.
        static string NameOf(List<Item> groupItems, string parameter)
        {
            var common = groupItems[0].Path.AsEnumerable();
            foreach (var item in groupItems.Skip(1))
                common = common.Zip(item.Path, (left, right) => (left, right)).TakeWhile(pair => pair.left == pair.right).Select(pair => pair.left).ToArray();
            return Clip(common.LastOrDefault() ?? parameter);
        }

        static string PathOf(GameObject root, Transform transform) =>
            UnityEditor.AnimationUtility.CalculateTransformPath(transform, root.transform);

        // Plays the FX on one preview copy of the avatar. Every sample starts
        // from the copy's own activeSelf with a new graph, so write-defaults-off
        // states of an earlier sample do not leak into the next.
        sealed class Sampler : IDisposable
        {
            readonly Scene scene;
            readonly GameObject copy;
            readonly Transform[] transforms;
            readonly bool[] rest;
            readonly Animator animator;
            readonly RuntimeAnimatorController controller;
            readonly Dictionary<string, AnimatorControllerParameter> parameters = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);

            internal Sampler(GameObject avatar, RuntimeAnimatorController controller)
            {
                this.controller = controller;
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    copy = UnityEngine.Object.Instantiate(avatar);
                    copy.name = avatar.name;
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    animator = copy.GetComponent<Animator>();
                    if (animator == null) animator = copy.AddComponent<Animator>();
                    animator.runtimeAnimatorController = null;
                    animator.enabled = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    animator.fireEvents = false;
                    copy.SetActive(true);
                    // Instantiate keeps the hierarchy order, so index i is the
                    // same object in the avatar and in this copy.
                    transforms = copy.GetComponentsInChildren<Transform>(true);
                    rest = transforms.Select(transform => transform.gameObject.activeSelf).ToArray();
                    var graph = PlayableGraph.Create("VR Vlog object toggle parameters");
                    try
                    {
                        var playable = AnimatorControllerPlayable.Create(graph, controller);
                        for (var i = 0; i < playable.GetParameterCount(); i++)
                        {
                            var parameter = playable.GetParameter(i);
                            parameters[parameter.name] = parameter;
                        }
                    }
                    finally { graph.Destroy(); }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            // The controller's own default for a parameter the avatar does not list.
            internal float Default(string name)
            {
                if (!parameters.TryGetValue(name, out var parameter)) return 0f;
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool: return parameter.defaultBool ? 1f : 0f;
                    case AnimatorControllerParameterType.Int: return parameter.defaultInt;
                    case AnimatorControllerParameterType.Float: return parameter.defaultFloat;
                    default: return 0f;
                }
            }

            // activeSelf of every object of the avatar, in hierarchy order.
            internal bool[] Sample(Dictionary<string, float> values)
            {
                for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.SetActive(rest[i]);
                var graph = PlayableGraph.Create("VR Vlog object toggle sampling");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimatorControllerPlayable.Create(graph, controller);
                    AnimationPlayableOutput.Create(graph, "Object toggles", animator).SetSourcePlayable(playable);
                    foreach (var pair in values)
                    {
                        if (!parameters.TryGetValue(pair.Key, out var parameter)) continue;
                        switch (parameter.type)
                        {
                            case AnimatorControllerParameterType.Bool: playable.SetBool(pair.Key, pair.Value != 0f); break;
                            case AnimatorControllerParameterType.Int: playable.SetInteger(pair.Key, Mathf.RoundToInt(pair.Value)); break;
                            case AnimatorControllerParameterType.Float: playable.SetFloat(pair.Key, pair.Value); break;
                        }
                    }
                    graph.Play();
                    graph.Evaluate(0f);
                    for (var frame = 0; frame < Frames; frame++) graph.Evaluate(FrameStep);
                    return transforms.Select(transform => transform.gameObject.activeSelf).ToArray();
                }
                finally { graph.Destroy(); }
            }

            public void Dispose()
            {
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
