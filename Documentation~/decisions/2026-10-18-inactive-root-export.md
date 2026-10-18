# 非表示のアバターも、書き出し用のコピーだけ表示にして書き出す

- 日付: 2026-10-18
- 対象: `Editor/ExportRendererSelection.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/ExportRecovery.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExportGimmickDetection.cs`・`Editor/BlinkExportSession.cs`・`Editor/ExportObjectExclusions.cs`・`Editor/PoseExportSession.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/Locales/*.json`・`Tools/validate.py`・`Tests/Editor/InactiveRootExportTests.cs`・`Tests/Editor/RendererSelectionTests.cs`

## 背景

アバター本体またはその親が非表示（inactive）だと、`ExportRendererSelection.RequireActiveRoot` が例外を投げ、書き出しが始まる前に止まっていた。非表示のまま書き出すと `activeInHierarchy` がすべて false になり、メッシュも表情も見つからない空のファイルになるのを防ぐためだった。衣装違いのアバターを並べて片方を非表示にしておく使い方は普通にあり、「不完全でも必ず書き出す」方針（`2026-10-18-stage-guard.md`）にも合わない。

## 決定

- 元のアバターは変更しない。`SetActive`・Undo・dirty のいずれも行わない。
- 表示かどうかは、アバター本体から下だけで判定する（`ExportRendererSelection.ActiveUnder`）。本体と親の非表示は無視し、本体より下で非表示のオブジェクトは従来どおり書き出さない。
- 書き出し用のコピー（`NdmfExportPreparation.InstantiateOwnedCopy`）と復旧用の比較コピー（`ExportRecoveryPreview.Create`）は、作った直後、非表示の置き場の下にあるうちにルートだけを表示にする。コピーの中では `ActiveUnder` と `activeInHierarchy` が一致するので、コピー側の判定はそのまま使う。
- 本体または親が非表示だったときは、警告「アバター本体または親が非表示だったため、書き出し用のコピーだけ表示にして書き出しました。シーンのアバターは変更していません。」を出し、`ExportFallbackReport` に「保持」として 1 行記録する。4 言語の訳を足した。

## 元のアバターを読む処理で直したもの

`activeInHierarchy`・`isActiveAndEnabled`・`GetComponentsInChildren` の既定（非表示を除く）で元のアバターを読んでいた処理を、`ActiveUnder` に揃えた。

- `ExportRendererSelection.Enumerate`（書き出すメッシュの一覧。表情の材料の判定・書き出し画面の瞬き設定も経由する）
- `ExportGimmickDetection.Analyze`・`Inspect`（エフェクト類の検出）と、復旧の検査での呼び出し
- `BlinkExportSession` の Avatar Descriptor からの瞬きの検出と `Validate`
- `ExportObjectExclusions` の、書き出すメッシュの骨を除外していないかの検査
- `PoseExportSession` の同梱ポーズのコンポーネントの収集
- `FaceEmoExpressions.ReadRepositories` のアバター内の表情設定の収集
- `ExportRecoveryPreview.Create`・`ExperimentalExpressionCaptureSession` の非表示の拒否を削除

## 変えなかったもの

- コピー側の判定（最適化・表情の照合・揺れ物・視線など）: コピーのルートは表示にしてあるので結果が変わらない。`RequireActiveRoot` はコピーにだけ使う（`ExportOptimizationBindings`）。
- `ExportSourceFingerprint`: 本体の表示状態を指紋に含めている。表示を切り替えれば再検査が要るのは正しい。
- シーン全体から表情設定ツールの起動コンポーネントを探す処理（`FindObjectsOfType`）: 非表示の復元用のチェックポイントを拾わないために非表示を除いている。アバターの外の非表示の親の下に置いた設定は見つからない。

## 検討した代替案と不採用理由

- 書き出しのあいだだけ元のアバターを表示にして戻す: シーンを書き換え、Undo と dirty の扱いが要る。途中で例外や取り消しが起きると戻し損ねる。表示にした瞬間に他のツールのコールバックが走る。
- 非表示のアバターを書き出す前に止める（従来どおり）: 方針に合わない。止める理由は、コピーを表示にすれば解消する。
- 非表示の子もすべて書き出す: シーンで見えていない衣装が混ざる。本体から下の表示状態は、作者の意図として残す。

## 残した課題

- `Animator.GetBoneTransform` などの Animator の問い合わせを、非表示の元のアバターに対して行う処理がある（除外の検査・視線の測定など）。EditMode テストで通ることは確かめたが、すべての経路は確かめていない。
- アバターの外の非表示の親の下に置いた表情設定は読まない（上記）。
