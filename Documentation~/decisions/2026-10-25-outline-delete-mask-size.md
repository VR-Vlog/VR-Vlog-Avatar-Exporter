# Delete Meshのアウトライン幅マスクは縮小せずに書き出す

- 日付: 2026-10-25
- 対象: `Editor/LilToonFullSnapshot.cs`、`Tests/Editor/FullTextureResizeTests.cs`、`CHANGELOG.md`、`README.en.md`、`Documentation~/README.md`

## 背景

lilToonのアウトラインで **Delete Mesh**（`_OutlineDeleteMesh`）をオンにしたマテリアルに、エディタでは表示されないアウトラインの三角形が、アプリのlilToon専用表示で黒い線として残ることがあった。実際のモデルでは、書き出したマスクでだけ残る三角形が58枚、逆にエディタでだけ残る三角形が12枚あった。

## 原因

lilToon 2.3.4のアウトラインのパスは、頂点ごとに幅を `_OutlineWidth * tex2Dlod(_OutlineWidthMask, uv, 0).r` で求める（mip 0をバイリニアで読む）。Delete Meshがオンのとき、この幅がちょうど0の頂点の位置をNaNにして、その頂点を含む三角形を描かない。消えるかどうかは「ちょうど0かどうか」で決まるので、マスクの値がわずかに変わるだけで結果が変わる。

`LilToonFullSnapshot.Texture()` は、lilToon専用表示の画像を `TextureResizePolicy.Size` で最大1024×1024に縮める。2048×2048のマスクを面積平均で1024×1024にすると、0の画素と0でない画素が混ざった場所の値が0でなくなり、0だった領域の境目が動く。エディタで0を読んでいた頂点が0より大きい値を読み（消えずに残る）、逆の頂点も出る。

## 決定

- 書き出すlilToonマテリアルのうち、2.3.4の記録に `_OutlineWidthMask` を持つシェーダーで `_OutlineDeleteMesh` が0でないものが使うアウトライン幅マスクは、エディタで読み込まれている大きさ（`source.width`・`source.height`）のまま書き出す。上限は既定の `LilToonMobileProfile.DefaultMaximumTextureSize`（1024）ではなく `LilToonMobileProfile.MaximumTextureSize`（2048）にする。収まる画像は縮小せず `LilToonFullTexture.Read` でそのまま読むので、画素はエディタと一致する。2048を超える画像は2048へ縮小する（`ReadResized` の中間面の1024は `Math.Max(width, …)` で目標の大きさを下回らないので、2048への縮小もそのまま動く）。
- 画像は `(Texture, normal)` ごとに1つにまとめて保存している。どのマテリアルが先に読んだかで大きさが変わらないように、`Capture` で書き出す全レンダラーのマテリアルを先に集め、`ReadMaterial` を呼ぶ前に対象のマスクの集合（`DeleteMeshMasks`）を作る。同じ画像をDelete Meshを使わないマテリアルが使っていても、大きい方で保存する。
- 上限は `Texture()` で1回決め、`Layout` と `Pixels`（中で `Layout` をもう一度呼ぶ）の両方に同じ値を渡す。法線として読む参照（`normal` がtrue）は対象にしない。
- 縮小したときだけ出す「lilToon画像を縮小しました」の注記は変えない。縮小しなかったマスクには出ない。
- 標準のMToon表示に書く `OutlineMaskTexture.Create`（`OutlineWidthMultiplyTexture`）は変えない。MToonのアウトラインには幅が0の頂点を消す処理が無く、幅0の頂点は殻が太らないだけなので、0の境目がずれても三角形が残ったり消えたりしない。
- アバター名やマテリアル名での分岐は足さない。

## 欠点

- 2048×2048のマスクは、1024×1024に縮めていたときと比べて1枚につき読み込み時のメモリがrgba32で約12MB（ミップマップ込みで約16MB）増える。Delete Meshを使うマテリアルのマスクだけが対象なので、多くのアバターでは0〜数枚になる。
- 2048を超えるマスクは縮小するので、0の境目がずれる可能性は残る。エディタの読み込み設定で2048以下にしてあれば起きない。
- 書き出し対象のマテリアルのうち、インストールされているlilToonが2.3.4の記録を満たさずMToonの近似になるもの（`Approximated`）も集合を作るときには数える。そのマスクを別のマテリアルが使っていると、Delete Meshを使わないマテリアルの分でも2048で保存されることがある。

## テスト

`Tests/Editor/FullTextureResizeTests.cs` に足した。

- `DeleteMeshMaskSetDoesNotDependOnReferenceOrder`: 集合が参照の順に依らないことと、上限が2048と1024になること。
- `DeleteMeshOutlineMaskKeepsItsEditorSizeUpToTheMobileMaximum`: Delete Meshのマスクは2048が2048のまま、4096が2048になり、Delete Meshを使わないマテリアルの2048は1024になる。注記は縮小したときだけ出る。
- `DeleteMeshOutlineMaskKeepsEveryExactZeroTexel`: 2048のマスクで、0の画素と0でない画素がエディタのマスクと1つもずれない。
- `SharedOutlineMaskIsStoredAtTheDeleteMeshSizeInEitherMaterialOrder`: 同じマスクをDelete Meshのマテリアルと使わないマテリアルが使うとき、どちらが先でも2048で保存される。
