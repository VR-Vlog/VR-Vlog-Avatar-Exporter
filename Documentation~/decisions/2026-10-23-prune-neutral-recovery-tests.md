# 中立の顔・書き出しの復旧・元データの指紋のテストから重複を外す

- 日付: 2026-10-23
- 対象: `Tests/Editor/DirectExpressionNativeLayersTests.cs`・`Tests/Editor/ExportRecoveryTests.cs`・`Tests/Editor/ExportSourceFingerprintSerializationTests.cs`・`Tests/Editor/Fixtures/BaseShapeFixture.cs`・`Tests/Editor/MergedFixedNeutralSamplingTests.cs`・`Tests/Editor/NeutralCurveConditionTests.cs`・`Tests/Editor/NeutralShapeExportTests.cs`・`Tests/Editor/NeutralShapeSamplerTests.cs`・`Tests/Editor/ParameterDriverExpressionTests.cs`・`Tests/Editor/SourceFingerprintTests.cs`・`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`・`Tools/test-compatibility-tooling.py`

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。残すのは、主経路を通す唯一のテスト、不具合の修正と一緒に足したテスト（同じ条件を確かめるテストがほかに無いとき）、ある分岐や断る条件に届く唯一のテストとする。外すのは、まったく同じテスト、残すテストと同じ経路で同じことを確かめるテスト、ほかのケースと同じ分岐を通る `TestCase` の行とする。行は分岐ごとに 1 つと、コードが比べる境界の値を残す。迷ったものは残した。

各テストを足したコミットは `git log -S<テスト名>` で調べた。消す前に、同じ確認がほかのテストにあるかを `Tests/Editor` で探した。10/23 に戻した `ParameterDriverExpressionTests` の Eye Height・Station・Contact のテストと `FxOffSelectionIsRejected…`・`NonUnitFxCommandCannotBeCancelled…` は対象から外した。

## 決定

### ExportRecoveryTests（メソッド 22 → 22、ケース 31 → 27）

残したもの:

- `SourceStampRejectsChangesToSourceAndRelatedAssets` の hierarchy・component・animation: 子の追加・コンポーネントの無効化・アニメーションの変更で記録が古くなることを、書き出しの復旧の画面の経路で確かめるのはここだけ。
- `ParentChangesInvalidateTheCapturedSourceOnlyWhenTheyReachTheFile` の active: 親を非表示にすると記録が古くなることを確かめるのはここだけ。メソッドの引数はそのままにした。
- `OnlyAnIdentifiedOptionalLayerGetsALayerRecipe`（2 ケース）: 復旧の案内の段階・理由・直し方が空でないことと、操作の種類を確かめるのはここだけ。迷ったので残した。
- そのほかの 19 メソッド: それぞれ別の失敗の診断、復旧の操作、プレビュー用のコピーの扱いを確かめ、同じ確認がほかに無い。

消したもの:

- `SourceStampRejectsChangesToSourceAndRelatedAssets` の material: `SourceFingerprintPruneTests.ChangesThatReachTheFileStillStopTheBoard` の material が、同じ `ExportRecoverySourceStamp.Matches` で材質の色の変更を確かめる。表情ボードと書き出しの復旧は同じ記録を使う。
- `ParentChangesInvalidateTheCapturedSourceOnlyWhenTheyReachTheFile` の position・rotation・scale: `SourceFingerprintPruneTests.MovingTheRootInTheSceneKeepsTheBoardCurrentButItsScaleDoesNot` が、ルートの位置・向き・親の追加では記録が古くならず、親の拡大縮小では古くなることを同じ記録で確かめる。

### ExportSourceFingerprintSerializationTests（メソッド 9 → 9、ケース 55 → 50）

残したもの:

