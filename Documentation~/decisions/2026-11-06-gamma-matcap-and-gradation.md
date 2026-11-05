# マットキャップの焼き込みとグラデーションの読み取りを、Gamma のプロジェクトでも Linear の値で行う

- 日付: 2026-11-06
- 対象: `Editor/MatcapApproximation.cs`・`Editor/MatcapApproximation.shader`・`Editor/TextureColors.cs`

## 背景

2026-11-05 に置き換えの焼き込み（`UnsupportedShaderSubstitution.Sampled`）を Gamma のプロジェクトでも Linear の値で計算するようにしたとき、同じ問題が 2 か所に残っていた。

- `MatcapApproximation`（MToon で描けないマットキャップの乗算・通常の暗さを主画像に焼き、マスクを重みの画像に焼く）は、Gamma のプロジェクトでは画像も色も sRGB の値のまま計算し、結果を sRGB の画像に変換せずに書いていた。アプリは書き出した画像を sRGB として Linear の値に直して描くので、Gamma のプロジェクトから書き出すと Linear のプロジェクトより暗くなる。たとえば乗算のマットキャップで主画像を 0.5 にするところが、Gamma では約 0.22 になっていた。
- `TextureColors.ReadLinear`（発光のグラデーションの画像を読む）は、Gamma のときに画像が sRGB かどうかを見ずに全部 `Color.linear` で直していた。sRGB でない（Linear のデータとして読み込んだ）画像は 2 回直すことになり、0.5 が約 0.22 になる。

Gamma のプロジェクトで失敗するテストが無かったのは、テストが `TestTextures.Linear`（Gamma では読んだ値をそのまま返す）で比べていて、Gamma の書き出しがアプリで暗く出ることを見ていなかったためである。

## 決定

- `MatcapApproximation` は、色空間によらず Linear の値で計算する。Gamma のプロジェクトでは、焼き込みのシェーダーが sRGB の主画像とマスクを Linear の値に直し、結果を sRGB に直して書く。マットキャップの平均も、sRGB の画像なら読んだあとで Linear の値に直す。色は `Color.linear` で渡す。Linear のプロジェクトでは今までと同じで、GPU が直す。
- `TextureColors.ReadLinear` は、Gamma のプロジェクトかつ sRGB の画像（`isDataSRGB`）のときだけ直す。`Sampled` と同じ条件にした。
- テストは、焼いた画像を `Color.linear` で読み（アプリと同じ読み方）、Linear の値で計算した期待値と比べる。sRGB の中間の色の画像で、Gamma と Linear の両方で同じ値になることを確かめるテストを足した。

## 確かめたこと

lilToon 2.3.4 と Exporter だけを入れた最小のプロジェクトを Gamma と Linear の 2 つ作り、`MatcapApproximationTests`・`EmissionMaskBakeTests` などマットキャップと発光を扱う 7 クラスを回した。

- Gamma: 修正前は 5 件失敗（足した 4 件と、乗算のマットキャップの既存のテストを `Color.linear` で読むようにした 1 件）。修正後は 329 件中 0 件失敗。
- Linear: 修正の前後とも 0 件失敗。

## 残したもの

2026-11-05 の判断の記録と同じく、GPU が補間したあとで Linear の値に直すので、色の違う画素どうしを補間するところは Linear のプロジェクトと少し違う。

マットキャップの平均は画像を 32x32 の点で読むので、どの点も元の画素の間を補間した値になる。隣り合う画素の色がほとんど同じ普通のマットキャップでは差は無視できるが、境目のくっきりした画像（白と黒が接するものなど）では平均もずれ、乗算の焼き込み全体に効く。Codex のレビューではこれを差し戻しの理由に挙げられたが、修正前の Gamma は全部の画素が sRGB の値のままで、これよりずれが大きかったので、この PR では入れていない。直すなら、元の解像度で読み、Linear の値に直してから CPU で平均する。
