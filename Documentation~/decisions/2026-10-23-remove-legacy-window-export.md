# 旧書き出し画面と、表情ボード以外の書き出しの経路を消す

- 日付: 2026-10-23
- 対象: `Editor/LilToonExporterWindow.cs`・`Editor/ExportSettings.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/`・`Tools/validate.py`・`Tools/run-unity-compatibility.py`・`README.md`・`README.en.md`・`Documentation~/`・`CHANGELOG.md`

## 背景

書き出しの入口は、どれも表情ボード（`ExperimentalExpressionCaptureWindow`）だけになっている。

- 7f141ad9（2026-10-10）でメニューの「VRMを書き出す」が表情ボードを開くようにした。
- 8a896626（2026-10-23）で、書き出しに失敗したあとの **書き出し画面を開く** も表情ボードを開くようにした。これで旧書き出し画面（`LilToonExporterWindow`）を開く経路が無くなった。
- 49d6c1fc（2026-10-22）で、表情ボードは書き出しのたびに「その他の表情」の一覧（`listFaces`）を必ず渡すようにした。

`ExperimentalExpressionCaptureSession.Export` には、次の3つの経路があった。

1. `listFaces` が null で、記録した表情も標準表情も無いとき: 表情メニュー・FX を再生する通常の書き出し。
2. 記録した表情が無く `replaceCustomFaces` が false のとき: 標準表情と `listFaces` の書き出し。
3. それ以外（記録した表情があるか `replaceCustomFaces` が true）: 記録した表情を静止した表情として書き出す。

製品コードでこのメソッドを呼んでいるのは、表情ボードの `ExperimentalExpressionCaptureWindow.cs` の1か所だけだった。そこでは `listFaces` に一覧から作った null でない配列を渡し、記録した表情には `Array.Empty<Pose>()` を渡し、`replaceCustomFaces` は渡していなかった（既定の false）。経路1は `listFaces == null` が条件なので通らず、経路3は記録した表情が1件以上か `replaceCustomFaces` が true が条件なので通らない。残るのは経路2だけだった。`session.Expressions`（記録した表情）は設定の保存・読み込みにだけ使われ、書き出しには入っていなかった。

`ExportSettings.Export`・`Creator`・`SaveVrm` は旧書き出し画面だけが呼んでいた。前の整理（9e47971a）では、書き出しから呼ばれないメンバーのうち旧書き出し画面が使うものは残していた。

## 決定

書き出す内容は変えずに、通らない経路と、それだけが使っていたコードを消す。

- `LilToonExporterWindow`: 画面の描画・設定の読み書き・瞬きの状態のキャッシュ（`BlinkStatusCache`）・書き出しの手順（`ExportOneClick`・`ShowExportCompletion`・`OpenAppPreview` など）を消した。表情ボードと転送が使う `SavedWithMissingShadersFormat`・`ExportAndSaveNormally`・`PackageVersion`・`SavedVrmTransferMethod`・`EmbeddedTransferMethod`・`EmbeddedTransferringMethod`・`RequireLilToon`・`InstalledLilToonVersion` は残した。
- `LilToonExporterWindow` は static クラスにせず、開かれたらすぐ自分を閉じる `EditorWindow` のまま残した。Unity は開いていたウィンドウを型の名前でレイアウトとスクリプトの再読み込みに保存するので、型が `EditorWindow` でなくなると、旧書き出し画面を開いたまま保存したレイアウトを読むときにウィンドウを復元できない。自分を閉じるだけのウィンドウにしておけば、その場合も何も表示せずに閉じる。ファイル名とクラス名は変えていない（`Tools/build-package.py` と `Tools/test-package.py` が、このファイルに転送の型の名前があることを確かめている）。
- `ExportSettings.Export`・`Creator`・`SaveVrm` を消した。保存する設定の形式と読み込み（`ExportSettingsStore`・`ExpressionPresetStore`）は変えていない。
- `ExperimentalExpressionCaptureSession.Export`: `listFaces` を必須の引数にし（null なら `ArgumentNullException`）、経路2だけを残した。引数の `recordedExpressions` と `replaceCustomFaces` を消した。
- 経路3と手動の記録にだけ使っていたセッションのメンバーを消した: `DiscoverCandidates`・`PreviewCandidate`・`ImportResult`・`ImportCandidates`・`SetWeight`・`CaptureBaseline`・`Capture`・`PreviewRecorded`・`MergeIdentical`・`InjectSourcePoses`（`ExperimentalExpressionCaptureSession.Clips.cs`）。`RestoreBaseline` は表情ボードが使うので残した。
- **アプリに転送（かんたん）** は今までどおり表情ボードの保存で書き出すので、何も変えない通常と同じVRMになる（`QuickTransferMatchesDefaultNormalTests` はそのまま）。

