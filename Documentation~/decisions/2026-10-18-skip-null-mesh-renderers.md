# メッシュが空の Renderer を書き出し用の複製で無効にしてから UniVRM に渡す

- 日付: 2026-10-18
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/Locales/*.json`・`Tests/Editor/RendererSelectionTests.cs`・`CHANGELOG.md`

## 背景

有効なオブジェクトに付いた有効な Renderer のうち、メッシュが空のもの（`SkinnedMeshRenderer.sharedMesh` が `null`、`MeshFilter` が無いか `sharedMesh` が `null` の `MeshRenderer`、頂点や面が 0 のメッシュ）があると、UniVRM 0.131 の `ModelExporter` はその Renderer を飛ばし、Renderer ごとにコンソールへ「mesh is null」などの警告を出す。出力の形状には影響しないが、一括で書き出すとこの警告が並び、見るべき警告が埋もれる。実際のアバターで、このような Renderer が複数あるものがあった。

Exporter 側はすでに `ExportRendererSelection.HasGeometry` で同じ条件を判定しており、`LilToonFullSnapshot`・`VrmMeshAttributePreparation`・`LilToonStructureCheck` は空のメッシュを読み飛ばしている。UniVRM に渡す直前だけ、空の Renderer がそのまま残っていた。

## 決定

- `UniVrmOneClickExporter.DisableEmptyMeshRenderers` を足し、`ExportCore` で `Vrm10AppearanceExporter.Export` を呼ぶ直前に、書き出し用の複製に対して呼ぶ。NDMF・最適化ツール・材質の置き換え・メッシュの複製（`MakeRendererMeshesUnique`）がすべて終わったあとなので、これらの処理がメッシュを外した Renderer も対象になる。
- 対象は `ExportRendererSelection.Enumerate` が返す（有効なオブジェクトの有効な）`SkinnedMeshRenderer` と `MeshRenderer` のうち、`HasGeometry` が偽のもの。`HasGeometry` が偽のメッシュは `ModelExporter.MeshCanExport` でも必ず飛ばされるので、UniVRM が出力するメッシュは変わらない。
- Renderer は削除せず `enabled = false` にする。`Enumerate` と `ModelExporter` はどちらも無効な Renderer を見ないので、無効にすれば両方から外れる。`MeshFilter`・オブジェクト・骨はそのまま残すので、ノードの階層と、表情・瞬き・最適化の対応付けが持っている Renderer の参照は壊れない。
- 外した Renderer は、アバターのルートからのパスを `, ` でつないで警告に 1 件だけ出す。文面は「メッシュが空のRendererを書き出しから外しました: 」で、既存の `ExporterLocalization.T("…: ") + 可変部分` の形に合わせ、en・ko・zh-Hans・zh-Hant の訳を足した。可変部分は前置きの外で連結するので、`DynamicErrorPrefixes` には足さない（あちらは例外のメッセージ全体を訳すためのもの）。
- メッシュを持つ Renderer が 1 つも無いときは何もしない。すべてを無効にすると、UniVRM と後続の処理が見る内容が変わり、これまでと違う失敗の仕方になるため。
- 元のアバターには触れない。複製でない（アセットの）`GameObject` を渡したときは例外にする。

## 確認したこと

- `RendererSelectionTests.EmptyMeshRenderersAreLeftOutOfTheExportCopyOnly` で、メッシュのある `SkinnedMeshRenderer` 2 つに、メッシュが `null` の `SkinnedMeshRenderer` と、メッシュの無い `MeshFilter` を持つ `MeshRenderer` を足したアバターを、通常の書き出しと lilToon の材質情報を含む書き出しの両方で書き出し、次を確かめる。
  - 書き出しが成功し、GLB のメッシュが 2 つ残る。
  - 書き出し中のログに「mesh is null」が出ない。
  - 警告が 1 件だけあり、2 つの Renderer のパスを含む。
  - 元のアバターの Renderer は有効のままで、メッシュと材質も変わらない。
- `RendererSelectionTests.CopyWithoutAnyMeshIsLeftForTheExistingFailure` で、メッシュを持つ Renderer が無い複製では何も無効にせず、警告も出さないことを確かめる。
- Unity 2022.3.22f1 で、ほかの修正と合わせてパッケージの EditMode テストを全件実行し、上の 2 つ（3 件）が通った。失敗した 9 件は、この変更の無い状態でも失敗するものと、同時に試した別の修正のものだった。

## 検討した代替案と不採用理由

- Renderer を `DestroyImmediate` する: UniVRM の出力は同じだが、表情・瞬き・最適化の対応付けが Renderer の参照を持っていると、破棄後に Unity の「破棄されたオブジェクト」を踏む。無効にするだけで UniVRM から外れるので、壊す理由が無い。
- UniVRM のログ（`UniGLTFLogger.SetLogger`）を書き出しの間だけ差し替えて「mesh is null」を捨てる: グローバルな状態を書き換えるので、書き出しが例外で抜けたときや並行して動くほかのツールに影響する。同じ経路のほかの警告まで扱いを決める必要もある。
- もっと前（NDMF の処理の前）で外す: NDMF や最適化ツールがあとからメッシュを外した Renderer を拾えない。処理の途中で Renderer を無効にすると、ほかのツールの判断も変わる。

## 影響

- 出力されるメッシュとノードは変わらない。
- 空の Renderer にしか使われていない材質は、UniVRM が材質を集める対象から外れるので VRM に入らなくなる。これまでは描画されない材質が VRM に入っていた。その材質の色を VRM の表情で動かしていた場合は、UniVRM がその対応付けを警告を出して落とす（これまでも見た目には出ない材質だった）。
- `ExportAttachmentSession`（独立したパーツの警告）と材質の置き換えは、これまでどおり空の Renderer も見る。今回はそこを変えていない。
