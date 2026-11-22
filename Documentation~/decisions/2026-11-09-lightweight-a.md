# 自動で軽量化するで、他のVRMアプリ向けの画像・インデックス・反射キューブも小さくする

- 日付: 2026-11-09
- 対象: `Editor/FallbackImageLightweight.cs`、`Editor/VertexDataLightweight.cs`、`Editor/LilToonFullSnapshot.cs`、`Editor/UniVrmOneClickExporter.cs`

## 背景

自動で軽量化するをオンにしたVRMを調べると、残っている容量の大きいものは次の4つだった。

- lilToonの拡張を持つマテリアルの MToon の画像。アプリはそのマテリアルを拡張から描き、MToon の画像を読まない（`LilToonLoadSession.FullyBoundMaterials`）。他のVRMアプリだけが見る。
- 描画のインデックス。UniVRM は頂点数にかかわらず 32 ビットで書く。
- 表情（モーフ）の sparse のインデックス。これも 32 ビットで書く。
- lilToon の反射キューブ（`_ReflectionCubeTex`）が HDR のとき、float のまま書く。

## 決定

自動で軽量化するがオンのときだけ、次の4つを足す。オフのときの出力は1バイトも変わらない。

1. MToon の画像を長辺 256 に縮める。表情が動かす・色や UV を変えるメッシュのマテリアル（顔・目・眉）は 512 にする。
   - 対象は、アプリが拡張から描くマテリアル（拡張の記録があり、バインドの無いノードが描かないもの）だけが使う画像に限る。サムネイル、他のマテリアル、知らないスロットが使う画像、色とデータの両方のスロットで使う画像は縮めない。
   - 色のスロットは sRGB で、法線などのデータのスロットは linear で平均する（`TextureResizePolicy.Resize`）。
   - 知らない拡張がある、バイナリが1チャンクに収まっていない、縮めても小さくならないファイルはそのまま返す。
2. 描画のインデックスは、最大値が 65534 以下なら 16 ビットで書き直す。65535 は glTF でプリミティブの区切りに予約されているので使わない。
3. sparse のインデックスは、最大値が 65535 以下なら 16 ビットで書き直す。sparse には区切りの値が無い。値と並びは変えないので、昇順で親の count 未満という条件はそのまま保たれる。
4. HDR の反射キューブは、全部の面と mip の値が有限で、絶対値が 65504 以下（half の最大値）なら half で書く。大きさと mip の数は変えない。

インデックスのどちらも、その bufferView を読むのがその accessor だけのときに限る。共有されている bufferView は変えない。

## 検討した代替案と不採用理由

- MToon の画像を JPEG にする: 透過を持つ画像が多く、他のVRMアプリでの見た目の差が大きいので今回は入れない。
- インデックスを Vendor の UniVRM の中で 16 ビットにする: Vendor は `Tools/vendor-univrm.py` で入れ直すたびに変更が消える。書き出し後の GLB を書き直す既存の仕組み（`VertexDataLightweight`）に足せば、オフのときに変わらないことも既存のテストで確かめられる。
- 反射キューブを 512 に縮める: lilToon は粗さから反射の mip を絶対の LOD で選ぶので、解像度を落とすと同じ粗さでのぼけ方が変わる。half にするだけなら見た目は丸めの差だけになる。アプリは float の画像を filtering のため half で GPU に上げているので、half 化で減るのはファイルの大きさだけで、メモリは変わらない。
- 表情モーフのメッシュだけを顔とみなす: 色や UV を変えるだけの表情の目が漏れるので、`materialColorBinds` と `textureTransformBinds` のマテリアルも顔に含めた。全身が1メッシュのアバターは全身が 512 になるが、他のVRMアプリでぼやけるよりはよい。

設計は別のモデルにも確かめさせた。対象の画像を「アプリが拡張から描くマテリアル」だけで決めず、サムネイルや他の用途との共有を除くこと、half にする前に値の範囲を確かめることは、その指摘で足した。

## 確かめたこと

- `FallbackImageLightweightTests`: 縮める画像と残す画像の振り分け、縮めた後の色、拡張の chunk が壊れないこと、書き出しをオンとオフで比べて UniVRM で読み込めること。2026-11-10 に `AnImageAMaterialWithoutTheExtensionAlsoUsesIsKept` を足した。アプリが拡張から描くマテリアルと、MToon で描くマテリアルが同じ画像を使うときに、どちらのマテリアルが先に並んでいても縮めないこと。`kept` に入れる判定を外すと落ちることも確かめた。
- `VertexDataLightweightTests`: 65534 と 65535 の境目、accessor の byteOffset、sparse の 65535 と 65536、共有された bufferView が残ること、書き出したVRMを読み込んだ三角形が元と同じこと。
- `HdrCubeLightweightTests`: half の範囲の判定と、オンでも範囲外のキューブや 2D の画像は float のままであること。
- アプリと同じ版の UniVRM で、16 ビットのインデックスと sparse を持つVRMを読み込めること。