## 消したテストと、書き出しの経路で残っている確認

- `BlinkExportTests.StatusReusesScansUntilExpiryOrARelevantSettingChanges`・`StatusClearsAnEarlierSuccessWhenTheLiveBindingBecomesInvalid`: 消した旧書き出し画面の `BlinkStatusCache` だけを確かめていた。瞬きの判定そのものは同じクラスのほかのテストが確かめている。
- `ExportSettingsTests.TheWindowKeepsTheStoredSettingsAfterAScriptReload`: 旧書き出し画面のフィールドをリフレクションで見ていた。設定の保存と読み込みは同じクラスのほかのテスト（`SettingsComeBackForTheSameAvatarAfterSelectingAnother` など）が確かめている。
- `ExpressionPresetTests.CaptureRouteKeepsRecordedCustomFacesBesideThePreset`: 経路3（記録した表情と標準表情を一緒に書き出す）。表情ボードの書き出しで標準表情を確かめるテストは同じクラスに残っている。
- `ExpressionPresetTests.ReplacingCustomFacesWithNothingLeavesNoKeptFace`（2ケース）: 経路3（`replaceCustomFaces`）。一覧が空のときに一覧に無い表情を入れないことは `ExperimentalExpressionCaptureTests.ListOnlyExportLeavesOutMenuFacesTheListDoesNotHave` の `0` のケースが確かめている。
- `ExpressionPresetTests` の6メソッド（`WithoutAssignmentsTheExportedExpressionsAreUnchanged`・`MouthGazeAndNeutralAssignmentsExportWithoutOverrides`・`AssignedBlinkReplacesTheAutomaticBlink`・`ConfirmingOneBlinkPresetKeepsTheOtherCandidates`・`NoneOnAMouthPresetStopsTheNameBinding`・`NoneOnEveryBlinkPresetWritesNoBlink`）の `recordFace = true` のケース: 経路3。`false` のケースは表情ボードと同じく一覧（空）を渡す書き出しに直して `[Test]` にした。
- `ExperimentalExpressionCaptureTests.RecordedFacesWithTheSameWeightsAreWrittenOnce`: 消した `MergeIdentical` だけを確かめていた。
- `ExperimentalExpressionCaptureTests.RetrySnapshotReplacesTheSourceCatalogAfterLiveRowsWereRemoved`: 経路3の再試行。表情ボードの書き出しの失敗からの回復は `ExportRecoverySessionTests` が確かめている。
- `ExperimentalExpressionCaptureTests.MeshRendererRecoveryMapsTheOriginalRendererToThePreparedExportCopy`: 経路3で、元のアバターのレンダラーを指す回復の操作を、表情ボードのコピーから準備したレンダラーへ対応付けることを確かめていた。表情ボードの経路は元のアバターをそのまま書き出すので対応付けが要らず、対応付けごと消した。補助表示のレンダラーを外す回復は `ExportFlowTests` と `MenuImportRecoveryTests` が確かめている。
- `ExperimentalExpressionCaptureTests.NeutralAndDuplicateFacesAreRejectedWithoutLosingExistingRecords`: 消した手動の記録（`SetWeight`・`Capture`）だけを確かめていた。
- `ExperimentalExpressionCaptureTests.CapturedFacesActuallyReimportAndReleaseToAnEditedBaseline`: 経路3（`CaptureBaseline`・`Capture` で記録した表情の書き出し）。一覧の表情が読み込み直したVRMで変形し元に戻ることは、名前を変えたメニューのテスト（下）と `ListOnlyExportLeavesOutMenuFacesTheListDoesNotHave` が確かめている。
- `ExperimentalExpressionCaptureTests.ExistingMenuCandidateCanBeDiscoveredPreviewedAndCapturedWithoutChangingItsController` の `model-only`・`none`・`synced`・`neutral`・`display`・`delayed`・`completed`・`audio`・`unknown` のケース: 消した候補の探索と取り込み（`DiscoverCandidates`・`ImportCandidates`）を確かめていた。テストは `ExistingMenuFaceExportsFromTheBoardListWithoutChangingItsController` に名前を変え、メニューの表情を一覧から書き出してもコントローラーが変わらないことを `listed` のケースで、音に反応するコールバックを断ることを `audio-reactive` のケースで確かめる。表情なしの書き出しは `ModelOnlyExportReimportsWithoutRequiringFacialFiles` が確かめている。

