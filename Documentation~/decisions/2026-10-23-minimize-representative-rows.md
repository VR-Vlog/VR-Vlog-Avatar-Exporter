# 書き出し・材質・メッシュ系のテストの TestCase を代表値に絞る

- 日付: 2026-10-23
- 対象: `Tests/Editor` の書き出し・材質・メッシュ・ポーズ系のテスト 27 ファイル、`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`・`INTEGRATION_CASES`・`compatibility_counts`、`Tools/test-compatibility-tooling.py` で件数を固定している値

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。`2026-10-23-minimize-repeated-testcases.md` で表情・ニュートラル系を絞ったのに続き、書き出し・材質・メッシュ系でも同じ判定を値だけ変えて通す `[TestCase]` を減らす。

行を残す基準は前回と同じにした。期待する結果・分岐ごとに 1 行、閾値や上限を確かめる処理では境界の行、入力の形が他の行と違う行を残す。真偽の引数で経路が変わるものは、ファイルのどこかに true と false の行が少なくとも 1 つずつ残るようにした。2 つの真偽の全組み合わせ（4 行）は、各因子が両方の値を取る対角の 2 行（true/false と false/true）に絞った。このリポジトリでは大半の行が不具合の修正と一緒に足されている（`git log -S` で確かめた）ため、修正と一緒に来たかどうかより入力の形が他と違うかで判断した。メソッドは 1 本も消していない。

## 決定

- `SelectedExpressionAppearanceTests`: 材質が無い場合、スカラーと有効フラグの変化、`_MainTex_ScrollRotate` の行を外した。PropertyBlock は（無効, 色）と（有効, ベクトル）、全スロットは同じ/無い/違うの 3 行、MaterialColor は通常・HDR の 2 行・Gamma、lilToon の混在は対角の 2 行にした。
  - 主経路で代わりに守れるか: 色・ベクトル・HDR・Gamma の各経路に 1 行以上残り、スカラーは色と同じ比較を通る。
- `FacialProjectionTests`: Trigger、拡大率 1、LipSync の Default と VisemeParameterOnly、tangent の行を外した。
  - 主経路で代わりに守れるか: 拡大率と LipSync は残した行が同じ判定を通る。
- `VrChatMenuExpressionTests`: 重み付きの末尾、MotionTime、パラメーターのカーブ、終端の上書き（true）、2 メソッドの tangent の行を外した。
  - 主経路で代わりに守れるか: 各メソッドに期待する結果ごとの行が残る。
- `HumanoidPoseTests`: Jaw、マスクの（false,false）と（true,true）、材質、名前の変更・Float の Equals・Int の If を外した。
  - 主経路で代わりに守れるか: マスクは対角の 2 行で両方の値が残り、条件の比較は他の演算子の行が同じ処理を通る。
- `LilToonFullExportTests`: 非有限の HDR は half と float それぞれ無限大と NaN を 1 行ずつにした。法線の格納形式は DXT5・EAC・R16_SNorm の行を外した。
  - 主経路で代わりに守れるか: 圧縮形式の判定は `NormalStorageTests` と残した形式の行が同じ分岐を通る。
- `FacialDepthEnvelopeTests`: 回転と拡大率の組を 2 メソッドとも（0, 0.01）と（90, 100）の 2 行にし、元の余白の内側と特異なバインドポーズの行を外した。
  - 主経路で代わりに守れるか: 両端の組が残るので回転・拡大率の扱いはそこで守れる。
- `ExportOptimizationBindingsTests`: 0.75 の行と（0.2, 0.8, true）、材質の動きを 3 行、AAO の 8 行を 4 行にした。
  - 主経路で代わりに守れるか: 残した行で色・発光・UV と真偽の両方の値が揃う。
- `PngWriterTests`: 寸法・形式の行を 16 → 9 行、チャンク検証を 3 → 2 行にした。
  - 主経路で代わりに守れるか: 色の形式ごとに 1 行と 1x1 の寸法、linear の true/false が残る。並列の 3 行は寸法の形が違うので残した。
- `NeutralParameterCurveTests`・`NeutralShapeEndpointTests`・`NormalStorageTests`: 同じ有限値の判定を値だけ変えて通す行（0.25・0.5・9.6・正の無限大など）を外した。
  - 主経路で代わりに守れるか: NaN・負・0・境界の行が同じ判定を通る。
- `AppearanceRecoveryTests`: 空のメッシュと使用中・消費済みは対角の 2 行、対応しない画像は lilToon と Multi のテクスチャ枠を 4 行にした。
  - 主経路で代わりに守れるか: 画像の判定はシェーダーと枠の種類ごとに 1 行あれば足りる。
- `PhysBoneSpringExportTests`: （true,false,false）、入れ子の true、AAO の false を外した。
  - 主経路で代わりに守れるか: 入れ子と AAO の反対側の値は同じファイルの別メソッドに残る。
- `GestureBlendTreeTests`: cycle と mirror の行を外した。
  - 主経路で代わりに守れるか: 他の BlendTree の行が同じ変換を通る。
