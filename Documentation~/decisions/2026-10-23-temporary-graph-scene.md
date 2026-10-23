# 表情サンプリングの一時 Animator グラフを、シーンを未保存にしない作り方で組む

- 日付: 2026-10-23
- 対象: `Editor/VrChatExpressionSampler.cs`（`PrivateState`・表情を直接当てるときの一時グラフ・関係ないレイヤーの置き換え）・`Editor/ExpressionEvaluationSession.cs`・`Tests/Editor/TemporaryGraphSceneTests.cs`・`CHANGELOG.md`

## 背景

表情のサンプリングでは、元の FX を写した保存しない一時の Animator グラフ（`HideAndDontSave`）を組み、表情に関係ないレイヤーを空の待機ステートだけのレイヤーに置き換えている。この待機ステートと、表情を直接当てるときの子ステートマシン・入口の遷移・ステートを `AnimatorStateMachine.AddState`・`AddStateMachine`・`AddEntryTransition` で作っていた。これらは Undo を記録し、グラフが保存されない隠しオブジェクトでも、開いているシーンを未保存の状態にする。

Unity 2022.3 で、保存したばかりのシーンで `HideAndDontSave` のステートマシンに `AddState` を 1 回呼ぶだけで `Scene.isDirty` が true になることと、表情に関係ないレイヤーを持つ FX で表情を 1 つサンプリングするとシーンが未保存になることを確かめた。書き出し用のコピーはすでにプレビューシーンで作っていて、シーンを汚さない。利用者から見ると、何も変えていないのに表情ボードを開く・書き出すだけでシーンに未保存の印が付き、閉じるときに保存を求められる。

## 決定

- 一時グラフのステートは `new AnimatorState`（`HideAndDontSave`）を作って `states` の配列に足す（`VrChatExpressionSampler.PrivateState`）。子ステートマシンと入口の遷移も `new` で作って `stateMachines`・`entryTransitions` の配列に入れる。元の FX を写す部分は以前から `new` と配列の代入で組んでいたので、同じ書き方に揃えた。
- 作ったものは、これまでどおり一時グラフの破棄の一覧に入れる。
- 書き出し用のコピーをプレビューシーンで作る仕組み（`NdmfExportPreparation.InstantiateOwnedCopy`）は変えない。

## 確認

- `TemporaryGraphSceneTests.SamplingAnExpressionKeepsTheSavedSceneUnmodified` で、表情に関係ないレイヤーを持つ FX を保存済みシーンでサンプリングし、表情の値が取れ、シーンが未保存にならないことを確かめる。変更前の develop では同じ手順でシーンが未保存になった。

## 欠点

- `AnimatorStateMachine` の `Add*` を新しく一時グラフに使うと同じことが起きる。一時グラフでは配列の代入で組む。
