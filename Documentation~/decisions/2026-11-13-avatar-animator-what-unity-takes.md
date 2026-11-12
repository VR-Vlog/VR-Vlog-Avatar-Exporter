# FXの動きの拡張には、Unityが実際に通す遷移と読むパラメーターだけを書く

- 日付: 2026-11-13
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`、各言語のロケール

## 背景

`VRVLOG_avatar_animator`（2026-11-12の決定記録）は、FXの遷移をMuteとSoloの印つきでそのまま書き、条件が指すパラメーターは名前があれば表に足していた。読む側（アプリ）が、Unityと同じ扱いをすべて自分で再現しないといけない形だった。

レビューで、FXが宣言していない名前の条件や、型に合わない種類の条件がUnityでどう扱われるかが決まっていないと指摘があった。推測で決めず、Unity 2022.3.22f1のEditModeで`Animator.Update`を回して、ステートの移り方を5回に分けて測った。

## 測って分かったこと

1. 条件が通るのは、boolに`If`・`IfNot`、triggerに`If`だけ、floatに`Greater`・`Less`、intに`Greater`・`Less`・`Equals`・`NotEqual`。それ以外の組み合わせ、コントローラーが宣言していない名前、名前の無い種類の条件を持つ遷移は、ステート・Any State・Entry・サブステートマシンから出る遷移のどれでも通らない。Exit Timeがあっても、ほかの条件が成り立っていても通らない。その遷移より後ろに並ぶ読める遷移は通る
2. MuteとSoloは実行時にも効く（ステートの遷移、ルートのAny State、Entry）。Soloの遷移は、自分がMuteでも、読めない条件を持っていても、同じ並びのSoloでない遷移を止める。サブステートマシンから出る遷移では、MuteもSoloも効かない
3. floatでないパラメーターをブレンドツリー・Directの重み・ステートのSpeed・Motion Time・Cycle Offsetに使うと、Unityはその値を読まず、別のfloatの値を読む。どのfloatになるかはパラメーターの並び順で決まり、アバターの意図とは関係がない
4. 宣言の無いパラメーターを使う1D・2Dのツリーは、ステートの直下なら何も再生しない。ほかのツリーの子になっていると、親の重みの残りで再生され、結果はツリーだけからは決まらない
5. ステートのSpeed・Motion Time・Cycle Offsetのパラメーターが宣言されていなければ、設定しなかったときと同じになる
6. レイヤーは既定のステートから始まり、ルートのEntryの遷移は始まるときには見られず、Exitから入り直すときだけ見られる。サブステートマシンのEntryの遷移は、条件・Mute・Soloとも上と同じ決まりで見られる
7. サブステートマシンが持つAny Stateの遷移は、どの深さのステートから始めても、行き先がどこでも通らない（3段の入れ子まで確認）。そのSoloもルートのAny Stateを止めない。ルートのAny Stateは入れ子の中のステートへも通り、ステート自身の遷移より先に見られる

## 決定

- パラメーターの表はExpression ParametersとFXのパラメーターだけにする。宣言の無い名前を足さない
- 遷移は、Unityが通さないものを書かない: 行き先の無いもの、読めない条件（1の外のもの）を持つもの、Muteのもの、Soloの遷移と同じ並びにあるSoloでないもの。MuteとSoloは書き出すときに当てて、形式から`mute`と`solo`を外す。サブステートマシンから出る遷移は2のとおり全部書く
- Any Stateの遷移はルートのステートマシンの分だけを書く。読む側は、ルート以外のステートマシンにAny Stateの遷移があるデータを拒む
- パラメーターが読めない1D・2Dのツリーは「モーション無し」、重みが読めないDirectの子は書かない、読めないステートの設定は「設定なし」にする。3と4のUnityの動きはアバターの意図と関係なく、データからも決まらないので再現しない。この3つは`omitted`に数えない。数えると、Unityで何も起きないものまで「省いた」と出るため
- Parameter Driverの操作で、名前（`copy`の`source`も）が表に無いものは書かず、`omitted`の`operation`に数える。`omitted`の種類は10個になる
- メニューの項目は、Expression Parametersが宣言した名前だけを指す
- 読む側の検査に、条件の種類とパラメーターの型の組み合わせ（`AvatarAnimatorData.ConditionTypes`）、ツリー・Directの重み・ステートの設定がfloat（Mirrorはbool）であることを足す
- 2026-11-12の決定記録で次の変更に回した、ブレンドツリーをたどり方ごとに展開した後の件数の上限を、この変更で足す。1つのコントローラーで4096個まで（`MaximumExpandedMotions`）とし、超えたら段を外して、理由に「BlendTreeに含まれるモーションが上限の4096個を超えています。」を出す。読む側も同じ上限で拒む
- 再生する側の決まり（6と7）は`Formats/README.md`とスキーマの説明に書く

## 今回は変えないもの

- 表情の解析（`ExpressionDependencies`など）は、サブステートマシンから出る遷移にもMuteとSoloを当て、サブステートマシンのAny Stateも有効として扱っている。7の測定とは違うが、今の書き出しの結果が変わるので、この変更では触らない

## 確かめ方

- EditModeのテスト（`AvatarAnimatorCaptureTests`・`AvatarAnimatorDataTests`）に、1〜7の決まりごとのケースを足した
- 手持ちのアバターを前と同じ設定で書き出し、拡張の外は1バイトも変わらないことを、比較ツールの`--allow-added-extension VRVLOG_avatar_animator`で確かめる
