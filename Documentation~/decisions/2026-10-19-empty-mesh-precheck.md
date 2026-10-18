# メッシュが 1 つもないアバターは書き出しの前に止める

- 日付: 2026-10-19
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportSettings.cs`・`Editor/Locales/*.json`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`CHANGELOG.md`

## 背景

プロジェクト内のアバターを書き出し画面と同じ経路でまとめて書き出したところ、シーンに置かれた、アバターの記述子とヒューマノイドのAnimatorを持つが、Rendererにメッシュが1つもないオブジェクトが 1 件あった。Renderer は MeshFilter の付いていない Mesh Renderer だけだった。

このオブジェクトはすべての処理を通り、メッシュを 1 つも含まない 29 KB の VRM（GLB に JSON のチャンクしかないもの）を成功として保存した。そのあいだにコンソールへ UniVRM の「mesh is null」と Unity の「Assertion failed: ValidTRS()」が合わせて 197 件出た。結果の警告には「VRM表情の追加 — VRMの表情を設定するためのnode・mesh情報がありません」「瞬きの表情の追加 — The given key 'meshes' was not present in the dictionary.」「フェイストラッキング — Unified Expressions の書き出しに必要な VRM 情報がありません」が並んだ。使う人には中身のないアバターと、成功したように見える結果が残る。

2026-10-18 の決定（`2026-10-18-skip-null-mesh-renderers.md`）は、メッシュを持つ Renderer が 1 つもない複製を「これまでと違う失敗の仕方になる」として `DisableEmptyMeshRenderers` で何もせずに残した。実際にはそこで止まる既存の失敗はなく、書き出しは空のまま成功していた。

## 決定

- 書き出し画面・**アプリに転送**・一括書き出しが共通で使う書き出し処理（`ExportSettings.Export`）で、ヒューマノイドの検査（`ValidateSourceHumanoid`）の直後に、選んだアバターにメッシュがあるかを確かめる（`UniVrmOneClickExporter.ValidateSourceHasMesh`）。メッシュがなければ `InvalidOperationException` で止める。
- 判定は止めすぎない側に倒す。非表示のオブジェクトと無効な Renderer も含めてアバターの下のすべての Renderer を見て、次のどれか 1 つでもあれば通す。
  - `sharedMesh` が `ExportRendererSelection.HasGeometry` を満たす Skinned Mesh Renderer
  - MeshFilter が付いていて、その `sharedMesh` が `HasGeometry` を満たす Mesh Renderer
- MeshFilter は `TryGetComponent` で取る。`GetComponent<MeshFilter>()?.` はエディターで偽の null を返して例外になる（`2026-10-19-meshrenderer-without-meshfilter.md`）。
- 元のアバターの検査は止めすぎない側に倒したので、非表示のオブジェクト・無効な Renderer・除外したオブジェクトにだけメッシュがあるアバターは通る。書き出しはこれらを出力しないので、`ExportSettings.Export` は書き出した VRM も確かめる（`UniVrmOneClickExporter.ValidateOutputHasMesh`）。GLB の `meshes` が空なら `InvalidOperationException` で止め、保存しない。こちらは重い処理のあとになるが、元のアバターからは判断できない場合だけが当たる。
- 文言は日本語を原文にし、状態の行と操作の行の 2 行にした（元のアバター:「書き出せるメッシュがありません」「Skinned Mesh RendererかMesh Filter付きのMesh Rendererを持つアバターを選択」、書き出した VRM:「書き出し対象のメッシュがありません」「非表示のオブジェクトと除外の設定を見直す」）。隣の検査の文言は句点で終わる文の形だが、画面の文言の決まり（句読点を使わない・状態と操作を 1 行ずつ）に合わせた。英語・韓国語・簡体字・繁体字の訳を足した。後ろに可変部分が続かないので `DynamicErrorPrefixes` には加えない。

## 検討した代替案と不採用理由

- `UniVrmOneClickExporter.Export` か `ExportAndSaveNormally` の先頭で確かめる: 自動テストの多くはメッシュのない Generic の小さなアバターをこの 2 つに渡して、出力のバイト列や途中の失敗を確かめている。ここで止めると、メッシュと関係のないテストの意図が変わる（ヒューマノイドの検査を `ExportSettings.Export` に置いた理由と同じ）。
- `DisableEmptyMeshRenderers` で、メッシュのある Renderer がないときに止める: 書き出し用の複製を作り、NDMF と最適化ツールを通したあとなので、重い処理を回してから止めることになる。元のアバターにメッシュがない場合は最初に分かる。
- 元のアバターの検査で、有効なオブジェクトの有効な Renderer だけを数える（`ExportRendererSelection.Enumerate` と同じ条件）: 書き出し用の複製では、NDMF・Modular Avatar・表情の初期状態の反映で表示が変わることがあり、元のアバターの表示状態だけでは出力されるかを決められない。止めすぎを避けるため元のアバターではすべてを数え、出力されなかった場合は書き出した VRM の検査で止める。

## 影響

- メッシュを 1 つでも持つアバターの書き出しは変わらない。
- Renderer のどこにもメッシュがないアバターは、書き出しの処理を始める前に、理由と選び直しの案内を表示して止まる。これまでは空の VRM が保存されていた。
- 非表示のオブジェクト・無効な Renderer・除外したオブジェクトにだけメッシュがあり、書き出した VRM にメッシュが 1 つも入らなかった場合は、保存せずに止まる。これまでは空の VRM が保存されていた。
- 書き出し用の複製に対する `DisableEmptyMeshRenderers` は変えていない。NDMF や最適化ツールが複製からすべてのメッシュを外した場合も、書き出した VRM の検査で止まる。
- 試験的な表情の収録画面からの書き出しは `ExportSettings.Export` を通らないので、どちらの検査も通らない（ヒューマノイドの検査と同じ扱い）。収録には顔のメッシュが要るため、空の VRM になることはない。

## 確認したこと

- `PhysBoneSpringExportTests.MeshRendererWithoutMeshFilterSourceStopsBeforeExport`: ヒューマノイドで、Renderer が MeshFilter のない Mesh Renderer だけのアバターを `ExportSettings.Export` に渡すと、ヒューマノイドの検査は通り、この検査のメッセージで止まる。警告は 1 件も出ない。
- `PhysBoneSpringExportTests.NullSkinnedMeshSourceStopsBeforeExport`: Renderer がメッシュの外れた Skinned Mesh Renderer だけのアバターで止まる。
- `PhysBoneSpringExportTests.MeshOnInactiveChildPassesSourceMeshCheck`: メッシュが非表示の子にしかないアバターは止めない。
- `PhysBoneSpringExportTests.OutputWithoutMeshIsRejected`: `meshes` のない GLB は止め、ある GLB は通す。
- `PhysBoneSpringExportTests.MeshOnlyOnInactiveChildStopsAfterExport`: メッシュが非表示の子にしかないアバターを `ExportSettings.Export` に渡すと、元のアバターの検査は通り、書き出した VRM の検査のメッセージで止まる。
