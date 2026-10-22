# 同じ分岐を別の入力で確かめるテストと文言だけのテストを減らす

- 日付: 2026-10-23
- 対象: `Tests/Editor` の `ExportGimmickTests`・`InactiveRootExportTests`・`ExportSettingsTests`・`SectionedToonSubstitutionTests`・`ConstraintExportTests`・`VrmLookAtExportTests`・`NonLilToonExportTests`・`DeterministicNamesTests`・`ExpressionAnimationDocumentTests`・`ExportStageGuardTests`・`PoseLimitPlanTests`（`PoseLimitChooserTests` を含む）・`AttachmentConnectionTests`・`ExportFidelityTests`・`ExpressionSettlementTests`、`Tools/run-unity-compatibility.py`

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。残すのは、書き出しの主経路を通す少数の結合テスト、主経路で代わりに守れない過去の不具合の回帰テスト、壊れたら気づきにくい純粋なロジックの代表値だけにする。このコミットでは、同じ分岐を別の入力で確かめ直しているテストと、警告・報告の文言だけを確かめるテストを対象にした。消す前に、主経路のテストで代わりに守れるかを1件ずつ確かめた。

## 決定

### `ExportGimmickTests`

- `DisabledAutomaticOptionPreservesAllRenderers` は残す。レビューで、その経路の唯一の守りと分かったので戻した。
- `AutomaticOptionOffKeepsStructuralGimmicks` を消す。主経路で代わりに守れるか: 確かめていた1行（`AutoExclude = false` で何も自動で外さない）を `VrChatOnlyGimmickWithAMenuLeavesAsAWhole` に移した。
- `TableFindsAModularAvatarRenderTextureCamera` を消す。主経路で代わりに守れるか: 同じ表の規則は `StructuralGimmickReplacesTheTableFindingInsideIt` が同じ部品で通る。
- `OrdinaryAvatarPartsMatchNoTableRule` を消す。主経路で代わりに守れるか: 普通の部品を誤って外さないことは `NamesBlackColorAndShapeDoNotIdentifyAGimmick` と、主経路の書き出しテスト（服・体が残る）が守る。
- `KeepingAChildPreventsAutomaticHierarchyDeletion` を消す。主経路で代わりに守れるか: 子を残すと親ごとの除外が止まることは `VrChatOnlyGimmickWithAMenuLeavesAsAWhole`（`prop` を残す行）が守る。
- `InactiveAvatarRootStillHasItsGimmicksFound` は残す。レビューで、その経路の唯一の守りと分かったので戻した。
- `RemovedAuxiliaryRendererDoesNotExcludeRetainedOrMovedFaceMorphs` の `[TestCase(false)]` を消し、元のパスへ移した顔の行（`true`）だけ残す。`true` の行が同じ判定を通ったうえで移動も扱う。

### `InactiveRootExportTests`

- `AShownAvatarRecordsNoHiddenRoot` を消す。主経路で代わりに守れるか: 表示中かどうかをアバターの中だけで判断することは同じクラスの `ActivityIsJudgedInsideTheAvatar` が守り、表示中のアバターで余計な警告が出ないことは、表示中のアバターを丸ごと書き出して警告が空であることを確かめるテストが守る。

### `ExportSettingsTests`

- `EachDifferentProblemIsShownOnce` を消す。警告を1回だけ出す仕組みの確認で、設定を失わないことの保証ではない。主経路で代わりに守れるか: 読めないファイルを上書きしないことは `AFileThatCannotBeReadIsNeverOverwritten` が守る。

### `SectionedToonSubstitutionTests`

- `LightingSwitchedOffCastsNoShadow` は残す。レビューで、その経路の唯一の守りと分かったので戻した。
- `OnlyBrightnessUnderATintedMainColorIsNotReported` は残す。レビューで、その経路の唯一の守りと分かったので戻した。
- `ApplyNamesTheMappedMaterialsAndTheSectionsLeftOut` を消す。警告の文言と並びだけの確認。変換の中身は `Create` を使う他のテストが確かめている。

### `ConstraintExportTests`

