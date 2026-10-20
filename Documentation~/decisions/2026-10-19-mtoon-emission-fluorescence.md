# 標準MToonの発光の強さへ蛍光（`_EmissionFluorescence`）を反映する

- 日付: 2026-10-19
- 対象: `Editor/UniVrmOneClickExporter.cs`（`FallbackColor`）

## 背景

lilToon 2.3.4の発光（`lil_common_frag.hlsl`）は、蛍光を次の式で適用する。

```
emissionColor.rgb = lerp(emissionColor.rgb, emissionColor.rgb * fd.invLighting, _EmissionFluorescence)
invLighting = saturate((1 - lightColor) * sqrt(lightColor))
```

`invLighting` は白・強さ1の通常の照明では0になり、暗い照明でも最大で約0.385（`lightColor` が1/3のとき）にとどまる。蛍光を1にし、白の `_EmissionColor` と全面の発光画像を設定したマテリアルは、明るい場面ではほぼ光らない。標準MToonへの代替出力はこのプロパティを無視していたため、同じマテリアルが最大の強さで光り、書き出したVRMでは発光させている部分（瞳など）が白っぽく光って見えていた。

## 決定

- 標準MToonの `EmissiveFactor`（`FallbackColor` の `emissionColor`）に、従来の `_EmissionColor × _EmissionBlend` へ `1 - clamp01(_EmissionFluorescence)` を掛ける。プロパティが無いときは0として扱い、従来と同じ値になる。
- MToonの発光は照明で変えられないので、`invLighting = 0`（通常の照明の下）のときの値を使う。式に代入すると `lerp(1, 0, _EmissionFluorescence) = 1 - _EmissionFluorescence` になる。
- 暗い場面でだけ光る効果（最大で約0.385倍）は再現しない。
- `FallbackColor` は表情のマテリアル色の基準値（`UnifiedExpressionPreparation`）とVRM内の表情の検証でも使われ、どちらも書き出した `emissiveFactor` と同じ値を基準にするので、表情側の判定も揃う。
- lilToon専用表示（`VRVLOG_materials_liltoon`）には従来どおり元の `_EmissionFluorescence` をそのまま保存する。lilToonを扱えるランタイムは照明に応じた蛍光を自前で計算できる。元のマテリアルは変更しない。
- 発光マスクで瞳だけを光らせるマテリアルでは、この変更で瞳が淡く光る差が消えることを実機で確認した。肌全体がlilToonより明るく見える差は残るが、これは発光ではなく、lilToonが受ける光を `_LightMaxLimit` で頭打ちにするのにMToonには同じ上限が無いためで、この変更の対象外とする。

## 検討した代替案と不採用理由

- 暗い場面の値（最大の `invLighting` 約0.385）を使う: 蛍光を1にしたマテリアルが明るい場面でも0.385倍で光り続け、白っぽく光る症状が残る。通常の照明の下での見た目を優先する。
- 両者の中間の値を使う: どの場面の見た目とも一致せず、根拠のある値にならない。
- 蛍光が設定されたマテリアルで警告を出す: 書き出しの結果は通常の照明の下でlilToonと一致するので、警告は不要とした。
