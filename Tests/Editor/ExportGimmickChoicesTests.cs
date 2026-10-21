using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The gimmick list of the export windows: groups, header counts and the
    // "VRMに入れる" choice saved in the avatar's export settings.
    public sealed class ExportGimmickChoicesTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject avatar;
        Material hidden, normal, unspecified;
        string storePath, previousPath;

        [SetUp] public void SetUp()
        {
            avatar = Own(new GameObject("avatar"));
            hidden = Own(new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback")));
            normal = Own(new Material(Shader.Find("Hidden/VRVlogTests/lilToon")));
            unspecified = Own(new Material(Shader.Find("Hidden/VRVlogTests/ShadowSphere")));
            previousPath = ExportSettingsStore.PathOverride;
            storePath = Path.Combine(Path.GetTempPath(), "vrvlog-gimmick-choices-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = storePath;
        }

        [TearDown] public void TearDown()
        {
            for (var i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            ExportSettingsStore.PathOverride = previousPath;
            if (File.Exists(storePath)) File.Delete(storePath);
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent ?? avatar.transform, false); return go;
        }
        MeshRenderer Render(string name, Material material, Transform parent = null)
        {
            var go = Child(name, parent);
            var mesh = Own(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        static Type MaType(string fullName)
        {
            var type = FindType(fullName);
            if (type == null) Assert.Ignore("Installed MA is required for the structural gimmick rules.");
            return type;
        }

        // Kept only on VRChat by a Platform Filter.
        GameObject Filtered(string name)
        {
            var go = Child(name);
            using (var serialized = new SerializedObject(go.AddComponent(MaType(ExportGimmickRules.PlatformFilterType))))
            {
                serialized.FindProperty("m_platform").stringValue = "nadena.dev.ndmf.vrchat.avatar3";
                serialized.FindProperty("m_excludePlatform").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Render("prop", normal, go.transform);
            return go;
        }

        // A menu entry with a prop not drawn like the avatar and parts that only work in VRChat.
        GameObject StructuralGimmick(Transform parent = null)
        {
            var group = Child("gimmick", parent); group.AddComponent(MaType(ExportGimmickRules.MenuInstaller));
            Render("prop", Own(new Material(Shader.Find("Unlit/Texture"))), group.transform);
            Child("particles", group.transform).AddComponent<ParticleSystem>();
            Child("viewer", group.transform).AddComponent<Camera>();
            Child("speaker", group.transform).AddComponent<AudioSource>();
            return group;
        }

        static ExportGimmickRule AnchorRule() => new ExportGimmickRule
        {
            Id = "test-anchor", Reason = "test anchor", AnchorTypes = new[] { typeof(GimmickSettingsProbe).FullName },
            DedicatedTypes = new[] { "BoxCollider" }
        };

        [Test] public void GroupsFollowTheBoardOrderAndLeaveOutReviewAndNestedFindings()
        {
            Render("clothing", normal);
            var effect = Render("effect", hidden).gameObject;
            Render("unknown", unspecified);
            var gimmick = StructuralGimmick();
            var filtered = Filtered("vrchat only");
            StructuralGimmick(filtered.transform);
            var findings = ExportGimmickChoices.Analyze(avatar, null);
            Assert.That(findings.Any(f => f.Unit == GimmickExclusionUnit.Review), Is.True);
            var groups = ExportGimmickChoices.Groups(findings);
            Assert.That(groups.Select(group => group.Name), Is.EqualTo(new[] { "VRChat専用の指定", "VRChatでだけ動く仕組み", "代替表示で隠すメッシュ" }));
            Assert.That(groups.Select(group => group.Findings.Single().Target), Is.EqualTo(new[] { filtered, gimmick, effect }));
        }

        [Test] public void EveryRuleHasAGroupAndUnknownRulesGoLast()
        {
            foreach (var rule in ExportGimmickRules.All)
                Assert.That(ExportGimmickChoices.GroupName(new ExportGimmickFinding { RuleId = rule.Id, Unit = GimmickExclusionUnit.Hierarchy }),
                    Is.Not.EqualTo(ExportGimmickChoices.OtherGroup), rule.Id);
            var findings = new[]
            {
                new ExportGimmickFinding { Target = Child("other"), RuleId = "test-anchor", Unit = GimmickExclusionUnit.Hierarchy },
                new ExportGimmickFinding { Target = Child("camera"), RuleId = ExportGimmickRules.CameraGimmick, Unit = GimmickExclusionUnit.Hierarchy },
                new ExportGimmickFinding { Target = Child("mesh"), Unit = GimmickExclusionUnit.Renderer },
            };
            Assert.That(ExportGimmickChoices.Groups(findings).Select(group => group.Name),
                Is.EqualTo(new[] { "カメラ", ExportGimmickChoices.RendererGroup, ExportGimmickChoices.OtherGroup }));
        }

        [Test] public void HeaderCountsWhatTheExportRemovesAndKeeps()
        {
            var effect = Render("effect", hidden).gameObject;
            var gimmick = StructuralGimmick();
            var groups = ExportGimmickChoices.Groups(ExportGimmickChoices.Analyze(avatar, null));
            var settings = new ExportSettings();
            (int, int) Counts() => ExportGimmickChoices.Counts(groups, ExportGimmickChoices.Removed(groups.SelectMany(g => g.Findings), settings.Gimmicks()));
            Assert.That(Counts(), Is.EqualTo((2, 0)));
            ExportGimmickChoices.SetKept(groups.SelectMany(g => g.Findings).Single(f => f.Target == gimmick), settings, true);
            Assert.That(Counts(), Is.EqualTo((1, 1)));
            ExportGimmickChoices.SetKept(groups.SelectMany(g => g.Findings).Single(f => f.Target == effect), settings, true);
            Assert.That(Counts(), Is.EqualTo((0, 2)));
            ExportGimmickChoices.SetKept(groups.SelectMany(g => g.Findings).Single(f => f.Target == gimmick), settings, false);
            settings.AutoExcludeGimmicks = false;
            Assert.That(Counts(), Is.EqualTo((0, 2)));
            Assert.That(ExportGimmickChoices.Header(1, 2), Is.EqualTo(string.Format(ExporterLocalization.T(ExportGimmickChoices.HeaderFormat), 1, 2)));
            Assert.That(ExportGimmickChoices.Header(0, 0), Is.EqualTo(ExporterLocalization.T(ExportGimmickChoices.EmptyHeader)));
        }

        [Test] public void ChoiceRoundTripsThroughTheSavedSettings()
        {
            var effect = Render("effect", hidden).gameObject;
            var finding = ExportGimmickChoices.Groups(ExportGimmickChoices.Analyze(avatar, null)).Single().Findings.Single();
            Assert.That(finding.Target, Is.SameAs(effect));
            var settings = ExportSettingsStore.Load(avatar);
            Assert.That(ExportGimmickChoices.Kept(finding, settings), Is.False);
            var revision = ExportSettingsStore.Revision;
            ExportGimmickChoices.SetKept(finding, settings, true);
            ExportGimmickChoices.SetKept(finding, settings, true);
            ExportSettingsStore.Save(avatar, settings);
            Assert.That(ExportSettingsStore.Revision, Is.GreaterThan(revision));
            var loaded = ExportSettingsStore.Load(avatar);
            Assert.That(loaded.IncludedGimmicks, Is.EqualTo(new[] { effect }));
            Assert.That(ExportGimmickChoices.Kept(finding, loaded), Is.True);
            Assert.That(ExportGimmickChoices.Removed(new[] { finding }, loaded.Gimmicks()), Is.Empty);
            ExportGimmickChoices.SetKept(finding, loaded, false);
            ExportSettingsStore.Save(avatar, loaded);
            loaded = ExportSettingsStore.Load(avatar);
            Assert.That(loaded.IncludedGimmicks, Is.Empty);
            Assert.That(ExportGimmickChoices.Removed(new[] { finding }, loaded.Gimmicks()), Is.EqualTo(new[] { finding }));
        }

        [Test] public void ComponentChoiceKeepsOnlyItsComponents()
        {
            var holder = Child("settings"); var probe = holder.AddComponent<GimmickSettingsProbe>();
            Child("part", holder.transform);
            var findings = ExportGimmickDetection.Analyze(avatar, new[] { AnchorRule() }, null);
            var finding = ExportGimmickChoices.Groups(findings).Single().Findings.Single();
            Assert.That(finding.Unit, Is.EqualTo(GimmickExclusionUnit.Component));
            Assert.That(ExportGimmickChoices.GroupName(finding), Is.EqualTo(ExportGimmickChoices.OtherGroup));
            Assert.That(ExportGimmickChoices.RowName(finding), Is.EqualTo("settings／Gimmick Settings Probe"));
            Assert.That(ExportGimmickChoices.PathOf(finding, avatar), Is.EqualTo("avatar/settings"));
            var settings = new ExportSettings();
            Assert.That(ExportGimmickChoices.Removed(findings, settings.Gimmicks()), Is.EqualTo(new[] { finding }));
            ExportGimmickChoices.SetKept(finding, settings, true);
            Assert.That(settings.IncludedGimmickComponents, Is.EqualTo(new Component[] { probe }));
            Assert.That(settings.IncludedGimmicks, Is.Empty);
            Assert.That(ExportGimmickChoices.Removed(findings, settings.Gimmicks()), Is.Empty);
        }

        [Test] public void ParentChoiceIsInheritedByTheRowsBelow()
        {
            var container = Child("container");
            var effect = Render("effect", hidden, container.transform).gameObject;
            var finding = ExportGimmickChoices.Groups(ExportGimmickChoices.Analyze(avatar, null)).Single().Findings.Single();
            var settings = new ExportSettings();
            settings.IncludedGimmicks.Add(container);
            Assert.That(ExportGimmickChoices.Inherited(finding, settings), Is.True);
            Assert.That(ExportGimmickChoices.Kept(finding, settings), Is.False);
            Assert.That(ExportGimmickChoices.Removed(new[] { finding }, settings.Gimmicks()), Is.Empty);
            // Objects left out by hand are not listed again.
            Assert.That(ExportGimmickChoices.Groups(ExportGimmickChoices.Analyze(avatar, new[] { container })), Is.Empty);
            Assert.That(effect, Is.Not.Null);
        }

        [Test] public void PartsSummaryListsTheVrChatOnlyPartsInAFixedOrder()
        {
            string Summary(GameObject root) => ExportGimmickChoices.PartsSummary(root.GetComponentsInChildren<Component>(true), s => s);
            var root = Child("parts");
            Assert.That(Summary(root), Is.Empty);
            Child("constraint", root.transform).AddComponent<UnityEngine.Animations.ParentConstraint>();
            Child("light", root.transform).AddComponent<Light>();
            Child("speaker", root.transform).AddComponent<AudioSource>();
            Child("viewer", root.transform).AddComponent<Camera>();
            Child("second viewer", root.transform).AddComponent<Camera>();
            Assert.That(Summary(root), Is.EqualTo("カメラ・音・ライト・コンストレイントを含む"));
            var inactive = Child("particles", root.transform); inactive.AddComponent<ParticleSystem>(); inactive.SetActive(false);
            Assert.That(Summary(root), Is.EqualTo("カメラ・音・パーティクル・ライト・コンストレイントを含む"));
            Assert.That(ExportGimmickChoices.PartsSummary(new Component[] { null, root.transform }, s => s), Is.Empty);
            Assert.That(ExportGimmickChoices.PartsSummary(new Component[] { root.GetComponentInChildren<AudioSource>() }, s => "[" + s + "]"),
                Is.EqualTo("[[音]を含む]"));
        }

        [Test] public void ContentsDescribeWhatTheRowHolds()
        {
            var group = Child("group"); Child("viewer", group.transform).AddComponent<Camera>(); Child("speaker", group.transform).AddComponent<AudioSource>();
            var holder = Child("settings"); var probe = holder.AddComponent<GimmickSettingsProbe>();
            string Contents(ExportGimmickFinding finding) => ExportGimmickChoices.Contents(finding, s => s);
            Assert.That(Contents(new ExportGimmickFinding { Target = group, Unit = GimmickExclusionUnit.Hierarchy, Reason = "reason" }), Is.EqualTo("カメラ・音を含む"));
            Assert.That(Contents(new ExportGimmickFinding { Target = Child("empty"), Unit = GimmickExclusionUnit.Hierarchy }), Is.Empty);
            Assert.That(Contents(new ExportGimmickFinding { Target = holder, Unit = GimmickExclusionUnit.Component, Components = new Component[] { probe } }),
                Is.Empty);
            Assert.That(Contents(new ExportGimmickFinding { Target = Render("effect", hidden).gameObject, Unit = GimmickExclusionUnit.Renderer }), Is.EqualTo("メッシュ"));
            Assert.That(Contents(new ExportGimmickFinding { Target = group, Unit = GimmickExclusionUnit.Review }), Is.Empty);
        }

        [Test] public void SameGimmicksComparesTheChoicesAsSets()
        {
            var a = Child("a"); var b = Child("b"); var probe = Child("c").AddComponent<GimmickSettingsProbe>();
            var saved = new ExportGimmickOptions { IncludedObjects = new[] { a, b, null }, IncludedComponents = new Component[] { probe } };
            var preview = new ExportGimmickOptions { IncludedObjects = new[] { b, a }, IncludedComponents = new Component[] { probe } };
            Assert.That(ExportGimmickChoices.SameGimmicks(saved, preview), Is.True);
            preview.IncludedComponents = Array.Empty<Component>();
            Assert.That(ExportGimmickChoices.SameGimmicks(saved, preview), Is.False);
            preview.IncludedComponents = new Component[] { probe };
            preview.AutoExclude = false;
            Assert.That(ExportGimmickChoices.SameGimmicks(saved, preview), Is.False);
        }

        // The board's row: the toggle stays at the right edge and the object
        // field keeps a readable width when the window is narrow.
        [TestCase(620f)] [TestCase(440f)] [TestCase(320f)] [TestCase(120f)]
        public void GimmickRowKeepsTheToggleAndTheObjectFieldInPlace(float width)
        {
            var row = new Rect(14, 40, width, 18);
            var (name, field, contents, toggle) = ExperimentalExpressionCaptureWindow.GimmickRow(row, 90, 8);
            Assert.That(toggle, Is.EqualTo(new Rect(row.xMax - 90, 40, 90, 18)));
            Assert.That(name.x, Is.EqualTo(row.x));
            Assert.That(field.x, Is.EqualTo(name.xMax + 8).Within(.01f));
            Assert.That(contents.x, Is.EqualTo(field.xMax + 8).Within(.01f));
            Assert.That(contents.xMax + 8, Is.EqualTo(toggle.x).Within(.01f));
            foreach (var rect in new[] { name, field, contents }) { Assert.That(rect.y, Is.EqualTo(40)); Assert.That(rect.height, Is.EqualTo(18)); Assert.That(rect.width, Is.GreaterThanOrEqualTo(0)); }
            var rest = Mathf.Max(0, width - 90 - 24);
            Assert.That(field.width, Is.GreaterThanOrEqualTo(Mathf.Min(120, rest * .5f)));
            Assert.That(field.width, Is.GreaterThanOrEqualTo(rest * .35f - .01f));
        }
    }
}
