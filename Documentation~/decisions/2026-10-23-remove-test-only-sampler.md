# テストだけが呼んでいた `VrChatExpressionSampler.Sample`・`SampleDefaults` を消す

- 日付: 2026-10-23
- 対象: `Editor/VrChatExpressionSampler.cs`・`Editor/ExpressionSettlement.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/Locales/ExporterLocale_{en,ko,zh-Hans,zh-Hant}.json`・`Tests/Editor/ExpressionSettlementTests.cs`・`Tests/Editor/ParameterDriverExpressionTests.cs`・`Tests/Editor/VrChatMenuExpressionTests.cs`・`Tests/Editor/MergedFxDefaultsTests.cs`・`Tests/Editor/ExcludedLayerTests.cs`・`Tests/Editor/ExportGimmickTests.cs`・`Tests/Compatibility/OptimizationLocalizationTests.cs`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`

## 背景

`2026-10-23-remove-menu-playback.md` で表情メニュー・Gesture・FX・FaceEmo を再生するコードを消したとき、`VrChatExpressionSampler.Sample`・`SampleDefaults` と、その中で表情を評価する `Evaluate` は残した。製品のコード（`Editor/`）からは呼ばれていなかったが、多くのテストがこの入口で FX レイヤーの評価を確かめていたので、消すかどうかを別に決めることにしていた。

書き出し（`UniVrmOneClickExporter.Export`）は表情を `VrChatExpressionSampler.Analyze` で表情ボードの一覧から読み、表情ボードは表情を `ExperimentalInstalledExpressionPlayer.Sample` で再生する。どちらも `Sample`・`SampleDefaults` の評価を通らないので、テストがこの入口で確かめていた値は書き出す VRM に出ない。

## 決定

- 製品のコードから呼ばれていない次のものを消した（`Editor/` で 569 行）。
  - `VrChatExpressionSampler.Sample`・`SampleDefaults`・`Evaluate`
  - それだけが使っていた `StationaryBindings`・`SelectedRandomRestProbe`・`CurrentEffectiveMotion`・`MovingTree`・`MatchesExpectedStatePath`
  - `ExpressionSettlement.StateResolver`（`Evaluate` だけが使っていた）
  - これらだけが出していた文言 3 件（4 言語の訳を含む）
- 書き出しと表情ボードが使うものは残した。`ExpressionInspectCache.Begin`・`NeutralInputProof.Read`・`FacialProjectionScope.Session`・`SelectedExpressionAppearance`・`NeutralShapePlan.CreateProjection`、`VrChatGestureExpressions.ReadCurve`・`TargetClips`・`HasChangingMorph`・`ReadClip`・`ReadPose`、`FaceEmoExpressions.ReadClip`、`ExpressionSettlement`、`PoseMenuResolver`、`PoseSampling`、`ValidateFixedPose`・`FixedMotionTimeInputs`・`PreserveTemporalRest`・`SampleNeutral`・`ApplyPermanentOverrides` などで、中身は変えていない。
- `VrChatGestureExpressions.TargetClips` のコメントが、前の変更で消した `Add` を指したままだったので、表情ボードが使う形に書き直した。

書き出す VRM と表情ボードの動きは変わらない。

## テスト

`Sample`・`SampleDefaults` が求めた値だけを確かめていたテストは外した。製品のコードの処理を確かめるテストがほかに無いもの（唯一の守り）は、製品の入口から呼ぶ形に直すか、`Sample` の確認だけを外して残した。テストの名前は、表に書いた 1 件のほかは変えていない。件数はテストメソッドの数で、括弧の中はケースの数。

| テスト | 扱い | 理由 |
|---|---|---|
| `ExpressionSettlementTests.AHeldExitTimeIsReportedSeparatelyAndStillChecked` | 直す | 表情ボードの再生は今のパラメーターで条件が偽になる exit time の遷移を保持とみなさないので、元の Int の条件では警告が出ない。最後の静止の確認は Trigger を読まないので、セットされていない Trigger を待つ遷移は保持として報告する。`ExpressionSettlement.Held` を確かめるテストはほかに無いので、条件をその Trigger に、表情を最後のキーまで動くクリップに変えて、表情ボードの再生で値と警告（`FinalKeyOfClip` と `HeldTransitions`）を確かめる |
| `ExpressionSettlementTests.AnAnyStateEdgeThatCannotReachItsOwnStateIsNotFollowed` | 外す | 表情ボードの再生は exit time の Any State の遷移を静止の確認で断るので、`Settle` の追跡の確認にならない |
| `ExpressionSettlementTests` の `AStateRestartedEveryFrameByAnAnyStateEdgeHoldsTheFace`（2）、`AStateRestartedEveryFrameWithAMovingClipIsNotSaved`（2）、`AStateRestartedEveryFrameWithADriverIsNotSaved`（3）、`AStateRestartedEveryFrameWithCallbacksThatChangeNothingHoldsTheFace` | 外す | 毎フレーム入り直すステートは遷移中のままになり、表情ボードの再生は遷移中の状態を断る。断る理由が `Settle` ではなくなり、`Settle` の確認にならない |
| `ExpressionSettlementTests.AMotionTimeStateIsNotStepped` | 外す | `Sample` に渡す時計を差し替えて確かめていた。表情ボードの再生は時計を受け取らず、動く曲線を断る |
| `ExpressionSettlementTests.ALoopingHandMuscleCurveBesideTheFaceIsSettled`（2） | 外す | 表情ボードの再生は Animator の曲線を判定から外すので、手の曲線のループは起きない |
| `ExpressionSettlementTests.AFadeInBesideARestartingLayerReachesItsFinalKey` の `.25` のケース | 外す | 常に遷移中になり、表情ボードの再生は遷移中の状態を断る。`0` のケースは残した |
| `ExpressionSettlementTests` の残り 19 件（`ANonLoopingClipLongerThanTheWarmUpIsSavedAtItsLastKey`・`AStateReachedByALateExitTimeIsFollowed`・`ASelfTransitionIsFollowedOnceAndReported`・`TheHopLimitIsCountedPerLayer` など） | 直す | 最後のキー・遷移の追跡・自己遷移・遷移の回数の上限は `ExpressionSettlement.Settle` の処理で、ほかに確かめるテストが無い。`Sample` ではなく、表情ボードが呼ぶ `ExperimentalInstalledExpressionPlayer.Sample` で再生して同じ値と警告を確かめる |
| `ParameterDriverExpressionTests` の 35 件（42）（`UnwrittenFalseAndGuard…`・`MenuDriverChain…`・`AddRunsOncePerEntry…` など） | 外す | `Sample` で選んだ表情の値と、FX の重みのコマンドを評価した結果を確かめていた。Parameter Driver の挙動はほかのテストファイルが製品の経路で確かめ、`SupportedNumbersClampOnlyExpressionParametersAndFloorCopiedIntegers` が演算を直接確かめる |
| `ParameterDriverExpressionTests` の `FxOffSelectionIsRejectedAndLaterSamplesStartFromFreshDefaults`・`NonUnitFxCommandCannotBeCancelledByAnotherCommandOnTheSameState` | 直す | FX の重みを 1 以外にする状態を断る `ExpressionEvaluationSession` の処理は、表情ボードの再生と中立の顔の評価が通るのに、ほかに確かめるテストが無い。`Sample` ではなく `ExperimentalInstalledExpressionPlayer.Sample` で再生し、同じ文言で断ることと、断ったあとの再生が既定値から始まることを確かめる |
| `ParameterDriverExpressionTests` の `BodyPlayableControlAndTrackedHandDoNotBlockFixedFacialSampler`・`LocomotionControlDoesNotBlockFixedFacialSampling`・`TemporaryPoseSpaceDoesNotBlockFixedFacialSampling`・`MalformedTemporaryPoseSpaceIsNotIgnored`・`MalformedBodyPlayableControlIsNotIgnored`・`UnknownPlayableTargetIsNotIgnored`・`BodyControlDoesNotMakeAnExcludedOnlyLayerLookLikeAFace` | 直す | 体の Playable の制御と Pose Space を顔の判定で見逃さない・誤って断らない判定は、ほかに確かめるテストが無い。`Sample` の値と元の素材が変わらない確認を外し、判定と `FindExcludedLayers` の確認を残した |
| `VrChatMenuExpressionTests` の `MenuSelectionCapturesACombinationAndLeavesSourceUntouched`・`WriteDefaultsOffRetainsTheWholeEvaluatedFace`・`BlendTreeUsesTheMenuValueInsteadOfPublishingItsIndividualShapes`・`UnsupportedVisibilityIsReportedInsteadOfDroppingHalfAnExpression`・`DuplicateHierarchyPathsCannotCaptureTheWrongFace` | 外す | 表情メニューの値を `Sample` で評価した結果か、そこで断る条件を確かめていた |
| `VrChatMenuExpressionTests` の `DelayedExitIsFollowedAndReported`・`DelayedNonLoopingStepIsSavedAtItsLastKeyAndReported` | 外す | `Sample` で遷移の追跡と最後のキーを確かめていた。同じことを `ExpressionSettlementTests` が表情ボードの再生で確かめる |
| `VrChatMenuExpressionTests` の `ReachedConstantMorphTailsKeepTheNativePoseBeforeTheClipEnd`（2）・`ReachedNonLoopingMorphTerminalsKeepTheOriginalNativeHeldPose`（2）・`AReachedMorphTailCannotHideAFutureCurveOrCallback`（6）・`TerminalMorphProofCannotReplaceAnUnprovenNativeClockOrFutureState`（7）・`SelectedNativeUpperWritersDominateOnlyTheirExactLowerMorphStream`（5） | 外す | Unity の再生と `Sample` の値が一致することを確かめていた |
| `MergedFxDefaultsTests` の `ConstantIndependentEffectAppliesToNeutralAndSelectedExpression`（2）・`PermanentEffectUsesAuthoredLayerPriority`（4）・`NeutralWeightedUpperLayerKeepsMovingBaseWithoutBakingUnrelatedMorphs`（4）・`NeutralFractionalOverrideDoesNotBakeAnUnrelatedConstantFromAMovingBase`（2） | 外す | `Sample`・`SampleDefaults` が求めた常時適用の FX の値を確かめていた |
| `MergedFxDefaultsTests` の `NativeSupportBaseRejectsUnsafeZeroDurationInitialOrIntermediateMotion`（4）・`NativeSupportBaseValidatesNestedAndBlendTreeMotionsConservatively` の BlendTree のケース | 直す | 下のレイヤーのクリップを評価の前に断る `ValidateNativeSupportMotions` は `SampleNeutral` が呼ぶのに、ほかに確かめるテストが無い。`SampleDefaults` ではなく `ExpressionDependencies.AnalyzeNeutral` と `SampleNeutral` で、同じ文言で断ることを確かめる。入れ子のステートマシンのケースは、`SampleNeutral` が入らないステートマシンのクリップを確かめないので外し、名前から `Nested` を外して `NativeSupportBaseValidatesBlendTreeMotionsConservatively` にした |
| `MergedFxDefaultsTests` の `NativeSupportBaseRejectsUnsafePlayedClipAfterItsTransitionCompletes`（4）・`NeutralFractionalOverrideStillValidatesMovingRequiredBaseMorph`（2）・`NeutralFractionalOverrideRejectsDelayedBaseActivityChanges`（2）・`NeutralFractionalBaseActivityRetainsExternalAndWriterDependencies`（2）・`NeutralFractionalBaseCannotAssumeBuiltinGestureRemainsAtDefault` | 外す | `SampleDefaults` が断る条件だけを確かめていた |
| `MergedFxDefaultsTests` の `EquivalentConstantRelayPreservesNativeNeutralAndMenuEvenDuringTransitions`・`ConstantRelayProofRejectsOutputOrCallbackDifferences`・`ConstantRelayProofDoesNotAcceptAnSdkBehaviourEvenWhenClipsMatch`・`InstalledMaConstantParameterRelayKeepsMergedNeutralAndMenuPupilRemoval`・`FractionalStationaryFxUsesPreparedBaseOnceForMenuGestureAndFaceEmo`・`UnrelatedGestureLayerDoesNotBecomeANeutralSamplingRoot`・`RegisteredFaceEmoRetainedTargetCanMoveIntoAnOriginallyManuallyExcludedPath` | 直す | `Sample`・`SampleDefaults` の確認だけを外した。残りの確認（中立の顔・常時適用の判定・FaceEmo の対象の扱い）はほかに確かめるテストが無い |
| `MergedFxDefaultsTests` の `TimeVaryingDefaultLayerIsNotDeclaredPermanent`・`OrdinaryMenuDoesNotNeedAStationaryLayer`・`ExcludedPermanentTargetDoesNotBecomeANeutralSamplingRoot` | 直す | 常時適用のレイヤーを見つけない判定を `SampleDefaults` の結果で確かめていた。同じ判定をする `ExpressionDependencies.StationaryMorphBindings` が空を返すことを確かめる形にした |
| `ExcludedLayerTests` の 5 件（`TimedPetLayerDoesNotRejectRetainedFaceWhenEntireLayerIsExcluded` など） | 直す | `Sample` の値の確認を外し、`FindExcludedLayers` の確認を残した。レイヤーを除外する判定はほかに確かめるテストが無い |
| `ExportGimmickTests.RemovedAuxiliaryRendererDoesNotExcludeRetainedOrMovedFaceMorphs` | 直す | `SampleDefaults` の確認と、そのためだけに作っていたコントローラーを外した。書き出しで外す補助の表示が顔のモーフを外さない確認は残した |
| `TemporaryGraphSceneTests.SamplingAnExpressionKeepsTheSavedSceneUnmodified` | 外す | `Sample` で一時の Animator のグラフを作っても保存したシーンが変更扱いにならないことを確かめていた。表情ボードが使う `ExperimentalInstalledExpressionPlayer.Sample` で同じことを確かめる `PlayingAnInstalledExpressionKeepsTheSavedSceneUnmodified` が残る |

外したのは 64 件（107 ケース）と、`AFadeInBesideARestartingLayerReachesItsFinalKey` の 1 ケース、`NativeSupportBaseValidatesNestedAndBlendTreeMotionsConservatively` の入れ子の 1 ケース。`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` から外したテストを消し、ケース数を直した。`Tools/test-compatibility-tooling.py` の合計（`ParameterDriverExpressionTests` 63 → 21、`VrChatMenuExpressionTests` 64 → 35）を直した。

`ExpressionSettlementTests` で表情ボードの再生に直したテストは、`Settle` の前に 90 フレームの既定値の再生と、`Settle` の後の静止の確認が入る。期待値は `AHeldExitTimeIsReportedSeparatelyAndStillChecked` のほかは変えていない。

消した文言のうち「常時適用するFXの変形を確定できません。…」の訳があることを確かめていた `OptimizationLocalizationTests.OptimizationAndPermanentFxErrorsTranslateWithoutChangingAvatarLabels` の 2 行を外した。文言は `SampleDefaults` だけが出していて、4 言語の訳と一緒に消した。
