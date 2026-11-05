# 置き換えの焼き込みで読む画像の色を、Gamma のプロジェクトでも Linear の値に直す

- 日付: 2026-11-05
- 対象: `Editor/UnsupportedShaderSubstitution.cs`

## 背景

色空間が Gamma のプロジェクトで EditMode を回すと、`SectionedToonSubstitutionTests.APlacedDarkenDecalIsBakedOnItsAreaOnly` が失敗していた。develop に UTS と UnlitWF の近似が入ってからは、`UnlitWFApproximationTests.AShadeImageIsMultipliedIntoTheMainImage` と `UtsApproximationTests.AShadeImageWithAlphaIsRedrawnOpaque` も Gamma で失敗する。どれも Linear のプロジェクトでは通る。

lilToon 以外のシェーダーのマテリアルを置き換えるとき、デカール・輪郭線の色・色マスク・ランプなどは、画像を `UnsupportedShaderSubstitution.Sampled` で GPU から読んで計算し、焼き込む。`Sampled` は `GpuReadback.LinearPixels` の値をそのまま返していた。Linear のプロジェクトでは、sRGB の画像はサンプルするときに Linear の値へ直るが、Gamma のプロジェクトでは直らない。そのため Gamma では、sRGB の値を Linear の値として混ぜていて、焼き込んだ色が Linear のプロジェクトと違っていた。

lilToon のマテリアルを読む `TextureColors.ReadLinear` は、Gamma のときに読んだ値を Linear の値へ直している。置き換えの `Sampled` にはこの変換が無かった。

## 決定

`Sampled` で、Gamma のプロジェクトかつ sRGB の画像（`isDataSRGB`）のときは、読んだ値を `Color.linear` で Linear の値に直す。中央の行を読む `ReadRow` も `Sampled` を通すようにし、ランプも同じにした。

- テストではなく書き出し側の問題なので、テストの期待する値は変えていない。
- Linear のプロジェクトと、sRGB でない画像（法線マップ・マスクとして読み込んだ画像）の値は変わらない。
- `Sampled` は UTS・Sunao・UnlitWF の近似からも呼ばれるので、これらも Gamma で同じに直る。

## 残したもの

`MatcapApproximation` も `GpuReadback.LinearPixels` で直接読んでいて、Gamma のときに直していない。置き換えとは別の経路で、Gamma で失敗するテストも無いので、この変更には入れていない（2026-11-06-gamma-matcap-and-gradation.md で直した）。

GPU が補間したあとで Linear の値に直すので、Gamma のプロジェクトでは、色の違う画素どうしを補間するところ（縮小・境目・UV のずらし）が Linear のプロジェクトと少し違う。黒と白の間の 1 画素なら、Linear では 0.5、この方法では約 0.21 になる。補間の前に直すには元の解像度で読んで CPU で縮小し直す必要があり、作り直しが大きいので入れていない。`TextureColors.ReadLinear` も同じ順序で直している。前は全部の画素がずれていたが、残るのはこの補間するところだけになる。
