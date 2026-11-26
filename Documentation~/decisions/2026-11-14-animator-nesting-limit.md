# FX の StateMachine と BlendTree の入れ子に、書き出し全体で 256 階層の上限を置く

- 日付: 2026-11-14
- 対象: `Editor/AnimatorNesting.cs`・`Editor/ExpressionDependencies.cs`（`Controller`）・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ObjectToggleCapture.cs`・`Editor/VrChatGestureExpressions.cs`・`Tests/Editor/AnimatorNestingTests.cs`

## 背景

書き出しの表情・表示切替・ジェスチャーなどの処理は、FX のステートマシンと BlendTree を、入れ子 1 段につき 1 回の再帰でたどる（`ExpressionDependencies` の `Visit`・`InspectMotion`、`ExperimentalExpressionCaptureSession` の `Walk` など 30 か所ほど）。深さに上限が無く、入れ子が深すぎるとスタックを使い切る。Unity はスタックの溢れから戻れず、エディターごと落ちる。

FX の動きを拡張に書く段（`feature/full-reproduction`）は、自分の処理を再帰しない形にし、BlendTree の入れ子に 32 階層の上限を置いた。そのときの測定で、Unity 2022.3.22f1 のエディターの API（`AddStateMachine`）でステートマシンの入れ子の鎖を作ると、256 階層は作れて書き出せたが、1024 階層は作る途中で Unity が `StackOverflowException` を出した。ほかの処理の再帰はそのままだった。

## 決定

- コントローラーを読む前に、ステートマシンと BlendTree の入れ子の深さを再帰を使わずに調べ、どちらかが 256 階層を超えたら `InvalidOperationException`（「FXのStateMachineかBlendTreeの入れ子が上限の256階層を超えています。」）を投げる。調べるのはコントローラーごとに 1 回で、調べ終えたコントローラーは覚えておく。
- 調べる場所は、各処理がコントローラーを取り出す `ExpressionDependencies.Controller` と、これを通らずにコントローラーを読む 3 か所（`CollectTimedClips`・`ObjectToggleCapture.ParametersThatSwitchObjects`・`VrChatGestureExpressions.Resolve`）。30 か所の再帰を 1 つずつ書き直すより変える量が小さく、どの処理が先に読んでも同じ場所で止まる。
- 投げた例外は、ほかの読めない FX（参照の循環など）と同じく、その処理の段の失敗として扱われる。失敗で止めない設定では、その段を外して書き出しを続け、結果一覧の「理由」にこの文が出る。
- 上限は 256 にした。測定で書き出せた深さで、今書き出せているアバターを止めない。実際のアバターの入れ子は数段で、拡張の段の 32 階層にも 92 体で当たったものは無かった。
- 同期レイヤーは、同期元のステートマシンと、同期レイヤーが上書きしたモーションの両方を調べる。
- 循環（BlendTree が自分を含むなど）は深さとして数えない。循環は各処理がこれまでどおり自分の文（「FXのBlendTreeが循環しています。」など）で止める。

## 検討した代替案と不採用理由

- 各処理の再帰を 1 つずつスタックを使う形に書き直す: 30 か所ほどあり、たどる順番や途中の判定を変えずに書き直すのは変更が大きい。順番が変わると、表情の割り当てなどの結果が変わるおそれがある。
- 上限を拡張の段と同じ 32 にする: 拡張は読む側（アプリ）のための上限で、既存の書き出しの表情までは止める理由にならない。測って書き出せた 256 にした。

## 既知の制限

- 書き出しが自分で作る調査用のコントローラーは、元のコントローラーを写したもので、写した後の変更は調べ直さない。元より深くする処理は無い。
- 調べ終えたことはエディターのドメインが読み直されるまで覚えておくので、その間に同じコントローラーを手で 256 階層より深くしても調べ直さない。上限を超えたコントローラーは覚えないので、次の書き出しでも止まる。
- 256 と 1024 の間のどこで既存の再帰が溢れるかは測っていない。

## 確かめ方

- `AnimatorNestingTests` で、BlendTree とステートマシンの入れ子がそれぞれ 256 階層なら通り、257 階層なら止まること、同期レイヤーが上書きしたモーションも調べること、2 つの親から使われる BlendTree は深い方の経路で数えること、循環は深さとして止めないことを確かめる。
