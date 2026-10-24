# FXの通常時の顔・体形を基準形へ戻す処理を復活させる

- 日付: 2026-10-25
- 対象: `Editor/UniVrmOneClickExporter.cs`、`Editor/ExportStageGuard.cs`、`Editor/Compatibility/ExportNoteText.cs`、`Editor/LanTransfer/TransferNotes.cs`、`Editor/LanTransfer/AppTransferPanel.Conversion.cs`、`Editor/BlinkPreviewWindow.cs`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/*`

## 背景

PR #128（0.11.15-beta.1）で、書き出し時に`NeutralShapeSampler.Sample`でFXの通常状態を評価し、`NeutralShapeSnapshot.Apply`で書き出し用コピーへ当てる処理を外した。以後1.2.0まで、基準の顔・体形はMA/NDMFの前処理後コピーに保存されている値だけで決まっていた。

普段の顔をFXの待機状態で作っているアバター（待機中のFXレイヤーが口のシェイプキーを100に保つものなど）は、保存されている値が0なので、書き出したVRMでその顔が失われる。

## 原因

#128で外した処理は、FXの通常状態で一定に保たれるシェイプキーを基準形に焼き込む唯一の経路だった。外したことの影響を補うため、書き出し結果に「FXの初期状態の復元を省略した」という注記を出し、瞬きの確認画面に「Unity側で目の状態を調整してください」という案内を足していたが、どちらも顔が失われること自体は直さない。

## 決定

- `ExportStage.FxRest`（表示名「FXの通常時の顔・体形」、Phaseは`Expressions`）を足し、`NeutralShapeSampler.Sample`と`NeutralShapeSnapshot.Apply`をその`guard.Step`の中で呼ぶ。置き場所は表情・ジェスチャーの取り込みの後、`NormalizeSourceWeights`・`blink.ResolvePreparedNeutral`・`Capture`の前にする。瞬きの準備済み基準形は、FXで開いた目を見てから決まる。
- 時間で変わるBlendShape曲線は焼き込まず、書き出し用コピーの値を保持する（サンプラーの既存の扱いのまま）。同じクリップの一定の曲線は焼き込む。
- `FxRest`の`Outcome`は`Approximated`にする。この段階が失敗したときは、`ContinueOnStageFailure`で段階だけを省いて書き出しを続け、顔・体形は書き出し用コピーの値になる。1.2.0までと同じ見た目なので、書き出し自体は止めない。
- `preservePreparedNeutral: true`は残す。`FxRest`を省いたときにAAOが省いたFXの定数曲線を基準形へ焼き込み直さないようにするためと、サンプラーが保持した時間で変わる変形を守るため。代わりに、値が0の変形をAAOが固定する最適化は効かないままになる。
- #128で足した回避策を消す。`ExportNoteText.FxRestSkipped`、`TransferNotes`の読み取り、`AppTransferPanel`の表示、瞬きの確認画面の案内、それぞれの翻訳を削除する。`2026-10-23-export-notes-graphical.md`のうちFXの省略を表示する行は、この決定で不要になった。
- #128で「省略する」挙動に合わせたテストを、FXの通常状態を復元する期待値へ戻す。#128の後に入った変更（`BlinkExportSession.Resolve`が未解決の閉眼で例外を出さず`UnresolvedAutomatic`を返すようになったことなど）は、そのまま前提にする。
- テストを2本足す。`NeutralShapeExportTests.FxDefaultConstantShapeBesideATimeVaryingCurveIsBakedIntoTheBase`は、時間で変わる曲線と同じクリップにある一定の口のシェイプキーが基準形に入ることを確かめる。`NeutralShapeExportTests.FailedFxRestStillWritesTheFileWithTheCopysValues`は、`ExportStageGuard.FaultInjection`で`FxRest`を失敗させても書き出しが終わり、省いた段階として表示され、書き出し用コピーの値が基準形になることを確かめる。

アバター名や特定の構成での分岐は足さない。

## 欠点

- `preservePreparedNeutral: true`を残すので、値が0の変形をAAOで固定する最適化は効かない。
- 時間で変わる変形や固定できないFXの状態は、#128より前と同じく書き出し用コピーの値のままなので、VRChatでの見た目と異なる場合がある。その部分は書き出し結果に対象と理由を表示する。
