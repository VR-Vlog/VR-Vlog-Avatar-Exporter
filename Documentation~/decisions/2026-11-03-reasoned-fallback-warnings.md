# 保守側に倒した理由を警告に出し、Random の停止を例外の型で運ばない

- 日付: 2026-11-03
- 対象: `Editor/VrChatParameterDriver.cs`・`Editor/ExpressionEvaluationSession.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/ExpressionGestureCapture.cs`・`Editor/NeutralInputProof.cs`・`Editor/NeutralShapeSampler.cs`・`Editor/PoseMenuResolver.cs`・`Editor/PoseExportSession.cs`・`Editor/Locales/*.json`

## 背景

Parameter Driver の Random に当たったことは、専用の例外の型 `RandomCapability` で投げ、基準形の評価の最後でその型が内側にあるかをたどって見分けていた。Random は書き出しを止めるデータの誤りではなく、待機表情を固定できないことを伝える通常の結果なのに、例外の型だけがそれを運んでいた。

`NeutralInputProof.Read` と `PoseMenuResolver.CannotSelectBody` は、読み取りに失敗すると保守側（入力をすべて分からないものとする・すべての検査を行う）に倒すが、失敗の理由を捨てていた。書き出し後に「なぜそうなったか」を追えなかった。

## 決定

- `VrChatParameterDriver.Execute` は、Random で止まったときにその理由の文字列を返し、何も止まらなければ null を返す。データの誤りは今どおり例外で止める。
- 評価のセッションは、入場時に Random で止まった失敗を `RandomStop` として持つ。基準形の評価はその失敗と同じものかで見分け、例外の型をたどらない。`RandomCapability` と `IsRandomCapability` は消した。
- `NeutralInputProof.Read` と `CannotSelectBody` は、保守側に倒す動きをそのままにし、理由を既存の警告の一覧に 1 回だけ足す。新しい通知の経路は作らない。
  - `NeutralInputProof`: 「Expressions MenuとExpression Parametersを読み取れなかったため、どのパラメーターもメニューで変わりうるものとして通常時の表情を評価しました: 」に理由を続ける。基準形の評価（`NeutralShapeSampler.Sample`）からだけ警告の一覧を渡す。表情の解析（`VrChatExpressionSampler.Analyze`）は同じ読み取りを先に行うが、警告の一覧を持たないので渡さない。
  - `CannotSelectBody`: 「ポーズ: 体の姿勢を変えないメニューかを判定できなかったため、すべてのメニューで姿勢を確かめました — 」に理由を続ける。失敗はメニューの項目によらずレイヤーの読み取りで起きるので、項目ごとには出さない。
- `PoseMenuResolver` の、メニューの遷移を除いて解き直す所（`selectedByMenu`）の try/catch は残した。`Resolve` の例外は他の 2 か所で行の誤りとしてそのまま使っており、理由つきの結果に変えると `Resolve` の中の throw をすべて書き換えることになり、今より複雑になるため。

## 書き出しへの影響

VRM の中身は変わらない。Random で止まる場所・それまでに実行した操作・評価の失敗の文言は今と同じ。増えるのは、読み取りに失敗したときの警告 1 行ずつだけで、読み取れる通常の書き出しでは出ない。

## 確かめたこと

- `NeutralShapeSamplerTests.UnreadableMenuInventoryStaysUnprovedAndWarnsOnceWithItsReason`: 読み取れるときは警告が出ず、読み取りに失敗したときは入力が分からないままになり、2 回読んでも警告は 1 行だけになる。
- `PoseRegistrationTests.UnreadableBodyReachKeepsTheFullProofAndWarnsWhy`: 判定に失敗したメニューは今どおりすべての検査を通って同じ誤りで弾かれ、理由つきの警告が 1 行出る。ほかの場合は警告が出ない。
- Random の既存のテスト（`RandomNeutralRestTests`・`TemporalNeutralShapeTests`・`NeutralShapeSamplerTests`）は、型を見分けていた 1 行ずつを消し、残りはそのまま通る。
