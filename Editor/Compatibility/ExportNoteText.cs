namespace VRVlog.LilToonExporter
{
    // Notes the export writes and the page after a transfer reads back by kind (TransferNotes).
    // Both use these texts, so a changed sentence is still recognised there.
    public static class ExportNoteText
    {
        // {0} the installed lilToon version, {1} the one the app draws with.
        public const string LilToonRendered = "アプリではlilToon {1}として描画します（導入済みのバージョンは{0}）";
        // {0} the number of expressions.
        public const string ExpressionsAdded = "VRChatから{0}個の表情を登録しました。選択中は顔の形を保つため瞬き・口・視線の自動変形を止めます。";
        public const string ExpressionsNone = "VRChatから追加できた表情は0件です。選択用の表情がVRMにない場合、アプリの表情ボタンは表示されません。";
        public const string FxRestSkipped = "このベータ版ではFXの初期状態の復元を省略し、書き出し用コピーの設定を基本の顔・体形として保存しました。VRChatでの通常時の見た目と異なる場合があります。";

        // {0} texture name, {1}×{2} before, {3}×{4} after.
        public const string LilToonTextureResized = "lilToon画像「{0}」を{1}×{2}から{3}×{4}に縮小しました（読み込み時のメモリ使用量を軽減）。";
        public const string OutputTextureResized = "{0}: 出力用画像を {1}×{2} から {3}×{4} へ縮小しました。元の画像は変更していません。";
        // {0} material name, {1}×{2} after.
        public const string MainTextureResized = "{0}: メインカラーの焼き込み画像をモバイル向けに {1}×{2} へ縮小しました。";
        public const string AlphaMaskResized = "{0}: アルファマスクを含む画像を {1}×{2} へ縮小しました。";

        // PhysBone notes, each written as "<PhysBone name>: <sentence>". The first and the last are
        // written for every PhysBone; the others by its settings.
        public const string SpringForces = "揺れの力・減衰・重力はVRM向けの近似です。VRChatと同一の物理演算ではありません。";
        public const string SpringAdvanced = "Advanced積分／Stiffnessの独立効果は再現せず、PullとSpringから近似しました。";
        public const string SpringImmobileAllMotion = "Immobile（All Motion）は揺れの強さと減衰を上げて近似しました。";
        public const string SpringImmobileWorld = "Immobile（World）はアプリの安定化（揺れの基準をモデルのルートにする）に委ねました。";
        public const string SpringGravityFalloff = "Gravity Falloffは重力の減衰として近似しました。";
        public const string SpringStretch = "伸縮は保存せず、元のボーン長を保持しました。";
        public const string SpringAnimated = "Is Animatedは出力時の姿勢を基準とします。";
        public const string SpringLimit = "角度制限はUniVRMの制限へ近似しました。PolarのZ角度は0〜90度に制限します。";
        public const string SpringNotConverted = "Grab・Pose・外部衝突・PhysBoneパラメーターの連動は変換対象外です。";

        public static readonly string[] SpringSentences =
        {
            SpringForces, SpringAdvanced, SpringImmobileAllMotion, SpringImmobileWorld, SpringGravityFalloff,
            SpringStretch, SpringAnimated, SpringLimit, SpringNotConverted,
        };

        public static string Spring(string name, string sentence) => name + ": " + sentence;
    }
}
