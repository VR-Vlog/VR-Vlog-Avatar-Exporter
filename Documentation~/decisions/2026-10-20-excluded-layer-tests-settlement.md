# ExcludedLayerTests を、時間で決める表情の最終状態と警告を確かめるテストに直す

- 日付: 2026-10-20
- 対象: `Tests/Editor/ExcludedLayerTests.cs`

## 背景

統合ブランチで `ExcludedLayerTests` の 6 件が落ちていた。どれも「書き出さないオブジェクトだけを動かすレイヤーではないので外さない」ことを確かめたあと、`VrChatExpressionSampler.Sample` が `InvalidOperationException` を投げることを期待していた（1 件は「時間で遷移するFX状態は固定表情に変換できません」の文も確かめていた）。

459db56（`2026-10-19-expression-settlement.md`）で、メニューの表情の exit time の遷移は最後までたどって最終状態の値を書き出し、「表情の最終状態をAnimatorの時間遷移から確定しました」の警告を付けるようになった。`Sample` は例外を投げずに値を返すので、例外を待っていたテストだけが落ちる。459db56 の 1 つ前のコミットでは 11 件すべて通り、459db56 で 6 件が落ちることを EditMode テストで確かめた。459db56 は時間で決まる既存のテストを書き換えたが、このファイルは対象から漏れていた。

実装の回帰ではない。レイヤーを外すかどうかの判定（`FindExcludedLayers`）の結果は変わっておらず、各テストの前半の判定は通っている。

## 決定

例外を待つ代わりに、外さなかったレイヤーの exit time をたどったことを、警告 `ExpressionSettlement.FollowedTransitions` が付くことで確かめる。レイヤー 0 のメニューの遷移は exit time を持たないので、この警告はペットのレイヤーの遷移からしか出ない。あわせて最終状態の値を確かめる。

- 入れ子のステートマシンの中で、あとから顔を 90 にするステート: 最終状態の 90 になる（レイヤーを外していれば、メニューの値の 60 のまま）。
- 顔を一度も動かさない未来のステート（Write Defaults オン・空のクリップ・Motion なし）: 顔はメニューの値の 60 のまま。
- Blend Tree の重み 0 の葉と、AnimatorOverrideController で差し替えたクリップ: 最終状態はペットだけを動かす Write Defaults オフのステートなので、レイヤー 0 の 60 が残る。

## 確認したこと

- Unity 2022.3.22f1 の EditMode テストで `ExcludedLayerTests` の 11 件が通る。
- EditMode テスト全体の結果は PR 本文に書く。
