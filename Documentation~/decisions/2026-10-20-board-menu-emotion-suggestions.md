# 表情・ポーズ指定UIの喜怒哀楽の初期値に、VRChatメニューの項目名を使う

- 日付: 2026-10-20
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`（`MenuClipPaths`）・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`（`SuggestPresets`）・`Tests/Editor/ExpressionPresetTests.cs`・`README.md`

## 背景

書き出しの比較で、同じアバターでも **アプリに転送**（かんたん）では喜怒哀楽が 4 つ埋まり、表情・ポーズ指定UI（詳細）では 0 のままのものがあった。FX のクリップ名は「Face 1」のように何も言っておらず、メニューの項目名（`Face / Smile` など）にだけ感情の語があるアバターである。

かんたんの書き出しはメニュー表情ごとにメニューの経路を `ExpressionNameInference.MatchMenuPath` で読む（`2026-10-18-emotion-preset-inference.md`）。詳細の画面の候補は `AnimationClip` だけで、メニューの経路を持っていなかったので、クリップ名・英語の BlendShape 名・動かす BlendShape の組み合わせからしか初期値を選べなかった。

## 決定

- アバターの VRChat Expressions Menu を `VrChatExpressionMenu.Read` で読み、Toggle と Button の項目ごとに、その項目が選ぶ FX のクリップを `MenuClipPaths` で求める。クリップ → メニューの経路（`A / B / C`）の対応で、同じクリップを選ぶ項目が複数あればメニューの順にすべて持つ。
  - 対象は、FX のレイヤー（サブステートマシンも再帰で読む）で `motion` が `AnimationClip` そのもののステート。そのステートへ入る遷移（Any State・ステートからの遷移・Entry の遷移・サブステートマシンからの遷移）が次をすべて満たすときだけ、項目とクリップを結ぶ。
    1. 項目のパラメーターの条件があり、項目のパラメーターだけを既定値（Toggle を切った状態・Button を離した状態）に戻すと、そのどれかが偽になる。既定値は Expression Parameters の `defaultValue`、無ければ 0。
    2. Expression Parameters の既定値に、項目と親のサブメニューが決める値を重ねた値で偽になる条件が無い（`ExpressionDependencies.IsFalse`）。`Face == 1 && Variant == 1` は、`Variant` の既定値が 0 で項目もサブメニューも `Variant` を 1 にしないなら結ばない。
  - 1 つの項目が選ぶクリップが全レイヤーを通して 2 つ以上あるときは、その項目はどのクリップとも結ばない。`Face / Smile` が `Face = 1` にし、Eyes・Mouth・Brow の各レイヤーが `Face == 1` でそれぞれのクリップを出すアバターでは、どのクリップも顔の一部でしかないので、メニューの経路を持たせない。
  - 既定値でも真になる遷移（`Face < 2` のような、切っていても出る顔）は、その項目が決める顔ではないので結ばない。
  - 読めない・上限を超えるなど、途中で例外が出たときは対応なしとして扱い、画面は従来どおり開く。
  - 対応はセッションごとに 1 回だけ読む。元のアバターが変わるとセッション自体が使えなくなるので、作り直しは要らない。
- `SuggestPresets` は、クリップ名が何も言っていない（`ExpressionNameInference.SaysNothing`）クリップについてだけ、そのクリップのメニューの経路を `MatchMenuPath` で読む。`Mouth_Smile`・`EyeSmile` のように部位の語で感情が打ち消された名前は、経路が `Face / Smile` でも感情を入れない。段（Tier）はかんたんと同じで、クリップ名で決まった候補と同じ並びに入る。
  - クリップ名で決まったものはメニューの経路より優先する。
  - 経路どうしで感情が食い違うとき（`Face / Smile` と `Favorite / Sad` が同じクリップを選ぶ）は、どれも使わない。同じ感情を指す経路が複数あるのはよい。
  - 動かす BlendShape の組み合わせから選ぶのは、クリップ名とすべてのメニューの経路が何も言っていないときだけにした。かんたんが「何も言っていないメニュー表情」だけを形から読むのに合わせた。
- 入れた初期値は従来の自動の候補と同じ扱い（`PresetAssignment.Automatic`）で、画面で外せる。外した枠（`PresetAssignment.None`）と作者が設定した VRM の表情（`HasAuthoredPreset`）は置き換えない。
- 表情・ポーズ指定UIの書き出しは `inferEmotionPresets: false` のまま。2026-10-18 の決定の理由（画面で「なし」にした枠が書き出し時に勝手に埋まらない）はそのままで、今回は画面に見える自動の候補を増やすだけなので食い違わない。
- 自動の候補の理由（どの名前から選んだか）を枠に出す仕組みは今も無い。クリップ名・BlendShape・形から選んだ候補も理由を出していないので、メニューの経路だけを出す変更はしなかった。

## 検討した代替案と不採用理由

設計レビュー（Codex 相談）で出た案のうち、次を採用した。

- 項目の値で真・既定値で偽の条件があるときだけ結ぶ（パラメーターを参照しているだけの遷移では結ばない）。
- 経路どうしで感情が食い違うクリップには候補を出さない（メニューの順で先のものを選ばない）。
- 枠に理由を出すなら文言は「メニュー名から自動推定: <経路>」とする（今回は理由の表示自体を足していない）。

次は採用しなかった。

- メニューの経路を自動では入れず、候補の並び順にだけ使う: オーナーの指示が「かんたんの推定を初期値として入れる・外せるようにする」なので、初期値として入れた。
- 枠の近くに「メニュー名から補完」のボタンを置く: 同じ理由。自動の候補は既に外せて、外した枠は再び埋まらない。

## 限界

次のメニュー表情は詳細の画面の候補（1 つのクリップ）にならないので、今回も初期値に入らない。こうしたアバターでは、かんたんのほうが喜怒哀楽を多く埋めることがある。

- 複数のレイヤーを組み合わせて 1 つの顔になるもの（上の「1 つの項目が選ぶクリップが 2 つ以上」の規則で、部分のクリップにはメニューの経路を持たせない）
- Blend Tree・サブステートマシンそのものへ入る遷移で出るもの
- Radial Puppet など、連続調整の項目
- BlendShape 以外（マテリアル・オブジェクトの表示など）も動かすクリップ（画面で「対応外」になる）
- Modular Avatar の Merge Animator など、FX 以外の Controller にある遷移

## 確認

- `ExpressionPresetTests` に次を足した（Unity での実行は別途）。
  - `GenericallyNamedClipIsSuggestedByTheMenuItemThatSelectsIt`
  - `ClipNameWinsOverMenuPathAndTheBoardKeepsRemovalsAndAuthoredPresets`
  - `ClipSelectedByNoMenuItemIsSuggestedAsBefore`
  - `TransitionFalseUnderTheItemValueDoesNotLink`
  - `TransitionAlsoTrueAtTheOffValueDoesNotLink`
  - `MenuPathsThatDisagreeGiveNoSuggestion`
  - `FaceSplitOverLayersGivesNoMenuSuggestion`
  - `ClipNamingAPartTakesNoEmotionFromItsMenuPath`
  - `OtherParameterOffItsDefaultStopsTheLink`
  - `BoolToggleLinksItsClip`
