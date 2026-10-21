using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class NonLilToonExportTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        Material Standard(Color color, Texture2D main)
        {
            var material = new Material(Shader.Find("Standard")) { name = "Plain" };
            material.SetColor("_Color", color); material.SetTexture("_MainTex", main);
            material.SetTextureScale("_MainTex", new Vector2(2, 1));
            owned.Add(material);
            return material;
        }

        [Test] public void NonLilToonMaterialBecomesFlatMToonWithItsColorAndMainImage()
        {
            var main = new Texture2D(4, 4); owned.Add(main);
            var created = new List<Material>();
            var output = UniVrmOneClickExporter.CreateMToonDefault(Standard(Color.red, main), created);
            owned.AddRange(created);
            Assert.That(created, Is.EquivalentTo(new[] { output }), "The exporter owns and later destroys the converted copy.");
            // Pure red is identical in sRGB and linear, so the factor check
            // does not depend on MToon's color-space conversion.
            Assert.That(output.shader.name, Is.EqualTo("VRM10/MToon10"));
            Assert.That(output.name, Is.EqualTo("Plain"));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(new Color(1, 0, 0, 1)));
            Assert.That(output.GetColor("_ShadeColor"), Is.EqualTo(new Color(1, 0, 0, 1)));
            Assert.That(output.GetFloat("_AlphaMode"), Is.EqualTo(0f), "Opaque");
            Assert.That(output.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2, 1)));
        }

        [Test] public void AvatarWithoutLilToonStillProducesASnapshot()
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.transform.SetParent(avatar.transform, false);
            part.GetComponent<Renderer>().sharedMaterial = Standard(Color.white, null);
            var warnings = new List<string>();
            LilToonFullSnapshot snapshot = null;
            Assert.DoesNotThrow(() => snapshot = LilToonFullSnapshot.Capture(avatar, warnings: warnings));
            Assert.That(snapshot.IsEmpty, Is.True, "The exporter saves a plain MToon VRM instead of an empty lilToon extension.");
        }

        [Test] public void AvatarWhoseMaterialsAreAllMissingNamesTheRenderers()
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var outfit = new GameObject("Outfit").transform; outfit.SetParent(avatar.transform, false);
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = "Body"; part.transform.SetParent(outfit, false);
            part.GetComponent<Renderer>().sharedMaterials = new Material[] { null, null };
            var created = new List<Material>();
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ReplaceLilToonMaterials(
                avatar, created, new List<Texture2D>(), new List<string>(), false, convertOthers: true));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.MissingMaterialsError + "Outfit/Body"));
            Assert.That(created, Is.Empty);
        }

        [Test] public void AnAssignedNonLilToonMaterialKeepsTheLilToonMessage()
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.transform.SetParent(avatar.transform, false);
            part.GetComponent<Renderer>().sharedMaterials = new[] { null, Standard(Color.white, null) };
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ReplaceLilToonMaterials(
                avatar, new List<Material>(), new List<Texture2D>(), new List<string>(), false));
            Assert.That(error.Message, Is.EqualTo("選択したアバターに対応するlilToonマテリアルがありません。"));
        }

        [Test] public void AMissingBehaviourIsNeverTreatedAsAnExportComponent()
        {
            Assert.That(NdmfExportPreparation.PreservesExportBehaviour(null), Is.False);
        }
    }
}
