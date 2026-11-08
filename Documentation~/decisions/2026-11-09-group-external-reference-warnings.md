# 外部参照を外した警告は項目ごとに 1 行にまとめる

- 日付: 2026-11-09
- 対象: `Editor/NdmfExportPreparation.cs`

## 背景

2026-11-08 の決定（`2026-11-08-keep-build-results-when-a-pass-fails.md`）で、コピーのコンポーネントがアバターの外を指す参照はコピーの上で外して、結果一覧に 1 行ずつ出すようにした。配列の要素ごとに 1 行出るので、PhysBone の colliders がシーンの別のアバターを指していると結果一覧が同じ内容で埋まる。項目名にも `colliders.Array.data[3]` のような内部の表記がそのまま出ていた。

## 決定

- 同じコンポーネントの同じ項目で外した参照は 1 行にまとめ、最初の参照先と件数を出す（`ExternalReferencesWarning`）。1 件だけのときは今までの文のまま。
- 項目名から配列の番号（`.Array.data[n]`）を落とす。入れ子の配列の中の項目も、番号を除いた項目名でまとめる。
- 参照を外す動きは変えない。まとめるのは結果一覧の行だけ。

## 検討した代替案と不採用理由

- 参照先を全部並べる: 直す人が見るのは「どのコンポーネントのどの項目か」で、同じ別アバターの中の場所が続いても役に立たない。

## 確かめたこと

- `MaSceneReferencePreparationTests.ExternalReferencesInOneArrayAreDetachedAndReportedOnOneLine`: 配列の 3 要素が外を指すときに 1 行にまとまって件数が出ること、アバターの中を指す要素は残ること、元のアバターは変わらないこと。
- `OrdinaryExternalComponentReferencesAreDetachedOnTheCopyWithTheExactField`: 配列でない項目は今までの 1 行のままであること。
