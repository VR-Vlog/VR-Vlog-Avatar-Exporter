# 実装の内部をなぞるテストと、1 つのツール向けの特例のテストを減らす

- 日付: 2026-10-23
- 対象: `Tests/AppPreview/AppPreviewTests.cs`、`Tests/Editor` のキャッシュ・フィンガープリント・準備済みメニュー・FaceEmo 系のテスト 12 ファイル、`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。このグループのファイルには、キャッシュのヒット数やフィールドの一覧のように実装の内部をそのままなぞるテストと、FaceEmo の登録データや FX の組み方の細かい違いを 1 つずつ確かめる特例のテストが多かった。

消すのは、書き出しの主経路のテスト（実際に VRM を書き出すもの）か、別のクラスの結合テストが同じ結果を守っているものに限った。代わりに守るテストを名指しできず、不具合の修正と一緒に足されたもの（`git log -S` で確かめた）は残した。復旧（ExportRecovery）とフィンガープリントのテストは、ユーザーのファイルと復旧セッションを守るものなのでメソッドは残し、行だけを減らした。

最初の案のあと敵対的レビューで、消したテストの一部はその経路を守る唯一のテストだと分かったので戻した。戻したものは各ファイルの項に「レビューで戻した」と書いた。

## 決定

- `AppPreviewTests`: `AppModeShowsTheExportedLilToonMaterials` と `MToonModeIgnoresTheLilToonData` を `AppModeShowsLilToonAndMToonModeIgnoresIt` の 1 本にまとめた（同じ書き出しを 2 つのモードで読み込む）。
  - 主経路で代わりに守れるか: 書き出しを 1 回にしただけで、両モードの確認はそのまま残る。
- `ExpressionInspectCacheTests`: 2 本を消した。`FixtureReachesTheCopiedControlObjects` の確認は `ReplayReturnsTheSameOrderedResultAsAFreshWalk` の先頭へ移した。
  - `UnsavedControllersAreNotCached`・`ClosingTheScopeStopsCaching` → レビューで戻した。このキャッシュで保存していないコントローラーを除くこと・範囲を閉じたらキャッシュをやめることを守るテストが他に無い。
  - `CopiedTypesHaveNoUnlistedMembers` → 主経路で代わりに守れるか: リフレクションでフィールド名の一覧と突き合わせるだけで、書き出しの結果は確かめない。コピーの漏れは `ReplayReturnsTheSameOrderedResultAsAFreshWalk` の結果の比較で出る。
  - `FixtureReachesTheCopiedControlObjects` → 主経路で代わりに守れるか: 確認を `ReplayReturnsTheSameOrderedResultAsAFreshWalk` に移したので不要。
- `PreparedMenuMergedRendererTests`: 3 本を消し、2 本の行を減らした。`EqualStaticEndpointRoundTripsOnceAndReturnsToNeutral` の FaceEmo 登録の行を外し、使わなくなった `RegisteredMenu` と登録データの入れ子クラス、別の Renderer のままにする `merge: false` の分岐を消した。`TemporalCollisionRejectsBeforeAnyEntryBakes` は 5 → 2 行（値の違い・静的との衝突）。
  - `IndependentRenderersStillHaveIndependentTargets` → 主経路で代わりに守れるか: 統合しない Renderer ごとに休止値から持ち上げる計算は `ExpressionDirectBindTests.NonZeroRestScalesTheRebasedShapeFromRestToItsFrame` が守る。
  - `InconsistentCapturedSnapshotsStopBeforeBaking` → 主経路で代わりに守れるか: 取り込んだスナップショットを手で書き換えて食い違わせる内部の整合性の確認で、通常の書き出しではこの状態にならない。
  - `DifferentAnimatedShapesKeepIndependentPrograms` → 主経路で代わりに守れるか: `MatchingAnimatedAliasesHaveOneProgramAndPreserveSource`（動く別名）と `DifferentShapesShareOneCompositeAndRetainUnreferencedRest`（違うシェイプ）の組み合わせで守れる。
  - FaceEmo 登録の行 → 主経路で代わりに守れるか: 登録データから読んだ表情を実際に書き出して読み戻す確認は `FaceEmoMergedUnderlayTests.PartialRegisteredBranchKeepsCommonFaceAndExplicitAliasPrecedence` が持つ。
- `PreparedNeutralEligibilityTests`: 何も消さなかった。
  - `PreparedPathMappingCannotChooseBetweenAmbiguousOriginalOrCurrentPaths`・`PreparedPathMappingKeepsUnderlayOnlyRendererIdentityAcrossMoves` → レビューで戻した。準備済みのパスと元のパスの対応づけ（あいまいなら選ばない・下敷きだけの Renderer が移動しても同じものを指す）を守るテストが他に無い。
- `SourceFingerprintCacheTests`: `MaterialDefaultCachePopulationDoesNotInvalidateButAnEffectiveEditDoes` を 3 → 2 行（float と texture）。
  - 主経路で代わりに守れるか: color は float と同じ保存済みプロパティの判定を通る。メソッドは復旧の誤判定を守るので 3 本とも残した。
- `AppTransferQuickExportTests`: 1 本を消し、`TheHiddenBoardUsesTheSavedBlink` を 3 → 2 行（Auto と Manual）。
  - `TheBoardExportKeepsTheStageChoiceItIsGiven` → 主経路で代わりに守れるか: 転送が段階の失敗時の選択に従うことは `ExperimentalExpressionCaptureTests.TransferFollowsTheStageChoice` が守る。
  - None の行 → 主経路で代わりに守れるか: Manual の行と同じ「保存したモードをそのまま渡す」分岐を通る。
- `ConstraintSourceFingerprintTests`: メソッドは全部残し、行を 42 → 35 に減らした（作者の入力 8 → 4、部分的な VRC の入力 5 → 4、VRC の回転 8 → 7、壊れたクォータニオン 3 → 2）。
  - 主経路で代わりに守れるか: 残した行が同じ判定（ソルバーが出力を持てない・作者の入力が変わった）を通る。どのメソッドも復旧の修正（#89）と一緒に足されたので消さなかった。
  - ソルバーが持たない回転の disabled・unlocked の行と、VRC の回転の unlocked・inactive の行 → レビューで戻した。コンストレイントが無効・ロックされていない・非アクティブのときに回転を入力として扱う分岐は、それぞれこの行だけが守る。
- `DescriptorVisemeExportTests`: 1 本を消し、`CaptureIgnoresOtherLipSyncModes` を 3 → 1 行、`ActualExportBindsDescriptorVisemesThatAreNotKnownAliases` を 2 → 1 行（kana）にした。
  - `CaptureReadsBlendShapeVisemesWhateverTheirNames` → 主経路で代わりに守れるか: 名前によらず口のシェイプを読むことは `ActualExportBindsDescriptorVisemesThatAreNotKnownAliases` が実際の書き出しで守る。
  - 減らした行 → 主経路で代わりに守れるか: VisemeBlendShape 以外のモードは同じ 1 つの判定で外れる。lip と kana は名前が違うだけで同じ経路を通る。
- `ExportRecoveryFailureSeedTests`: メソッドは全部残し、`OpeningRecoveryKeepsTheFirstRealFailureWithoutRepeatingTheExport` を 3 → 2 行（既存のレポートと空のレポート）、`SeedingARealFailureNeverWeakensLaterSourceChangeGuards` を 3 → 2 行（再試行と保存）にした。
  - missing の行 → 主経路で代わりに守れるか: レポートが無いときは、空のレポートの行と同じく失敗からレポートを作り直す分岐（`ExportRecoveryReport.FromException`）を通る。
  - プレビューの行 → 主経路で代わりに守れるか: 再試行と同じ無効化の判定を通る。保存先を守る確認は再試行・保存の行に残る。
  - `CancellationAndMissingFailureCannotStartRecovery` → レビューで戻した。キャンセルや失敗が無いときに復旧セッション自体が作られないことを守るテストが他に無い。
  - existing の行 → レビューで戻した。書き出しが診断入りのレポートを渡したときに、そのレポートをそのまま引き継ぐ分岐はこの行だけが守る。
- `FaceEmoPreparedFxIntegrationTests`: 4 本を消し、残した 5 本のうち 4 本の行を減らした（`RegisteredBranchUsesOriginalPlayerSlotAndNativeCommonWeightOnce` 4 → 2、`RegisteredPlayerReadsRegistryProvenPreparedAugmentationAndExactCallbacks` 2 → 1、`RegisteredPlayerRejectsUnprovedOrAmbiguousPreparedAugmentation` 4 → 2、`RegisteredPlayerRejectsPreparedMotionChangesBeforeDeferredEvaluation` 2 → 1）。使わなくなった `ConfigureTransitioningCommon` を消した。
  - `FractionalPlayerRetainsTransitionedCommonPoseBelowItsRegisteredClip` → 主経路で代わりに守れるか: 割合の重みの PLAYER の下に共通の顔が残ることは、残した `RegisteredBranchUsesOriginalPlayerSlotAndNativeCommonWeightOnce` の割合の行と `DirectExpressionNativeLayersTests.FractionalPlayerBlendsWithTheEvaluatedLowerDefaultAcrossPossibleTransitions` が守る。
  - `AmbiguousGeneratedPlayerCannotSilentlyUseStandaloneLayerOrder` → 主経路で代わりに守れるか: FX の形が読めないときに止まることは、残した `RegisteredPlayerRejectsAmbiguousMissingOrChangedCallbackProvenance` の ambiguous の行が同じ「FX状態」の拒否で守る。
  - `RegisteredPlayerCallbacksFollowExactOrProvedRetargetedClipProvenance` → 主経路で代わりに守れるか: 付け替えたクリップの FX を読む結果は `MergedFxDefaultsTests.RegisteredFaceEmoClipFollowsReparentedTargetAndMergedPermanentFx` と、残した `RegisteredPlayerReadsRegistryProvenPreparedAugmentationAndExactCallbacks`（コールバックあり）が守る。
  - `RegisteredPlayerRejectsOriginalIdentityWithDifferentEffectiveOverride` → 主経路で代わりに守れるか: 登録と違うクリップを拒否することは、残した拒否のテストの ambiguous の行と同じ判定を通る。
  - `RegisteredPlayerRejectsUnprovedOrAmbiguousPreparedAugmentation` の unknown・ambiguous の行 → 主経路で代わりに守れるか: 登録データの型が分からないときは登録データが無いときと同じ拒否を通り、重複は `RegisteredPlayerRejectsAmbiguousMissingOrChangedCallbackProvenance` の ambiguous の行が同じ「重複」の拒否で守る。
  - `RegisteredPlayerRejectsPreparedMotionChangesBeforeDeferredEvaluation` の effective-override の行 → 主経路で代わりに守れるか: 評価の前に差し替わったことは clip-data の行と同じ「評価前に変わりました」の判定を通る。
  - `RegisteredPlayerRejectsAmbiguousMissingOrChangedCallbackProvenance` の missing・callback-changed の行 → レビューで戻した。登録したクリップが FX から消えたとき・コールバックが評価前に変わったときの拒否を守るテストが他に無い。
  - `RegisteredPlayerRejectsUnprovedOrAmbiguousPreparedAugmentation` の missing・wrong-origin の行 → レビューで戻した。NDMF が足した中身を、登録データが無い・元のクリップが違うときに取り込まないことを守るテストが他に無い。
  - `RegisteredPlayerRejectsPreparedMotionChangesBeforeDeferredEvaluation` の clip-data の行 → レビューで戻した。準備済みクリップの中身が評価前に変わったときの拒否を守るテストが他に無い。
  - 実際の書き出しでの確認は `MergedFxDefaultsTests.InstalledMaReparentsFaceEmoRendererAndMergesPermanentPupilFxBeforeEvaluation` と `MergedFxDefaultsTests.FractionalStationaryFxUsesPreparedBaseOnceForMenuGestureAndFaceEmo` が持つ。
- `FaceEmoDefaultFaceTests`: 2 本を消した。
  - `BranchUsesCommonUnderlayAndExplicitZeroWithoutInheritingModeDefault` → 主経路で代わりに守れるか: 分岐の下に共通の顔を敷き、モードの既定を引き継がないことは `FaceEmoMergedUnderlayTests.PartialRegisteredBranchKeepsCommonFaceAndExplicitAliasPrecedence` が書き出しまで通して守る。
  - `ExplicitOpeningZeroOverridesCommonOpeningHundred` → 主経路で代わりに守れるか: 同じテストの `openingZero` が true の行が守る。
- `FaceEmoMergedUnderlayTests`: 5 本を消し、使わなくなった `Value`・`Branch`・`Curve`・`Accessory` を消した。
  - `DuplicateCommonSamplesCoalesceOrRejectBeforePublication` → 主経路で代わりに守れるか: 同じバインドの曲線を 2 本持つ入力は Unity のクリップでは作れない（テストのコメントのとおり）。
  - `ConflictingSelectedAliasesStopBeforeAnyUnderlayIsPublished` → 主経路で代わりに守れるか: 統合した Renderer で値が食い違う別名を止めることは `PreparedMenuMergedRendererTests.DifferentStaticWeightsStopWithFinalRendererAndShape` と `TemporalCollisionRejectsBeforeAnyEntryBakes` が守る。
  - `AnimatedExplicitAliasSuppressesEveryCommonAliasAndKeepsItsProgram` → 主経路で代わりに守れるか: 明示した値が共通の顔より優先されることは `PartialRegisteredBranchKeepsCommonFaceAndExplicitAliasPrecedence` が守り、動く別名を 1 つのプログラムにすることは `PreparedMenuMergedRendererTests.MatchingAnimatedAliasesHaveOneProgramAndPreserveSource` が守る。
  - `AmbiguousPreparedRendererDoesNotPublishCommonDefaults` → 主経路で代わりに守れるか: 同じパスの Renderer が 2 つあるときに黙って当てないことは `NeutralShapeEndpointTests.DuplicateRendererPathsCannotSilentlyApplySampleToTheFirstSibling` が守る。
  - `CommonRendererAbsentFromBranchKeepsEveryOriginAndIndependentEndpoint` → 主経路で代わりに守れるか: 別の Renderer を休止値から持ち上げる計算は `ExpressionDirectBindTests.NonZeroRestScalesTheRebasedShapeFromRestToItsFrame` が守る。
- `Tools/run-unity-compatibility.py`: `BEHAVIOR_CASES` の `SourceFingerprintCacheTests`・`FaceEmoPreparedFxIntegrationTests` を上の件数に合わせた（`PreparedNeutralEligibilityTests` は戻したので元のまま）。

## 消さなかったもの

- `StationaryLayerCacheTests` の 3 本。保存していないコントローラーを読み直すことなど、どれも結果を確かめるテストで、上の削除の代わりにもなっている。
- `AppTransferQuickExportTests` のうち、かんたん転送が通常の書き出しと同じ中身になること、書き出したファイルの安全とスタンプ、表情ボードを残さないこと、止まったときに直す場所を示すことを確かめる 8 本。共有の `SavedFixture` は `QuickTransferMatchesDefaultNormalTests` と `ExportSettingsTests` も使うので残した。
- `SourceFingerprintCacheTests`・`ConstraintSourceFingerprintTests`・`ExportRecoveryFailureSeedTests` のメソッド（`ExportRecoveryFailureSeedTests` はレビューで戻した 1 本を含めて全部）。復旧で変更を誤って見落とす・誤って無効にすることを防ぎ、保存先のファイルを守るため。
- `DescriptorVisemeExportTests.CaptureIgnoresAnUnsetFaceMesh`（修正 d0fd7c93 と一緒に足した）と、記録した表情を書き出す `RecordedExpressionExportBindsDescriptorVisemes` の 2 行。
- `FaceEmoMergedUnderlayTests` の `PartialRegisteredBranchKeepsCommonFaceAndExplicitAliasPrecedence`（実際に VRM へ書き出して読み戻す）と `ListOnlyExportReadsARegisteredClipOverTheCommonFace`（修正 af7b8a43 と一緒に足した）。
- `FaceEmoDefaultFaceTests` の `UnresolvedOriginalRendererAndAnimatedCommonFaceAreActionableErrors`（ユーザーが直せるエラー文）と `PreparedOverrideAndExclusionApplyToTheCommonUnderlay`（Override Controller と除外の組み合わせを守るテストが他に無い）。
- `PreparedMenuMergedRendererTests.StaticAndConstantAnimatedAliasesAreEquivalent` の 2 行。順番によらず同じになることが目的なので両方の順を残した。

## 件数

| ファイル | メソッド | ケース |
| --- | --- | --- |
| AppPreviewTests | 5 → 4 | 5 → 4 |
| ExpressionInspectCacheTests | 7 → 5 | 7 → 5 |
| PreparedMenuMergedRendererTests | 9 → 6 | 16 → 8 |
| PreparedNeutralEligibilityTests | 8 → 8 | 8 → 8 |
| SourceFingerprintCacheTests | 3 → 3 | 5 → 4 |
| StationaryLayerCacheTests | 3 → 3 | 3 → 3 |
| AppTransferQuickExportTests | 9 → 8 | 11 → 9 |
| ConstraintSourceFingerprintTests | 16 → 16 | 42 → 35 |
| DescriptorVisemeExportTests | 7 → 6 | 12 → 7 |
| ExportRecoveryFailureSeedTests | 4 → 4 | 8 → 6 |
| FaceEmoPreparedFxIntegrationTests | 9 → 5 | 26 → 10 |
| FaceEmoDefaultFaceTests | 4 → 2 | 5 → 2 |
| FaceEmoMergedUnderlayTests | 7 → 2 | 10 → 3 |
| 合計 | 91 → 72 | 158 → 104 |
