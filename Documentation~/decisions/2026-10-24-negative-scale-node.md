# 負の拡大縮小で反転した小物を、向きを変えずに書き出す

- 日付: 2026-10-24
- 対象: `Editor/Vrm10AppearanceExporter.cs`・`Tests/Editor/NegativeScaleNodeTests.cs`

## 背景

手のボーンの下に、拡大縮小 (-1, 1, 1) で左右を反転させた小物（MeshRenderer）を置いたアバターを書き出すと、読み込んだ VRM で小物が違う向きに回って付いていた。1 軸か 3 軸を負にした反転で起き、その子のオブジェクトも一緒にずれる。

## 原因

`VrmLib.Model.ConvertCoordinate` は各ノードのワールド行列の X を反転し（`Model.cs` の `ReverseAxisAndFlipTriangle`）、ローカル行列から TRS を作り直す。このとき `Matrix4x4.ReverseX` と `Node.SetLocalMatrix` は行列を正の拡大縮小と `Matrix4x4.rotation` に分ける（`UnityExtensions.cs` の `Decompose`）。反転を含む行列（行列式が負）では `Matrix4x4.rotation` は回転にならないので、反転したノードとその下のノードの回転と位置が崩れていた。

## 決定

書き出しの複製から作ったモデルに対して、拡大縮小の 3 軸の積が負のノードとその下のノードだけ、変換前のローカルの TRS から変換後の値を直接作る。X の反転は F·M·F（F = diag(-1, 1, 1)）なので、位置の x と回転（クォータニオン）の y・z の符号を変え、拡大縮小はそのまま残せば厳密に一致する。

- それ以外のノードは UniVRM の結果をそのまま使うので、反転を含まないアバターの書き出しはバイト単位で変わらない。
- シーンの元のアバターには触れない。

## 成り立たない場合

- SkinnedMeshRenderer のボーンを負の拡大縮小で反転したものは、逆バインド行列が同じ分解を通るので直していない。

## 検証

- EditMode: `NegativeScaleNodeTests.MirroredPropKeepsWorldVerticesAndFrontFacesThroughVrmRoundTrip`（右手の下の回転した親に、(-1, 1, 1)・(1, 1, -1)・(-1, -1, -1) の小物とその子を置いて書き出し、読み込んだ VRM の頂点のワールド位置・法線・面の表の向きが元と一致することを確かめる）。修正前は 3 件とも失敗し、修正後は通る。
- 関連する EditMode のテスト（`RootScaleExportTests`・`HumanoidPoseTests`・`QuickTransferMatchesDefaultNormalTests`・`ConstraintExportTests`・`MissingMeshAttributesTests` など）を修正前と比べ、新しく失敗したテストはない。
