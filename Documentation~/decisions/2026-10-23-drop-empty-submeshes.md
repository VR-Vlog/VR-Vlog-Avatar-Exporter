# 中身が空のサブメッシュを書き出し用のコピーで省く

- 日付: 2026-10-23
- 対象: `Editor/UniVrmOneClickExporter.cs`（`DropEmptySubmeshes`・`ExportCore`）・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/RendererSelectionTests.cs`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`・`CHANGELOG.md`

## 背景

サブメッシュが複数あり、そのうち 1 つでもインデックスが 0 個のメッシュを持つ Renderer があると、書き出しが `ArgumentOutOfRangeException: Index was out of range` で止まっていた。メッシュの一部を削ったあとに空のサブメッシュがよく残り、VRChat へはそのままアップロードできる。

同梱の UniVRM 0.131 は `ModelExporter` で Unity のサブメッシュ 1 つにつき `Submesh` を 1 つ足し、`MeshWriter.ExportMeshDivided` が空のものにも primitive を作る。頂点が 0 個なので `ExtendBufferAndGetAccessorIndex` が -1 を返し、`MeshExportUtil` の `accessors[-1]` で例外になる。`Vrm10AppearanceExporter.Export` は `ExportStageGuard` の外で呼んでいるので、書き出し全体が止まる。`ExportRendererSelection.HasGeometry` はサブメッシュがすべて空のメッシュしか外さない。

## 決定

- `ExportCore` で `PreserveExtraMaterialSlots` の直後に `DropEmptySubmeshes` を呼ぶ。書き出し用のコピーの Renderer のうち、空のサブメッシュと空でないサブメッシュの両方を持つものについて、メッシュを複製して空でないサブメッシュだけにし（各サブメッシュのインデックスとトポロジはそのまま、頂点・bindpose・ブレンドシェイプは触らない）、空のサブメッシュに対応するマテリアルの枠を `sharedMaterials` から外す。複製は `temporaryMeshes` に入れ、書き出しのあとで破棄する。
- Unity は空のサブメッシュを何も描かないので、省いても見た目は変わらない。サブメッシュの数より多いマテリアルの枠（最後のサブメッシュを重ねて描く分）は `PreserveExtraMaterialSlots` が先に明示的なサブメッシュにしている。`exporterVersion` が無い呼び出しでは残っていることがあり、最後のサブメッシュが空でなければ後ろに残し、空なら何も描かないので一緒に外す。
- `exporterVersion` の有無にかかわらず呼ぶ。対象は、そのままでは UniVRM が例外で止まるメッシュだけになる。
- サブメッシュがすべて空のメッシュは、これまでどおり `DisableEmptyMeshRenderers` が Renderer ごと外す。
- 省いた Renderer ごとに「描画する三角形のないサブメッシュを省きました: 」とコピーのルートからのパスを警告に出す。4 言語の訳を足した。
- 同梱の UniVRM は変えない。

## テスト

- `RendererSelectionTests.EmptySubmeshesAreLeftOutWithTheirMaterialSlots` で、サブメッシュ 3 つのうち 2 つ目が空のメッシュとマテリアル 3 つを持つ Renderer を書き出し、例外が出ないこと、glTF のメッシュの primitive が 1 つ目と 3 つ目のマテリアルの 2 つだけになること、警告が 1 件出ること、元のアバターのメッシュとマテリアルが変わらないことを確かめる。
- `RendererSelectionTests` は互換検証のクラスなので、`Tools/run-unity-compatibility.py` のケース数を 3 から 4 に、`Tools/test-compatibility-tooling.py` の件数を合わせて直した。

## 欠点

- 表情の材質の行き先は Renderer と材質の枠の番号で記録している（`ExportOptimizationBindings`）。空のサブメッシュより後ろの枠を指していると番号がずれ、`VerifyMaterialEvidence` で食い違いとして扱われることがある。同じ Renderer に空のサブメッシュと材質を変える表情の両方があるときだけで、これまではその前に UniVRM で止まっていた。
