# VRM SpringBone のチェーンに同じジョイントが続いても揺れものの変換を止めない

- 日付: 2026-11-09
- 対象: `Editor/PhysBoneSpringExport.cs`

## 背景

`Convert` は、手作りの VRM SpringBone のチェーンのジョイントの間にある中間のボーンを予約するため、親に向かって辿る。`Transform.IsChildOf` は自分自身にも true を返すので、同じジョイントが続けて入っていると親を null まで辿って `NullReferenceException` になっていた。Inspector のリストの「+」は末尾の要素を複製するので、普通の操作で起きる。

## 決定

- 同じ Transform が続くときは辿らない。`existing.Add` は済んでいるので、予約の結果は変わらない。
- 重複したジョイントそのものは、この変更では取り除かない。PhysBone が無い経路でも今のまま重複して書き出されており、扱いを決めるのは別の話になる。

## 検討した代替案と不採用理由

- 重複を取り除いてから変換する: 重複があるときの出力が変わる。今回は落ちないことだけを直す。

## 確かめたこと

`PhysBoneSpringExportTests.AJointListedTwiceInAuthoredChainDoesNotStopConversion` は修正前に失敗し、修正後に成功する。同じクラスの既存のテストと、手作りのチェーンと PhysBone を持つアバターの書き出しの比較で、重複が無い入力の出力が変わらないことを見た。
