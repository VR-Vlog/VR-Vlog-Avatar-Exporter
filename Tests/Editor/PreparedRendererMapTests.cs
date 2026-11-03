using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class PreparedRendererMapTests
    {
        [Test]
        public void AsksOncePerRendererLeavesLostOnesToTheCallerAndLetsMergedOnesShareATarget()
        {
            var objects = new List<GameObject>();
            SkinnedMeshRenderer Renderer(string name)
            {
                var go = new GameObject(name); objects.Add(go);
                return go.AddComponent<SkinnedMeshRenderer>();
            }
            try
            {
                Assert.Throws<ArgumentNullException>(() => new PreparedRendererMap(null));
                var left = Renderer("left"); var right = Renderer("right"); var lost = Renderer("lost"); var merged = Renderer("merged");
                var asked = new List<SkinnedMeshRenderer>();
                var map = new PreparedRendererMap(old => { asked.Add(old); return old == lost ? null : merged; });

                Assert.That(map.Require(left), Is.SameAs(merged));
                Assert.That(map.Require(right), Is.SameAs(merged));
                Assert.That(map.Find(lost), Is.Null);
                var error = Assert.Throws<InvalidOperationException>(() => map.Require(lost));
                Assert.That(error.Message, Is.EqualTo(NdmfExportPreparation.UnknownRendererRelocation));
                Assert.That(map.Find(left), Is.SameAs(merged));
                Assert.That(asked, Is.EqualTo(new[] { left, right, lost }));

                var published = new Dictionary<string, int> { ["old"] = 1 };
                PreparedRendererMap.Publish(published, new Dictionary<string, int> { ["a"] = 2, ["b"] = 3 });
                Assert.That(published, Is.EqualTo(new Dictionary<string, int> { ["a"] = 2, ["b"] = 3 }));
            }
            finally
            {
                foreach (var go in objects) Object.DestroyImmediate(go);
            }
        }
    }
}
