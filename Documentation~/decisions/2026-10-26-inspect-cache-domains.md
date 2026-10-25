# FX の走査結果のキャッシュのキーに domains を入れる

- 日付: 2026-10-26
- 対象: `Editor/ExpressionInspectCache.cs`・`Editor/ExpressionDependencies.cs`・`Tests/Editor/ExpressionInspectCacheTests.cs`

## 背景

`ExpressionDependencies.NormalInputLayers` は、FX の Set だけが書き込む内部パラメーターに取りうる値の集合（domains）を持たせ、到達する状態が変わらなくなるまで `Inspect` を繰り返す（`2026-10-25-face-bone-rest-pose-not-appearance.md`）。公開側の `Inspect` は domains を渡されると `ExpressionInspectCache` を使わずに毎回グラフを走査していた。domains は表情のプローブごとに同じ中身で作り直されるので、FX の大きいアバターでは同じ走査が何度も繰り返され、書き出しが 1 体 22 秒から 34 秒に遅くなっていた。

## 決定

- `ExpressionInspectCache.Signature` に domains を受ける引数を足し、キーに含める。`Inspect` は domains の有無に関係なく `Signature` にキーを作らせる。
- キーには、名前を ordinal の順に並べ、`名前の長さ:名前=` のあとに集合の値を float のビット列（`BitConverter`）の昇順でカンマ区切りに並べる。domains が null のときと空の辞書のときは別のキーにする。
- `neutralFixed` と同じく、同じ中身でも検索の結果が変わりうる入れ物はキャッシュしない。domains が `Dictionary<string, ISet<float>>` で比較子が ordinal か既定でないとき、どれかの集合が `HashSet<float>` で比較子が既定でないときは、キーを作らない。
- 再生（replay）はそのまま使う。domains が変えるのは、`NeutralStates` で到達する状態と、`Conditions` で刈る遷移だけで、その結果はどれも走査の記録（`Layer` の複製・たどったクリップの順番・`drivers`・`unknown`）に入る。パスの除外を当て直すのはこれまでどおりクリップのバインディングから作る値だけで、domains には関係しない。

## 確認

- `ExpressionInspectCacheTests` で、domains の 4 通り（`Menu` が {0}・{1, 0}・null・空の辞書）の結果がキャッシュなしの走査と 2 巡とも一致すること、同じ中身の domains を別のオブジェクトで渡しても当たること（{0, 1} と {1, 0} も同じキー）、null と空の辞書が別の項目になること、比較子が ordinal でない辞書と `HashSet` 以外の集合はキャッシュしないことを確かめる。

- 計測用に作ったコントローラー（40 レイヤー・各 26 ステート・200 クリップ・各クリップ 60 本の BlendShape 曲線）に、同じ中身の domains を毎回新しいオブジェクトで渡して `Inspect` を 100 回呼ぶと、変更前は 5845 ms（キャッシュに入らない）、変更後は 2327 ms（1 回外れ・99 回当たり）だった（Unity 2022.3.22f1、同じ PC で 1 回ずつ）。アバターでの書き出しの時間は測っていない。

## 欠点

- 浮動小数の 0 と -0、NaN の違う表現は、集合としては同じでもキーが分かれる。キャッシュに当たらないだけで、結果は変わらない。
