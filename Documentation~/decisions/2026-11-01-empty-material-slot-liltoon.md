# マテリアルが空のスロットを持つ表示物は lilToon の割り当てに入れない

- 日付: 2026-11-01
- 対象: `Editor/LilToonFullSnapshot.cs`（`Capture`）

## 背景

UniVRM の `ModelExporter` は、空のスロットのマテリアル番号を `materials.IndexOf(null)` の -1 にし、glTF の primitive では `material` を省く。`LilToonFullSnapshot.Bind` はその -1 を表示物の割り当て（`bindings[].materials`）に書き、`LilToonFullContract.Validate` が `Missing material` で弾いていた。`ExportStage.LilToonSnapshot` が近似に落ちるので、空のスロットが 1 つあるだけでアバター全体の `VRVLOG_materials_liltoon` が無くなり、すべてのマテリアルが MToon の近似で表示されていた。

## 決定

- `Capture` で、空のスロットを持つ表示物を `selected` に入れない。割り当ても lilToon のマテリアルの読み取りもしない。メッシュの無い表示物を外すのと同じ場所に置いた。
- その表示物に lilToon のマテリアルが含まれていたときだけ、書き出し結果に表示物の名前を 1 行出す。
- アプリは割り当てのある表示物だけ lilToon に差し替え、ほかは標準 VRM のマテリアルのまま描くので、アプリと保存形式は変えない。

## 変えなかった箇所

- 保存形式の `bindings[].materials` に「マテリアル無し」を表す値を足すことはしなかった。アプリの読み込みと検査の両方を変える必要があり、空のスロットはもともと何も描かないか Unity のエラー表示になるだけなので、その表示物を lilToon にする利点が無い。
- 空のスロットの primitive 自体はこれまでどおり UniVRM が書き出す。

## 成り立たない場合

- 空のスロットと lilToon のマテリアルを同じ表示物に持つと、その表示物の lilToon のマテリアルは標準 VRM の近似になる。書き出し結果に名前が出るので、空のスロットを埋めれば lilToon で書き出せる。
