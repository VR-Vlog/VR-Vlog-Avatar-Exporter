# 乗算のシェーダーの材質を、lilToon の半透明で乗算になるブレンドに置き換える

- 日付: 2026-10-31
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/MultiplyProbe.shader`

## 背景

頬の赤みや影を、下の色に掛け合わせる乗算のパーティクル用シェーダー（`Blend Zero SrcColor`）で描くアバターがある。非対応シェーダーの置き換え（`2026-10-18-unsupported-shader-fallback.md`）は、こうした材質を lilToon の半透明の既定のブレンド（`One` / `OneMinusSrcAlpha`）にしていた。乗算用の画像は、効かせない部分が白で透明度が低い作りなので、普通のアルファ合成では白に近い色がうっすら重なるだけになり、アプリで頬の赤みがほとんど見えなかった。

また、古い書き方のシェーダーは `Blend` や `ZWrite` を SubShader の外の `Category` に書く。深度の書き込みの判定は SubShader の中しか読んでいなかったので、`Category` の `ZWrite Off` を読めず、深度を書く設定にしていた。

## 決定

- 色を描くパスが 1 つで、そのパスが乗算のブレンド（`Blend Zero SrcColor` または `Blend DstColor Zero`）を持つ材質を、乗算の材質として扱う。乗算のパスが 2 つ以上あると下の色を重ねて暗くするので、lilToon の 1 回の乗算では再現できない。こうした材質は今までどおりの置き換えにする。ブレンドはパス、SubShader、それを囲む `Category` の順に近いものを使う。`Blend [_SrcBlend] [_DstBlend]` のようにプロパティで書いたものは材質の値で読む。サーフェスシェーダーは対象にしない。
- シェーダーのファイルがない組み込みのシェーダーは、名前が `Particles/Multiply` で終わるものを乗算とする。シェーダーが見つからない材質は、保存されている `_SrcBlend` と `_DstBlend` で判定する。
- 乗算の材質は `Hidden/lilToonTransparent` にし、`_SrcBlend` を `DstColor`、`_DstBlend` を `OneMinusSrcAlpha` にする。lilToon の半透明は色に透明度を掛けて出す（premultiplied）ので、結果は `下の色 × lerp(1, 色, 透明度)` になり、乗算のシェーダーが白へ透明度で戻す描き方と一致する。
- 乗算のシェーダーはライトを受けないので `_AsUnlit` を 1 にする。ライトごとの加算パス（ForwardAdd）は `_SrcBlendFA` を `Zero`、`_DstBlendFA` を `One` にして何も足さない（lilToon の既定の `BlendOpFA` は `Max`）。
- `Category` に書いたコマンドは、そのなかの SubShader のコマンドとして読む。SubShader に同じコマンドがあればそちらを使う。深度の書き込み・色を描かない判定もこの規則で読む。
- アプリ（VR Vlog）は lilToon の `_SrcBlend` / `_DstBlend` / `_SrcBlendFA` / `_DstBlendFA` をそのまま使うので、アプリ側の変更はいらない。

## 確認したこと

- EditMode テストで、`Category` に `Blend Zero SrcColor` と `ZWrite Off` を書いたテスト用シェーダー、組み込みの `Legacy Shaders/Particles/Multiply`、プロパティで書いたブレンド、乗算でないブレンド（`DstColor SrcColor`・アルファ合成・パスごとに違うもの・乗算のパスが 2 つあるもの・SubShader が `Category` を上書きするもの）を確かめた。普通の半透明の材質は lilToon の既定のブレンドのまま。
- 乗算のパーティクル用シェーダーで頬の赤みを描くアバターを書き出し、Unity とアプリの実機で頬の赤みが元と同じ濃さで出ることを比べた。比較画像はリポジトリには入れていない。

## 分かっている制限

- 透明度で白へ戻さない乗算のシェーダー（出力色をそのまま掛ける）では、画像の透明度が 1 未満の部分が元より薄くなる。それでも結果は `下の色 × (1 - 透明度 + 色 × 透明度)` で、これまでのアルファ合成（`色 × 透明度 + 下の色 × (1 - 透明度)`）より必ず元に近い（暗い）。
- ライトの影響を受ける乗算のシェーダーも、ライトを受けない乗算にする。乗算で描く頬や影は、元がライトを受けないパーティクル用シェーダーのものがほとんどなので、こちらに合わせた。
- 描画先ごとのブレンド（`Blend 0 Zero SrcColor`）は乗算と判定せず、今までどおりの置き換えにする。

## 検討した代替案と不採用理由

- 乗算の色を主画像に焼き込んで普通の半透明にする: 下の色によって結果が変わる乗算は、画像だけでは再現できない。
- `_SrcBlend` を `DstColor`、`_DstBlend` を `Zero` にする: lilToon は色に透明度を掛けて出すので、透明な部分が黒くなる。
