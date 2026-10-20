using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class NegativeRendererBoundsTests
    {
        [Test]
        public void NegativeAuthoredExtentsAreStoredAsTheSameBoxWhileFilesStillRejectNegatives()
        {
            using var fixture=new AttachmentConnectionTests.Fixture();
            var source=new Material(Shader.Find("lilToon"));
            try
            {
                var skins=fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach(var skin in skins)skin.sharedMaterial=source;
                // Front keeps its own mesh when the export copy splits the shared mesh, so its authored bounds reach the file.
                var front=skins.Single(s=>s.name=="Front");
                front.localBounds=new Bounds(new Vector3(.1f,1.2f,-.3f),new Vector3(2,-.78f,2));
                Assert.That(front.localBounds.extents.y,Is.LessThan(0),"Unity keeps the authored negative extent this case reproduces.");
                var warnings=new List<string>();
                var bytes=UniVrmOneClickExporter.Export(fixture.Source,"Negative bounds","Tests",warnings,exporterVersion:"0.10.0-preview.1",lilToonVersion:"2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var glb=GlbDocument.Read(bytes);F.Validate(glb.Json,bytes.Length,glb.Binary.Length);
                var states=F.List(F.Root(glb.Json),"bindings").Select(F.Object).Select(b=>F.Object(F.Get(b,"renderer"))).ToArray();
                var state=states.Single(s=>Mathf.Abs(F.Vector(F.Get(s,"boundsCenter"))[1]-1.2f)<1e-5f);
                var extents=F.Vector(F.Get(state,"boundsExtents"));
                Assert.That(extents[0],Is.EqualTo(1f).Within(1e-6f));Assert.That(extents[1],Is.EqualTo(.39f).Within(1e-6f));Assert.That(extents[2],Is.EqualTo(1f).Within(1e-6f));
                Assert.That(warnings.Any(w=>w.Contains(front.name) && w.Contains("描画範囲（Bounds）")),Is.True);
                Assert.That(front.localBounds.extents.y,Is.LessThan(0),"Export must not edit the source renderer.");
                state["boundsExtents"]=new List<object>{1.0,-.39,1.0,0.0};
                var error=Assert.Throws<InvalidDataException>(()=>F.Validate(glb.Json,bytes.Length,glb.Binary.Length));
                Assert.That(error.Message,Is.EqualTo("Invalid renderer bounds."));
            }
            finally {Object.DestroyImmediate(source);}
        }
    }
}
