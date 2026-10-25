using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The gimmick list both windows show and the include choice behind
    // each row. The choices live in ExportSettings, so a choice made in one
    // window is what the other window and every export read.
    internal static class ExportGimmickChoices
    {
        internal const string HeaderFormat = "VRChat用のギミック（除外{0}件・含める{1}件）";
        internal const string EmptyHeader = "VRChat用のギミック（0件）";
        internal const string PartsFormat = "{0}を含む";
        internal const string OtherGroup = "その他のギミック";
        internal const string RendererGroup = "代替表示で隠すメッシュ";

        // Short group names in the order the board lists them. Rules of the
        // table that are missing here fall into OtherGroup, last.
        static readonly (string Rule, string Name)[] GroupNames =
        {
            (ExportGimmickRules.PlatformFilter, "VRChat専用の指定"),
            (ExportGimmickRules.VrChatOnlyStructure, "VRChatでだけ動く仕組み"),
            (ExportGimmickRules.SkeletonTakeover, "体の骨を付け替える仕組み"),
            (ExportGimmickRules.PoseMotion, "ポーズのアニメーション"),
            (ExportGimmickRules.CameraGimmick, "カメラ"),
            (ExportGimmickRules.RenderTextureCamera, "カメラ映像の表示"),
            (ExportGimmickRules.LightLimitSettings, "照明の設定"),
            (ExportGimmickRules.PettingSystem, "撫でシステム"),
        };

        // The parts that only work inside VRChat, in the order a row lists them.
        // The export diagnostics count the avatar's components by the same kinds.
        internal static readonly string[] PartNames = { "カメラ", "音", "パーティクル", "ライト", "接触", "コンストレイント" };
        static readonly string[] ContactTypes = { ExportGimmickRules.ContactReceiver, ExportGimmickRules.ContactSender };

        // The objects the user left out by hand are not judged again.
        internal static List<ExportGimmickFinding> Analyze(GameObject avatar, IEnumerable<GameObject> excluded)
        {
            if (avatar == null) return new List<ExportGimmickFinding>();
            var manual = (excluded ?? Array.Empty<GameObject>()).Where(go => go != null && go != avatar).ToArray();
            bool Manual(Transform t) => manual.Any(go => t == go.transform || t.IsChildOf(go.transform));
            return ExportGimmickDetection.Analyze(avatar, Manual);
        }

        // Findings worth a row: a finding inside a hierarchy that another
        // finding removes as a whole goes with that hierarchy.
        internal static IEnumerable<ExportGimmickFinding> Listed(IEnumerable<ExportGimmickFinding> findings)
        {
            var all = (findings ?? Array.Empty<ExportGimmickFinding>()).Where(f => f.Target != null).ToArray();
            var roots = all.Where(f => f.Unit == GimmickExclusionUnit.Hierarchy).ToArray();
            return all.Where(f => !roots.Any(root => root != f && f.Target.transform.IsChildOf(root.Target.transform))).ToArray();
        }

        // A parent the user keeps also keeps this finding.
        internal static bool Inherited(ExportGimmickFinding finding, ExportSettings settings) => InheritedFrom(finding, settings) != null;

        // The nearest kept parent the row follows, or null.
        internal static GameObject InheritedFrom(ExportGimmickFinding finding, ExportSettings settings) =>
            settings.IncludedGimmicks.Where(go => go != null && go != finding.Target && finding.Target.transform.IsChildOf(go.transform))
                .OrderByDescending(go => Depth(go.transform)).FirstOrDefault();

        static int Depth(Transform transform)
        {
            var depth = 0;
            for (var t = transform; t != null; t = t.parent) depth++;
            return depth;
        }

        // Whether the user kept this finding itself. A Component finding keeps
        // only its components, not the object (which can be the avatar root).
        internal static bool Kept(ExportGimmickFinding finding, ExportSettings settings) =>
            finding.Unit == GimmickExclusionUnit.Component
                ? finding.Components.Any(settings.IncludedGimmickComponents.Contains)
                : settings.IncludedGimmicks.Contains(finding.Target);

        internal static void SetKept(ExportGimmickFinding finding, ExportSettings settings, bool keep)
        {
            if (finding.Unit == GimmickExclusionUnit.Component)
            {
                foreach (var component in finding.Components)
                {
                    settings.IncludedGimmickComponents.Remove(component);
                    if (keep && component != null) settings.IncludedGimmickComponents.Add(component);
                }
            }
            else if (keep) { if (!settings.IncludedGimmicks.Contains(finding.Target)) settings.IncludedGimmicks.Add(finding.Target); }
            else settings.IncludedGimmicks.Remove(finding.Target);
        }

        // The findings the export leaves out with these options, decided the
        // same way the export copy is built (ExportGimmickSession).
        internal static HashSet<ExportGimmickFinding> Removed(IEnumerable<ExportGimmickFinding> findings, ExportGimmickOptions options)
        {
            var removed = new HashSet<ExportGimmickFinding>();
            if (options == null || !options.AutoExclude) return removed;
            var all = (findings ?? Array.Empty<ExportGimmickFinding>()).Where(f => f.Target != null).ToArray();
            var roots = new HashSet<GameObject>(ExportGimmickDetection.AutomaticRoots(all, options));
            removed.UnionWith(all.Where(f => f.Unit == GimmickExclusionUnit.Hierarchy && roots.Contains(f.Target)));
            removed.UnionWith(ExportGimmickDetection.AutomaticComponents(all, options));
            removed.UnionWith(all.Where(f => f.Unit == GimmickExclusionUnit.Renderer &&
                !ExportGimmickDetection.Kept(f.Target.transform, options.IncludedObjects)));
            return removed;
        }

        internal static string GroupName(ExportGimmickFinding finding) =>
            finding.Unit == GimmickExclusionUnit.Renderer && finding.RuleId == null ? RendererGroup : GroupName(finding.RuleId);

        internal static string GroupName(string ruleId)
        {
            foreach (var (rule, name) in GroupNames) if (rule == ruleId) return name;
            return OtherGroup;
        }

        // The automatic findings in groups, in the board's order. Review
        // findings stay out: the export keeps them without a choice.
        internal static List<(string Name, List<ExportGimmickFinding> Findings)> Groups(IEnumerable<ExportGimmickFinding> findings)
        {
            int Order(string name)
            {
                for (var i = 0; i < GroupNames.Length; i++) if (GroupNames[i].Name == name) return i;
                return name == RendererGroup ? GroupNames.Length : GroupNames.Length + 1;
            }
            return Listed(findings).Where(f => f.Unit != GimmickExclusionUnit.Review)
                .GroupBy(GroupName).OrderBy(group => Order(group.Key))
                .Select(group => (group.Key, group.ToList())).ToList();
        }

        internal static (int Removed, int Included) Counts(IEnumerable<(string Name, List<ExportGimmickFinding> Findings)> groups, ICollection<ExportGimmickFinding> removed)
        {
            var all = groups.SelectMany(group => group.Findings).ToArray();
            var count = all.Count(removed.Contains);
            return (count, all.Length - count);
        }

        internal static string Header(int removed, int included) => removed + included == 0
            ? ExporterLocalization.T(EmptyHeader)
            : string.Format(ExporterLocalization.T(HeaderFormat), removed, included);

        // "オブジェクト名／コンポーネント名" for a Component finding.
        internal static string RowName(ExportGimmickFinding finding)
        {
            if (finding.Unit != GimmickExclusionUnit.Component) return finding.Target.name;
            var types = ComponentNames(finding);
            return types.Length == 0 ? finding.Target.name : finding.Target.name + "／" + string.Join("・", types);
        }

        static string[] ComponentNames(ExportGimmickFinding finding) =>
            finding.Components.Where(c => c != null).Select(c => ObjectNames.NicifyVariableName(c.GetType().Name)).Distinct().ToArray();

        // The board's dim column: what the row holds. The rule's reason stays
        // in the tooltip, so the column does not repeat the group heading.
        internal static string Contents(ExportGimmickFinding finding, Func<string, string> translate)
        {
            switch (finding.Unit)
            {
                case GimmickExclusionUnit.Hierarchy: return PartsSummary(finding.Target.GetComponentsInChildren<Component>(true), translate);
                case GimmickExclusionUnit.Renderer: return translate("メッシュ");
                default: return "";
            }
        }

        // "カメラ・音を含む" for the VRChat-only parts among the components,
        // or "" when there are none.
        internal static string PartsSummary(IEnumerable<Component> components, Func<string, string> translate)
        {
            var found = new bool[PartNames.Length];
            foreach (var component in components ?? Array.Empty<Component>())
            {
                var index = PartIndex(component);
                if (index >= 0) found[index] = true;
            }
            var parts = PartNames.Where((_, i) => found[i]).Select(translate).ToArray();
            return parts.Length == 0 ? "" : string.Format(translate(PartsFormat), string.Join("・", parts));
        }

        internal static int PartIndex(Component component)
        {
            if (component == null) return -1;
            if (component is Camera) return 0;
            if (component is AudioSource) return 1;
            if (component is ParticleSystem) return 2;
            if (component is Light) return 3;
            if (ExportGimmickDetection.IsType(component, ContactTypes)) return 4;
            var type = component.GetType();
            if (component is UnityEngine.Animations.IConstraint || type.Namespace != null &&
                (type.Namespace == "VRC" || type.Namespace.StartsWith("VRC.", StringComparison.Ordinal)) &&
                type.Name.EndsWith("Constraint", StringComparison.Ordinal)) return 5;
            return -1;
        }

        internal static string PathOf(ExportGimmickFinding finding, GameObject avatar)
        {
            if (avatar == null || finding.Target == avatar) return finding.Target.name;
            var path = AnimationUtility.CalculateTransformPath(finding.Target.transform, avatar.transform);
            return string.IsNullOrEmpty(path) ? finding.Target.name : avatar.name + "/" + path;
        }

        internal static bool SameGimmicks(ExportGimmickOptions a, ExportGimmickOptions b)
        {
            if (a == null || b == null) return a == b;
            return a.AutoExclude == b.AutoExclude &&
                new HashSet<GameObject>((a.IncludedObjects ?? Array.Empty<GameObject>()).Where(go => go != null))
                    .SetEquals((b.IncludedObjects ?? Array.Empty<GameObject>()).Where(go => go != null)) &&
                new HashSet<Component>((a.IncludedComponents ?? Array.Empty<Component>()).Where(c => c != null))
                    .SetEquals((b.IncludedComponents ?? Array.Empty<Component>()).Where(c => c != null));
        }
    }
}
