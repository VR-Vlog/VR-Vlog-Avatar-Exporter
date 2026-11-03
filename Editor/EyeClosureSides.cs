using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // Finds the two one-eye shapes that together make a known bilateral
    // blink on the same mesh, whatever they are called (ウィンク / ウィンク右,
    // eye_close_left / eye_close_right, ...). The mesh decides: each side
    // must move only the avatar's own left or right eye, measured from the
    // humanoid eyes, and left + right must rebuild the bilateral shape.
    // Names only choose among equally good pairs and veto a pair whose
    // side marker says the other eye. The thresholds were read from the
    // local avatars with Tools/collect-eye-closure-corpus.py and
    // Tools/summarize-eye-closure-corpus.py.
    internal static class EyeClosureSides
    {
        // Share of a side shape's movement on its own side of the face.
        internal const float OneSided = .9f;
        // Share of a side shape's movement where the bilateral shape moves.
        internal const float Inside = .9f;
        // |left + right - bilateral|^2 / |bilateral|^2 at most.
        internal const float Rebuilt = .05f;
        const float Tie = .001f;

        internal sealed class Pair
        {
            internal string Left, Right;
            internal float Residual;
        }

        // The avatar's own left: from its right eye to its left eye, else
        // between the upper arms. Without a humanoid nothing is measured.
        internal static bool TryLeftAxis(Transform root, out Vector3 centre, out Vector3 left)
        {
            centre = left = Vector3.zero;
            var animator = root != null ? root.GetComponent<Animator>() : null;
            if (animator == null) return false;
            foreach (var (l, r) in new[] { (HumanBodyBones.LeftEye, HumanBodyBones.RightEye),
                (HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm) })
            {
                var a = HumanoidBones.Resolve(animator, l); var b = HumanoidBones.Resolve(animator, r);
                if (a == null || b == null || (a.position - b.position).sqrMagnitude < 1e-10f) continue;
                centre = (a.position + b.position) * .5f;
                left = (a.position - b.position).normalized;
                return true;
            }
            return false;
        }

        sealed class Side
        {
            internal int Index;
            internal string Name;
            internal Vector3[] Within; // deltas on the bilateral shape's vertices
            internal float Outside;    // squared movement elsewhere
        }

        internal static Pair Find(SkinnedMeshRenderer renderer, string both, Vector3 centre, Vector3 leftAxis)
        {
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null) return null;
            var bothIndex = mesh.GetBlendShapeIndex(both);
            if (bothIndex < 0 || mesh.GetBlendShapeFrameCount(bothIndex) == 0) return null;
            var deltas = new Vector3[mesh.vertexCount];
            Final(mesh, bothIndex, deltas);
            var support = Enumerable.Range(0, deltas.Length).Where(i => deltas[i] != Vector3.zero).ToArray();
            if (support.Length == 0) return null;
            var target = support.Select(i => deltas[i]).ToArray();
            var energy = target.Sum(delta => delta.sqrMagnitude);
            var inSupport = new bool[deltas.Length];
            foreach (var i in support) inSupport[i] = true;

            var side = SidesOfVertices(renderer, centre, leftAxis);
            if (side == null) return null;
            var lefts = new List<Side>(); var rights = new List<Side>();
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                if (index == bothIndex || mesh.GetBlendShapeFrameCount(index) == 0) continue;
                Final(mesh, index, deltas);
                float total = 0, outside = 0, onLeft = 0, onRight = 0;
                for (var i = 0; i < deltas.Length; i++)
                {
                    var moved = deltas[i].sqrMagnitude;
                    if (moved == 0) continue;
                    total += moved;
                    if (!inSupport[i]) outside += moved;
                    if (side[i] > 0) onLeft += moved; else if (side[i] < 0) onRight += moved;
                }
                if (total == 0 || outside > total * (1 - Inside)) continue;
                var list = onLeft >= total * OneSided ? lefts : onRight >= total * OneSided ? rights : null;
                list?.Add(new Side { Index = index, Name = mesh.GetBlendShapeName(index),
                    Within = support.Select(i => deltas[i]).ToArray(), Outside = outside });
            }

            Pair best = null; var bestNamed = false;
            foreach (var l in lefts)
                foreach (var r in rights)
                {
                    if (Contradicts(l.Name, BlinkShapeNames.ClosingLeft) || Contradicts(r.Name, BlinkShapeNames.ClosingRight)) continue;
                    var miss = l.Outside + r.Outside;
                    for (var k = 0; k < target.Length; k++) miss += (l.Within[k] + r.Within[k] - target[k]).sqrMagnitude;
                    var residual = miss / energy;
                    if (residual > Rebuilt) continue;
                    var named = Siblings(l.Name, r.Name);
                    // Equally good pairs (the same shapes under two names) go
                    // to the one named as a pair, then to the earlier shapes.
                    if (best != null && (residual > best.Residual + Tie || residual >= best.Residual - Tie && (bestNamed || !named))) continue;
                    best = new Pair { Left = l.Name, Right = r.Name, Residual = residual };
                    bestNamed = named;
                }
            return best;
        }

        static void Final(Mesh mesh, int index, Vector3[] deltas) =>
            mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, deltas, null, null);

        // +1 on the avatar's left of the centre, -1 on its right, 0 on it.
        static int[] SidesOfVertices(SkinnedMeshRenderer renderer, Vector3 centre, Vector3 leftAxis)
        {
            var skinned = renderer.bones != null && renderer.bones.Length > 0;
            var baked = skinned ? new Mesh() : null;
            try
            {
                Vector3[] vertices; Matrix4x4 toWorld;
                if (skinned)
                {
                    // Baked with the scale, so only the position and rotation remain.
                    renderer.BakeMesh(baked, true);
                    vertices = baked.vertices;
                    toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                }
                else { vertices = renderer.sharedMesh.vertices; toWorld = renderer.transform.localToWorldMatrix; }
                if (vertices.Length != renderer.sharedMesh.vertexCount) return null;
                var result = new int[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                {
                    var along = Vector3.Dot(toWorld.MultiplyPoint3x4(vertices[i]) - centre, leftAxis);
                    result[i] = along > 1e-6f ? 1 : along < -1e-6f ? -1 : 0;
                }
                return result;
            }
            finally { if (baked != null) Object.DestroyImmediate(baked); }
        }

        // A name that marks the other eye.
        static bool Contradicts(string name, int side)
        {
            var marked = BlinkShapeNames.MarkedSide(Normalize(name), out _);
            return marked != BlinkShapeNames.NotClosing && marked != side;
        }

        // The same name apart from its side marker; one of them may be bare,
        // as in ウィンク / ウィンク右.
        static bool Siblings(string left, string right)
        {
            var a = BlinkShapeNames.MarkedSide(Normalize(left), out var leftRoot);
            var b = BlinkShapeNames.MarkedSide(Normalize(right), out var rightRoot);
            if (a == BlinkShapeNames.NotClosing && b == BlinkShapeNames.NotClosing) return false;
            if (a == BlinkShapeNames.NotClosing) leftRoot = Normalize(left);
            if (b == BlinkShapeNames.NotClosing) rightRoot = Normalize(right);
            return string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
        }

        // Full-width digits and half-width kana read as their usual forms.
        static string Normalize(string name) => (name ?? "").Normalize(NormalizationForm.FormKC);
    }
}
