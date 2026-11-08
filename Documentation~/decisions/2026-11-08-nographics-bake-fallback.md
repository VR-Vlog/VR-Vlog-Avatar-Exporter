# GPU が無いときは発光とアルファマスクを焼き込まず、元の画像のまま書き出して警告に出す

- 日付: 2026-11-08
- 対象: `Editor/GpuReadback.cs`・`Editor/EmissionMaskBaker.cs`・`Editor/AlphaMaskBaker.cs`・`Editor/LilToonEmissionPolicy.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ShaderApprox/OrelsToonApproximation.cs`・`Editor/Locales/*.json`

## 背景

`-nographics` のように GPU が無い Editor では、描いて読み戻した画素が決まらない（白に近い値や 538 のような値が返る）。シェーダーの置き換え（`UnsupportedShaderSubstitution.Sampled`）は GPU が無いと null を返して焼かずに済ませていたが、次の 3 つは確かめずにその画素で焼いていた（#713 で見つけた）。

- `EmissionMaskBaker.Create`（発光のマスクとメインカラーの色付け）
- `AlphaMaskBaker.Bake`（アルファマスク）
- `LilToonEmissionPolicy.GradientFactor`（発光のグラデーション）

このため GPU の無い書き出しは、警告なしに発光やアルファの壊れた VRM になっていた。

orels Toon の発光は焼けないときに元の画像をそのまま使うが、第 1 版の画像を 2 回掛ける処理と、第 2 版のチャンネルと Power が、写せない設定の一覧に出ずに失われていた。

## 決定

- 読み戻せるかどうかの判定を `GpuReadback.Available` の 1 か所にまとめ、`Sampled` もこれを使う。
- 発光: GPU が無いときは焼かずに `_EmissionMap` をそのまま MToon に渡し、グラデーションは掛けない（白）。焼くはずだったマテリアルにだけ「GPUを使えないため、発光のマスク・色付け・グラデーションの焼き込みを省略しました。」の警告を出す。
- アルファマスク: GPU が無いときはマテリアルを変えずに（画像・マスク・色のアルファもそのまま）返し、「GPUを使えないため、アルファマスクの焼き込みを省略しました。」の警告を出す。
- orels Toon: 画像を焼けないときは、第 1 版は `Emission Map Counted Twice`、第 2 版は `Emission Map Channel`・`Emission Power`（両方なら `Emission Map Channel and Power`）を写せない設定として報告する。
- 警告の 2 文を 4 言語の表に足す。
- `EmissionMaskBakeTests` で `-nographics` のときに Ignore していた 12 件を、焼かずに元の画像のまま・警告に出ることを確かめる形に変える。

## 検討した代替案と不採用理由

- 書き出し全体で 1 回だけ「GPU が無い」と出す: 焼くものが無いアバターにも出てしまい、どのマテリアルが元と違うかも分からない。
- メインの画像のベイク（`LilToonMainTextureBaker`）とマットキャップの近似にも同じ判定を入れる: この 2 つは今回の項目の外で、`-nographics` で残っている失敗（FullTextureResize・Matcap など）と合わせて別に扱う。

## 確かめたこと

Unity 2022.3.22f1・lilToon 2.3.4 の Gamma と Linear のプロジェクトで実行した。

- `EmissionMaskBakeTests` と `OrelsToonApproximationTests` の 44 件: GPU ありで Gamma・Linear とも 44/44、`-nographics` の Linear で 44/44。
- 製品の変更だけを戻すと、`-nographics` で足したテストの 14 件が落ちる（グラデーションが 538 になる、焼いた画像ができる、報告が空など）。
- 発光・アルファ・シェーダーの置き換え・ロケールを使う 23 クラスの 720 件: GPU ありで Gamma・Linear とも 720/720。`-nographics` の失敗 40 件は develop と同じで、増えた失敗は無い。develop で Skipped だった 12 件は通るようになった。
