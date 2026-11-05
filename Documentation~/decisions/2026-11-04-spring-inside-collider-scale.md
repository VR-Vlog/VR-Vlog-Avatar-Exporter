# Inside Boundsのコライダーの半径にワールドスケールを掛けて書き出す

- 日付: 2026-11-04
- 対象: `Editor/PhysBoneSpringExport.cs`

## 背景

UniVRM 0.131のソルバー（`SpringBoneCollision`）は、外側の球とカプセルでは半径に`maxColliderScale`を掛けるが、Insideの球とカプセルでは`collider.radius`をそのまま読む。

Root Transformのワールドスケールが1でないコライダーをInside Boundsのまま書き出すと、VRMの中で球が縮み、ボーンが中へ引き込まれる。スケール13.74倍のコライダーを持つアバターでは、半径1.37mの球が0.1mになり、スカートが元と75度ずれて動かなくなっていた。

## 決定

- Insideの球とカプセルは、書き出す半径にRoot Transformのワールドスケールの最大成分を掛ける。スケールの取り方はソルバーの外側の判定に合わせる。
- 外側のコライダーと、カプセルの両端の位置は変えない。

## 残る制約

書き出す半径はUniVRMのソルバーに合わせた値なので、Insideの半径にスケールを掛けるビューアーでは球の大きさが変わる。スケールが1より大きいアバターでは拘束が緩み、1より小さいアバターでは狭くなる。外側の半径にもスケールを掛けないビューアーでは、Insideだけが実寸になり外側は小さいままになる。同梱のUniVRM（0.131.4）やVR Vlogアプリ側のUniVRMを更新するときは、ソルバーの扱いが変わっていないか確かめて、ここも見直す。

## 確かめ方

`InsideCollidersKeepTheirWorldSizeOnAScaledRootTransform`が、Insideの半径だけにスケールが掛かり、外側の半径とカプセルの両端が変わらないことを見る。`InsideCollidersUseTheScaleOfTheirRootTransform`がコライダーと別のRoot Transformのスケールを使うことを、`InsideColliderRadiusComesBackFromTheFileUnchanged`がVRMファイルを読み戻しても半径の値が変わらないことを見る。ソルバーを動かして拘束の位置を見るテストではないので、ソルバーの扱いが変わってもこのテストでは検出できない。

検証環境の実アバターでは、該当のスカート20本の骨の向きの差が74.94度から7.68度になった。書き出したVRMファイルを読み戻しても同じ値だった。