- `UnsavedPayloadValuesInvalidateWithoutMutatingTheSource` の signed64・unsigned64・doubleUlp・doubleSignedZero・enum・arraySize とそのほかの型: 型ごとに指紋の処理が分かれる。整数は 64 ビットの両端、実数は最小の差と符号付きのゼロを残した。arraySize は配列の長さの区切りを確かめる。
- `LongUnicodeTextIsStableAcrossEncoderChunks…` の 1023・8191: 1023 は 1024 文字ごとに区切って変換する境界、8191 は 8192 バイトのバッファの境界に当たる。
- グラデーションとカーブの各ケース: 項目ごとに別の値を指紋に足していて、1 つ外すとその項目の変更を確かめるテストが無くなる。
- `CyclicAndSharedManagedGraphs…` と `ExposedReference…` の各ケース: 参照が空のときとあるときで別の分岐を通る。
- このファイルのテストは、顔のトラッキングでメモリーが足りなくなった不具合の修正（指紋を逐次で求める変更）で足した。

消したもの:

- int: signed64・unsigned64 と同じ整数の処理（`longValue`）を通る。
- float: doubleUlp と同じ実数の処理（`doubleValue`）を通る。
- char・layerMask: enum と同じ処理（`intValue`）を通る。
- `LongUnicodeTextIsStableAcrossEncoderChunks…` の 2047: 1023 と同じ 1024 文字の区切りの境界を確かめていた。

### SourceFingerprintTests（メソッド 13 → 13、ケース 62 → 57）

残したもの:

- `UnsavedMeshInputsInvalidateTheStamp` の vertices・uv0・uv7・layout・indices・submesh・bounds・bindpose・boneIndex・boneWeight・morphVertex・morphTangent・frameWeight・shapeName・frameCount: 頂点の値、頂点の属性の並び（uv7 で属性が増える、layout で次元が変わる）、インデックス、サブメッシュ、ボーン、ブレンドシェイプの項目ごとに別の値を指紋に足す。
- `CustomRenderTextureAuthoredSettings…` の initMaterialColor 以外: 設定と参照ごとに別の値を確かめる。
- `StaticTextureContentUpdatesInvalidateTheStamp`（3 ケース）: 迷ったので残した。
- `EffectiveSkinnedBoundsAndTheirAuthoredInputsRemainGuarded`（4 ケース）: 中心と大きさのケースは迷ったので残した。
- そのほかのメソッド: 多くはメモリーの修正・遅延で求める境界の修正・カメラ映像の修正・Custom Render Texture の修正で足したもので、どれも同じ条件を確かめるテストがほかに無い。

消したもの:

- `UnsavedMeshInputsInvalidateTheStamp` の normals・tangents・colors: 頂点の属性はまとめて生のバイトで指紋に足すので、vertices・uv0 と同じ処理を通る。
- `UnsavedMeshInputsInvalidateTheStamp` の morphNormal: `HundredMorphsAtHundredThousandVerticesRemainCorrectAndReportNativeTiming` が同じ `SetShapes(…, "morphNormal")` の変更で記録が古くなることを確かめる。
- `CustomRenderTextureAuthoredSettings…` の initMaterialColor: テストの材質と初期化の材質は同じものなので、materialColor と同じ材質の値の変更を確かめていた。

### NeutralShapeExportTests（メソッド 1 → 1、ケース 6 → 3）

残したもの:

- `PreparedRestSurvivesExportWithoutRestoringInitialFx` の lilToon の拡張を付けない 3 ケース: 開いた口のモーフ・独立したまばたき・閉じたモーフを戻す、の 3 つで中立の顔の求め方が分かれる。

消したもの:

- lilToon の拡張を付ける 3 ケース: `exporterVersion`・`lilToonVersion` は材質の書き出し（lilToon の拡張を足す段階）だけを変え、中立の顔とモーフは同じ処理を通る。拡張を付けないケースが同じ組み合わせを確かめる。`2026-10-23-minimize-repeated-testcases.md` で MToon と lilToon を同じ経路とみなしたのと同じ判断。

### ParameterDriverExpressionTests（メソッド 17 → 17、ケース 34 → 32）

残したもの:

