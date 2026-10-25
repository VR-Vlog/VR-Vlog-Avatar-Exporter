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
        // The camera, as the Scene view keeps it: a turn, a pivot moved off the
        // body's center by panning, and the distance as a share of the full-body fit.
        Quaternion view;
        Vector3 pan;
        float zoom = 1;
        ViewTool dragTool;
        readonly int controlHint = "ExcludedObjectsPreview".GetHashCode();

        internal enum ViewTool { None, Orbit, Pan, Zoom, Look }

        internal ExcludedObjectsPreview(GameObject source)
        {
            this.source = source != null ? source : throw new ArgumentNullException(nameof(source));
            Build();
            view = Front;
        }

        // The keys and buttons in the tooltip, named as on this computer.
        internal static string Controls
        {
            get
            {
                var mac = Application.platform == RuntimePlatform.OSXEditor;
                return string.Format(ExporterLocalization.T("ドラッグ・{0}+ドラッグで回転\n中ドラッグ・{0}+{1}+ドラッグで移動\n右ドラッグで見回す\nホイール・{0}+右ドラッグで拡大\nFで全身を表示"),
                    mac ? "Option" : "Alt", mac ? "Command" : "Ctrl");
            }
        }

        internal GameObject Copy => copy;
        internal GameObject Source => source;
        internal bool Usable => copy != null && utility != null;
        // What the camera was fitted to, in the avatar's own axes. For tests.
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
        // the body does not shrink the avatar. The box is in the avatar's own
        // axes (world positions turned back by the root's rotation), so x is the
        // width seen from its front even when the root is turned in the scene.
        // Null when nothing is shown.
        internal static Bounds? BodyBounds(GameObject avatar)
        {
            // Particles, trails and lines can reach far from the body; leave them out of the fit.
            var renderers = ExportRendererSelection.Enumerate(avatar)
                .Where(item => !(item is ParticleSystemRenderer) && !(item is TrailRenderer) && !(item is LineRenderer)).ToArray();
            if (renderers.Length == 0) return null;
            var toBody = Matrix4x4.Rotate(Quaternion.Inverse(avatar.transform.rotation));
            var reach = SkeletonReach(avatar, toBody);
            Bounds? fit = null, whole = null;
            var baked = new Mesh();
            try
            {
                foreach (var renderer in renderers)
                {
                    var box = MeshBounds(renderer, baked, toBody);
                    if (whole.HasValue) { var all = whole.Value; all.Encapsulate(box); whole = all; }
                    else whole = box;
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
            // Everything lies outside the skeleton's reach: fit all the meshes, still by their vertices.
            return fit ?? whole;
        }

        static Bounds MeshBounds(Renderer renderer, Mesh baked, Matrix4x4 toBody)
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
            if (vertices == null || vertices.Length == 0)
            {
                var drawn = renderer.bounds;
                toWorld = Matrix4x4.identity;
                vertices = new Vector3[8];
                for (var corner = 0; corner < 8; corner++)
                    vertices[corner] = drawn.center + Vector3.Scale(drawn.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            }
            var toFrame = toBody * toWorld;
            var box = new Bounds(toFrame.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) box.Encapsulate(toFrame.MultiplyPoint3x4(vertex));
            return box;
        }

        // Around a humanoid skeleton: from just below the feet to a head's
        // height again above the head bone, which leaves room for large heads,
        // hair and ears, and half that height out to the sides for wide
        // sleeves, skirts and wings.
        static Bounds? SkeletonReach(GameObject avatar, Matrix4x4 toBody)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            var head = HumanoidBones.Resolve(animator, HumanBodyBones.Head);
            if (head == null) return null;
            var top = toBody.MultiplyPoint3x4(head.position);
            var bones = new Bounds(top, Vector3.zero);
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var found = HumanoidBones.Resolve(animator, bone);
                if (found != null) bones.Encapsulate(toBody.MultiplyPoint3x4(found.position));
            }
            var height = top.y - bones.min.y;
            if (height <= .01f) return null;
            var side = height * .5f;
            var reach = new Bounds();
            reach.SetMinMax(
                new Vector3(bones.min.x - side, bones.min.y - height * .1f, bones.min.z - side),
                new Vector3(bones.max.x + side, top.y + height, bones.max.z + side));
            return reach;
        }

        float HalfView => Mathf.Tan((utility != null ? utility.camera.fieldOfView : 30) * .5f * Mathf.Deg2Rad);

        Quaternion Body => copy != null ? copy.transform.rotation : source.transform.rotation;

        // From the avatar's front, looking at it.
        Quaternion Front => Quaternion.LookRotation(Body * Vector3.back, Body * Vector3.up);

        // The full body fits the rectangle, seen along the turn: the camera backs
        // off for the height, or for the width when the rectangle is the wider of
        // the two, and the distance is measured at the body's near side, which is
        // its depth away from the center.
        float Fit(Rect rect, Quaternion turn)
        {
            var extent = framed ? bounds.extents : Vector3.one * .5f;
            var axes = Quaternion.Inverse(turn) * Body;
            Vector3 Across(Vector3 axis) => new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            var seen = Across(axes * Vector3.right) * extent.x + Across(axes * Vector3.up) * extent.y + Across(axes * Vector3.forward) * extent.z;
            var fit = Mathf.Max(seen.y, seen.x / Mathf.Max(.01f, rect.width / Mathf.Max(1, rect.height))) * 1.08f;
            return Mathf.Max(.3f, fit / HalfView + seen.z);
        }

        // The front's fit, times the zoom: turning keeps the distance.
        float Distance(Rect rect) => Fit(rect, Front) * zoom;

        internal Vector3 Pivot => (copy == null ? source.transform.position : framed ? copy.transform.rotation * bounds.center : copy.transform.position) + pan;

        // Where the camera is and where it looks. For tests.
        internal Pose CameraPose(Rect rect) => new Pose(Pivot - view * Vector3.forward * Distance(rect), view);

        // The Scene view's mouse buttons (Unity 2022.3, SceneViewMotion): Alt and
        // the left button orbits, the middle button pans (also with Alt, or Alt
        // and the action key with the left button), the right button looks
        // around, Alt and the right button zooms, and on a Mac Option, Control
        // and the left button zooms too. The left button alone orbits as well:
        // there is nothing here to select.
        internal static ViewTool ToolFor(int button, bool alt, bool action, bool control)
        {
            switch (button)
            {
                case 0: return alt && action ? ViewTool.Pan : alt && control ? ViewTool.Zoom : ViewTool.Orbit;
                case 1: return alt ? ViewTool.Zoom : ViewTool.Look;
                case 2: return ViewTool.Pan;
                default: return ViewTool.None;
            }
        }

        // The Scene view's own steps: 0.003 radians a point, the turn up and down
        // about the camera's right and sideways about the world's up.
        static Quaternion Turn(Quaternion rotation, Vector2 delta)
        {
            rotation = Quaternion.AngleAxis(delta.y * .003f * Mathf.Rad2Deg, rotation * Vector3.right) * rotation;
            return Quaternion.AngleAxis(delta.x * .003f * Mathf.Rad2Deg, Vector3.up) * rotation;
        }

        // Around the pivot.
        internal void Orbit(Vector2 delta) => view = Turn(view, delta);

        // The camera stays where it is and turns, so the pivot moves in front of it.
        internal void Look(Rect rect, Vector2 delta)
        {
            var distance = Distance(rect);
            var camera = Pivot - view * Vector3.forward * distance;
            view = Turn(view, delta);
            pan += camera + view * Vector3.forward * distance - Pivot;
        }

        // The point under the mouse keeps under it, as at the pivot's distance.
        internal void Pan(Rect rect, Vector2 delta, bool fast)
        {
            var perPoint = 2 * Distance(rect) * HalfView / Mathf.Max(1, rect.height);
            pan += view * new Vector3(-delta.x, delta.y, 0) * perPoint * (fast ? 4 : 1);
        }

        // Toward the point under the mouse, as the Scene view's wheel does; at
        // the center when no point is given.
        internal void Zoom(Rect rect, float factor, Vector2? at)
        {
            var before = Distance(rect);
            zoom = Mathf.Clamp(zoom * factor, .02f, 10);
            if (!at.HasValue) return;
            var half = HalfView;
            var x = (at.Value.x - rect.x) / Mathf.Max(1, rect.width) * 2 - 1;
            var y = 1 - (at.Value.y - rect.y) / Mathf.Max(1, rect.height) * 2;
            var point = view * new Vector3(x * half * rect.width / Mathf.Max(1, rect.height) * before, y * half * before, 0);
            pan += point * (1 - Distance(rect) / before);
        }

        // F: the whole body in view again, at its center and seen from where the
        // camera now looks (the Scene view's F keeps the turn too).
        internal void FrameAll(Rect rect) { pan = Vector3.zero; zoom = Fit(rect, view) / Fit(rect, Front); }

        // True when the mouse or F moved the camera and the window should repaint.
        internal bool Draw(Rect rect)
        {
            if (!Usable) return false;
            var current = Event.current;
            var id = GUIUtility.GetControlID(controlHint, FocusType.Keyboard, rect);
            if (current.type == EventType.Repaint && rect.width > 1 && rect.height > 1)
            {
                var camera = utility.camera;
                utility.BeginPreview(rect, GUIStyle.none);
                var pose = CameraPose(rect);
                camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                // Our own fit: Render would widen the view of a narrow rectangle again.
                utility.Render(false, false);
                GUI.DrawTexture(rect, utility.EndPreview(), ScaleMode.StretchToFill, false);
            }
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!rect.Contains(current.mousePosition) || GUIUtility.hotControl != 0) return false;
                    dragTool = ToolFor(current.button, current.alt, EditorGUI.actionKey, current.control);
                    if (dragTool == ViewTool.None) return false;
                    // The keyboard as well, so that F reaches the preview after a click,
                    // in the quick window's IMGUIContainer too.
                    GUIUtility.hotControl = GUIUtility.keyboardControl = id;
                    current.Use();
                    return false;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) return false;
                    if (dragTool == ViewTool.Orbit) Orbit(current.delta);
                    else if (dragTool == ViewTool.Look) Look(rect, current.delta);
                    else if (dragTool == ViewTool.Pan) Pan(rect, current.delta, current.shift);
                    else if (dragTool == ViewTool.Zoom) Zoom(rect, 1 + HandleUtility.niceMouseDeltaZoom * (current.shift ? 9 : 3) * .003f, null);
                    current.Use();
                    return true;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) return false;
                    GUIUtility.hotControl = 0;
                    dragTool = ViewTool.None;
                    current.Use();
                    return false;
                case EventType.ScrollWheel:
                    if (!rect.Contains(current.mousePosition)) return false;
                    // The wheel zooms at the center and Alt and the wheel toward the mouse, as in the 3D Scene view.
                    Zoom(rect, 1 + current.delta.y * .015f, current.alt ? current.mousePosition : (Vector2?)null);
                    current.Use();
                    return true;
                case EventType.KeyDown:
                    if (current.keyCode != KeyCode.F || current.modifiers != EventModifiers.None && current.modifiers != EventModifiers.FunctionKey) return false;
                    if (GUIUtility.keyboardControl != id && (GUIUtility.keyboardControl != 0 || !rect.Contains(current.mousePosition))) return false;
                    FrameAll(rect);
                    current.Use();
                    return true;
            }
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
