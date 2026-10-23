# 書き出しの表情を表情ボードの一覧だけから読み、メニューを再生する入口を閉じる

- 日付: 2026-10-23
- 対象: `Editor/VrChatExpressionSampler.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/VrChatExpressionMenu.cs`・`Editor/Locales/ExporterLocale_{en,ko,zh-Hans,zh-Hant}.json`・`Tests/Editor/*`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`

## 背景

表情ボードからの書き出しは、表情ボードの「その他の表情」の一覧（`listFaces`）を VRM の表情にしていて、表情メニュー・Gesture・FX・FaceEmo の表情を再生しない。一方で `UniVrmOneClickExporter.Export` に一覧を渡さない（`listFaces` が null の）ときだけは、`VrChatExpressionSampler.Analyze` が以前の経路のまま、メニューの各項目と Gesture・FX・FaceEmo の表情を再生して表情にしていた。製品の呼び出し元（表情ボード）は必ず一覧を渡すので、この経路を通るのはテストと調査用の呼び出しだけだった。表情の候補が 512 件を超えたときにメニューの表情を外して読み直す処理と、その文言もこの経路だけのものだった。

## 決定

- `Analyze` は常に一覧から表情を読む。メニューは、中立の顔と一覧の読み取りが使うパラメーター・Controller を得るためにだけ読む（`SkipPlayback`）。メニュー・Gesture・FX・作り込みの表情・FaceEmo の再生を呼ばない。
- `listFaces` が null のときは、一覧が空のときと同じく表情を書き出さない。既定の顔の扱いは変えない。
- 引数 `authoringSource`・`menuPolicy` と、`VrChatMenuImportPolicy.ListFaces`・`Source.MenuOmittedForCandidateLimit`・`MenuOverCandidateLimit` の文言（4 言語の訳を含む）を外した。一覧が 512 件を超えるときは、これまでどおり `AddListFaces` が止める。
- 再生する側のコード（`VrChatFxExpressions.Add`・`VrChatGestureExpressions.Add` など）は、この変更ではまだ消さない。どこからも呼ばれなくなったものを次の変更でまとめて消す。

表情ボードから書き出す VRM は変わらない。

## テスト

再生の結果だけを確かめていたテストは外し、それ以外を確かめていたテストは一覧を渡す呼び出しに直した。

| テスト | 扱い | 理由 |
|---|---|---|
| `ExpressionSettlementTests.AMenuItemFollowedThroughAnExitTimeIsListedAsApproximated` | 外す | メニュー項目を再生した結果の警告を確かめていた |
| `ExpressionSettlementTests.AMenuItemSelectingAMovingTreeIsNotSettledToOneFrame` | 外す | メニュー項目が選ぶ BlendTree の再生を確かめていた |
| `ExpressionSettlementTests.AStillTreeIsSettledUnlessAClipMovesItsInput`（2 件） | 外す | 同上 |
| `ExpressionSettlementTests.AMovingTreeReachedLateIsNotSettledToOneFrame` | 外す | 同上 |
| `MenuImportSdkRecoveryTests.AMenuPastTheCandidateLimitIsLeftOutAndGesturesAreKept` | 外す | 消した読み直しの処理そのもの |
| `DirectFxExpressionDiscoveryTests.DescriptorFxIsAuthoritativeWhenRootAnimatorControllerIsNone` | 直す | ルートの Animator に Controller が無いとき、Descriptor の FX を `Controller` にすることを確かめる唯一のテスト。一覧の読み取りでも `VrChatExpressionMenu.Read` がこの解決を行うので、一覧の表情 1 件で確かめる。FX の作り込みの表情を再生する遷移は外した |
| `ParameterDriverExpressionTests.MenuAnalysisImportsStationDependentFaceUsingTheFixedNormalContext` | 外す | メニュー項目を再生した値を確かめていた。固定の文脈での評価は同じクラスの `SampleFixed` のテストが確かめている |
| `AdditionalParameterConstantTests.SerializedUnsavedSdkInputsAreReadBeforeTheListIsRead` | 直す | 保存前の SDK の入力を `Analyze` が読むことを確かめる。メニューを外す場合と外さない場合の 2 件を 1 件にし、表情が出ないことを確かめる |
| `FacialProjectionTests.ANewAnalysisRechecksChangedGeometryAfterBothSuccessfulAndFailedFaceProofs` | 直す | `Analyze` ごとに形状の確認をやり直すことを、一覧の表情で確かめる |
| `FaceEmoMergedUnderlayTests.ListOnlyExportReadsARegisteredClipOverTheCommonFace` | 直す | 新しい引数の形にしただけ |
| `ExpressionExportRoundTripTests.DriverMenuAndAnimatedGestureSurviveRealExportAndReimport`（2 件） | 直す | 止まった表情と動く表情の書き出しと読み込み後の形を、一覧の 2 件で確かめる。メニューと Gesture の出どころの確認は外した |
| `FacialProjectionTests.ExportedProjectedFaceReimportsWithPreparedOutfitAndReturnsExactlyToRest` | 直す | 投影した表情と基本の顔への戻りを、一覧の表情で確かめる |
| `LegacyBlinkMenuRoundTripTests.SplitLegacyBlinkAndDistinctNamedMenusKeepTheirAppearanceAfterReimport`（2 件） | 直す | 左右に分かれたまばたきと表情の形を、一覧の表情で確かめる。同じ状態を選ぶ 2 つのメニュー項目を 1 つにまとめる確認は外した |
| `MaPermanentAppearanceExportTests.MergedPermanentPupilHideSurvivesVrmReloadBlinkAndMenuGeometry`（4 件） | 直す | MA が常時のレイヤーを合成することは実際の再生で確かめたまま、書き出しの基本の顔・まばたき・表情の形を一覧の表情で確かめる。一覧の表情はクリップをそのまま読み、合成された常時のレイヤーを重ねないので、瞳は表情でも隠れないことを確かめるようにした |
| `MaReactiveExpressionExportTests.MenuReactiveShapeSurvivesCanonicalBuildAndVrmReload`（3 件） | 外す | MA のメニュー項目からビルドで作られる FX を再生した表情を確かめていた。一覧に渡せるクリップが無い |
| `MenuImportSdkRecoveryTests.OversizedMenuExportsInFullWithoutChangingSharedInputs` | 直す | 大きなメニューがあっても書き出しが通り、入力を変えないことを、一覧の 2 件で確かめる |
| `MergedFxDefaultsTests.FractionalStationaryFxGeometrySurvivesOneClickExportAndVrmReimport`（2 件） | 直す | 重み 0.5 の常時の FX（Override・Additive）が、書き出しと読み込みの後の基本の顔に入らないことを確かめる唯一のテスト。基本の顔は書き出し用コピーの値のままで一覧に左右されないので、期待値は変えずに一覧の 2 件で確かめる。メニューと Gesture の表情に常時の FX を 1 回だけ重ねた再生の結果の確認は外した |
| `SelectedExpressionAppearanceTests.MixedLilToonGesturePreservesPreparedAppearanceAndMorphEndpointsAfterExport`（2 件） | 直す | 材質のカーブを含む表情でも、書き出し用コピーの見た目と表情の形が残ることを、一覧の表情で確かめる |
| `SelectedLayerControlTests.SelectedLayerControlMenuSurvivesFullVrmRoundTrip` | 外す | メニュー項目が SDK のレイヤー制御で重みを変えた再生の結果を確かめていた |
| `TemporalNeutralShapeTests.TemporalRequiredEndpointExportsPreparedRestAndTheExactAuthoredProgram`（2 件） | 直す | 動く表情の書き出しと基本の顔を、一覧に渡した動くクリップで確かめる |

lilToon の版ごとの比較（`LilToonVersionParityHarness`）は一覧を渡さずに書き出すので、表情の無い VRM を比べるようになる。比べているのは材質なので変えない。