- `BodyPlayableControlAndTrackedHandDoNotBlockFixedFacialSampler` の Action・Gesture・Additive: 許すレイヤーの値ごとに 1 ケース。
- `MalformedBodyPlayableControlIsNotIgnored`（7 ケース）・`MalformedTemporaryPoseSpaceIsNotIgnored`（2 ケース）: 重みと時間の範囲外・無限・NaN のそれぞれで断る条件が分かれ、不正な値を見逃した不具合の修正で足した。
- 10/23 に戻した Eye Height・Station・Contact のテストと `FxOffSelectionIsRejected…`・`NonUnitFxCommandCannotBeCancelled…`。
- そのほかのメソッド: それぞれ別の判定を確かめ、同じ確認がほかに無い。

消したもの:

- `LocomotionControlDoesNotBlockFixedFacialSampling` の (true, true): `IsLocomotionControl` は種類と項目があるかだけを見て値を見ない。ほかのコントローラーに置いても、直接呼ぶ判定は変わらない。(false, false) が同じ判定を確かめる。
- `TemporaryPoseSpaceDoesNotBlockFixedFacialSampling` の (true, true): `IsTemporaryPoseSpace` は遅延の時間だけを見て、入る・抜ける、固定の遅延の値を見ない。(false, false) が同じ判定を確かめる。

### DirectExpressionNativeLayersTests（メソッド 11 → 11、ケース 21 → 21）

すべて残した。各メソッドは、直接の入口を書き換える前に断る条件か、書き換えたあとの値を別々に確かめる。`InvalidSelectedStateProvenance…`（3 ケース）と `LowerPoseDependencies…`（4 ケース）は行ごとに断る理由が違う。`DroppedSelectedDrivers…` の machine は nested machine と同じ再帰の処理を通るが、迷ったので残した。

### MergedFixedNeutralSamplingTests（メソッド 4 → 4、ケース 11 → 11）

すべて残した。行ごとに読む値かモーフが違い、`2026-10-23-minimize-repeated-testcases.md` でも残している。

### NeutralCurveConditionTests（メソッド 4 → 4、ケース 5 → 5）

すべて残した。時間で抜ける中継の証明、証明できないとき、競合する Parameter Driver、境界の近くの重みは、それぞれ別の条件で、ほかに確かめるテストが無い。`NativeConstantParameterRelay…` の 2 ケースは Write Defaults のレイヤーの有無で処理が分かれる。

### NeutralShapeSamplerTests（メソッド 67 → 67、ケース 140 → 140）

すべて残した。行は `2026-10-23-minimize-repeated-testcases.md` で減らしてあり、残りのメソッドに同じ入力と同じ確認の組は無かった。`ConstraintEnabledSupportKeepsPreparedRestWithoutExecutingConstraint` の 0 と 1 は迷ったので残した。

### Fixtures/BaseShapeFixture.cs

変えていない。今回外したケースだけで使う補助は無かった。

## 件数

| ファイル | メソッド | ケース |
| --- | --- | --- |
| DirectExpressionNativeLayersTests | 11 → 11 | 21 → 21 |
| ExportRecoveryTests | 22 → 22 | 31 → 27 |
| ExportSourceFingerprintSerializationTests | 9 → 9 | 55 → 50 |
| MergedFixedNeutralSamplingTests | 4 → 4 | 11 → 11 |
| NeutralCurveConditionTests | 4 → 4 | 5 → 5 |
| NeutralShapeExportTests | 1 → 1 | 6 → 3 |
| NeutralShapeSamplerTests | 67 → 67 | 140 → 140 |
| ParameterDriverExpressionTests | 17 → 17 | 34 → 32 |
| SourceFingerprintTests | 13 → 13 | 62 → 57 |
| 合計 | 148 → 148 | 365 → 346 |

## 検証

- `Tools/test-compatibility-tooling.py` が通った。
- EditMode テストは別の PC の Unity 2022.3 で回す。
