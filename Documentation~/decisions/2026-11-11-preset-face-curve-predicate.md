# 顔の横の曲線の判定を 1 か所にまとめ、標準の表情のキャッシュに曲線の切り替えを入れる

- 日付: 2026-11-11
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`

## 背景

表情のクリップを読むとき（`BuildClipPoseUnchecked`）と、標準の表情の枠が外す曲線を名指すとき（`PresetLeftOut`）は、どちらも「BlendShape でなく、手の形で入る顔の横で無視する曲線でもない」を出発点にしている。この条件を 2 か所で別々に書いていて、片方だけ直すと、読むときは通るのに枠の注意には出る（またはその逆）ずれが起きうる（#751 のレビューの P3）。

標準の表情の顔のキャッシュ（`presetPoses`）は、ボードの編集の数・BlendShape の上限の設定・手の形のクリップの数が変わったら捨てる。ボーンと表示切替を書き出すかどうか（`ExpressionTransformCurves.Enabled`）は入っておらず、同じセッションのまま切り替えると古い結果を返す。切り替えるのはテストだけだが、読む結果はこの値で変わる。

## 決定

- 共通の条件を `BesideFace(binding, gesture)` にまとめ、2 か所ともこれから始める。それぞれの残りの条件（読むときに断るもの・枠が外すもの）は今のまま。
- `presetPoses` を捨てる条件に `ExpressionTransformCurves.Enabled` を足す。

書き出しと表情ボードの動きは変えない。キャッシュの変更で動きが変わるのは、同じセッションのまま `ExpressionTransformCurves.Enabled` を切り替えたとき（今はテストだけ）で、古い結果を返さなくなる。

## 検討した代替案と不採用理由

- 読むときと枠の判定を 1 つの分類（断る・外す・書く）にまとめる: 読む側は「標準の表情に入れた顔か」、枠の側は「その枠がボーンを書くか」で分かれ、残りの条件は別のもの。分類の型を足すと今より複雑になる。

## 確かめたこと

- 表情と標準の表情のテスト（`ExpressionPresetTests`・`ExperimentalExpressionCaptureTests`・`ExpressionBoneAnimationExportTests`・`QuickTransferMatchesDefaultNormalTests`・`AppTransferQuickExportTests`）: 312 件のうち 308 件が通り、4 件は Ignore（手持ちの検証用のプロジェクトに無い依存を使うケース）。
- `Tools/test-compatibility-tooling.py`・`Tools/validate.py` が通る。
