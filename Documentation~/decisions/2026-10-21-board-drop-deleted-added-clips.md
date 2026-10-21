# 追加したあとで削除された表情ファイルも追加した一覧から外す

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`
- [追加したあとで書き出せなくなった表情ファイルを追加した一覧から外す](2026-10-21-board-drop-broken-added-clips.md) の「入れなかったもの」の 1 つ目を置き換える。

## 背景

表情ボードに自分で追加した `.anim` をプロジェクトから削除すると、クリップが null になる。`DropBrokenInputs` はクリップのある書き出せないファイルだけを外していたので、削除されたファイルは追加した一覧（`clipInputs`）に残り、次のことが起きていた。

- 見えないファイルが 64 件の上限に数えられる。
- 理由は「AnimationClipが指定されていません」で、利用者が何をしたら起きたのか分からない。

## 決定

- 確かめ直すところ（`RefreshCandidateValidation`）で、クリップが null の追加ファイルに「ファイルが削除されました。」（`DeletedClipReason`）を理由として付け、書き出せないファイルと同じく追加した一覧から外して、`RejectedMessage` の文を状態の行に出す。追加したファイルは追加の時点で null ではないので、null になるのは削除されたときだけになる。
- そのファイルを置いていた枠はそのまま残す。枠は削除されたファイルを「見つからない」と表示しており、保存はその理由で止まる。空にすると、利用者がどの枠を置き直すべきかが分からなくなる。
- 4 言語の訳を足した。

## 確認したこと

- `ExperimentalExpressionCaptureTests.ADeletedAddedFileIsRemovedAndItsSlotStillShowsTheMissingFile`: 追加して枠に置いたファイルを `AssetDatabase.DeleteAsset` で消して確かめ直すと、追加した一覧から外れ、状態の行にファイル名と「ファイルが削除されました。」が出て、枠は残り保存が止まる（`BlocksExport`）こと。
- `ExperimentalExpressionCaptureTests` と `ExpressionBoardTests` の 167 件中 163 件が通った。落ちた 4 件は、この PC のテスト用プロジェクトに無い外部パッケージを使う `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces(…,"APL",…)` で、開発ブランチでも同じく落ちる。
