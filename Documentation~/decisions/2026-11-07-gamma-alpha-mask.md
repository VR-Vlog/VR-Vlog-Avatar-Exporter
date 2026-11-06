# アルファマスクの焼き込みで、Gamma のプロジェクトでも sRGB のマスクを Linear の値で読む

- 日付: 2026-11-07
- 対象: `Editor/AlphaMaskBaker.cs`・`Editor/AlphaMaskBaker.shader`・`Tests/Editor/EmissionMaskBakeTests.cs`

## 背景

lilToon のアルファマスク（`_AlphaMaskMode` が 0 以外の透過・カットアウト）は、書き出すときにマスクを主画像のアルファへ焼き込む（`AlphaMaskBaker`）。焼き込みのシェーダーはマスクの赤の値から透明度を作る。

Linear のプロジェクトでは、sRGB のマスクはサンプルするときに Linear の値へ直る。Gamma のプロジェクトでは直らないので、同じマスクから違う透明度ができていた。灰色（0.5）のマスクを `_AlphaMaskScale` 0.5・`_AlphaMaskValue` 0.1 で置き換えると、Linear では約 0.21、Gamma では約 0.35 になる。2026-11-05（`Sampled`）と 2026-11-06（マットキャップと発光のグラデーション）で、焼き込みは色空間によらず Linear の値で計算することにしたが、アルファマスクは残っていた（#690 のレビューで見つけた）。

`EmissionMaskBakeTests` のアルファマスクのテストは、マスクを Linear のデータとして作っていたので、この差は出ていなかった。

## 決定

- Gamma のプロジェクトで、マスクが sRGB の画像（`isDataSRGB`）のときだけ、焼き込みのシェーダーがマスクの値を `GammaToLinearSpaceExact` で Linear の値に直してから透明度を作る。
- 主画像の色はこれまでどおり手を加えずに書き戻す。Gamma では sRGB の値のまま読んでそのまま書き、Linear では GPU が読むときと書くときに直すので、どちらも元の画素と同じ値になる。
- Linear のプロジェクトと、Linear のデータとして読み込んだマスクの結果は変わらない。

## 確かめたこと

lilToon 2.3.4 と Exporter だけを入れた最小のプロジェクトを Gamma と Linear で作り、Unity 2022.3.22f1 の batchmode で `EmissionMaskBakeTests` を回した。sRGB と Linear のデータのマスクで同じ期待値になることを確かめるテスト（`AnAlphaMaskIsReadInLinearValues`）を足した。

- 修正前の Gamma: sRGB のマスクのケースが失敗した（期待 0.207 に対して 0.349）。
- 修正後: Gamma・Linear とも 12 件すべて成功した。

## 残したもの

2026-11-05 と 2026-11-06 の記録と同じく、GPU が補間したあとで Linear の値に直すので、値の違う画素どうしを補間するところ（縮小・境目）は Linear のプロジェクトと少し違う。

Linear のデータとして読み込んだ主画像の色は、Gamma と Linear で書き戻す値が違う（Linear では sRGB に直して書くので、アプリでは元と同じ Linear の値に見える。Gamma では値のまま書くので、アプリでは暗く見える）。主画像を Linear のデータとして読み込む作りはまれで、焼き込みを通らないときの扱いと合わせて決める必要があるので、この変更ではマスクだけを直した。
