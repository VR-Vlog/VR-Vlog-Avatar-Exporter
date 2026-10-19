# 標準MToonの発光へ発光マスクと主色の強さを焼き込む

- 日付: 2026-10-19
- 対象: `Editor/EmissionMaskBaker.cs`・`Editor/EmissionMaskBaker.shader`・`Editor/UniVrmOneClickExporter.cs`・`Editor/LilToonEmissionPolicy.cs`・`Editor/LilToonMaterialReader.cs`

## 背景

標準MToonへの代替出力は、発光を `EmissiveTexture = _EmissionMap`・`EmissiveFactor = _EmissionColor × _EmissionBlend` だけで書き出していた。lilToon 2.3.4の発光（`lil_common_frag.hlsl` の `lilEmission`）は、ここへさらに `_EmissionBlendMask` を掛け、`_EmissionMainStrength` で主色（albedo）を掛け合わせる。発光画像がほぼ白で、マスクで小さな範囲（瞳など）だけ光らせるマテリアルでは、マスクが落ちて顔や体の全体が真っ白に光り、瞳が見えなくなっていた。

## 決定

- 発光が有効で、発光マスクが設定されている（未設定・組み込みの白画像を除く）か `_EmissionMainStrength` が0より大きいとき、出力用の発光画像を1枚焼く。値は lilToon 2.3.4 の `EmissionMap × EmissionBlendMask`（RGBA）に `lerp(1, albedo, _EmissionMainStrength)` を掛け、MToonに発光のアルファが無いので最後にアルファをRGBへ掛けたもの。albedo は主画像 × `_Color`（2nd・3rd レイヤーと色調補正は既存の焼き込みで主画像に入っている）。`_EmissionColor` と `_EmissionBlend` は従来どおり `EmissiveFactor` に残す。
- MToonは発光画像を主画像のUV変換で読むので、焼いた画像は主画像のUV空間で作る。各画素を主画像のTiling/Offsetの逆変換でUV0へ戻し、発光画像・マスクはそれぞれのTiling/Offsetと固定角度（`_ScrollRotate.z`）で読む。lilToon 2.3.4 の標準シェーダーは `LIL_FEATURE_ANIMATE_EMISSION_MASK_UV` を定義するので、マスクは主画像のUVではなくUV0と自身のTiling/Offsetで読む。
- 焼けないとき（発光のUVがUV0以外、主画像のTilingが0・非有限）は従来どおり元の発光画像を使い、マスクの近似の警告を残す。焼いたマスクについては警告しない。加算以外の合成方法は従来どおり警告する。マスクのスクロール・回転は既存の「特殊UV・回転・時間変化」の警告に含める。
- 焼いた画像は既存の焼き込み（`AlphaMaskBaker` など）と同じく書き出し中だけの一時画像として `textures` に持たせ、書き出し後に破棄する。元のマテリアルは変更しない。

## 検討した代替案と不採用理由

- マスクの平均値を `EmissiveFactor` に掛ける: 小さな範囲だけ光らせる用途では全体が暗く光るだけで、範囲の違いを再現できない。
- マスクをCPUで合成する: 元画像が読み取り不可のことが多く、既存の焼き込みと同じGPUの経路の方が扱いが揃う。
- 発光の点滅・グラデーション・視差・蛍光（`_EmissionFluorescence`）も焼く: 時間・視点・光源に依存し、静止画像では表せない。従来どおり省略する（点滅・グラデーション・視差は既存の警告の対象）。