テストメソッドは残して、書き出しの呼び方を表情ボードと同じにしたもの:

- `ExperimentalExpressionCaptureTests` で `session.Export` を呼ぶテストは、すべて表情ボードと同じく一覧を渡す。`recordFace` のあるテストは、`true` で一覧に表情ファイルを1件、`false` で空の一覧を渡す。記録した表情が要るテスト（設定の保存・読み込み）は、テストの中で `Expressions` に直接足す。複数のアバターを続けて書き出すテストの `CaptureBaseline` は、同じ検査をする `RestoreBaseline` に置き換えた。
- `DescriptorVisemeExportTests.RecordedExpressionExportBindsDescriptorVisemes` は `BoardExportBindsDescriptorVisemes` に名前を変え、表情ボードの書き出しで確かめる。
- `LilToonInstallationTests.ExportChecksAcceptTheFoundLilToon` と `PhysBoneSpringExportTests.MeshRendererWithoutMeshFilterSourceStopsBeforeExport` は、消した `ExportSettings.Export` の代わりに、表情ボードの書き出しと、書き出しが最初に呼ぶ `UniVrmOneClickExporter.ValidateSourceHasMesh` で確かめる。

## そのままにしたこと

- 保存する設定の形式と読み込み（`ExportSettingsStore`・`ExpressionPresetStore`）。旧書き出し画面で選べた **補助ギミックを自動除外** と補助ギミックの **VRMに入れる** の選択は、保存済みの設定にあれば表情ボードの書き出しでも今までどおり使う。
- 旧書き出し画面で **補助ギミックを自動除外** をオフにして保存した人が、表情ボードからオンに戻す操作は無い。旧書き出し画面への入口はメニュー（7f141ad9）と失敗のあとの画面（#400）で既に無くなっており、このPRで変わらない。戻す操作を足すかどうかは別に決める。
- 表情ボードのセッションを作るときに行っていた瞬きの取り込みと、回復用のレンダラーの対応付けを消したので、統合ツールで瞼のレンダラーをまとめたアバターなど、前はセッションを作る段階で失敗していたアバターでも表情ボードが開くようになる。前に書き出せていたアバターの出力は変わらない。
- `Editor/Locales/*.json` の、旧書き出し画面だけが使っていた文言。`Tools/validate.py` は消すことを求めていない。

## 次に消せる候補

今回の変更で書き出しから使われなくなったもの。ほかの作業中のブランチと同じファイルを触るので、このPRでは残した。

- `UniVrmOneClickExporter.Export` の、`listFaces` が null のときに表情メニュー・FX を再生する経路と、`inferEmotionPresets` が true のときの処理（表情ボードは常に false を渡す）。
- `UniVrmOneClickExporter.Export` の `recoverySource`・`recoveryMaterialSource`・`descriptorVisemes` の引数。経路3だけが渡していた。
- `VrChatVisemeExport.Read` と `VrChatVisemeExport.Captured`。経路3と、上の `descriptorVisemes` を渡すテスト（`NdmfLostMorphFollowTests`）だけが使う。
- `ExportSettings.Assign` と `VrmMenuExpressions.CountRegistered`（テストだけが使う）。

## 確認したこと

- `Tools/validate.py` から旧書き出し画面と `ExportSettings.Export` の中身を確かめる行を消した。
- `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` から、消した `BlinkExportTests` の2メソッドを外し、`Tools/test-compatibility-tooling.py` が通った。
- 消したメンバーの名前を `Editor/`・`Tests/`・`Tools/`・`.github/`・`Documentation~/` で検索し、過去の決定記録のほかに残っていないことを確かめた。
- Unity の EditMode でのコンパイルとテストは CI で確かめる。
