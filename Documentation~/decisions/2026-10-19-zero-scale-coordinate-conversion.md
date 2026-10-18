# サイズ 0 のオブジェクトがあっても座標変換でコンソールにエラーを出さない

- 日付: 2026-10-19
- 対象: `Editor/Vrm10AppearanceExporter.cs`・`Tests/Editor/MissingMeshAttributesTests.cs`・`Tools/run-unity-compatibility.py`・`CHANGELOG.md`

## 背景

プロジェクト内のアバターとギミックを書き出し画面と同じ経路でまとめて書き出したところ、撮影用のギミックを付けたアバター 1 件で、書き出しは成功したもののコンソールに Unity の「Assertion failed on expression: 'ValidTRS()'」が 197 件出た。ギミックは使わないときに親のオブジェクトの Scale を 1e-45（float で表せるいちばん小さい正の値・`float.Epsilon`）にして隠す作りで、その下に 190 個あまりのオブジェクトがあった。同じギミックには Scale がちょうど 0 のオブジェクトもある。

スタックトレースはすべて `Vrm10AppearanceExporter.Export` から呼ぶ UniVRM の `Model.ConvertCoordinate` の中だった。UniVRM は Unity の座標系から VRM 1.0 の座標系へ移すとき、各ノードのワールド行列の X を反転し、親の逆行列を掛けてローカルの位置・回転・Scale に分解し直す。Scale が 0 や 1e-45 のノードの行列は使える逆行列を持たないので、その下のノードでは「親の逆行列 × 自分の行列」が回転と Scale に分解できない行列になり、分解が `Matrix4x4.rotation` の検査（`ValidTRS`）に引っかかって 1 ノードにつき 1 件のエラーが出る。Scale がちょうど 0 の場合も 1e-45 の場合も、変更前のコードでテストが同じエラーで失敗することを確かめた。書き出した VRM では、サイズ 0 の親より下のノードの位置と Scale がすべて 0 になっていた。親が 0 のあいだは見た目は同じだが、元の配置は失われる。

## 決定

- `Vrm10AppearanceExporter.ConvertCoordinateKeepingZeroScale` を足し、`ConvertCoordinate` の呼び出しをこれに替える。
  - 変換の前に、Scale のどれかの軸の絶対値が 1e-6 より小さいノード（0 と 1e-45 の両方）を集め、その軸だけを 1 にしてワールド行列を計算し直す。
  - `ConvertCoordinate` を呼ぶ。行列はすべて分解できるので、エラーは出ない。
  - 集めたノードの Scale を元の値に戻し、ワールド行列を計算し直す。X 軸の反転は Scale を変えないので、戻した値がそのまま VRM 1.0 の座標系での値になる。
- 書き出した VRM では、サイズ 0 のノードは元どおり Scale 0 になり、その下のノードは元のローカルの位置・回転・Scale を保つ。これまでは位置と Scale が 0 になっていた。
- Unity のオブジェクト（元のアバター・書き出し用の複製）は変えない。変えるのは UniVRM の書き出し用のモデルだけ。

## 検討した代替案と不採用理由

- 書き出し用の複製で Scale 0 を小さな値（1e-4 など）に置き換える: 出力の Scale が元と違う値になり、その下にメッシュがあると点のように残る。スキンのボーンの行列も変わる。
- サイズ 0 の部分をノードごと書き出しから外す: そこにボーンやコンストレイントの参照先があると壊れる。何を外すかはギミックの除外の判断で、座標変換の層で決めることではない。
- コンソールへの出力を抑える: エラーは Unity の内部から出るので、Exporter の側からは選んで抑えられない。抑えたとしても、下のノードの配置が失われる問題は残る。
- 回転と Scale が混ざって行列が分解できない場合（親の不均一な Scale の下の回転）は今回は扱わない。今回の総当たりの書き出しでは出ていない。

## 影響

- Scale を 0 やごく小さな値にして隠したギミックを付けたアバターで、書き出しのたびに出ていたエラーがなくなる。
- サイズ 0 のノードがないアバターは、変換の前後で何もしないので出力は変わらない。

## 検証

- `MissingMeshAttributesTests.ZeroScaleBranchKeepsChildTransformsWithoutValidTrsAssertions`: Scale が 0 と 1e-45 の親（位置と回転を持つ）とその子（位置と Scale を持つ）を付けたアバターを書き出し、書き出しのあいだにエラーのログが出ないこと、親の Scale が元の値・親の位置と回転が X を反転した値・子の位置が X を反転した値・子の Scale が元の値であることを確かめる。`LogAssert` は Unity 内部の `Assertion failed` をテストの失敗として扱わないので、`Application.logMessageReceived` でログを集めて確かめる。変更前のコードでは 2 件とも「Assertion failed on expression: 'ValidTRS()'」で失敗し、変更後は合格した。
