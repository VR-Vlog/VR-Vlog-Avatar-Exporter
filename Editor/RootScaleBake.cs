using System;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // ModelExporter turns the avatar root's children into glTF scene roots
    // and never writes the root's own TRS. In VRChat the root scale sets the
    // avatar size, so move it into the direct children while exporting; their
    // world matrices stay the same. Root position and rotation are not baked
    // on purpose: the avatar is exported at the origin facing forward.
    internal sealed class RootScaleBake : IDisposable
    {
        private readonly Transform root;
        private readonly Vector3 rootScale;
        private readonly (Transform, Vector3, Vector3)[] children;

        private RootScaleBake(Transform root, (Transform, Vector3, Vector3)[] children)
        {
            this.root = root; rootScale = root.localScale; this.children = children;
        }

        internal static RootScaleBake Apply(GameObject avatar)
        {
            var root = avatar.transform;
            var scale = root.localScale;
            if (scale == Vector3.one) return new RootScaleBake(root, Array.Empty<(Transform, Vector3, Vector3)>());
            if (!Finite(scale) || Mathf.Abs(scale.x * scale.y * scale.z) <= 1e-12f)
                throw new InvalidOperationException(root.name + ": ルートの拡縮を保存できません。ゼロまたは不正な拡縮が含まれています。");
            // Validate every child before changing any of them.
            var saved = new (Transform, Vector3, Vector3)[root.childCount];
            var baked = new (Vector3 Position, Vector3 Scale)[saved.Length];
            for (var i = 0; i < saved.Length; i++)
            {
                var child = root.GetChild(i);
                saved[i] = (child, child.localPosition, child.localScale);
                baked[i] = (Vector3.Scale(scale, child.localPosition), ChildScale(root, child, scale));
            }
            var bake = new RootScaleBake(root, saved);
            for (var i = 0; i < saved.Length; i++)
            {
                var child = saved[i].Item1;
                child.localPosition = baked[i].Position; child.localScale = baked[i].Scale;
            }
            root.localScale = Vector3.one;
            return bake;
        }

        public void Dispose()
        {
            if (root == null) return;
            root.localScale = rootScale;
            foreach (var (child, position, scale) in children)
                if (child != null) { child.localPosition = position; child.localScale = scale; }
        }

        // S * R * C must equal R * C'. R^-1 * S * R is diagonal only when the
        // rotation keeps the unequal scale axes on axes; otherwise it needs shear.
        private static Vector3 ChildScale(Transform root, Transform child, Vector3 scale)
        {
            if (scale.x == scale.y && scale.y == scale.z) return child.localScale * scale.x;
            var m = Matrix4x4.Rotate(Quaternion.Inverse(child.localRotation)) * Matrix4x4.Scale(scale) * Matrix4x4.Rotate(child.localRotation);
            var tolerance = 1e-5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    if (row != column && Mathf.Abs(m[row, column]) > tolerance)
                        throw new InvalidOperationException(root.name + ": ルートの拡縮を保存できません。" + child.name +
                            "の回転と不均等な拡縮の組み合わせで、現在の形を保てません。");
            return Vector3.Scale(new Vector3(m.m00, m.m11, m.m22), child.localScale);
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}
