# 使えない表情が入った標準表情の枠は、空のまま書き出す

- 日付: 2026-11-10
- 対象: `Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Documentation~/ExperimentalExpressionCapture.md`・`Documentation~/CloudTransfer.md`・`CHANGELOG.md`

## 背景

[2026-11-09の決定記録](2026-11-09-preset-slot-drops-visibility.md)で、自分で標準表情の枠に入れた表示切替つきのクリップは、顔の変形だけで書き出すようにした。ほかの理由で枠がエラーになるとき（基準の顔と形状が同じ・メッシュが見つからない・追加したファイルが消えた・左右の目で同じ閉眼の変形を使う など）は、表情ボードの保存もアプリに転送（かんたん）も、ボードのエラーとして止まったままだった（[2026-10-20の決定記録](2026-10-20-app-transfer-modes.md)）。

服の表示切替だけのクリップを視線などの枠に入れると、顔の変形を外したあとに何も残らず「基準の顔と形状が同じです」になる。かんたんな転送は表情ボードを開かないので、利用者は理由が分からないまま書き出せない。オーナーの判断は「不完全でも必ず書き出す」（[2026-10-18の決定記録](2026-10-18-stage-guard.md)）に沿って、エラーの枠を空にして書き出す。

## 決定

- 書き出し（`ExperimentalExpressionCaptureSession.Export`）は、標準表情の枠ごとに表情ボードと同じ検査（`SlotError`）をかけ、エラーの枠を利用者が選んだ「なし」に置き換えて続ける。視線・ニュートラルは名前から埋めず、口・まばたきは空の表情になる（元の VRM の設定がある枠はそれを残す）。
- 空にした枠は、書き出し結果の一覧に「<枠の名前>: <理由>（この枠は空のまま書き出しました）」と 1 枠 1 行で出す。かんたんな転送では、ほかの結果と同じく Console の書き出しの詳細に出る。
- 全部の枠を調べてから空にするので、左右の目が同じ閉眼の変形を使うときは両方とも空になる。表情ボードも両方を赤くしている。
- 表情ボードは枠のエラーで保存・転送・**次へ** を止めない（`BoardBlockReason` から外した）。枠には今までどおり理由を赤字で出し、下の行に「<理由>（この枠は空のまま書き出します）」を出す。
- 表情ボードの通常の書き出しとかんたんな転送は同じ `Save` → `Export` を通るので、かんたん＝通常の決まりは保たれる。決める場所は `Export` の 1 か所だけで、かんたん側に枠ごとの処理は無い。
- その他の表情に置いたファイルが書き出せないとき（`board.Unexported`）、反映していない書き出さないオブジェクト、ギミックの未反映は今までどおり止める。枠を空にするのとちがい、どのファイル・オブジェクトを抜くかを利用者が決めるため。

## 選ばなかった案

- 今のまま止める: かんたんな転送の利用者は表情ボードを開くまで書き出せない。理由の文は出るが、どこで直すかが分かりにくい。
- かんたんな転送だけ空にする: 通常とかんたんで書き出す中身が変わり、`QuickTransferMatchesDefaultNormalTests` の決まりに反する。
- エラーの枠を名前から埋め直す: 利用者が自分で入れた枠を別の表情で埋めると、何が入ったか分からなくなる。空にして理由を出すほうが直しやすい。

## 確かめたこと

- `ExpressionPresetTests.AChosenSlotThatCannotBeWrittenIsWrittenEmpty`: 基準と同じ形のクリップを自分で lookUp に入れても書き出せ、lookUp は書かれず、ほかの枠はそのまま書かれ、結果に 1 行出る。
- `ExpressionPresetTests.BlinkClipsSharingAShapeAreAnEyeSlotError`: 左右の目が同じ変形を使うと両方の枠が空になり、それぞれの理由が結果に出る。
- `ExperimentalExpressionCaptureTests.BottomBarNamesWhatBlocksTheSave` ほか: 枠のエラーで `BlocksExport` が立たず、`SkippedSlotNote` がボードの順で最初の枠の理由を返す。
