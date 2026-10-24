using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // Texts of what the conversion changed, on the board's page after a transfer.
    internal static class ConversionText
    {
        internal const string LilToon = "lilToon {0}で描画";
        internal const string LilToonInstalled = "導入済み {0}";
        internal const string Expressions = "VRChatから表情を登録";
        internal const string ExpressionCount = "{0}個";
        internal const string ExpressionsHold = "選択中は瞬き・口・視線の自動変形を止めます";
        internal const string NoExpressions = "VRChatから登録できた表情はありません";
        internal const string NoExpressionsDetail = "選択用の表情がVRMにない場合はアプリに表情ボタンが表示されません";
        internal const string Textures = "テクスチャを縮小";
        internal const string TextureCount = "{0}枚";
        internal const string Springs = "PhysBoneをVRMの揺れものに変換";
        internal const string BoneCount = "{0}本";
        internal const string AllBones = "すべて";
        internal const string BySetting = "設定による変更";
        internal const string BoneList = "揺れものの一覧";

        // A short label for each sentence of ExportNoteText.SpringSentences, in the same order.
        internal static readonly string[] SpringLabels =
        {
            "揺れの力・減衰・重力は近似",
            "Advanced積分とStiffnessをPull・Springで近似",
            "Immobile（All Motion）を揺れの強さと減衰で近似",
            "Immobile（World）はアプリの安定化で代用",
            "Gravity Falloffを重力の減衰で近似",
            "伸縮は保存せず元のボーン長を保持",
            "Is Animatedは書き出し時の姿勢を基準に使用",
            "角度制限をUniVRMの制限で近似",
            "Grab・Pose・外部衝突・パラメーター連動は変換しない",
        };

        internal static string SpringLabel(string sentence) =>
            SpringLabels[System.Array.IndexOf(ExportNoteText.SpringSentences, sentence)];
    }

    // What the conversion changed: one row per kind with a built-in icon and its size at the right end.
    public sealed partial class AppTransferPanel
    {
        // A folded row per kind; opened, the details one per line. The PhysBone sentences every
        // PhysBone has come once, then the sentences by setting with the number of PhysBones and their names folded.
        private void AddConversion(VisualElement host)
        {
            var notes = exportNotes;
            if (notes.LilToonDrawn != null)
                host.Add(KindRow("Shader Icon", string.Format(T(ConversionText.LilToon), notes.LilToonDrawn),
                    string.Format(T(ConversionText.LilToonInstalled), notes.LilToonInstalled), null));
            if (notes.Expressions > 0)
            {
                var fold = KindRow("AnimationClip Icon", T(ConversionText.Expressions), string.Format(T(ConversionText.ExpressionCount), notes.Expressions), "notes.expressions");
                fold.Add(Detail(T(ConversionText.ExpressionsHold)));
                host.Add(fold);
            }
            else if (notes.Expressions == 0)
            {
                var fold = KindRow("console.warnicon.sml", T(ConversionText.NoExpressions), "", "notes.expressions");
                fold.Add(Detail(T(ConversionText.NoExpressionsDetail)));
                host.Add(fold);
            }
            if (notes.Textures.Count > 0)
            {
                var fold = KindRow("Texture Icon", T(ConversionText.Textures), string.Format(T(ConversionText.TextureCount), notes.Textures.Count), "notes.textures");
                AddFirst(fold, notes.Textures.Select(texture => (System.Func<VisualElement>)(() => Pair(texture.Name, Size(texture), 168))));
                host.Add(fold);
            }
            if (notes.SpringBones.Count > 0)
            {
                var all = notes.SpringBones.Count;
                var fold = KindRow("Cloth Icon", T(ConversionText.Springs), string.Format(T(ConversionText.BoneCount), all), "notes.springs");
                foreach (var spring in notes.Springs.Where(spring => spring.Bones.Count == all))
                    fold.Add(Pair(T(ConversionText.SpringLabel(spring.Sentence)), T(ConversionText.AllBones), 64, T(spring.Sentence)));
                var bySetting = notes.Springs.Where(spring => spring.Bones.Count < all).ToList();
                if (bySetting.Count > 0)
                {
                    var label = Detail(T(ConversionText.BySetting));
                    label.style.marginTop = 4;
                    fold.Add(label);
                }
                foreach (var spring in bySetting)
                {
                    var index = System.Array.IndexOf(ExportNoteText.SpringSentences, spring.Sentence);
                    var bones = KindRow(null, T(ConversionText.SpringLabels[index]), string.Format(T(ConversionText.BoneCount), spring.Bones.Count), "notes.spring." + index);
                    bones.Q<Toggle>().tooltip = T(spring.Sentence);
                    AddFirst(bones, spring.Bones.Select(bone => (System.Func<VisualElement>)(() => Line(bone))));
                    fold.Add(bones);
                }
                var list = KindRow(null, T(ConversionText.BoneList), string.Format(T(ConversionText.BoneCount), all), "notes.bones");
                AddFirst(list, notes.SpringBones.Select(bone => (System.Func<VisualElement>)(() => Line(bone))));
                fold.Add(list);
                host.Add(fold);
            }
        }

        static string Size(TransferTextureNote texture) =>
            (texture.FromWidth > 0 ? texture.FromWidth + "×" + texture.FromHeight + " → " : "→ ") + texture.Width + "×" + texture.Height;

        // A kind: a folded row with an icon, the name and its size at the right end. A row without
        // a key has nothing to open; its arrow keeps its place so the icons line up.
        private static Foldout KindRow(string icon, string text, string value, string key)
        {
            var fold = new Foldout { text = text, value = key != null && OpenKinds.TryGetValue(key, out var open) && open };
            if (key != null) fold.RegisterValueChangedCallback(changed => { if (changed.target == fold) OpenKinds[key] = changed.newValue; });
            var toggle = fold.Q<Toggle>();
            toggle.style.height = 20;
            toggle.style.marginLeft = 0;
            toggle.style.marginRight = 0;
            toggle.tooltip = text;
            var input = toggle.Q(className: Toggle.inputUssClassName);
            input.style.flexGrow = 1;
            if (key == null)
            {
                toggle.Q(className: Foldout.checkmarkUssClassName).style.visibility = Visibility.Hidden;
                toggle.pickingMode = PickingMode.Ignore;
                input.pickingMode = PickingMode.Ignore;
            }
            var name = toggle.Q<Label>(className: Toggle.textUssClassName);
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            if (icon != null) input.Insert(input.IndexOf(name), Icon(icon));
            var count = new Label(value);
            count.AddToClassList("t");
            count.AddToClassList("grey");
            count.style.flexShrink = 0;
            count.style.marginLeft = 8;
            count.style.unityTextAlign = TextAnchor.MiddleRight;
            input.Add(count);
            fold.contentContainer.style.marginLeft = 16;
            return fold;
        }

        private static Image Icon(string name)
        {
            var image = new Image { image = EditorGUIUtility.IconContent(name).image, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = 16; image.style.height = 16; image.style.marginRight = 4; image.style.flexShrink = 0;
            return image;
        }

        // The first targets, then the rest on request.
        private static void AddFirst(Foldout fold, IEnumerable<System.Func<VisualElement>> rows)
        {
            var all = rows.ToList();
            foreach (var row in all.Take(FirstNoteItems)) fold.Add(row());
            if (all.Count <= FirstNoteItems) return;
            var more = new Label(string.Format(T(AppTransferText.ShowRest), all.Count - FirstNoteItems));
            more.AddToClassList("t");
            more.AddToClassList("link");
            more.style.height = 18;
            more.RegisterCallback<ClickEvent>(_ =>
            {
                var at = fold.contentContainer.IndexOf(more);
                more.RemoveFromHierarchy();
                foreach (var row in all.Skip(FirstNoteItems)) fold.contentContainer.Insert(at++, row());
            });
            fold.Add(more);
        }

        private static Label Detail(string text)
        {
            var label = Line(text);
            label.AddToClassList("grey");
            return label;
        }

        // A name on the left and a value of a fixed width on the right.
        private static VisualElement Pair(string left, string right, int width, string tooltip = null)
        {
            var row = TableRow();
            var name = Line(left);
            name.style.flexGrow = 1; name.style.flexShrink = 1;
            if (tooltip != null) name.tooltip = tooltip;
            row.Add(name);
            var value = Line(right);
            value.AddToClassList("grey");
            value.style.width = width; value.style.flexShrink = 0; value.style.marginLeft = 8;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(value);
            return row;
        }

        private static VisualElement TableRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 18;
            return row;
        }
    }
}
