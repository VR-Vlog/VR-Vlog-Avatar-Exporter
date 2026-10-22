# 書き出しから呼ばれず、テストとツールだけが使っていたコードを消す

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/VrChatFixedExpressionCurve.cs`・`Editor/UnifiedExpressionRegistry.cs`・`Editor/VrChatExpressionMenu.cs`・`Editor/ExportAuthor.cs`・`Editor/ExportGimmickDetection.cs`・`Editor/ExportRecoverySession.cs`・`Editor/MaterialBakeIssue.cs`・`Editor/LilToonExporterWindow.cs`・`Tests/Editor/`・`Tests/LanTransfer/CloudTransferWindowTests.cs`・`Tools/`・`CHANGELOG.md`

## 背景

`Editor/` に、書き出し・画面のどこからも呼ばれず、テストと `Tools/` のハーネスだけが呼んでいるメンバーが残っていた。テストはそのメンバーが正しく動くことを確かめているが、書き出しの経路はそのメンバーを通らないので、書き出しの不具合を防いでいない。読む人には「書き出しでこの処理が動く」と誤解させる。

消す前に、メンバーごとに `Editor/`・`Runtime/`・`Tests/`・`Tools/` を名前で検索し、`nameof`・文字列・リフレクション（`Tools/ExportRegressionRunner.cs` など）での参照と、Unity 無しで回すハーネス（`Tools/run-behavior-tests.ps1`・`Tools/run-bake-tests.ps1`）がコンパイルするファイルの一覧も確かめた。

## 決定

次のメンバーを消す。どれも書き出しの経路から呼ばれていないので、書き出す内容は変わらない。

- `ExperimentalExpressionCaptureSession.ImportClips`: 表情ボードは `ReplaceClipRecords` で手動のクリップを入れ替えている。`ImportResult` は候補の取り込み（`ImportCandidates`）が使うので残す。
- `VrChatExpressionSampler.ApplyMergedDefaults`・`IsExcludedPreparedPath`: 書き出しはFXの既定値を書き出し用の複製へ適用していない（`Tools/validate.py` に、書き出しから呼ばないことを確かめる行がある）。
- `VrChatFixedExpressionCurve`（ファイルごと）と、そのテスト用の `Tests/Editor/Fixtures/FixedExpressionCurveFixture.cs`。
- `UnifiedExpressionRegistry.IsBlink`・`IsEyeLook`。
- `VrChatExpressionMenu` の製品コードにあった `#if EXPORTER_BEHAVIOR_TESTS` の分岐。ハーネスが `VrChatExpressionMenu.cs` をコンパイルしなくなったので要らない。
- `ExportAuthor.Forget`: テストが保存した作者名を消すためだけに使っていた。テスト側に同じ処理（`ExportAuthorTests.Forget`）を置き、そのために `ExportAuthor.PreferencePrefix` を `private` から `internal` にした。
- `ExportGimmickDetection.KnownNadeRoot`: テストは `ExportGimmickDetection.Matches` と規則の一覧（`ExportGimmickRules`）で同じことを確かめる。
- `ExportRecoverySession.CreatePreview`。
- `MaterialBakeIssue.WithOmissions`。
- `LilToonExporterWindow.ShouldShowFailureAfterFailedAttempt`（`Tools/validate.py` の切り出し位置も直した）と `DetailedTransferMethod`。

## 消したテストと、書き出しの経路で残っている確認

- `MergedFxDefaultsTests.ApplyingDefaultsRequiresAnIndependentExportCopy`・`ApplyingCommittedFxDefaultsChangesOnlyTheCopy`: 消した `ApplyMergedDefaults` だけを確かめていた。既定値を読み取る `SampleDefaults` は、同じクラスのほかのテストが確かめている。
- `ExportFidelityTests.ApprovedLayerOmissionChangesOnlyExportCopy`: 書き出しは省略の指定（`MaterialBakeOptions` の中身）を作らない。焼き込み前の検査は `PreflightExclusionsUseMaterialIdentity`（名前を変えて省略の行を消した）と `Tools/BakeBehaviorTests.cs` が確かめている。
- `Tools/BakeBehaviorTests.cs` の `ConsentUsesMaterialIdentityAndDoesNotMutatePriorOptions`・`ApprovedOmissionOnlyChangesExportCopies` と、アルファの検査の中の省略の3行: 上と同じ理由。
- `ExportRecoveryFailureSeedTests.SeedingARealFailureNeverWeakensLaterSourceChangeGuards` の `preview` のケース: 元のアバターが変わったことの検出（`CheckForChanges`）は、残した `retry`・`save` のケースが確かめている。
- `FixedExpressionCurveFixture`: 製品コードに使う箇所が無い。
- `MenuTraversalFixture` は `Tools/BehaviorTests.cs`（Unity 無しの実行）から外しただけで、Unity の EditMode では `VrChatMenuExpressionTests` から引き続き回る。

