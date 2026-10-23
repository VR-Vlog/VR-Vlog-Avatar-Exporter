using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // The whole avatar beside the objects page. It is a plain copy of the avatar
    // in a preview scene of its own (no NDMF build), made once when the page
    // opens; choosing 除外 or 含める only hides or shows its objects and
    // renderers, so the preview follows every choice at once. The source, its
    // scene and its prefab are never touched.
    internal sealed class ExcludedObjectsPreview : IDisposable
    {
        readonly GameObject source;
        GameObject copy;
        PreviewRenderUtility utility;
        readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
        // What this preview turned off on the copy, with the state to give back.
        readonly Dictionary<GameObject, bool> hiddenObjects = new Dictionary<GameObject, bool>();
        readonly Dictionary<Renderer, bool> hiddenRenderers = new Dictionary<Renderer, bool>();
        Bounds bounds;
        bool framed;
        float yaw, zoom = 1;

        internal ExcludedObjectsPreview(GameObject source)
        {
            this.source = source != null ? source : throw new ArgumentNullException(nameof(source));
            Build();
        }

        internal GameObject Copy => copy;
        internal GameObject Source => source;
        internal bool Usable => copy != null && utility != null;
        // What the camera was fitted to. For tests.
        internal Bounds Framing => bounds;

        void Build()
        {
            Release();
            try
            {
                copy = NdmfExportPreparation.InstantiateOwnedCopy(source);
                void Map(Transform from, Transform to)
                {
                    copies[from] = to;
                    for (var index = 0; index < from.childCount && index < to.childCount; index++) Map(from.GetChild(index), to.GetChild(index));
                }
                Map(source.transform, copy.transform);
                utility = new PreviewRenderUtility();
                NdmfExportPreparation.ShowOwnedCopyInPreview(copy, utility);
                // The same camera and lights as the expressions page preview.
                utility.camera.fieldOfView = 30;
                utility.camera.nearClipPlane = .01f; utility.camera.farClipPlane = 100;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(.18f, .18f, .2f);
                utility.lights[0].intensity = 1;
                utility.lights[0].transform.rotation = Quaternion.Euler(30, 150, 0);
                utility.lights[1].intensity = .5f; utility.ambientColor = Color.gray;
                framed = false;
            }
            catch { Release(); throw; }
        }

        // Hides what the VRM leaves out: whole objects, and single renderers.
        // Anything shown before and not listed now comes back as it was.
        internal void Show(IEnumerable<GameObject> objects, IEnumerable<Renderer> renderers)
        {
            if (copy == null) return;
            var objectList = (objects ?? Array.Empty<GameObject>()).Where(item => item != null && Under(item.transform)).ToArray();
            var rendererList = (renderers ?? Array.Empty<Renderer>()).Where(item => item != null && Under(item.transform)).ToArray();
            // An object added to the avatar after the copy was made: copy it again.
            if (objectList.Any(item => !copies.ContainsKey(item.transform)) || rendererList.Any(item => !copies.ContainsKey(item.transform))) Build();
            // Framed once, on the avatar as it is in the scene before anything is
            // hidden, so a choice never moves the camera and what 含める brings
            // back on the body is in view.
            if (!framed) Frame();
            var nextObjects = new HashSet<GameObject>(objectList.Select(item => copies.TryGetValue(item.transform, out var to) ? to.gameObject : null).Where(item => item != null));
            var nextRenderers = new HashSet<Renderer>(rendererList.Select(CopyOf).Where(item => item != null));
            foreach (var pair in hiddenObjects.Where(pair => !nextObjects.Contains(pair.Key)).ToArray())
            {
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
                hiddenObjects.Remove(pair.Key);
            }
            foreach (var pair in hiddenRenderers.Where(pair => !nextRenderers.Contains(pair.Key)).ToArray())
            {
                if (pair.Key != null) pair.Key.enabled = pair.Value;
                hiddenRenderers.Remove(pair.Key);
            }
            foreach (var item in nextObjects)
                if (!hiddenObjects.ContainsKey(item)) { hiddenObjects.Add(item, item.activeSelf); item.SetActive(false); }
            foreach (var item in nextRenderers)
                if (!hiddenRenderers.ContainsKey(item)) { hiddenRenderers.Add(item, item.enabled); item.enabled = false; }
        }

        bool Under(Transform target) => target == source.transform || target.IsChildOf(source.transform);

        // The renderer of the same type at the same place among its object's renderers.
        Renderer CopyOf(Renderer renderer)
        {
            if (!copies.TryGetValue(renderer.transform, out var owner)) return null;
            var type = renderer.GetType();
            var index = Array.IndexOf(renderer.GetComponents(type), renderer);
            var candidates = owner.GetComponents(type);
            return index >= 0 && index < candidates.Length ? candidates[index] as Renderer : null;
        }

        // Whether the copy shows this object of the avatar now. For tests.
        internal bool Shows(GameObject item) =>
            item != null && copies.TryGetValue(item.transform, out var to) && to.gameObject.activeInHierarchy;

        internal bool Shows(Renderer item)
        {
            var copied = item != null ? CopyOf(item) : null;
            return copied != null && copied.enabled && copied.gameObject.activeInHierarchy;
        }

        void Frame()
        {
            var body = BodyBounds(copy);
            if (body == null) return;
            bounds = body.Value;
            framed = true;
        }

        // The body to fit: each mesh by its own vertices, not Renderer.bounds,
        // which a skinned mesh often widens on purpose. On a humanoid each mesh
        // is cut to the reach of its skeleton, so a camera or tool kept far from
        // the body does not shrink the avatar. Null when nothing is shown.
        internal static Bounds? BodyBounds(GameObject avatar)
        {
            // Particles, trails and lines can reach far from the body; leave them out of the fit.
            var renderers = ExportRendererSelection.Enumerate(avatar)
                .Where(item => !(item is ParticleSystemRenderer) && !(item is TrailRenderer) && !(item is LineRenderer)).ToArray();
            if (renderers.Length == 0) return null;
            var reach = SkeletonReach(avatar);
            Bounds? fit = null;
            var baked = new Mesh();
            try
            {
                foreach (var renderer in renderers)
                {
                    var box = MeshBounds(renderer, baked);
                    if (reach.HasValue)
                    {
                        var min = Vector3.Max(box.min, reach.Value.min);
                        var max = Vector3.Min(box.max, reach.Value.max);
                        if (min.x > max.x || min.y > max.y || min.z > max.z) continue;
                        box.SetMinMax(min, max);
                    }
                    if (fit.HasValue) { var grown = fit.Value; grown.Encapsulate(box); fit = grown; }
                    else fit = box;
                }
            }
            finally { Object.DestroyImmediate(baked); }
            // Everything lies outside the skeleton's reach: fall back to what is drawn.
            if (!fit.HasValue)
            {
                var all = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) all.Encapsulate(renderer.bounds);
                fit = all;
            }
            return fit;
        }

        static Bounds MeshBounds(Renderer renderer, Mesh baked)
        {
            Vector3[] vertices = null;
            var toWorld = renderer.transform.localToWorldMatrix;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                // Baked with the scale, so only the position and rotation remain.
                skinned.BakeMesh(baked, true);
                vertices = baked.vertices;
                toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                vertices = filter.sharedMesh.vertices;
            if (vertices == null || vertices.Length == 0) return renderer.bounds;
            var box = new Bounds(toWorld.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) box.Encapsulate(toWorld.MultiplyPoint3x4(vertex));
            return box;
        }

        // Around a humanoid skeleton: from just below the feet to a head's
        // height again above the head bone, which leaves room for large heads,
        // hair and ears, and half that height out to the sides for wide
        // sleeves, skirts and wings.
        static Bounds? SkeletonReach(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            var head = HumanoidBones.Resolve(animator, HumanBodyBones.Head);
            if (head == null) return null;
            var bones = new Bounds(head.position, Vector3.zero);
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var found = HumanoidBones.Resolve(animator, bone);
                if (found != null) bones.Encapsulate(found.position);
            }
            var height = head.position.y - bones.min.y;
            if (height <= .01f) return null;
            var side = height * .5f;
            var reach = new Bounds();
            reach.SetMinMax(
                new Vector3(bones.min.x - side, bones.min.y - height * .1f, bones.min.z - side),
                new Vector3(bones.max.x + side, head.position.y + height, bones.max.z + side));
            return reach;
        }

        // The full body fits the rectangle: the camera backs off for the height,
        // or for the width when the rectangle is the wider of the two. True when
        // a drag or the wheel moved the camera and the window should repaint.
        internal bool Draw(Rect rect)
        {
            if (!Usable) return false;
            var current = Event.current;
            if (current.type == EventType.Repaint && rect.width > 1 && rect.height > 1)
            {
                var camera = utility.camera;
                var half = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                var aspect = rect.width / rect.height;
                var extent = framed ? bounds.extents : Vector3.one * .5f;
                var fit = Mathf.Max(extent.y, extent.x / aspect) * 1.08f;
                // Seen from the front: the width is the body's, and the distance is
                // measured at its front, which is its depth away from the center.
                var distance = Mathf.Max(.3f, fit / half + extent.z);
                var center = framed ? bounds.center : copy.transform.position;
                utility.BeginPreview(rect, GUIStyle.none);
                camera.transform.position = center + Quaternion.AngleAxis(yaw, Vector3.up) * copy.transform.forward * distance * zoom;
                camera.transform.LookAt(center, Vector3.up);
                // Our own fit: Render would widen the view of a narrow rectangle again.
                utility.Render(false, false);
                GUI.DrawTexture(rect, utility.EndPreview(), ScaleMode.StretchToFill, false);
            }
            if (!rect.Contains(current.mousePosition)) return false;
            if (current.type == EventType.MouseDrag && current.button == 0)
            { yaw = Mathf.Repeat(yaw + current.delta.x + 180, 360) - 180; current.Use(); return true; }
            if (current.type == EventType.ScrollWheel)
            { zoom = Mathf.Clamp(zoom + current.delta.y * .04f, .25f, 2); current.Use(); return true; }
            return false;
        }

        void Release()
        {
            hiddenObjects.Clear(); hiddenRenderers.Clear(); copies.Clear();
            utility?.Cleanup(); utility = null;
            if (copy != null) Object.DestroyImmediate(copy);
            copy = null;
            NdmfExportPreparation.ReleaseUnusedCopyScene();
        }

        public void Dispose() => Release();
    }
}
