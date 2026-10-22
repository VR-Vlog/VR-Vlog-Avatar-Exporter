# 互換表示と書き出し結果の確認のテストから重複を外す

- 日付: 2026-10-23
- 対象: `Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/ExportFallbackConfirmationTests.cs`・`Tests/Editor/MatcapApproximationTests.cs`・`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。残すのは、主経路を通すテスト、実際に起きた不具合の再発を防ぐテスト、計算の代表値を少しだけ、とする。ほかのテストに同じ確認が無いものは、この 3 つに当たらなくても残す。

3 ファイルの各テストについて、`git log -S<テスト名>` で足したコミットを調べ、不具合の修正（コミットの説明か CHANGELOG の「直しました」）と一緒に足されたものは残した。消す前に、同じ確認がほかのテストにあるかを `Tests/Editor` と `Tools` で探した。

## 決定

### MaterialFallbackTests（メソッド 14 → 13、ケース 21 → 17）

残したもの:

- `LightingSchemaStoresAuthoredValuesAndFallbackUsesCorrectUnits`: 影の境界・発光の強さ・リムの変換を直した修正（#30）で足した。`_ShadingShiftFactor` を確かめるのはここだけ。
- `OutlineKeepsAuthoredColorInsteadOfMultiplyingByAlbedo`: 輪郭線がほぼ黒になった不具合の修正。
- `OutlineMaskConvertsRedToGreenAndRetainsSource`: 輪郭線の幅のマスクの変換・使い回し・頂点色で太さを決める輪郭線を外すことを確かめるのはここだけ（0.5.1 の口元の輪郭線の修正）。
- `EnabledShadowUsesBaseImageForNullOrBuiltInWhiteShadeMap`: 影の側が白くなった不具合の修正（#14）。
- `DefaultExportKeepsShadeImageAndIntentionalEmissionWithoutChangingSource`: 既定の設定で互換表示を作る主経路。元の材質が変わらないことも確かめる。
- `FakeShadowFallbackIsInvisibleAndOrdinaryFallbackStaysOpaque`: FakeShadow の代替がメッシュ全体を影の色で覆った不具合の修正。
- `ColorMaskZeroFallbackIsInvisible`（2 ケース）: 色を描かない材質が殻のように見えた不具合の修正。0 と 15 で結果が分かれる。
- `TransparentFallbackWritesDepthWhenLilToonDoes`（2 ケース）: 半透明の材質で奥の面が透けた不具合の修正。1 と 0 で結果が分かれる。
- `GradatedEmissionUsesTheAverageOfTheGradation`（3 → 2 ケース）: グラデーションの係数が `FallbackColor` に掛かることを確かめるのはここだけ。止まったグラデーション（速さ 0）は別の分岐を通る。
- `FluorescentEmissionUsesTheLitSceneStrength`（4 → 2 ケース）: 蛍光の発光が明るい場面で白く飛んだ不具合の修正。
- `MultiplyMatcapIsOmittedWhileAddedMatcapStays`: 乗算のマットキャップが光った不具合の修正。マットキャップの色を空にすることと、加算のマットキャップの `_RimLightingMix` はここだけで確かめる。
- `ShadeColorMultipliesTheMainColorLikeLilToon`: 主色を暗くした材質の影が白っぽくなった不具合の修正。
- `SecondShadowColorIsMixedIntoTheShadeColor`: 2 影が抜けていた不具合の修正。

消したもの:

- `OptOutRetainsEmissionInBothMaterialRepresentations`: 主画像と同じ画像の発光を消す設定の既定値がオフになってからは、`DefaultExportKeeps…` と同じ入力・同じ確認になった。
- `GradatedEmissionUsesTheAverageOfTheGradation` の速さ -1 のケース: 速さ 1 と同じ分岐を通り、結果も同じ。
- `FluorescentEmissionUsesTheLitSceneStrength` の 0 と 0.5 のケース: 既定値（設定しない）と 1 の 2 つで、変わらない場合と発光が消える場合が揃う。0 は既定値と同じ値。

### ExportFallbackConfirmationTests（メソッド 15 → 14、ケース 17 → 15）

残したもの:

- `EveryShippedLocaleTranslatesTheResultWords`: 結果一覧の文言が 4 言語の訳にあることを確かめるのはここだけ（`Tools/validate.py` は失敗文と復旧の文だけを見る）。
- `BatchModeSavesWithoutTheList`（`BatchAndNonInteractiveCallersSaveWithoutTheList` から改名）: バッチ実行では一覧を出さずに保存する。
- `AnExportWithoutFallbacksNeverAsks`: 代わりに処理したものが無い書き出しで確認を出さない主経路。
- `TheListShowsEveryFallbackWithItsTargets`: 対象名を 2 件で切らないことと、既定の理由がエディターの言語で出ることを確かめるのはここだけ。
- `KeptItemsAreListedOnceWithoutAsking`: 対象名が重複した不具合の修正で足した。
- `TheAnswerDecidesBetweenSavingAndShowingWhatToChange`（2 ケース）: 保存する／設定を変更するの 2 つの答えの主経路。
- `NonInteractiveSavingKeepsTheEarlierBehaviour`: 確認を出さない呼び出しで、一覧を出さずに保存する主経路。
- `TargetsAreFoundAgainOnTheAvatarByPath`: 書き出し用のコピーにしか無い対象と、ルートの対象をアバターに戻すことを確かめるのはここだけ。
- `ARealExportReportsBlinkAndMaterialFallbacksOnce`: 実際の書き出しで記録される項目の主経路。
- `AMaterialWithAMissingShaderIsFoundOnceAndOtherSlotsAreIgnored`・`ARealExportWithAMissingShaderSavesAndWarnsOnce`・`TheSavedTitleWarnsOnlyWhenAShaderIsMissing`: シェーダーが見つからないのに成功と表示した不具合の修正。
- `AnExcludedRendererWithAMissingShaderIsNotCounted`・`TwoMaterialsWithTheSameNameCountAsTwo`: 書き出さないオブジェクトまで数えた不具合と、同じ名前の材質を 1 つと数えた不具合の修正。

消したもの:

- `AStageIsNamedWithItsPhase`: 段階の表示名の文字列を 2 つ確かめるだけ。訳があることは `EveryShippedLocaleTranslatesTheResultWords` が確かめる。
- `BatchAndNonInteractiveCallersSaveWithoutTheList` の `interactive: false` のケース: `NonInteractiveSavingKeepsTheEarlierBehaviour` が実際の保存まで通して同じことを確かめる。

### MatcapApproximationTests（メソッド 3 → 3、ケース 4 → 4）

すべて残した。

- `MaskLimitsTheAddedMatcapToTheMaskedArea`: マスクで一部だけに付けたマットキャップが服全体に掛かった不具合の修正。
- `MultiplyMatcapDarkensTheMainImageWhereMasked`: 同じ修正の乗算の経路。主画像に焼き込むのを確かめるのはここだけ。
- `BakesThatPassTheImageLimitAreLeftOut`（2 ケース）: 焼き込みで画像の数が上限を超え、lilToon の拡張が外れた不具合の修正。上限を超える 55 と超えない 4 で結果が分かれる。

## 件数

| ファイル | メソッド | ケース |
| --- | --- | --- |
| MaterialFallbackTests | 14 → 13 | 21 → 17 |
| ExportFallbackConfirmationTests | 15 → 14 | 17 → 15 |
| MatcapApproximationTests | 3 → 3 | 4 → 4 |
| 合計 | 32 → 30 | 42 → 36 |

## 検証

- `Tools/test-compatibility-tooling.py` が通った。
- EditMode テストは別の PC の Unity 2022.3 で回す。
