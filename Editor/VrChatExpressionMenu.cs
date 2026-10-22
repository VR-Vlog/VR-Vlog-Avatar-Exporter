using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // An export recipe, never an edit to the descriptor or shared menu asset.
    internal sealed class VrChatMenuImportPolicy
    {
        internal bool SkipAll;
    }

    // The SDK remains optional. Read its public serialized data without taking
    // an assembly dependency or changing the user's descriptor/menu assets.
    internal static class VrChatExpressionMenu
    {
        internal sealed class MorphValue
        {
            internal string Path = "", Shape = "";
            internal float Weight = 0f;
        }

        internal sealed class Entry
        {
            internal string Id, Name, Error;
            // The VRM expression name: the menu label, otherwise the clip name.
            // Name keeps the source path for the export window and messages.
            internal string ExportName;
            internal string ControlType, ControlParameter;
            internal double Duration = 0;
            internal bool Loop = false;
            internal readonly List<AnimatedMorph> Animation = new List<AnimatedMorph>();
            internal readonly Dictionary<string, float> Parameters = new Dictionary<string, float>(StringComparer.Ordinal);
            internal readonly List<MorphValue> Values = new List<MorphValue>();
            // A sampled menu cannot supply a weight for a morph which does not
            // exist yet. Recheck these references after authoring preparation.
            internal readonly List<MorphValue> Unevaluated = new List<MorphValue>();
            internal readonly List<string> Messages = new List<string>();
            internal bool UsesFacialProjection = false;
            // Where the entry came from (VRVlog.Expressions.ExpressionSource):
            // menu, gesture or faceEmo. Every reader sets it explicitly.
            internal string Source = VRVlog.Expressions.ExpressionSource.Menu;
            // The clip a gesture state plays; null for the other readers. Typed as
            // Object so the behavior tests compile this file without the Unity clip type.
            internal UnityEngine.Object Clip = null;

            // ExportName, or the last element of Name when it is blank.
            internal string VrmName()
            {
                if (!string.IsNullOrWhiteSpace(ExportName)) return ExportName;
                var boundary = Name?.LastIndexOf(" / ", StringComparison.Ordinal) ?? -1;
                var last = boundary >= 0 ? Name.Substring(boundary + 3) : Name;
                return string.IsNullOrWhiteSpace(last) ? Name : last;
            }
        }

        internal sealed class AnimatedMorph
        {
            internal string Path = "", Shape = "";
            internal VRVlog.Expressions.ExpressionAnimationData.Curve Curve = null;
        }

        internal sealed class Source
        {
#if !EXPORTER_BEHAVIOR_TESTS
            // The host runner checks menu traversal/serialization without
            // Unity geometry evaluation. This session belongs to native Unity.
            internal FacialProjectionScope.Session FacialProjectionSession;
            // FX states the hand-gesture import already read without error.
            // Authored faces skip them instead of reading the same face twice.
            internal readonly HashSet<(int Layer, UnityEditor.Animations.AnimatorState State)> GestureStates =
                new HashSet<(int Layer, UnityEditor.Animations.AnimatorState State)>();
            // Shows the expression being read after the menu loop. Null when
            // the import runs without a progress bar.
            internal Action<string> Progress;
#endif
            internal RuntimeAnimatorController Controller;
            internal readonly Dictionary<string, float> Defaults = new Dictionary<string, float>(StringComparer.Ordinal);
            internal readonly HashSet<string> ExpressionParameters = new HashSet<string>(StringComparer.Ordinal);
            // Neutral appearance ownership needs complete input metadata even
            // when expression import intentionally skips menu candidates.
            internal bool NeutralInputInventoryComplete = false;
            internal readonly HashSet<string> MenuInputs = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, (bool Saved, bool NetworkSynced)> ParameterPersistence =
                new Dictionary<string, (bool Saved, bool NetworkSynced)>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> ExpressionParameterTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly List<RuntimeAnimatorController> OtherControllers = new List<RuntimeAnimatorController>();
            internal readonly HashSet<string> ExternalParameters = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<Entry> Entries = new List<Entry>();
            internal readonly List<string> Messages = new List<string>();
        }

        internal static object Root(GameObject avatar)
        {
            var descriptor = Descriptor(avatar);
            return descriptor != null && Member(descriptor, "customExpressions") is bool enabled && enabled
                ? Member(descriptor, "expressionsMenu") : null;
        }
        static Component Descriptor(GameObject avatar) => avatar.GetComponents<Component>().FirstOrDefault(c => c != null &&
            c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");

        internal static Source ReadMenu(object menu, VrChatMenuImportPolicy policy = null)
        {
            var result = new Source();
            Walk(menu, result, policy);
            return result;
        }

        internal static Source Read(GameObject avatar, VrChatMenuImportPolicy policy = null)
        {
            var result = new Source();
            result.Defaults["IsLocal"] = 1f;
            result.Defaults["TrackingType"] = 6f;
            var descriptor = Descriptor(avatar);
            if (descriptor == null) { result.Messages.Add("VRChat Avatar Descriptorがありません。既存のVRM表情はそのまま保存します。"); return result; }
            if (Member(descriptor, "customExpressions") is bool enabled && enabled)
                Walk(Member(descriptor, "expressionsMenu"), result, policy);
            else result.Messages.Add("VRChatのCustom Expressionsが無効です。FXに登録されたジェスチャー表情を確認します。");
            foreach (var parameter in Items(Member(Member(descriptor, "expressionParameters"), "parameters")))
            {
                var name = Member(parameter, "name") as string;
                if (!string.IsNullOrEmpty(name))
                {
                    result.Defaults[name] = Number(Member(parameter, "defaultValue"));
                    result.ExpressionParameters.Add(name);
                }
            }
            if (Member(descriptor, "customizeAnimationLayers") is bool custom && custom)
                foreach (var layer in Items(Member(descriptor, "baseAnimationLayers")).Concat(Items(Member(descriptor, "specialAnimationLayers"))))
                {
                    if (Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                    var runtime = Member(layer, "animatorController") as RuntimeAnimatorController;
                    if (Member(layer, "type")?.ToString() == "FX") result.Controller = runtime;
                    else if (runtime != null) result.OtherControllers.Add(runtime);
                }
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var name = component.GetType().Name;
                var parameter = Member(component, "parameter") as string;
                if (string.IsNullOrEmpty(parameter)) continue;
                if (name == "VRCContactReceiver") result.ExternalParameters.Add(parameter);
                if (name == "VRCPhysBone")
                    foreach (var suffix in new[] { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" })
                        result.ExternalParameters.Add(parameter + suffix);
            }
            if (result.Controller == null) result.Messages.Add("カスタムFX Animatorがありません。メニューの表情を解決できません。");
            return result;
        }

        static void Walk(object menu, Source result, VrChatMenuImportPolicy policy)
        {
            if (policy?.SkipAll == true)
            {
                result.Messages.Add(Localize("選択した対策により、VRChatメニュー由来の表情・ポーズの取り込みをすべて省略しました。"));
                return;
            }
            WalkEntries(menu, "", "", new Dictionary<string, float>(StringComparer.Ordinal), new HashSet<object>(),
                new HashSet<(object, string)>(), result);
        }

        // A submenu is read once for each set of values its parents select,
        // so the work follows the size of the menus rather than the number of
        // routes to them. The same submenu under the same values holds the
        // same expressions, so a second route adds nothing.
        static void WalkEntries(object menu, string prefix, string idPrefix, Dictionary<string, float> parents,
            HashSet<object> stack, HashSet<(object, string)> read, Source result)
        {
            if (menu == null || menu is UnityEngine.Object obj && obj == null) return;
            if (stack.Contains(menu)) { result.Messages.Add(prefix + ": 循環しているサブメニューを省略しました"); return; }
            if (!read.Add((menu, Selection(parents)))) return;
            stack.Add(menu);
            try
            {
                var index = 0;
                foreach (var control in Items(Member(menu, "controls")))
                {
                    var id = idPrefix + "/" + index++;
                    var label = Member(control, "name") as string;
                    if (string.IsNullOrWhiteSpace(label)) label = "名称未設定";
                    var name = string.IsNullOrEmpty(prefix) ? label : prefix + " / " + label;
                    var type = Member(control, "type")?.ToString();
                    var values = new Dictionary<string, float>(parents, StringComparer.Ordinal);
                    var parameter = Member(Member(control, "parameter"), "name") as string;
                    if (!string.IsNullOrEmpty(parameter)) values[parameter] = Number(Member(control, "value"));
                    if (type == "SubMenu") { WalkEntries(Member(control, "subMenu"), name, id, values, stack, read, result); continue; }
                    var entry = new Entry { Id = id + "\n" + name + "\n" + JsonDom.Serialize(values.ToDictionary(p => p.Key, p => (object)p.Value)), Name = name,
                        ExportName = label, ControlType = type, ControlParameter = parameter, Source = VRVlog.Expressions.ExpressionSource.Menu };
                    foreach (var pair in values) entry.Parameters.Add(pair.Key, pair.Value);
                    if (type != "Button" && type != "Toggle") entry.Error = "連続調整（Puppet）は一つの固定表情に変換できません。";
                    else if (string.IsNullOrEmpty(parameter)) entry.Error = "操作対象のパラメーターがありません。";
                    result.Entries.Add(entry);
                }
            }
            finally { stack.Remove(menu); }
        }

        // Exact and independent of insertion order, so two routes that set
        // the same values in a different order meet the same read.
        static string Selection(Dictionary<string, float> values) => string.Join("\n", values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        internal static object Member(object value, string name)
        {
            if (value == null) return null;
            var type = value.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetField(name, flags)?.GetValue(value) ?? type.GetProperty(name, flags)?.GetValue(value);
        }

        static string Localize(string message)
        {
#if EXPORTER_BEHAVIOR_TESTS
            // Host traversal tests intentionally compile without Editor APIs.
            return message;
#else
            return ExporterLocalization.T(message);
#endif
        }

        private static IEnumerable<object> Items(object value) => value is IEnumerable list ? list.Cast<object>() : Enumerable.Empty<object>();
        private static float Number(object value)
        {
            var number = value == null ? 0f : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidOperationException("表情メニューに不正な数値があります。");
            return number;
        }
    }
}
