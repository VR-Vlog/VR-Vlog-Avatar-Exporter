# 長さのない Inside カプセルを球で書き出す

- 日付: 2026-11-05
- 対象: `Editor/PhysBoneSpringExport.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

PhysBone のカプセルは、高さが直径以下だと両端が同じ位置になる。UniVRM 0.131 のソルバーの `TryResolveCapsuleCollisionInside` はカプセルの長さの 2 乗で割るので、長さが 0 だと NaN になり、衝突しない。ボーンは Inside の範囲から出ていく。

同梱のソルバーを直接呼んで確かめた結果は次のとおり。半径 1 のコライダーに、中心から 2 離れた点を渡している。

| コライダー | 長さ | 衝突 | 押し戻した先 |
| --- | --- | --- | --- |
| CapsuleInside | 0 | しない | なし |
| CapsuleInside | 1 | する | (0, -1, 0) |
| SphereInside | 0 | する | (0, -1, 0) |

外側のカプセル（`TryResolveCapsuleCollision`）は長さ 0 を球として扱うので、この問題は無い。

## 決定

Inside のカプセルで両端が同じ位置になるものは、`SphereInside` で書き出す。PhysBone では高さが直径以下のカプセルは球と同じ形なので、形は変わらない。半径にワールドスケールを掛ける処理（[2026-11-04](2026-11-04-spring-inside-collider-scale.md)）はこれまでどおり通る。

同じ位置かどうかは `Vector3` の `==` で見る。差が 1e-5 未満の短いカプセルも球になるが、形の差はその長さの分だけで、見た目には影響しない。

外側のカプセルは変えない。動きが同じなので、ファイルを変える理由が無い。

## 影響範囲

検証用プロジェクトの prefab 1506 個を文字列で調べると、Inside のカプセルは 42 個あり、高さが直径以下のものは 0 個だった。prefab の上書きで値を変えているものはこの数に入っていない。実際のアバターで動きが変わる例は確かめていない。

## 確認

- `PhysBoneSpringExportTests` の 60 件が通る（Unity 2022.3.22f1）。修正前のコードでは足した 5 件のうち、長さのない Inside の 3 件が `CapsuleInside` のままで失敗する。
- 上の表は同梱のソルバーの `SpringBoneCollision.TryCollide` を一時的なテストから呼んだ結果で、このテストはコミットしていない。
- アプリの実機では見ていない。
