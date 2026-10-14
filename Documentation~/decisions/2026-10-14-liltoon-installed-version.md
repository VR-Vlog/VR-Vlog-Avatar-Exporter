# 書き出しに導入済みの lilToon の版を記録する

- 日付: 2026-10-14
- 対象: `Editor/LilToonFullSnapshot.cs`・`Editor/LilToonExporterWindow.cs`・`Schema/VRVLOG_materials_liltoon.2.0.schema.json`・`Documentation~/README.md`
- Issue: #147（Epic #145）

## 背景

#146 で lilToon 2.0.0〜2.3.4 のプロジェクトから書き出せるようにした。書き出しの `VRVLOG_materials_liltoon.sourceLilToonVersion` はアプリの契約（`LilToonFullContract.Validate`）が 2.3.4 以外を拒否するので、どの版から書き出しても 2.3.4 になる。このままでは、ファイルがどの版の lilToon から書き出されたかを後から追えない。

## 決定

- `sourceLilToonVersion` は 2.3.4 のまま、「マテリアルが準拠する lilToon のカタログの版」と定義し直す。スキーマ（`Schema/VRVLOG_materials_liltoon.2.0.schema.json`）の `description` に書いた。値の制約（`const: "2.3.4"`）は変えない。
- 導入済みの lilToon の版は glTF の `asset.extras.VRVLOG_lilToonInstalledVersion` に文字列で書く。キー名は、同じ `LilToonFullSnapshot.Inject` が同じ場所に書いている `VRVLOG_lilToonColorSpace`（書き出し元のプロジェクトの色空間）にそろえた。どちらも書き出し元のプロジェクトの情報で、`VRVLOG_lilToon` を前置きにすると拡張と関係するキーだと分かる。
- `RequireSupportedLilToon` は範囲を確かめたうえで導入済みの版を返す。`UniVrmOneClickExporter.Export` の `lilToonVersion` は「導入済みの版」の意味になり、`LilToonFullSnapshot.Inject` はそれを範囲で検査してから `asset.extras` に書く。範囲外なら（書き出し画面を通さない呼び出しでも）書き出しを止める。lilToon 拡張付きの書き出しをするかどうかは今までどおり `exporterVersion` の有無で決まり、`lilToonVersion` は `Inject` での範囲の検査と記録にだけ使う。
- lilToon のマテリアルが無いアバター（`vrvlogWithoutLilToonMaterials` の印が付く経路）では書かない。lilToon のカタログに準拠したマテリアルが無く、版の記録に意味が無いため。
- アプリのコード（VR Vlog 本体）と、アプリと共有している `Editor/LilToonFullContract.cs` は変えない。

## 検討した代替案と不採用理由

- `VRVLOG_materials_liltoon` 拡張の中に `installedLilToonVersion` を足す: 契約は拡張のキーを `Keys(root, …)` で完全一致で検査するので、アプリを変えずには読めない。
- `sourceLilToonVersion` に導入済みの版を書く: 同じく契約が 2.3.4 以外を拒否する。
- `asset.generator` に版を書き足す: UniVRM が書く値で、書式が決まっていない。機械で読むのに向かない。
- キー名を `vrvlogLilToonVersion` にする: `vrvlogWithoutLilToonMaterials` の書き方には合うが、どの版（カタログか導入済みか）か名前から分からない。

## 影響

- 2.0.0〜2.3.4 のどの版で書き出しても、`sourceLilToonVersion` は 2.3.4、`asset.extras.VRVLOG_lilToonInstalledVersion` はその版になる。2.3.4 から書き出したファイルとの差はこのキーの値だけになる（#149 で自動照合する）。
- アプリの読み込みは `asset.extras` のうち `VRVLOG_lilToonColorSpace` だけを読む（`Editor/LilToonFullContract.cs` の `SourceColorSpace`）。足したキーは読まれず、読み込みの結果は変わらない。
- これまでのファイル（このキーが無い）も今までどおり読める。

## 未決

- アプリ側でこの版を表示・利用するかは決めていない（今回はアプリを変えない）。

## 出典

- `Editor/LilToonFullContract.cs` の `SourceColorSpace`（`asset.extras` から `SourceColorSpaceKey` だけを読む）と `Validate`（`sourceLilToonVersion` を `LilToon234Catalogue.Version` と比べ、拡張のキーを完全一致で検査する）
- Epic #145 の方針（`sourceLilToonVersion` は 2.3.4 のまま、実際の版は `asset.extras` に記録する）
