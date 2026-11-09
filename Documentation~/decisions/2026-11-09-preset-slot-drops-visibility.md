# 自分で標準表情の枠に入れた顔は、顔の変形だけで書き出す

- 日付: 2026-11-09
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Documentation~/ExperimentalExpressionCapture.md`・`CHANGELOG.md`

## 背景

アプリに転送（かんたん）が「書き出せませんでした / 表情の割り当てを直すと作れます / 視線 上: 顔の変形以外の変更を含みます。マテリアル・ボーン・表示切替は追加対応が必要です: <オブジェクトのパス> / m_IsActive」で止まる、という報告があった。

1.9.1 で表情のボーンと表示切替の書き出しを戻してから（[2026-11-07の決定記録](2026-11-07-bone-based-expressions.md)）、表情ボードは表示切替を含むクリップもその他の表情のカードとして並べる。カードはメニューやドラッグで標準表情の枠にも入れられる。一方、標準表情の枠は BlendShape だけを持つので、枠の検査（`BuildClipPoseUnchecked` の `presetFace`）は BlendShape 以外の曲線を見つけるとエラーにしていた。利用者が自分で入れた割り当ては保存され、表情ボードでは赤い枠になり、かんたんな転送はボードのエラーで止まる（[2026-10-20の決定記録](2026-10-20-app-transfer-modes.md)）。

## 決定

- 利用者が自分で標準表情の枠に入れたクリップは、エラーにせず、BlendShape だけを標準表情に入れて書き出す。ボーン・表示切替・指のポーズは標準表情には入れない。
- 枠には「顔の変形だけ使う」と出し、書き出しの結果には外した曲線（`パス / プロパティ`）を表情ごとに 1 つ出す。
- 自動で割り当てる候補はこれまでどおり BlendShape だけのクリップに限る（[2026-10-31](2026-10-31-expression-bone-animation.md)・[2026-11-07](2026-11-07-bone-based-expressions.md)の決定記録）。オンにしたことで自動の割り当てが変わらないようにするため。`PresetAssignment.Automatic` で分ける。
- マテリアルの曲線や参照の曲線など、もともと表情に出来ないクリップは今までどおりカードの段階でエラーにする。

## 選ばなかった案

- 表示切替を含むクリップを標準表情の枠に入れられないようにする: 入れた割り当てが既に保存されている利用者は、表情ボードを開いて外すまで転送できないまま残る。「不完全でも必ず書き出す」（[2026-10-18の決定記録](2026-10-18-stage-guard.md)）に沿わない。
- 標準表情にも `transforms` を書き出す: アプリが標準表情の `transforms` を再生するかを確かめておらず、視線やまばたきの BlendShape の扱いとも絡むので別の作業にする。

## 確かめたこと

- `ExpressionPresetTests.AChosenPresetThatAlsoShowsAnObjectKeepsItsShapesAndSaysWhatItLeftOut`: 表示切替を含むクリップを自分で lookUp に入れるとエラーにならず、BlendShape が 1 つ入り、結果に外した曲線が出る。自動の割り当てなら今までどおりエラーになる。
- `ExperimentalExpressionCaptureTests.GestureFacesIgnoreBoneCurvesBesideTheFace`: 自分で入れた割り当てはエラーにならず、外した曲線を `PresetLeftOut` が返す。自動の候補は変わらない。
