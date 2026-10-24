# リムライトや逆光ライトを使う材質でも、MatCap のマスクをリム乗算画像に焼く

- 日付: 2026-10-24
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/MatcapApproximation.cs`・`Tests/Editor/MatcapApproximationTests.cs`
- 改める記録: `2026-10-22-mtoon-matcap-mask.md` の決定の 1 つめと、検討した代替案の「リムライトを使う材質にもマスクを焼く」

## 背景

`2026-10-22-mtoon-matcap-mask.md` では、MToon のリム乗算画像（`RimMultiplyTexture`）に MatCap の重みを焼くのは、lilToon のリムライトも逆光ライトも使っていない材質だけにした。リム乗算画像はリムライトにも掛かるので、リムライトの範囲を変えないためである。

ところが加算の MatCap にマスク（`_MatCapBlendMask`）を付け、リムライトか逆光ライトも使っている衣装では、マスクが落ちて MToon の MatCap がメッシュ全体に掛かっていた。黒い衣装ではマスクの外側も含めて全体が金色に光って見え、lilToon とはっきり違っていた。

## 決定

- MatCap にマスクがある材質は、リムライトや逆光ライトを使っていても `RimMultiplyTexture` に MatCap の重み（`マスク × lerp(1, albedo, _MatCapMainStrength)`、スクリーンのときはさらに `1 − albedo`）を焼く。リムライトの画像（`_RimColorTex`）があっても重みを優先する。
- マスクが無い材質は変えない。リムライトを使っていれば従来どおり `_RimColorTex` を入れ、パラメトリックリムが黒のときだけ重みを焼く。
- 重みを作らない材質（MatCap 画像が無い・乗算の MatCap・MatCap が無効）は、マスクがあっても従来どおり `_RimColorTex` か画像なしにする。

マスクが落ちると MatCap がメッシュ全体を照らすのに対し、リムがマスクの範囲に縮むのはふちの一部の見た目だけで済むので、マスクを残すほうを選んだ。

## 修正前後の比較

マスクのある加算の MatCap と、リムライトか逆光ライトを使う材質を含む衣装 2 つで比べた。衣装だけを基本姿勢で置き、正面から同じカメラで lilToon と MToon（修正前・修正後）を撮った。比べたのはマスクが白い画素の範囲で、値はその平均の sRGB（0〜255）である。

| 衣装 | 画素数 | lilToon | MToon 修正前 | MToon 修正後 |
|---|---|---|---|---|
| 黒い衣装 | 103234 | (50.8, 50.2, 52.6) | (116.6, 109.4, 96.1) | (53.7, 53.2, 55.5) |
| 白い衣装 | 102037 | (246.5, 238.6, 238.1) | (252.1, 250.7, 250.5) | (244.8, 235.8, 235.1) |

黒い衣装は修正前に全体が金色に寄り、修正後は lilToon との差が各チャンネル 3 以内になった。白い衣装も修正前は白く飛び、修正後は差が 3 以内になった。

## 制約

- マスクの外ではリムライトも消える。lilToon ではリムライトはマスクに関係なくふち全体に出るので、マスクの外のふちが lilToon より暗くなる。
- リムライトにも MatCap の重み（主色を掛ける量）が掛かる。`_MatCapMainStrength` が大きい材質では、マスクの内側のリムが主色に寄る。
- リムライトの画像（`_RimColorTex`）は使われなくなる。MToon のリム乗算画像は 1 枚なので、両方を入れる先が無い。リムライトの画像とマスクの積を焼けば両方を残せるが、その場合は MatCap 側が `_RimColorTex` で欠ける。今回の衣装では重みだけで lilToon に近づいたので、積は焼かない。

## 検証

- EditMode: `MatcapApproximationTests.MaskStaysWhenARimLightIsOn`（リムライト・逆光ライトのそれぞれで、マスクのある MatCap の重みがリム乗算画像に入り、マスクが黒の画素は 0・白の画素は 1 になること、マスクを外すとリムライトは `_RimColorTex`・逆光ライトは画像なしに戻ることを確かめる）。Unity 2022.3.22f1・lilToon 2.3.4 の空のプロジェクトで `MatcapApproximationTests`・`MaterialFallbackTests`・`LilToonFullExportTests`・`SectionedToonSubstitutionTests`・`FullTextureResizeTests` を回し、失敗は 0 件だった。
