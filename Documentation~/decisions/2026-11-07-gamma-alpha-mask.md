# アルファマスクの焼き込みで、Gamma のプロジェクトでも sRGB のマスクを Linear の値で読む

- 日付: 2026-11-07
- 対象: `Editor/AlphaMaskBaker.cs`・`Editor/AlphaMaskBaker.shader`・`Tests/Editor/EmissionMaskBakeTests.cs`

## 背景

lilToon のアルファマスク（`_AlphaMaskMode` が 0 以外の透過・カットアウト）は、書き出すときにマスクを主画像のアルファへ焼き込む（`AlphaMaskBaker`）。焼き込みのシェーダーはマスクの赤の値から透明度を作る。

Linear のプロジェクトでは、sRGB のマスクはサンプルするときに Linear の値へ直る。Gamma のプロジェクトでは直らないので、同じマスクから違う透明度ができていた。灰色（0.5）のマスクを `_AlphaMaskScale` 0.5・`_AlphaMaskValue` 0.1 で置き換えると、Linear では約 0.21、Gamma では約 0.35 になる。2026-11-05（`Sampled`）と 2026-11-06（マットキャップと発光のグラデーション）で、焼き込みは色空間によらず Linear の値で計算することにしたが、アルファマスクは残っていた（#690 のレビューで見つけた）。

`EmissionMaskBakeTests` のアルファマスクのテストは、マスクを Linear のデータとして作っていたので、この差は出ていなかった。

## 決定

- Gamma のプロジェクトで、マスクが sRGB の画像（`isDataSRGB`）のときだけ、先にマスクと同じ大きさの Linear の一時画像（RHalf。使えなければ ARGBHalf）へ、画素ごとに `GammaToLinearSpaceExact` で直して描く。焼き込みはこの一時画像を読むので、主画像の大きさへ広げるときの補間も Linear の値どうしで起き、Linear のプロジェクトと同じになる。
- 焼き込みのシェーダーの中で、補間した後の値を直す形は採らなかった。白黒のマスクを広げた境目では、補間した後に直すと Linear のプロジェクトより暗い側へずれ、カットアウトの輪郭が変わる（直す前の Gamma は、白黒のマスクでは Linear と同じだった）。
- 主画像の色はこれまでどおり手を加えずに書き戻す。主画像と同じ大きさで焼くときは、Gamma では sRGB の値のまま読んでそのまま書き、Linear では GPU が読むときと書くときに直すので、どちらも元の画素と同じ値になる。
- Linear のプロジェクトと、Linear のデータとして読み込んだマスクの結果は変わらない。

## 確かめたこと

lilToon 2.3.4 と Exporter だけを入れた最小のプロジェクトを Gamma と Linear で作り、Unity 2022.3.22f1 の batchmode で `EmissionMaskBakeTests` を回した。灰色のマスクの透明度が Linear の値で計算した値になることを確かめるテスト（`AnAlphaMaskIsReadInLinearValues`）と、白黒の 2 画素のマスクを 8 画素の主画像へ広げたとき、sRGB のマスクの透明度が Linear のデータのマスクと画素ごとに同じになることを確かめるテスト（`AnSrgbMaskEdgeIsBlendedInLinearValues`）を足した。

- 修正前の Gamma: sRGB のマスクのケースが失敗した（期待 0.207 に対して 0.349）。
- 補間の後で直す最初の版の Gamma: 白黒の境目のテストが失敗した（境目の画素が 0.125 に対して 0.016）。
- 修正後: `EmissionMaskBakeTests` と `LayerAlphaTests` の 27 件が Gamma・Linear ともすべて成功した。

## 残したもの

マスクの縮小（タイリングを 2 以上にしたとき）は、一時画像から作り直したミップマップを読むので、元の画像のミップマップを読む Linear のプロジェクトと少し違うことがある。

主画像より大きいマスクに合わせて主画像を広げるときは、主画像の色を Gamma では sRGB の値のまま、Linear では Linear の値で補間するので、広げた画素の色が少し違う。これは前からの違いで、この変更では変えていない。

Linear のデータとして読み込んだ主画像の色は、Gamma と Linear で書き戻す値が違う（Linear では sRGB に直して書くので、アプリでは元と同じ Linear の値に見える。Gamma では値のまま書くので、アプリでは暗く見える）。主画像を Linear のデータとして読み込む作りはまれで、焼き込みを通らないときの扱いと合わせて決める必要があるので、この変更ではマスクだけを直した。
