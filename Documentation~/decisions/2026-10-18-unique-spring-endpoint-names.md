# 揺れ物の末端に重ならない名前を付ける

- 日付: 2026-10-18
- 対象: `Editor/PhysBoneSpringExport.cs` の `Convert`・`Editor/SkinnedMeshFallbackWeights.cs` の `RemapOmittedRootJoint`・`Tests/Editor/PhysBoneSpringExportTests.cs`

## 背景

PhysBone を VRM の揺れ物へ変換するとき、Endpoint Position や分岐の平均位置から作る末端は、すべて `VRVlog Spring Endpoint` という同じ名前の GameObject だった。UniVRM は書き出しの前にノード名の重複を直し（`ForceTransformUniqueName`）、2 つ目以降を `<親の名前>-<名前>` に付け直して、1 件ごとに `force rename !!` の警告を出す。多くのアバターをまとめて書き出したところ、1 体につき数十件の警告が出て、ログのほかの警告が読めなくなった。出力のノード名も UniVRM の付け直しに頼っていた。

アバターのルートを参照するメッシュごとに作る `VR Vlog exported root joint` も、同じ名前のまま同じ親に並ぶので、同じ理由で付け直しの対象になる。

## 決定

- 末端は作るときに `<親の名前>-VRVlog Spring Endpoint` と名付ける。2 つ目以降の末端に UniVRM が付けていた名前と同じ形にする。
- その名前が書き出し用コピーの中のいずれかの Transform か、先に作った末端で使われていれば、作った順に ` 2`・` 3` … を付ける。使用中の名前の一覧は、変換ごとに 1 回だけ、末端を作る前の階層から作る。
- ルートのジョイントは従来の名前を最初の 1 つに使い、2 つ目からは ` 2`・` 3` … を付ける。名前の一覧は `SkinnedMeshFallbackWeights.Preserve` の呼び出しごとに 1 回作る。検証に失敗して消したジョイントの名前は一覧から外す。
- 名前の比較は大文字と小文字を区別する（UniVRM の重複判定と同じ）。

## 検討した代替案と不採用理由

- UniVRM の付け直しに任せる: 出力は正しいが、警告が末端の数だけ出る問題が残る。
- 連番だけを付ける（`VRVlog Spring Endpoint 2` など）: どの骨の末端か名前から分からなくなり、2 つ目以降の出力のノード名も変わる。

## 影響

- 末端とルートのジョイントについて、UniVRM の `force rename !!` の警告が出なくなる。
- 2 つ目以降の末端の出力ノード名は、親の名前が重ならない限り従来と同じ。UniVRM が付け直さなかった最初の 1 つ（階層の順で最初に見つかる末端）は `VRVlog Spring Endpoint` から `<親の名前>-VRVlog Spring Endpoint` に変わる。揺れ物の設定はノードの番号で参照するので、揺れ方と見た目は変わらない。
- アバター自身の骨の名前の重複は、従来どおり UniVRM が付け直す。
