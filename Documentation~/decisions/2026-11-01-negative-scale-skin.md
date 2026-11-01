# 負の拡大縮小で反転したスキンメッシュを、形と面の向きを変えずに書き出す

- 日付: 2026-11-01
- 対象: `Editor/Vrm10AppearanceExporter.cs`・`Tests/Editor/NegativeScaleNodeTests.cs`
- Issue: #590

## 背景

メッシュの Transform の拡大縮小の 3 軸の積が負になっている SkinnedMeshRenderer の衣装（FBX から取り込んだもの）を含むアバターを書き出すと、読み込んだ VRM でその衣装が上下逆さまになって下にずれ、裏返って黒く描かれた。Unity 2022.3 で VRM を読み込んだ時点で崩れていた。

[2026-10-24 の決定](2026-10-24-negative-scale-node.md)でノードの TRS は直したが、「成り立たない場合」に書いたスキンの逆バインド行列が残っていた。

## 原因

Unity の bindposes は「ボーンの worldToLocal × メッシュの localToWorld」なので、メッシュの Transform が反転していると bindposes も反転を含む（行列式が負）。`VrmLib.Model.ConvertCoordinate` は inverseBindMatrices を `Matrix4x4.ReverseX` で X 反転するが、`ReverseX` は行列を位置・`Matrix4x4.rotation`・正の拡大縮小に分けて組み直すので、反転が消える。読み込み側の UniVRM（`NodeImporter`）も同じ `ReverseX` を通す。

確かめた例では、元の bindposes 16 本の行列式が -0.003224 で、書き出した inverseBindMatrices は 16 本とも +0.003224 になっていた。スキンで変形した頂点は、ノードの TRS で置いた頂点に対して y = 0.33m 付近を軸に上下が入れ替わっていた。

## 決定

書き出しの座標変換（`ConvertCoordinateKeepingZeroScale`）の前に、bindposes がすべて反転を含むスキンだけ、反転を bindposes からメッシュのデータへ移す（`UnmirrorSkins`）。F = diag(-1, 1, 1) とすると bindpose·v = (bindpose·F)·(F·v) なので、

- inverseBindMatrices を右から F 倍する。行列式が正になり、`ReverseX` の分解で崩れない。
- メッシュのローカルの頂点位置・法線と、シェイプキーの位置・法線の差分の x の符号を変える。

スキンで変形した頂点と法線は元と同じ値になる。反転を UniVRM の分解に通さない形にしたので、読み込む側の実装によらない。

あわせて、そのメッシュのノードの反転も外す。ワールド行列 W を W·F にし（ローカルの拡大縮小の x の符号を変える）、子のローカル行列を F·L にして子のワールド行列を保つ。スキンはノードの行列を使わないので形は変わらない。

- 面の表の向きを決める行列が読み込む側で違う。glTF の仕様と three.js はメッシュのノードの行列式で決め、Unity はスキンメッシュをルートボーンの空間で描くのでノードの反転を見ない。ノードが反転したままだと、Unity では正しく描かれても three.js では裏返って黒くなった。ノードの反転を外すと、どちらでも元と同じ面が表になる。
- メッシュのノードが同じアバターのボーンを兼ねているときは、ボーンを動かさないようノードはそのままにする。

- bindposes が反転を含まないスキンは触らないので、反転を含まないアバターの書き出しは変わらない。
- 複製から作った書き出し用のモデルだけを書き換え、シーンの元のアバターには触れない。
- 同じバッファを複数のメッシュが共有していても 1 回だけ反転する。

## 成り立たない場合

- 一部のボーンの bindposes だけが反転を含むスキンは、メッシュ全体を一方に合わせられないので変えずに書き出す。
- 接線（tangent）は今までどおり UniVRM の変換のまま。

## 検証

- EditMode: `NegativeScaleNodeTests.MirroredSkinnedMeshKeepsWorldVerticesAndFrontFacesThroughVrmRoundTrip`（3 軸・1 軸の反転と、Z だけ負の拡大縮小で、回転した親の下のスキンメッシュを 3 本のボーンで変形させて書き出し、読み込んだ VRM でスキンを掛けた頂点のワールド位置・法線と、面の表の向きをルートボーンで決めたとき（Unity）とメッシュのノードで決めたとき（glTF・three.js）の両方が元と一致することを確かめる）。修正前は 3 件とも失敗し、修正後は通る。
- EditMode: `NegativeScaleNodeTests.UnmirrorSkinsMovesTheMirrorIntoMeshAndBlendShapes`（シェイプキーの差分もボーンの空間で元と一致すること、メッシュのノードの反転が外れて子のワールド行列が変わらないこと、一部のボーンだけ反転したスキンは変わらないこと）。
- 実際のアバターで、反転したスキンの衣装を書き出し直し、inverseBindMatrices の行列式が正になり、スキンで変形した頂点の範囲がノードから求めた範囲と一致すること、Unity で VRM を読み込んだときと three-vrm で描いたときの両方で、衣装が元の位置に表を向いて描かれることを確かめた。
