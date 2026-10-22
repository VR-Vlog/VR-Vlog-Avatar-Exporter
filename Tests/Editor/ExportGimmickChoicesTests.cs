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
    // The gimmick list of the export windows: the "VRMに入れる" choice saved
    // in the avatar's export settings.
    public sealed class ExportGimmickChoicesTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject avatar;
        Material hidden;
        string storePath, previousPath;

        [SetUp] public void SetUp()
        {
            avatar = Own(new GameObject("avatar"));
            hidden = Own(new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback")));
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
        static ExportGimmickRule AnchorRule() => new ExportGimmickRule
        {
            Id = "test-anchor", Reason = "test anchor", AnchorTypes = new[] { typeof(GimmickSettingsProbe).FullName },
            DedicatedTypes = new[] { "BoxCollider" }
        };

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
            Assert.That(ExportGimmickChoices.InheritedFrom(finding, settings), Is.EqualTo(container));
            // The row names the nearest kept parent.
            var inner = Child("inner", container.transform);
            effect.transform.SetParent(inner.transform, false);
            settings.IncludedGimmicks.Add(inner);
            Assert.That(ExportGimmickChoices.InheritedFrom(finding, settings), Is.EqualTo(inner));
            settings.IncludedGimmicks.Remove(inner);
            effect.transform.SetParent(container.transform, false);
            Assert.That(ExportGimmickChoices.Kept(finding, settings), Is.False);
            Assert.That(ExportGimmickChoices.Removed(new[] { finding }, settings.Gimmicks()), Is.Empty);
            // Objects left out by hand are not listed again.
            Assert.That(ExportGimmickChoices.Groups(ExportGimmickChoices.Analyze(avatar, new[] { container })), Is.Empty);
            Assert.That(effect, Is.Not.Null);
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
    }
}
