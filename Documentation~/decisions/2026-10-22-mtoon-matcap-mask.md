# 標準MToonのMatCapへマスク・主色・合成方法を焼き込む

- 日付: 2026-10-22
- 対象: `Editor/MatcapApproximation.cs`・`Editor/MatcapApproximation.shader`・`Editor/UniVrmOneClickExporter.cs`

## 背景

標準MToonへの代替出力は、MatCapを `MatcapTexture = _MatCapTex`・`MatcapColorFactor = _MatCapColor × _MatCapBlend` だけで書き出し、乗算（`_MatCapBlendMode` 3）は省いていた。lilToon 2.3.4のMatCap（`lil_common_frag.hlsl` の `lilGetMatCap`）は、ここへ `_MatCapBlendMask` を掛け、`_MatCapMainStrength` で主色（albedo）を掛け合わせ、`lilBlendColor` で通常・加算・スクリーン・乗算のいずれかで合成する。MToonのMatCapは加算だけで、メッシュ全体に掛かる。

このため次の差が出ていた。

- マスクで袖口や金具だけに光沢を付けた材質で、マスクが落ちて服全体が白っぽく光る。暗い色の服ほど目立つ。
- スクリーンは明るい所ほど足す量が減るが、加算として明るい所にも同じ量を足す。
- 乗算のMatCapで陰影を付けた材質は、その陰影が無くなり明るく見える。

## 決定

- MToonのリム乗算画像（`RimMultiplyTexture`）はMatCapにも掛かる。lilToonのリムライトもバックライトも使っておらず、MToonのパラメトリックリムが黒のときだけ、この画像にMatCapの重みを焼く。重みは `マスク × lerp(1, albedo, _MatCapMainStrength)` で、スクリーンのときはさらに `1 − albedo` を掛ける。スクリーン `max(dst + src − dst × src, dst)` は `dst + src × (1 − dst)` と同じで、明るい側の色の代わりに albedo を使う。マスクが無く、主色の強さが0で、スクリーンでもなければ従来どおり画像を作らない。
- 通常と乗算のMatCapは主画像を暗くする。lilToonの `lerp(dst, out, blend × a × mask)` はマスクについて線形なので、MatCapを見る向きで平均すると `dst × (1 − mask × 暗くする量)` になる。暗くする量は、通常が `blend × colA × mean(a)`、乗算が `blend × colA × (mean(a) − colRGB × mean(a × rgb) × lerp(1, albedo, _MatCapMainStrength))`。平均はMatCap画像の円の内側（lilToonが `_MatCapTex_ST` を掛けて読む範囲）を32×32で読み出して線形空間で求める。焼いた主画像は `BaseColorTexture` と、影の色の画像が無いときの `ShadeColorTexture` に使う。通常のMatCapの足す側は従来どおりMToonのMatCapで描く。乗算は従来どおりMToonのMatCapへは入れない。
- 焼いた画像は主画像のUV空間で作る。lilToonはマスクを `uvMain` とマスク自身のTiling/Offsetで読み、MToonはリム乗算画像と主画像を主画像のUV変換で読むので、画素の位置がそのまま対応する。
- 焼いた画像は既存の焼き込み（`EmissionMaskBaker` など）と同じく書き出し中だけの一時画像として `textures` に持たせ、書き出し後に破棄する。元のマテリアルは変更しない。lilToonの拡張に保存する値と画像は変えない。
- MatCap画像が無い材質は、MToonのMatCapが何も足さないので重みを焼かない。通常のMatCapでは主画像も暗くしない。
- 平均は浮動小数のレンダーテクスチャで読む。この形式を使えない環境では主画像を暗くしない。
- 平均を求める空間はシェーダーが計算する空間に合わせる。リニアのプロジェクトでは線形空間、ガンマのプロジェクトでは画素値と色をそのまま使う。
- 同じ主画像・マスク・色・強さの材質は、1回の書き出しの中で焼いた画像を共有する。画像名は入力から決まる名前にし、同じアバターを2回書き出すと同じ名前になる。
- 焼いた画像で標準VRMの画像数がアプリの上限（128）を超えたときは、焼き込みなしで書き出し直す。上限を超えるとlilToonの拡張が外れる（保存の検査が厳しい設定では保存できない）ので、そのアバターは焼き込みを入れる前と同じ出力にする。
- 2nd MatCap（`_UseMatCap2nd`）は従来どおり標準MToonでは省く。MToonのMatCapは1つで、1stと同時に使う材質では入れる先が無い。

## 制約

- 焼いた画像は書き出した時点の `_Color`・`_MatCapColor` で作る。表情のアニメーションでこれらの色を変えても、焼いた主画像とMatCapの重みは変わらない。
- 焼いた画像は主画像の解像度で作り、主画像のWrap・Filterで読まれる。lilToonはマスクをマスク自身の解像度とWrap・Filterで読むので、マスクだけ解像度やWrapが違う材質では境目の細かさや繰り返しの端で差が出る。

## 検討した代替案と不採用理由

- リムライトを使う材質にもマスクを焼く: リム乗算画像はリムライトにも掛かり、リムライトの範囲が変わる。リムライトの画像とマスクの積を焼けば揃うが、リムライトと MatCap の両方を使う材質は少なく、今回は従来どおりにする。
- 乗算のMatCapをMToonのMatCapへ負の色で入れる: glTFの係数は負にできない。
- 平均ではなく正面から見た色（円の中心）で暗くする: 正面の明るい所だけを使うと、ふちが暗くなる画像で暗くする量を小さく見積もる。