- `TheVrmFormulaFollowsTheSourceExactlyWhenKept` を消す。主経路で代わりに守れるか: そのまま残す回転コンストレイントは `SiblingRotationWithTheSameRestBecomesAKeptVrmRotation` と `AFullExportWritesTheNodeConstraint` が守る。
- `AVrchatRotationConstraintIsReadWithoutTheSdkReference` は残す。レビューで、その経路の唯一の守りと分かったので戻した。
- `EveryShippedLocaleTranslatesTheConstraintResults` を消す。翻訳の文言だけの確認で、訳が無くても元の文が出る。
- `AnOffConstraintKeepsThePoseAndIsListed` の `inactiveAndUnlocked` の行を消す（4→3ケース）。止まっているときの分岐は `inactive` の行が通る。使わなくなった `switch` の分岐も消した。

### `VrmLookAtExportTests`

- `MeasuredEyeLookPosesBecomeOneToOneRangeMaps` を消す。主経路で代わりに守れるか: 左右で違う角度（20度と30度）の計測と読み戻しは `RotatedScaledHeadSwappedSidesAndLinkedPosesMatchUniVrm` が守る。ただし、上下の角度が左右の目で違うとき（上が12度と14度、下が16度と15度）に小さいほうを使うことは、もう確かめていない。警告は計測できないときだけ1件出ることを `UnmeasurableEyeLookKeepsTheDefaultRangeAndWarns` が守る。計測できた目の動きで警告が出ないことを直接確かめるテストは無くなった（`EyesWithoutEyeLookUseTheEyeMidpointAndTheDefaultRange` が確かめるのは目の動きが無いときだけ）。元の目の回転を書き換えないことの確認は `RotatedScaledHeadSwappedSidesAndLinkedPosesMatchUniVrm` に移した。

### `NonLilToonExportTests`

- `AnAssignedNonLilToonMaterialKeepsTheLilToonMessage` を消す。エラーの文言を確かめるほか、空のマテリアル枠があっても割り当て済みのマテリアルが1つでもあれば「マテリアルが無い」エラーにしない分岐（割り当ての有無の判定）も通っていた。主経路で代わりに守れるか: すべて空のときの分かれ方は `AvatarWhoseMaterialsAreAllMissingNamesTheRenderers` が守り、割り当て済みのマテリアルを持つアバターの書き出しは主経路のテストが毎回通る。

### `DeterministicNamesTests`

- `HashHasTheLengthOfTheReplacedGuid` を消す。主経路で代わりに守れるか: 32桁の16進であることは `SameInputsGiveTheSameName`（値を固定）と `BlinkBakeGivesTheSameNamesOnEveryExport`（正規表現）が守る。

### `ExpressionAnimationDocumentTests`

- `AppVersionsRejectOtherForms` を11→6ケースにする。残すのは `"1.0"`（部分の数）・`"1..0"`（空の部分）・`"01.0.0"`（先頭の0）・`"+1.0.0"` と `" 1.0.0"`（数の読み取りなら通ってしまう記号と空白）・`null`。消した `"1.0.0.0"`・`"1.0.a"`・`"-1.0.0"`・`"1.0.0-beta"`・`""` は残した行と同じ分岐を通る。

### `ExportStageGuardTests`

- `OverTheLimitPosesKeepTheFirst128` を消す。主経路で代わりに守れるか: 上限を超えたポーズの後ろを落として128件にすることは `PoseLimitChooserTests.TheNormalExportAsksOnceAndListsWhatWasLeftOut` が守る。上限を超えたまま選ぶと例外になる確認はそのテストに移した。

### `PoseLimitPlanTests`

- `PoseLimitPlanTests.WithinTheLimitsNothingIsDroppedWhateverWasSaved` を消す。主経路で代わりに守れるか: 保存済みの選択があっても余計なポーズを落とさないことは `PoseLimitChooserTests.ATooLargePoseAloneDropsNothingElseAndKeepsTheSavedChoice` が確かめる。
- `PoseLimitPlanTests.OverTheCountTheDefaultDropsTheLastRows` を消す。主経路で代わりに守れるか: 既定で後ろから落とすことは `PoseLimitChooserTests.QuickAndBatchExportsUseTheDefaultWithoutAsking` と `TheNormalExportAsksOnceAndListsWhatWasLeftOut`（既定は保存しない）が守る。

