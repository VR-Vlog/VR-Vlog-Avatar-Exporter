# 表情メニュー・Gesture・FX・FaceEmo を再生して表情を作るコードを消す

- 日付: 2026-10-23
- 対象: `Editor/VrChatFxExpressions.cs`・`Editor/VrChatAuthoredFaces.cs`・`Editor/AnimatedGestureTree.cs`・`Editor/NativeTreeTrajectory.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/VrChatExpressionMenu.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/NeutralShapePlan.cs`・`Editor/Locales/ExporterLocale_{en,ko,zh-Hans,zh-Hant}.json`・`Tests/Editor/*`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`

## 背景

前の変更（`2026-10-23-close-menu-playback.md`）で、`VrChatExpressionSampler.Analyze` は常に表情ボードの一覧から表情を読むようになり、表情メニュー・Gesture・FX・作り込みの表情・FaceEmo の表情を再生する入口を閉じた。再生する側のコードはそのとき残したので、製品のコード（`Editor/`）のどこからも呼ばれないまま、テストだけが呼んでいた。

## 決定

- 製品のコードから呼ばれなくなった次のものを消した。
  - ファイルごと: `VrChatFxExpressions`（FX の表情を探して再生する）、`VrChatAuthoredFaces`（作り込みの表情を読む）、`AnimatedGestureTree`（Gesture の BlendTree を再生する）、`NativeTreeTrajectory`（BlendTree の時間変化を追う）
  - `VrChatGestureExpressions.Add`・`SameOutcome`・`SameMorphs`
  - `FaceEmoExpressions.Add`・`ReadRegistered`・`AddClip`
  - `VrChatExpressionMenu.Source.GestureStates`・`Progress`
  - `VrChatExpressionSampler.SampleFixed`
  - `NeutralShapePlan.CreateObservedHistoryProjection`
  - これらだけが出していた文言 5 件（4 言語の訳を含む）
- 中立の顔と一覧の表情の読み取りが使うものは残した。`VrChatGestureExpressions.ReadCurve`・`TargetClips`・`HasChangingMorph`・`ReadClip`・`ReadPose`、`FaceEmoExpressions.ReadClip`、`ExpressionSettlement`、`PoseMenuResolver`、`PoseSampling`、`VrChatExpressionMenu.Read`（`SkipPlayback`）、`ValidateFixedPose`・`SampleNeutral`・`ApplyPermanentOverrides` などで、中身は変えていない。
- `VrChatExpressionSampler.Sample`・`SampleDefaults` と、それが使う内部の評価も残した。これらは前の変更より前から製品のコードには呼ばれず、テストだけが使っている。多くのテストが FX レイヤーの評価をこの入口で確かめていて、消すとそれらを書き直すことになるので、消すかどうかは別に決める。続く `2026-10-23-remove-test-only-sampler.md` で、`Sample`・`SampleDefaults`・`Evaluate` を消した。

表情ボードから書き出す VRM は変わらない。

## テスト

再生の結果だけを確かめていたテストは外した。残すコードの挙動も一緒に確かめていたテストは、再生の確認だけを外すか、残す API で読む形に直した。件数はテストメソッドの数で、括弧の中はケースの数。

| テスト | 扱い | 理由 |
|---|---|---|
| `GestureBlendTreeTests`（ファイルごと・21 件、34） | 外す | Gesture の BlendTree を再生した表情を確かめていた |
| `DirectFxExpressionDiscoveryTests` の 72 件（111） | 外す | FX の表情を探して再生した結果を確かめていた |
| `DirectFxExpressionDiscoveryTests.DescriptorFxIsAuthoritativeWhenRootAnimatorControllerIsNone` | 残す | ルートの Animator が空のとき記述子の FX を Controller にすることの唯一の確認。#419 で一覧を渡す形に直したもので、再生に頼らない |
| `FxDiscoveryPruningTests`（ファイルごと・3 件、11） | 外す | FX の表情の探索の絞り込みを確かめていた |
| `InferredStatePathTests`（ファイルごと・3 件、6） | 外す | FX の表情を探すときのステートの経路を確かめていた |
| `NativeTreeTrajectoryTests`（ファイルごと・9 件、16） | 外す | BlendTree の時間変化の追跡を確かめていた |
| `FacialProjectionTests` の 16 件（33） | 外す | 作り込みの表情・ハードウェアの表情・保持された Gesture の表情を再生した結果を確かめていた |
| `ParameterDriverExpressionTests` の `Fixed…` など 21 件（42） | 外す | `SampleFixed` で選んだ表情を固定の文脈で再生した値を確かめていた |
| `SelectedLayerControlTests` の 10 件（20） | 外す | 選んだ表情での FX レイヤーの重みの再生を確かめていた |
| `VrChatMenuExpressionTests` の `Gesture…` 8 件（13） | 外す | Gesture の表情の登録と再生を確かめていた |
| `NeutralLayerControlTests` の `SelectedExpressions…`・`ExplicitSelectedExpressions…` 3 件（9） | 外す | 選んだ表情の再生を確かめていた |
| `DirectExpressionNativeLayersTests` の `GestureRegistration…`・`SharedGestureClip…` 2 件（3） | 外す | Gesture の登録と再生を確かめていた |
| `NeutralShapeSamplerTests.FixedMenuAcceptsOnlyProvenUnchangedOrUnboundActivation`（2）、`MergedFixedNeutralSamplingTests.FixedExternalMenuKeepsPermanentMorphAndNativeWriteDefaults`（2）、`NeutralParameterCurveTests.SelectedMenuExpressionRetainsItsStrictParameterCurveContract`、`HeldAutomaticExpressionTests.GeneralFixedSamplingStillRejectsTheRelevantAutomaticRandomOperation`、`ExpressionSettlementTests.AGestureAboveALoopingHandIdleLayerIsExported` | 外す | `SampleFixed` か Gesture の再生の結果だけを確かめていた |
| `ExpressionImportRecoveryTests.FaceEmoDefaultFaceIsNamedByItsPatternAndBranchesByTheirClip`、`MenuImportSdkRecoveryTests.WholeMenuOmissionKeepsFaceEmoRegisteredClipParsingIndependent` | 外す | 消した `FaceEmoExpressions.ReadRegistered` が付ける表情の名前と、その読み取りを確かめていた |
| `AdditionalParameterConstantTests` の 15 件 | 直す | 選んだ表情（`SampleFixed`）の確認を外し、中立の顔と依存の確認を残した。`OnlyAnIndependentUnchangedInputCanPruneAConflictingProducer` と `SoleDormantResetPreservesTheNativeNeutralAndSelectedExpression` は選んだ表情の有無で分けていた 2 件を 1 件にした |
| `AdditionalPlayableCallbackTests.DormantSdkFxWeightCommandsPermitNeutralAndFixedSelectionWithoutSourceMutation`、`AdditionalPlayableMorphTests.AdditionalMorphCannotBeSilentlyDroppedFromFixedExpressionEndpoints`、`VrChatMenuExpressionTests.RepeatedFxParameterDeclarationsKeepMenuExpressions` | 直す | `SampleFixed` の確認を外し、中立の顔・`ValidateAdditionalProbeBehaviours`・`FixedNeutralValues` の確認を残した |
| `FaceEmoPreparedFxIntegrationTests`・`MergedFxDefaultsTests`・`FaceEmoMergedUnderlayTests`・`ExpressionImportRecoveryTests` の FaceEmo のテスト | 直す | FaceEmo の登録を `ReadRegistered` ではなく、残す `CaptureRegistered` で集めたクリップを `ReadClip` で読む形にした |
| `ExportFidelityTests.FaceEmoCaptureReadsRegisteredNestedPatternsAndRejectsBrokenOrder` | 直す | 入れ子の登録と並び順の検査を `CaptureRegistered` で確かめる形にした。表情の名前は `ReadRegistered` が付けていたので確認を外し、名前を変えた |

前の記録で「固定の文脈での評価は同じクラスの `SampleFixed` のテストが確かめている」とした `ParameterDriverExpressionTests` のテストも、この変更で外した。固定の文脈で中立の顔を求める処理は、`NeutralShapeSampler` と `ExpressionDependencies` に `fixedContext` を渡すテストが引き続き確かめる。

選んだ表情の確認を外したテストのうち、名前に `Selected` や `Fixed` が残るもの（`SoleDormantResetPreservesTheNativeNeutralAndSelectedExpression` など）は、名前を変えずに残した。
