# 常時適用 FX との合成を確かめる 2 回のプローブで、選んだ表情の値に関係しない判定と解析を使い回す

- 日付: 2026-10-21
- 対象: `Editor/VrChatExpressionSampler.cs`・`Tests/Editor/HeldAutomaticExpressionTests.cs`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`・`CHANGELOG.md`

## 背景

`VrChatExpressionSampler.ApplyPermanentOverrides` は、選んだ表情と常時適用の FX の合成を確かめるため、ローカル関数 `Probe` を `Probe(0)` と `Probe(100)` の 2 回呼ぶ。2 回の呼び出しで作るコントローラーは、選んだレイヤーに置くプローブのクリップの定数値だけが違う。

手元のアバター 30 体で測ると、表情の読み込みの合計 491.8 秒のうち、`Probe(0)`・`Probe(100)` の合計が 242.3 秒を占めていた。時間がかかった 4 体では、プローブ 1 回あたり次の時間がかかっていた。

- `ExpressionDependencies.AnalyzeNeutral`: 97〜272 ミリ秒
- `HeldAutomaticExpressionLayers.FindUnownedRandomRest`: 56〜233 ミリ秒
- `SampleNeutral`: 106〜200 ミリ秒

この 4 体では、`AnalyzeNeutral` と `FindUnownedRandomRest` の区間（`op.deps`・`op.randomRest`）の合計が、1 回の書き出しあたり約 140 秒になっていた。

選んだレイヤーが元のスロットに入るとき（`nativeSlot`）について、コードを読んで次のことを確かめた。

- `HeldAutomaticExpressionLayers.Find` と `FindUnownedRandomRest` は、プローブのクリップの値を範囲と定数かどうかの確認でしか読まず、プローブのレイヤー自身は判定の対象から外す。2 回のプローブのあいだにアバターは書き換えない。このため、止めるレイヤーの集合・自動まばたきかどうか・Random の待機レイヤーの集合・保持する BlendShape の集合は、2 回とも同じになる。
- `scalarPlan.CommittedMorphs.ExceptWith` は同じ集合を引くだけなので、2 回目は何も変えない。1 回目の `AnalyzeNeutral` より前に行われる。通知は `Contains` で重複を除いているので、2 回目に同じ文を足すことはない。
- `AnalyzeNeutral` も 2 回とも同じ結果になる。`SampleNeutral` が結果を書き換えるのは `Layers`・`NativeSupportLayers` から同じ入力で求めたレイヤーを引く `ExceptWith` だけで、何度行っても同じになる。

元のスロットに入らないとき（`nativeSlot` でないとき）は、プローブのたびに共有の `originalLayers` の `defaultWeight` と `syncedLayerIndex` を書き換えるので、2 回の結果が同じとは言えない。

## 決定

- `nativeSlot` のとき、1 回目のプローブで求めた `Find`・`FindUnownedRandomRest` の結果（止めるレイヤーの集合は `Find` の結果に Random の待機レイヤーを足したもの）を覚え、2 回目はそれを使う。両方の呼び出しが終わってから覚えるので、途中で例外が出たときは覚えない。
- 覚えた結果を使うときも、`CommittedMorphs.ExceptWith`、止めるレイヤーの置き換え、`controller.layers` の設定、2 つの通知の追加は、これまでと同じ順番で毎回行う。
- `nativeSlot` のとき、1 回目の `AnalyzeNeutral` の結果を 2 回目でもそのまま使う。その後の `SampleNeutral`、`context.UsedParameters` への追加、`NormalInputLayers` は毎回行う。
- テスト用に、実際に判定と解析が走ったことを知らせる `VrChatExpressionSampler.ProbeAnalysisObserver` を足す（`ExportStageGuard.FaultInjection` と同じ形）。

## 変えないこと

- `nativeSlot` でないときは、これまでどおりプローブごとに解析する。共有のレイヤーを書き換える動きもそのままにする。
- 選んだ Parameter Driver の書き込みを確かめる `ExpressionDependencies.Analyze`（`omittedDriverWrites` があるとき）は、毎回行う。
- 覚えた集合は、その後は読むだけで書き換えない。
- 1 つの表情の中だけで使い回し、ほかの表情やほかの `ApplyPermanentOverrides` の呼び出しには持ち越さない。

## 確認したこと

- `HeldAutomaticExpressionTests` に、Random の待機レイヤーと自動まばたきを持つアバターで表情を 1 つ保持したとき、判定と解析がそれぞれ 1 回だけ走り、Random の待機レイヤーの通知が 1 回だけ出て、表情の値がこれまでのテストと同じになることを確かめるテストを足した。
- 毎回計算し直す版と、`AnalyzeNeutral` だけ毎回計算し直す版に一時的に書き換えると、足したテストがそれぞれ判定の回数と解析の回数のところで失敗することを確かめた。
- EditMode のテスト（`HeldAutomaticExpressionTests`・`SelectedLayerControlTests`・`RandomNeutralRestTests`・`NeutralShapeSamplerTests`・`ParameterDriverExpressionTests`・`VrChatMenuExpressionTests`）は 538 件すべて通った。`Tools/test-compatibility-tooling.py UnityProfilePolicyTests` も通った。
- 書き出し結果の突き合わせと時間: （計測後に追記）