### `AttachmentConnectionTests`

- `GeneratedRootJointsStayFixedWhileSameNamedHairCanBeAttached` を6→4ケースにする。`(1, true)` と `(2, true)` を消す。`weightMode` が2のときは早く抜けるので `includeConnected` は効かない。`weightMode` が1のときは固定する関節ができないので、固定した関節を付け替えられないことの確認が空になり、`includeConnected` で変わる部分は `(0, false)` と `(0, true)` が通る。
- `OnlyActiveEnabledVrmConstraintsEstablishOrBlockAttachment` を4→3ケースにする。`(false, false)` は無効・非アクティブのそれぞれ片方の行と同じ分岐。
- `ManualAndMaAppearanceMatchAfterCompleteExportAndReload` を5→2ケースにする。`0`（変更なし）と `7`（3つの規則をすべて通す）を残し、1つずつの `1`・`2`・`4` を消す。

### `ExportFidelityTests`

- `StaticDecalBoundsAndUvCopyAreBaked` を4→2ケースにする。`("2nd", false)` と `("3rd", true)` で、層の違いとUVのコピーの有無を両方通る。

### `ExpressionSettlementTests`

- `NativeClocksMatchTheSettlementConversion` を4→2ケース（`(.5f, false)`・`(2f, true)`）、`ANonLoopingClipLongerThanTheWarmUpIsSavedAtItsLastKey` を3→2ケース（`.5f`・`2f`）、`AStateRestartedEveryFrameWithADriverIsNotSaved` を4→3ケース（`(0f, true)` を消す）にする。速さ1は他の行の間の値で、別の分岐を通らない。メソッドはどれも不具合修正で入ったので残した。

### `Tools/run-unity-compatibility.py`

- `BEHAVIOR_CASES` から消したメソッドを外し、`ExpressionSettlementTests` のケース数を合わせた。

## 消さなかったもの

- `MenuImportSdkRecoveryTests`・`ExportObjectExclusionTests`: 元のアバターを書き換えないこと、復旧、リリース後の不具合修正の回帰で、主経路のテストに無い入力を使う。
- `ExpressionBoardTests`: `ARecordRouteOverSixtyFour` は外した64件の上限が戻らないことを、主経路に無い件数で守る。
- `NativeTreeTrajectoryTests`: すべて1つの不具合修正で入った回帰テスト。
- `OutlineRendererScaleTests`・`ExportGimmickChoicesTests`: 近い写しが無かった。
- `ExportStageGuardTests` の段階ごとのテスト（`TestCaseSource`）: 壊れたファイルや途中までのファイルを書かないための守り。
- `PoseLimitChooserTests` のうち、保存済みの選択を消さない・取り消しで書き出しを止める・かんたん転送とバッチで同じ結果にするテスト: ユーザーの保存した設定を失わないための守り。
- `ExpressionAnimationDocumentTests` のメソッド: どれも別の分岐を通る。

## 件数

触った14ファイルの合計で、テストメソッド238→223、ケース300→266（`TestCaseSource` の2メソッドはケース数に入れていない）。

| ファイル | メソッド | ケース |
| --- | --- | --- |
| `ExportGimmickTests` | 43→39 | 45→40 |
| `InactiveRootExportTests` | 7→6 | 7→6 |
| `ExportSettingsTests` | 15→14 | 15→14 |
| `SectionedToonSubstitutionTests` | 21→20 | 21→20 |
| `ConstraintExportTests` | 25→23 | 32→29 |
| `VrmLookAtExportTests` | 7→6 | 7→6 |
| `NonLilToonExportTests` | 5→4 | 5→4 |
| `DeterministicNamesTests` | 7→6 | 7→6 |
| `ExpressionAnimationDocumentTests` | 18→18 | 28→23 |
| `ExportStageGuardTests` | 12→11 | 11→10 |
| `PoseLimitPlanTests`（2クラス） | 13→11 | 14→12 |
| `AttachmentConnectionTests` | 14→14 | 31→25 |
| `ExportFidelityTests` | 15→15 | 21→19 |
| `ExpressionSettlementTests` | 36→36 | 56→52 |