- `BlinkExportTests`: `eye_close_left`・古い目の `eye_blink_2`・`Blink_R` を外し、2 つの真偽の組を対角の 2 行にした。
  - 主経路で代わりに守れるか: 左右と名前の形ごとに 1 行残る（`BlinkLeft` は残した）。
- `MissingMeshAttributesTests`: 直接の UniVRM 書き出しと往復は 3〜4 因子の組を 4 行、頂点の追加は対角の 2 行、UV0・法線・縮退は片方の値だけにした。
  - 主経路で代わりに守れるか: 残した組で各因子が true と false を両方取る。
- `FxDiscoveryPruningTests`: 14 → 8 行（none と Animator・Set・Add・Copy・Random・callback・conflict を 1 行ずつ）。
  - 主経路で代わりに守れるか: 操作の種類ごとに 1 行残る。
- `FullTextureResizeTests`: （true, 4096, 8）を外した。
  - 主経路で代わりに守れるか: 上限 4096 の境界は他の行に残る。
- `MaReactiveExpressionExportTests`・`AvatarOptimizerBoxIntegrationTests`・`PreparedNeutralExportTests`: 3 因子の組 8 行を各因子が両方の値を取る 4 行に、6 行を 3 行に、2 因子は対角の 2 行にした。
  - 主経路で代わりに守れるか: 各因子の真偽が残した組で揃う。
- `SelectedLayerControlTests`: 即時の切り替えを 4 行、DirectClip を 5 行にし、Gesture の行を外した。
  - 主経路で代わりに守れるか: 0・0.5・1 の端と中間、無効の行が残る。
- `MenuImportRecoveryTests`: 件数 1 と 300、TwoAxisPuppet と Button の行を外した。
  - 主経路で代わりに守れるか: 件数の境界と他の操作の種類が同じ判定を通る。
- `SkinnedMeshFallbackWeightTests`・`ExcludedLayerTests`・`NdmfLostMorphFollowTests`: 2 つの真偽の組を対角の 2 行にした。
  - 主経路で代わりに守れるか: 各因子の両方の値が残る。
- `LilToonFullRenderTests`: 第 3 メイン、第 2 発光、第 2 MatCap、ファーの（2, 0.5）を外した。
  - 主経路で代わりに守れるか: 第 2 メイン・第 1 発光・第 1 MatCap とファーの他の行が同じ描画の比較を通る。

## 消さなかったもの

- `UnifiedExpressionMergedRendererTests`・`InvariantMorphOwnershipTests`・`LilToonStructureCheckTests`・`ExportVrmLilToonPreviewTests`・`TextureResizeTests` は、行ごとに期待する結果か入力の形が違うので触らなかった。
- 閾値・上限の境界の行、言語ごとの行、入力の形が他と違う行。
- 規約・同意・ライセンス、データの消失・ファイルの安全を確かめるテスト（`AvatarLicenseTests`・`ExportAuthorTests` など）。

## 件数

| ファイル | メソッド | ケース |
| --- | --- | --- |
| SelectedExpressionAppearanceTests | 17 → 17 | 62 → 47 |
| FacialProjectionTests | 29 → 29 | 64 → 58 |
| VrChatMenuExpressionTests | 39 → 39 | 78 → 72 |
| HumanoidPoseTests | 18 → 18 | 42 → 35 |
| LilToonFullExportTests | 21 → 21 | 52 → 44 |
| FacialDepthEnvelopeTests | 7 → 7 | 30 → 20 |
| ExportOptimizationBindingsTests | 24 → 24 | 43 → 33 |
| PngWriterTests | 8 → 8 | 28 → 19 |
| NeutralParameterCurveTests | 11 → 11 | 27 → 22 |
| AppearanceRecoveryTests | 9 → 9 | 33 → 17 |
| PhysBoneSpringExportTests | 30 → 30 | 43 → 40 |
| GestureBlendTreeTests | 21 → 21 | 35 → 33 |
| BlinkExportTests | 19 → 19 | 43 → 36 |
| NormalStorageTests | 9 → 9 | 21 → 16 |
| NeutralShapeEndpointTests | 13 → 13 | 23 → 21 |
| MissingMeshAttributesTests | 10 → 10 | 39 → 22 |
| FxDiscoveryPruningTests | 3 → 3 | 17 → 11 |
| FullTextureResizeTests | 11 → 11 | 19 → 18 |
| MaReactiveExpressionExportTests | 4 → 4 | 15 → 9 |
| AvatarOptimizerBoxIntegrationTests | 4 → 4 | 15 → 9 |
| SelectedLayerControlTests | 17 → 17 | 41 → 33 |
| PreparedNeutralExportTests | 5 → 5 | 13 → 9 |
| MenuImportRecoveryTests | 15 → 15 | 23 → 19 |
| SkinnedMeshFallbackWeightTests | 7 → 7 | 12 → 10 |
| ExcludedLayerTests | 6 → 6 | 11 → 9 |
| NdmfLostMorphFollowTests | 5 → 5 | 10 → 8 |
| LilToonFullRenderTests | 6 → 6 | 24 → 20 |
| 触らなかった 5 ファイル | 33 → 33 | 62 → 62 |
| 合計 | 401 → 401 | 925 → 752 |