テストメソッドは消さずに、確認の行だけを消したもの:

- `ExportRecoverySessionTests` の `ShouldShowFailureAfterFailedAttempt` の2か所。この判定は書き出しから呼ばれていなかった。判定の元になるセッションの状態（取り消し後の `WasCanceled` と、次の失敗での `Failure`）の確認は同じテストに残っている。
- `CloudTransferWindowTests` の `DetailedTransferMethod` の確認。表情ボードから転送の画面へ進む入口は、同じテストの `EmbeddedTransferringMethod` の確認が見ている。
- `ExperimentalExpressionCaptureTests` は `ImportClips` を `ReplaceClipRecords` に置き換えた。`ImportClips` にだけあった、同じクリップを2回取り込んだときに2回目を飛ばす確認と、`Skipped` の確認を消した。
- `MergedFxDefaultsTests` の3メソッドは、`ApplyMergedDefaults` で複製へ適用したあとの確認だけを消し、元のアバターとFXについての確認は残した。`IsExcludedPreparedPath` を呼んでいた1メソッドは、同じ判定をテストの中に書いた。

## そのままにしたこと

- `NeutralShapeSampler.Sample`・`WithAffected`・`Describe` と `NeutralAdditionalDataPreflight`: 書き出しからは呼ばれていないが、`Sample` を入口にしたテスト（`NeutralShapeSamplerTests` など約115メソッド・600ケース以上）が、書き出しでも使う部品（`VrChatExpressionSampler.SampleNeutral`・`AnalyzeNeutral`・`NeutralShapePlan`・`NeutralRandomRest`・`NeutralInputProof`）を確かめている。Unity を起動せずにこれらを書き出しの入口へ書き換えて同じ確認になるか確かめられないので残した。`NeutralAdditionalDataPreflight` は `Sample` だけが呼んでいる。
- `VrChatExpressionMenu.ReadMenu`: `Read` の中から呼ばれているので消さない。
- `ExportGimmickDetection.UnsafeRootReason`: 書き出しが使う判定（`PettingSystemUnsafeReason` と非公開の `SharedUnsafeReason`）を組み合わせて確かめる入口で、これに代わる書き出し側の入口が無い。
- `ExpressionShapeNames.cs`・`LilToonEmissionPolicy.cs` の `#if EXPORTER_BEHAVIOR_TESTS`: Unity 無しのハーネスが今もこのファイルをコンパイルしているので残した。
- `Editor/ExportSourceFingerprint.cs` は触っていない。

## 次に消せる候補

今回消したメンバーだけが使っていたため、書き出しから呼ばれなくなったもの。このPRの範囲外として残した。

- `VrChatExpressionSampler.SampleDefaults`（`ApplyMergedDefaults` だけが呼んでいた。テストは多い）
- `ExportRecoveryPreview` と `ExportRecoverySession` の `previewExcludedObjects`（`CreatePreview` だけが読んでいた）
- `MaterialBakeOptions` と `Omits`（中身を入れる `WithOmissions` が無くなったので、常に空）
- `FaceEmoExpressions` の `preparedExcludedPath` 引数

## 確認したこと

- Unity 無しのハーネス: `Tools/run-bake-tests.ps1`（355件）・`Tools/run-behavior-tests.ps1`（396件ほか）がすべて通った。件数の減りは消した確認とハーネスから外したフィクスチャーの分。
- `Tools/test-compatibility-tooling.py` が通った（`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` から消した2メソッドを外した）。
- Unity の EditMode でのコンパイルとテストは CI で確かめる。
