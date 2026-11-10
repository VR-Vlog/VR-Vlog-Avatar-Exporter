using System;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 視線の動く範囲: five face previews in a cross (up, left, straight, right,
    // down) beside one slider for up, down and the sides. A slider turns the
    // eyes of the preview of its own direction, on the avatar itself.
    // The section holds no settings: the host hands it the value and saves
    // what it returns, so it can sit on any page that has the avatar in a
    // PreviewRenderUtility (today the 視線の設定 step of the expression board).
    internal sealed class GazeRangeSection : IDisposable
    {
        internal const string Title = "視線の動く範囲";
        internal const string Caption = "アプリでカメラを目で追う角度";
        internal const string NoEyeBones = "目のボーンが無いため変えられません";
        internal const string AuthoredVrm = "アバターのVRMの視線の設定を使うため変えられません";
        internal const string Straight = "通常";
        internal const string UpLabel = "上", DownLabel = "下", SideLabel = "左右", LeftLabel = "左", RightLabel = "右";
        // {0} the angles, as 4° or 上 14°・下 11°・左右 20°.
        internal const string EyeLookNote = "初期値は VRChat の Eye Look の角度（{0}）";
        internal const string VrmDefaultNote = "初期値は VRM の標準の角度（{0}）";
        // Under VRM's default the eyes reach that angle only at a 90° gaze; a
        // changed direction follows the gaze one to one (VrmLookAtExport.Range.Linear).
        internal const string FollowNote = "変えた方向は標準より大きく目が動きます";

        // The preview copy, drawn by the host's PreviewRenderUtility.
        internal sealed class Target
        {
            internal GameObject Source;
            internal GameObject Avatar;
            internal PreviewRenderUtility Preview;
            // Puts the face the eyes are judged on (the neutral one) on Avatar
            // and returns what puts the previous face back. Null keeps the face.
            internal Func<Action> ShowNeutralFace;
        }

        internal enum Direction { Up, Left, Straight, Right, Down }
        static readonly Direction[] Directions = { Direction.Up, Direction.Left, Direction.Straight, Direction.Right, Direction.Down };

        const float Cell = 88, Gap = 6, CaptionHeight = 18, SliderLabelWidth = 40, FieldWidth = 44, UnitWidth = 14, SlidersMinWidth = 200;

        readonly RenderTexture[] textures = new RenderTexture[Directions.Length];
        readonly bool[] stale = new bool[Directions.Length];
        GameObject inspectedSource;
        bool inspected, inspectFailed;
        VrmLookAtExport.GazeInspection inspection;
        GameObject renderedAvatar;
        GUIStyle captionStyle, warningStyle, centerStyle;
        bool renderFailed;

        // The host made a new copy or showed it differently: draw every preview again.
        internal void Invalidate()
        {
            inspected = false;
            inspectedSource = null;
            renderedAvatar = null;
            renderFailed = false;
        }

        // Returns the value to save when a slider moved, else null.
        internal GazeRangeOptions Draw(Target target, GazeRangeOptions value)
        {
            value = value ?? new GazeRangeOptions();
            EnsureStyles();
            if (!inspected || target?.Source != inspectedSource)
            {
                inspectedSource = target?.Source;
                renderedAvatar = null;
                // A failed inspection leaves the section unchangeable, so a slider
                // never saves 1° from the empty ranges it would otherwise show.
                inspection = default;
                inspected = true;
                try { inspection = VrmLookAtExport.Inspect(inspectedSource); inspectFailed = false; }
                catch (Exception exception) when (!(exception is ExitGUIException))
                {
                    inspectFailed = true;
                    Debug.LogException(exception);
                }
            }
            var title = ExporterLocalization.T(Title);
            GUI.Label(GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true)), title, EditorStyles.boldLabel);
            var editable = !inspectFailed && inspection.Support == VrmLookAtExport.GazeSupport.Ranges;
            var reason = inspectFailed ? null : inspection.Support == VrmLookAtExport.GazeSupport.NoEyeBones ? NoEyeBones
                : inspection.Support == VrmLookAtExport.GazeSupport.AuthoredVrm ? AuthoredVrm : null;
            var captionRect = GUILayoutUtility.GetRect(100, CaptionHeight, GUILayout.ExpandWidth(true));
            GUI.Label(captionRect, ExporterLocalization.T(reason ?? Caption), reason != null ? warningStyle : captionStyle);

            var initial = inspection.Ranges.Shown();
            var shown = editable ? inspection.Ranges.With(value).Shown() : initial;
            GazeRangeOptions changed = null;
            using (new EditorGUI.DisabledScope(!editable))
            {
                // Less a vertical scrollbar, so opening the section never adds a horizontal one.
                var width = Mathf.Max(0, EditorGUIUtility.currentViewWidth - 60);
                var gridWidth = Cell * 3 + Gap * 2;
                var cellHeight = Cell + CaptionHeight;
                var gridHeight = cellHeight * 3 + Gap * 2;
                // Beside the cross when there is room, under it otherwise. The
                // sizes are fixed so a longer label never moves the previews.
                var beside = width >= gridWidth + 16 + SlidersMinWidth;
                var slidersHeight = EditorGUIUtility.singleLineHeight * 5 + 10;
                var area = GUILayoutUtility.GetRect(gridWidth, beside ? gridHeight : gridHeight + 8 + slidersHeight, GUILayout.ExpandWidth(true));
                var grid = new Rect(area.x, area.y + 4, gridWidth, gridHeight);
                var sliders = beside
                    ? new Rect(grid.xMax + 16, grid.y + cellHeight + Gap, Mathf.Max(SlidersMinWidth, area.xMax - grid.xMax - 16), slidersHeight)
                    : new Rect(grid.x, grid.yMax + 8, Mathf.Max(SlidersMinWidth, area.xMax - grid.x), slidersHeight);
                // Rendered in the layout pass, which draws nothing, so the render
                // targets it switches never touch what this repaint draws.
                if (Event.current.type == EventType.Layout && !renderFailed)
                {
                    // A preview that cannot be drawn stays blank until the host
                    // invalidates the section; the sliders still work.
                    try { Render(target, editable ? inspection.Ranges.With(value) : (VrmLookAtExport.Ranges?)null); }
                    catch (Exception exception) when (!(exception is ExitGUIException))
                    {
                        if (!renderFailed) Debug.LogWarning(string.Format(ExporterLocalization.T("視線の動く範囲のプレビューを描けませんでした: {0}"), exception.Message));
                        renderFailed = true;
                    }
                }
                DrawCross(grid, shown, !editable);
                changed = DrawSliders(sliders, editable, value, initial, shown);
            }
            return changed;
        }

        void DrawCross(Rect grid, GazeRangeOptions shown, bool dim)
        {
            foreach (var direction in Directions)
            {
                var (column, row) = Slot(direction);
                var cell = new Rect(grid.x + column * (Cell + Gap), grid.y + row * (Cell + CaptionHeight + Gap), Cell, Cell);
                EditorGUI.DrawRect(new Rect(cell.x - 1, cell.y - 1, cell.width + 2, cell.height + 2), ExporterColors.Hairline);
                var texture = textures[(int)direction];
                if (texture != null)
                {
                    var color = GUI.color;
                    if (dim) GUI.color = new Color(color.r, color.g, color.b, color.a * .5f);
                    GUI.DrawTexture(cell, texture, ScaleMode.ScaleToFit, false);
                    GUI.color = color;
                }
                GUI.Label(new Rect(cell.x - Gap, cell.yMax, Cell + Gap * 2, CaptionHeight), CellLabel(direction, shown), centerStyle);
            }
        }

        internal static (int Column, int Row) Slot(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return (1, 0);
                case Direction.Left: return (0, 1);
                case Direction.Right: return (2, 1);
                case Direction.Down: return (1, 2);
                default: return (1, 1);
            }
        }

        // ↑ 上 4° under each turned preview; the straight one only names itself.
        internal static string CellLabel(Direction direction, GazeRangeOptions shown)
        {
            switch (direction)
            {
                case Direction.Up: return "↑ " + ExporterLocalization.T(UpLabel) + " " + Degrees(shown.Up);
                case Direction.Down: return "↓ " + ExporterLocalization.T(DownLabel) + " " + Degrees(shown.Down);
                case Direction.Left: return "← " + ExporterLocalization.T(LeftLabel) + " " + Degrees(shown.Side);
                case Direction.Right: return "→ " + ExporterLocalization.T(RightLabel) + " " + Degrees(shown.Side);
                default: return ExporterLocalization.T(Straight);
            }
        }

        static string Degrees(float value) => Mathf.RoundToInt(value) + "°";

        GazeRangeOptions DrawSliders(Rect area, bool editable, GazeRangeOptions value, GazeRangeOptions initial, GazeRangeOptions shown)
        {
            var line = EditorGUIUtility.singleLineHeight;
            int Row(int index, string label, float current, float start)
            {
                var row = new Rect(area.x, area.y + index * (line + 2), area.width, line);
                GUI.Label(new Rect(row.x, row.y, SliderLabelWidth, line), ExporterLocalization.T(label));
                // Reaches the initial and the stored angle, so opening never clamps a saved value.
                var maximum = Mathf.Max(GazeRangeOptions.SliderMaximum, Mathf.CeilToInt(start), Mathf.CeilToInt(current));
                var slider = new Rect(row.x + SliderLabelWidth, row.y, Mathf.Max(40, row.width - SliderLabelWidth - UnitWidth), line);
                var result = EditorGUI.IntSlider(slider, Mathf.RoundToInt(current), 1, maximum);
                GUI.Label(new Rect(slider.xMax + 2, row.y, UnitWidth, line), "°");
                return result;
            }
            var up = Row(0, UpLabel, shown.Up, initial.Up);
            var down = Row(1, DownLabel, shown.Down, initial.Down);
            var side = Row(2, SideLabel, shown.Side, initial.Side);
            var note = new Rect(area.x, area.y + 3 * (line + 2) + 4, area.width, line);
            var text = InitialNote(initial, inspection.FromEyeLook);
            GUI.Label(note, new GUIContent(text, text), captionStyle);
            if (editable && !inspection.FromEyeLook)
            {
                var follow = ExporterLocalization.T(FollowNote);
                GUI.Label(new Rect(note.x, note.yMax + 2, note.width, line), new GUIContent(follow, follow), captionStyle);
            }
            return Moved(editable, value, initial, shown, up, down, side);
        }

        // The value to save from the sliders' angles, else null. A disabled
        // slider still clamps what it shows (a failed inspection shows 0°, the
        // slider 1°), which is no change by the user, so it never saves.
        internal static GazeRangeOptions Moved(bool editable, GazeRangeOptions value, GazeRangeOptions initial, GazeRangeOptions shown, int up, int down, int side)
        {
            if (!editable) return null;
            var next = value.Copy();
            var moved = false;
            if (up != Mathf.RoundToInt(shown.Up)) { next.Up = Stored(up, initial.Up); moved = true; }
            if (down != Mathf.RoundToInt(shown.Down)) { next.Down = Stored(down, initial.Down); moved = true; }
            if (side != Mathf.RoundToInt(shown.Side)) { next.Side = Stored(side, initial.Side); moved = true; }
            return moved ? next : null;
        }

        // Target.ShowNeutralFace for a host that shows faces of type T: a
        // neutral face shown only in part goes back before the failure is reported.
        internal static Func<Action> NeutralFace<T>(Func<T> current, Action<T> show, Func<T> neutral) => () =>
        {
            var shown = current();
            try { show(neutral()); }
            catch { show(shown); throw; }
            return () => show(shown);
        };

        // A dragged slider changes the value on every move; the host saves it
        // once, when the drag lets go (or it leaves the avatar or closes).
        internal sealed class Unsaved
        {
            GameObject avatar;

            internal void Changed(GameObject source) => avatar = source;

            // The avatar to save for, once, when no control holds the mouse
            // or now is set; else null.
            internal GameObject Take(bool now = false)
            {
                if (!now && GUIUtility.hotControl != 0) return null;
                var result = avatar;
                avatar = null;
                return result;
            }
        }

        // Back at the avatar's own angle, the setting is cleared, so the
        // export writes the avatar's range exactly as before (not a rounded one).
        internal static float Stored(int degrees, float initial) => degrees == Mathf.RoundToInt(initial) ? 0 : degrees;

        internal static string InitialNote(GazeRangeOptions initial, bool fromEyeLook)
        {
            int up = Mathf.RoundToInt(initial.Up), down = Mathf.RoundToInt(initial.Down), side = Mathf.RoundToInt(initial.Side);
            var angles = up == down && down == side ? up + "°"
                : string.Format("{0} {1}°・{2} {3}°・{4} {5}°", ExporterLocalization.T(UpLabel), up, ExporterLocalization.T(DownLabel), down, ExporterLocalization.T(SideLabel), side);
            return string.Format(ExporterLocalization.T(fromEyeLook ? EyeLookNote : VrmDefaultNote), angles);
        }

        // How far each eye turns for a preview, in the avatar root's frame:
        // yaw toward +X, pitch upward. The camera faces the avatar, so the
        // preview on the right of the screen looks toward the avatar's -X:
        // the outer side of its left eye (at -X) and the inner side of its right.
        internal static (Vector2 Left, Vector2 Right) EyeTurns(Direction direction, VrmLookAtExport.Ranges ranges)
        {
            switch (direction)
            {
                case Direction.Up: return (new Vector2(0, ranges.VerticalUp.Output), new Vector2(0, ranges.VerticalUp.Output));
                case Direction.Down: return (new Vector2(0, -ranges.VerticalDown.Output), new Vector2(0, -ranges.VerticalDown.Output));
                case Direction.Right: return (new Vector2(-ranges.HorizontalOuter.Output, 0), new Vector2(-ranges.HorizontalInner.Output, 0));
                case Direction.Left: return (new Vector2(ranges.HorizontalInner.Output, 0), new Vector2(ranges.HorizontalOuter.Output, 0));
                default: return (Vector2.zero, Vector2.zero);
            }
        }

        // Draws only the previews whose angle changed since the last time, into
        // textures kept across draws. ranges is null when the eyes cannot turn.
        internal void Render(Target target, VrmLookAtExport.Ranges? ranges)
        {
            if (target?.Avatar == null || target.Preview == null) return;
            var animator = target.Avatar.GetComponent<Animator>();
            var head = animator != null && animator.isHuman ? HumanoidBones.Resolve(animator, HumanBodyBones.Head) : null;
            var left = animator != null && animator.isHuman ? HumanoidBones.Resolve(animator, HumanBodyBones.LeftEye) : null;
            var right = animator != null && animator.isHuman ? HumanoidBones.Resolve(animator, HumanBodyBones.RightEye) : null;
            // Drawn at twice the cell for a sharp face on any display scale.
            var points = Cell * 2;
            var size = Mathf.RoundToInt(points * Mathf.Max(1, EditorGUIUtility.pixelsPerPoint));
            var fresh = renderedAvatar != target.Avatar || renderedTurns == null;
            for (var i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null && textures[i].width == size) continue;
                if (textures[i] != null) UnityEngine.Object.DestroyImmediate(textures[i]);
                textures[i] = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                fresh = true;
            }
            var turns = new (Vector2 Left, Vector2 Right)[Directions.Length];
            for (var i = 0; i < Directions.Length; i++)
            {
                turns[i] = ranges.HasValue ? EyeTurns(Directions[i], ranges.Value) : default;
                stale[i] = fresh || renderedTurns[i] != turns[i];
            }
            if (Array.TrueForAll(stale, value => !value)) return;
            var root = target.Avatar.transform;
            Vector3 middle; float viewSize;
            if (left != null && right != null)
            {
                middle = (left.position + right.position) * .5f;
                viewSize = Mathf.Max(.05f, Vector3.Distance(left.position, right.position) * 2.6f);
            }
            else
            {
                // Without eyes the face is framed from the head, as the board frames it.
                middle = head != null ? head.position + Vector3.up * .06f : root.position + Vector3.up * 1.4f;
                viewSize = .22f;
            }
            var leftRest = left != null ? left.localRotation : Quaternion.identity;
            var rightRest = right != null ? right.localRotation : Quaternion.identity;
            var camera = target.Preview.camera;
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            Action restore = null;
            try
            {
                restore = target.ShowNeutralFace?.Invoke();
                var distance = viewSize * .5f / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                for (var i = 0; i < Directions.Length; i++)
                {
                    if (!stale[i]) continue;
                    if (left != null) Turn(root, left, leftRest, turns[i].Left);
                    if (right != null) Turn(root, right, rightRest, turns[i].Right);
                    target.Preview.BeginPreview(new Rect(0, 0, points, points), GUIStyle.none);
                    Texture drawn;
                    try
                    {
                        camera.aspect = 1;
                        camera.transform.position = middle + root.forward * distance;
                        camera.transform.LookAt(middle, root.up);
                        target.Preview.Render();
                    }
                    finally { drawn = target.Preview.EndPreview(); }
                    var active = RenderTexture.active;
                    Graphics.Blit(drawn, textures[i]);
                    RenderTexture.active = active;
                }
            }
            finally
            {
                // The eyes, the camera and the face go back as the host left
                // them, whichever of them fails to.
                if (left != null) left.localRotation = leftRest;
                if (right != null) right.localRotation = rightRest;
                camera.ResetAspect();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                restore?.Invoke();
            }
            renderedAvatar = target.Avatar;
            renderedTurns = turns;
        }

        (Vector2 Left, Vector2 Right)[] renderedTurns;

        // UniVRM's bone look-at: yaw and pitch on top of the rest rotation, in
        // the avatar root's frame (see VrmLookAtExport.TryEye).
        internal static void Turn(Transform root, Transform eye, Quaternion restLocal, Vector2 yawPitch)
        {
            eye.localRotation = restLocal;
            var frame = root.rotation;
            eye.rotation = frame * Quaternion.Euler(-yawPitch.y, yawPitch.x, 0) * Quaternion.Inverse(frame) * eye.rotation;
        }

        void EnsureStyles()
        {
            if (captionStyle != null) return;
            captionStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
            captionStyle.normal.textColor = ExporterColors.Muted;
            warningStyle = new GUIStyle(captionStyle);
            warningStyle.normal.textColor = ExporterColors.Warning;
            centerStyle = new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter };
        }

        public void Dispose()
        {
            for (var i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null) UnityEngine.Object.DestroyImmediate(textures[i]);
                textures[i] = null;
            }
            renderedAvatar = null; renderedTurns = null;
        }
    }
}
