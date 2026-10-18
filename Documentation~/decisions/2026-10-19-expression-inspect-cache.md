# 表情の読み込み中は FX の依存関係の走査結果を使い回す

- 日付: 2026-10-19
- 対象: `Editor/ExpressionInspectCache.cs`・`Editor/ExpressionDependencies.cs`・`Editor/VrChatExpressionSampler.cs`・`Tests/Editor/ExpressionInspectCacheTests.cs`・`CHANGELOG.md`

## 背景

表情の多いアバターでは、書き出しの時間の大半が表情の読み込み（`VrChatExpressionSampler.Analyze`）にかかる。Stopwatch で区間を測ると、その中で最も長いのは `ExpressionDependencies.Inspect` だった。Inspect は Animator Controller のレイヤー・ステート・遷移・Behaviour・クリップを Unity の API でたどり、レイヤーごとに読むパラメーター・書くパラメーター・動かす変形などをまとめる。

計測した 6 体のうち 2 体の内訳（Unity 2022.3.22f1、1 回ずつ）:

| アバター | 書き出し全体 | Inspect の合計 | Inspect の呼び出し | 引数の組み合わせ |
|---|---|---|---|---|
| shinano | 287.1 秒 | 172.2 秒 | 15005 回 | 715 通り |
| custa | 24.3 秒 | 11.7 秒 | 2518 回 | 183 通り |

shinano では、Inspect の呼び出しのうち 12845 回がアセットとして保存されているコントローラー（NDMF の一時アセットを含む）に対するもので、残りの 2160 回が評価用に作るプローブ（`HideAndDontSave`）に対するものだった。呼び出し元は `AdditionalParameterConstants.Prove`・`InspectOtherControllers`・`NormalInputLayers` などに分かれていて、同じグラフを同じ条件で何度も走査している。

## 決定

- `ExpressionInspectCache` を足し、`VrChatExpressionSampler.Analyze` の間だけ有効にする（`using var inspectCache = ExpressionInspectCache.Begin();`）。スコープの外では Inspect の動作は変わらない。
- キャッシュの対象は、コントローラーが保存済みのアセット（`EditorUtility.IsPersistent`）で `DontSave` が付いていないときだけにする。Override Controller は元のコントローラーも同じ条件を満たすときだけ。Analyze の中でグラフを書き換えている箇所（`ExpressionEvaluationSession`・`ExperimentalExpressionControllerScope` のコピー、`NativeTreeTrajectory` の選択状態の差し替え、`VrChatExpressionSampler` のプローブ、FaceEmo の表情の複製）は、どれも `HideAndDontSave` の複製に対するもので、アセットのコントローラーとクリップは書き換えていない。
- キーは、コントローラーのインスタンス ID・`allowFxControls`・`typedConditions`・`fxLayerCount`・`neutralFixed` の中身（名前の順に並べ、値は float のビット列）にする。`neutralFixed` が ordinal 以外の比較子を持つ辞書や `Dictionary` 以外の実装のときは、同じ中身でも検索の結果が変わりうるのでキャッシュしない。
- パスの除外（`excludedPath`）はキーに含めない。除外の影響を受けるのはクリップのバインディングから作る値（`Morphs`・`HasBindings`・`NonMorphBindings`・`DynamicMorph`）だけなので、1 回目の走査では除外に関係しない値と、たどったクリップの順番だけを記録する。2 回目以降は、記録したクリップのバインディングに呼び出し元の除外を当てて、これらの値を作り直す。`Morphs` に入る順番も 1 回目の走査と同じになる。
- 走査中に呼び出し元の `drivers` と `unknown` へ加えた内容は順番どおりに記録し、2 回目以降は同じ条件（`drivers` にまだ無いときだけ追加）で再生する。
- 2 回目以降に返す `Layer`・`Program`・`Operation`・`SdkLayerWeightControl` は毎回新しく作る。呼び出し元が結果を書き換えても次の呼び出しに影響しない。1 回の結果の中で同じ `Program` を共有している箇所（`DriverPrograms`・`FxCommands`・`drivers`）は、複製でも同じオブジェクトを共有させる。`AdditionalParameterConstants.Prove` などの `Distinct()` は参照で比較しているので、呼び出しごとに別のオブジェクトになる点も元と同じにしている。
- クリップのバインディングの一覧と、変形の値が動くかどうか（`DynamicMorph` の判定）は、保存済みのクリップに限ってスコープの中で使い回す。
- `excludedPath` の答えは、1 回の Inspect の中ではパスごとに 1 度だけ聞く。大きな FX では、同じ Renderer のパスがほとんどのクリップに出てくる。キャッシュの有無に関係なく、同じ呼び出しの中で同じパスに違う答えを返す `excludedPath` は無い前提にしている。

## 確認したこと

- 8 体のアバターを同じ PC・同じ Unity プロジェクトで書き出し、変更前（`dcc9e28`）と変更後の VRM の sha256 を比べた。書き出せる 6 体（custa・kikyo・kipfel・mamehinata・ququ・shinano）はすべて一致した。残りの 2 体は変更前から書き出せない（chocolate は NDMF の処理が環境の都合で止まり、noraneko は `FixedNeutralValues` で `TrackingType` が重複するという別の不具合で止まる）。
- 書き出しの時間（1 回ずつ、同じ PC で別の Unity が動いていたので数秒の揺れがある）:

| アバター | 変更前 | 変更後 | Inspect の合計（前→後） |
|---|---|---|---|
| custa | 24.3 秒 | 14.5 秒 | 11.7 → 1.8 秒 |
| kikyo | 65.0 秒 | 44.5 秒 | 28.0 → 7.4 秒 |
| kipfel | 302.5 秒 | 187.6 秒 | 144.8 → 29.8 秒 |
| mamehinata | 136.2 秒 | 76.5 秒 | 75.8 → 17.5 秒 |
| ququ | 3.3 秒 | 3.3 秒 | — |
| shinano | 287.1 秒 | 144.6 秒 | 172.2 → 33.1 秒 |

  キャッシュの当たりは約 99.7%（shinano で 12780 回当たり、65 回外れ）。`excludedPath` の答えを 1 回の Inspect の中で使い回す前は、shinano で `excludedPath` が 950 万回呼ばれて 63 秒かかっていた（使い回したあとは 3 万回）。Inspect の残りは、当たったときのバインディングの作り直しと、キャッシュしないプローブの走査が半分ずつ。
- `ExpressionInspectCacheTests` で、保存済みのコントローラーに対して、除外と `neutralFixed` の組み合わせ 5 通りの結果（集合の順番を含む）と `drivers`・`unknown` が、キャッシュなしの走査と 2 巡とも一致することを確かめる。ほかに、2 回目以降に返すオブジェクトが新しいこと、プローブ（未保存のコントローラー）はキャッシュしないこと、スコープを閉じると無効になること、複製するクラスに新しいフィールドが足されたら失敗すること、を確かめる。
