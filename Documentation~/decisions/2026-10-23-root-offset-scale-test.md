# ルートが原点になく拡大縮小もあるアバターの書き出しをテストする

- 日付: 2026-10-23
- 対象: `Tests/Editor/RootScaleExportTests.cs`
- 関連: [2026-10-22-root-transform-export.md](2026-10-22-root-transform-export.md)

## 背景

2026-10-22にルートを書き出しの間だけ原点・回転なしに置くようにしたとき、テストはルートの拡大縮小が1の場合（`RootPlaceInTheSceneIsNotExportedAndIsRestored`）と、原点で拡大縮小だけがある場合（`UniformRootScaleReachesSceneRootNodesAndIsRestored`）に分かれていた。両方が同時にあるとき、`RootScaleBake.Apply`は子に拡大縮小を焼き込んでから`Place`でルートを原点に置くが、この組み合わせを確かめるテストが無かった。

## 決定

`ScaledRootAwayFromTheOriginIsExportedAtTheOriginAndIsRestored`を足す。ルートを(-10, 0.25, 3)・Y 90°・拡大縮小0.5に置いて書き出し、Hipsが(0, 0.5, 0)・拡大縮小0.5・回転なしで書かれること、書き出し後に複製のルートの位置・回転・拡大縮小と全ノードのローカルの値が元に戻ることを確かめる。

製品の動作は変えない。
