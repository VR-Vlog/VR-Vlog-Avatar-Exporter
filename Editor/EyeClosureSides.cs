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
            internal string Name;
            internal Vector3[] Within; // deltas on the bilateral shape's vertices
            internal double Outside;   // squared movement elsewhere
            internal double Squared;   // |Within|^2
            internal double OnTarget;  // Within . bilateral
            // Where it moves off its own side (or on the centre line): the
            // only vertices a left and a right shape can both move.
            internal int[] Spill;
            internal bool MarksTheOtherEye;
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
            var energy = target.Sum(delta => (double)delta.sqrMagnitude);
            var inSupport = new bool[deltas.Length];
            foreach (var i in support) inSupport[i] = true;

            var side = SidesOfVertices(renderer, centre, leftAxis);
            if (side == null) return null;
            var supportSide = support.Select(i => side[i]).ToArray();
            var lefts = new List<Side>(); var rights = new List<Side>();
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                if (index == bothIndex || mesh.GetBlendShapeFrameCount(index) == 0) continue;
                Final(mesh, index, deltas);
                // Most shapes (mouth, brows, ...) never move the bilateral
                // shape's vertices, so they cannot be a side: skip them before
                // going through the whole mesh.
                var touches = false;
                foreach (var i in support) if (Moves(deltas[i])) { touches = true; break; }
                if (!touches) continue;
                float total = 0, outside = 0, onLeft = 0, onRight = 0;
                for (var i = 0; i < deltas.Length; i++)
                {
                    var d = deltas[i];
                    var moved = d.x * d.x + d.y * d.y + d.z * d.z;
                    if (moved == 0) continue;
                    total += moved;
                    if (!inSupport[i]) outside += moved;
                    if (side[i] > 0) onLeft += moved; else if (side[i] < 0) onRight += moved;
                }
                if (total == 0 || outside > total * (1 - Inside)) continue;
                var isLeft = onLeft >= total * OneSided;
                if (!isLeft && onRight < total * OneSided) continue;
                var within = support.Select(i => deltas[i]).ToArray();
                double squared = 0, onTarget = 0; var spill = new List<int>();
                for (var k = 0; k < within.Length; k++)
                {
                    if (!Moves(within[k])) continue;
                    squared += within[k].sqrMagnitude; onTarget += Vector3.Dot(within[k], target[k]);
                    if (isLeft ? supportSide[k] <= 0 : supportSide[k] >= 0) spill.Add(k);
                }
                (isLeft ? lefts : rights).Add(new Side { Name = mesh.GetBlendShapeName(index), Within = within, Outside = outside,
                    Squared = squared, OnTarget = onTarget, Spill = spill.ToArray(),
                    MarksTheOtherEye = Contradicts(mesh.GetBlendShapeName(index), isLeft ? BlinkShapeNames.ClosingLeft : BlinkShapeNames.ClosingRight) });
            }

            Pair best = null; var bestNamed = false;
            foreach (var l in lefts)
                foreach (var r in rights)
                {
                    if (l.MarksTheOtherEye || r.MarksTheOtherEye) continue;
                    // |l + r - t|^2 = |l|^2 + |r|^2 + |t|^2 + 2 l.r - 2 l.t - 2 r.t,
                    // where l.r only sums where both move: l's spill (off the
                    // left) and r's spill on the left.
                    double cross = 0;
                    foreach (var k in l.Spill) cross += Vector3.Dot(l.Within[k], r.Within[k]);
                    foreach (var k in r.Spill) if (supportSide[k] > 0) cross += Vector3.Dot(l.Within[k], r.Within[k]);
                    var miss = l.Outside + r.Outside + l.Squared + r.Squared + energy + 2 * (cross - l.OnTarget - r.OnTarget);
                    var residual = (float)(Math.Max(0, miss) / energy);
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

        // Whether the side shapes together move the meshes as the bilateral
        // bindings do, within Rebuilt. Sides that also move something else
        // (a mouth corner, a brow) would change the blink.
        internal static bool Rebuilds(IEnumerable<BlinkShapeBinding> bilateral, IEnumerable<BlinkShapeBinding> sides)
        {
            var bothSum = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
            var sideSum = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
            bool Add(Dictionary<SkinnedMeshRenderer, Vector3[]> sums, BlinkShapeBinding binding)
            {
                var mesh = binding.Renderer != null ? binding.Renderer.sharedMesh : null;
                var index = mesh != null ? mesh.GetBlendShapeIndex(binding.Shape) : -1;
                if (index < 0 || mesh.GetBlendShapeFrameCount(index) == 0) return false;
                if (!bothSum.ContainsKey(binding.Renderer)) bothSum[binding.Renderer] = new Vector3[mesh.vertexCount];
                if (!sideSum.ContainsKey(binding.Renderer)) sideSum[binding.Renderer] = new Vector3[mesh.vertexCount];
                var deltas = new Vector3[mesh.vertexCount]; Final(mesh, index, deltas);
                var sum = sums[binding.Renderer]; var scale = binding.Weight / 100f;
                for (var i = 0; i < deltas.Length; i++) sum[i] += deltas[i] * scale;
                return true;
            }
            if (!bilateral.All(binding => Add(bothSum, binding)) || !sides.All(binding => Add(sideSum, binding))) return false;
            double miss = 0, energy = 0;
            foreach (var renderer in bothSum.Keys)
            {
                var b = bothSum[renderer]; var s = sideSum[renderer];
                for (var i = 0; i < b.Length; i++) { energy += b[i].sqrMagnitude; miss += (s[i] - b[i]).sqrMagnitude; }
            }
            return energy > 0 && miss <= Rebuilt * energy;
        }

        static bool Moves(Vector3 delta) => delta.x != 0 || delta.y != 0 || delta.z != 0;

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
