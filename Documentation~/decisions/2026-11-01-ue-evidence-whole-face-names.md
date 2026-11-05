# 顔全体をまとめた Unified Expressions の名前だけでは、Unified Expressions のアバターと判定しない

- 日付: 2026-11-01
- 対象: `Editor/UnifiedExpressionRegistry.cs`・`Tests/Editor/UnifiedExpressionExportTests.cs`

## 背景

トゥーン調のアバターには、口の形を描き替えたシェイプキーを `Mouth_smile`・`Mouth_left`・`Mouth_right` のような名前で持ち、手動の切り替えに使うものがある。書き出しは名前を Unified Expressions（UE＝顔のトラッキング用の表情の名前の体系）の名前に読み替えており、`Mouth_smile` は `MouthSmile` になる。`MouthSmile` は ARKit の 52 チャンネルと重ならないので、これだけで UE のアバターと判定していた。その結果 `UE/MouthSmile`・`UE/MouthLeft`・`UE/MouthRight` を書き出し、アプリがカメラの顔トラッキングでこれらを動かして、描き替えた口が普段の口に重なって顔が崩れた。

## 決定

- 両目・両眉・口全体などをまとめた名前（`EyeClosed`・`EyeSquint`・`EyeWide`・`BrowDown`・`BrowUp`・`NoseSneer`・`CheekSquint`・`MouthUpperUp`・`MouthLowerDown`・`MouthStretch`・`MouthDimple`・`MouthPress`・`LipSuck`・`MouthSmile`・`MouthSad`・`MouthOpen`）は、ARKit と重なる名前と同じく、それだけでは UE のアバターと判定しない。これらは手動の切り替えにも使われる一般的な語だからである。
- `UE/` から始まる名前、左右や上下に分かれた UE の名前（`MouthCornerPullLeft`・`MouthRaiserUpper` など）があれば、今までどおり UE のアバターと判定する。判定したあとは、まとめた名前も今までどおり UE の表情として書き出す。
- 判定は `UnifiedExpressionRegistry.IsDistinctive` の 1 か所で行う。書き出す前の判定（`UnifiedExpressionPreparation`）と書き出した VRM での判定（`VrmUnifiedExpressions`）は同じ関数を使うので、両方に効く。

## 確認したこと

- EditMode テストで、`Mouth_smile`・`Mouth_left`・`Mouth_right` だけを持つメッシュは `UE/` の表情を書き出さないこと、`MouthRaiserUpper` または `UE/JawOpen` を足すと 3 つとも書き出すことを確かめた。

## 分かっている制限

- まとめた名前だけで UE の表情を作ったアバターは、UE のアバターと判定されなくなる。こうしたアバターは名前に `UE/` を付ければ今までどおり書き出せる。
