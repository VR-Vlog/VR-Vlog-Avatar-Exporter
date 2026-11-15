# 評価器が、コントローラーの下の姿勢がどれだけ残るかを出す

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`（`HostShare`）、`Tests/AnimatorComparison/AvatarAnimatorComparisonCases.cs`（`actionOverHands`・`HostPose`）、`Tests/AnimatorComparison/AvatarAnimatorEvaluatorComparisonTests.cs`（`CompareOverHost`）、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Tools/AvatarAnimatorSettledCheck.cs`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-4）

## 背景

アプリは、GestureとActionのマッスルを自分の姿勢（トラッキング・エモート・歩き）の上に重ねる。VRChatでは一番下のBaseが歩きの姿勢を全身に書き、GestureとActionはその上に重みで重なる。アプリではBaseに当たるのがアプリの姿勢で、評価器はそれを知らない。

評価器は、マッスルを書くコントローラーについて、マスクの中のマッスルを重みに関係なくすべて書いたことにする（クリップに無いマッスルは休みの値）。Unityがhumanoidのレイヤーの姿勢をまとめて1つの姿勢として作るため（`2026-11-16-avatar-animator-muscle-empty-side.md`）。そのため、重みが0のActionも体じゅうのマッスルを`Written`にし、値は休みの値と混ぜた結果になる。アプリが`Written`のマッスルを`Values`で上書きすると、Actionの重みが0の間もアプリの姿勢が消え、重みが上がる途中は休みの姿勢から混ざるので1フレームで跳ぶ。

## 決めたこと

- 評価器に`HostShare`を足す。対象ごとに、全コントローラーの下にあるもの（Base、つまりアプリの姿勢）が`Values`にどれだけ残るかを出す。書いたコントローラーごとの (1 − プレイアブルの重み) の積で、どのコントローラーも書かなければ1。`Values`と`Written`は変えない
- `Values`は下に何も無いところを休みの値から混ぜているので、アプリが姿勢の値hを持つマッスルは`Values + (h − rest) × HostShare`で出せる。Actionの重みが0ならhのまま、重みが上がると時間をかけてActionの姿勢へ移る
- 評価器の内部の規則（どのコントローラーがどのマッスルを書くか、マスク、FXの扱い）をアプリに写さずに済むよう、重みの積は評価器の中で出す。評価器を唯一の正本にするため
- 落ち着いたフレーム（#3664）では`Values`と同じく前の値のまま。落ち着きの照合（`AvatarAnimatorSettledCheck`）で、飛ばしたときと毎フレーム進めたときの`HostShare`も比べる

## 確かめたこと

- Unityの実測: 比較テストに`CompareOverHost`を足した。humanoidで2つ以上のコントローラーを再生するケースを、Gestureの下にもう1つ入力（マスクなし・重み1）を置いてもう一度再生する。その入力のコントローラーは、マッスルの対象ごとに休みの値から0.15〜0.35離れた姿勢を保つ。Unityの`GetHumanPose`のマッスルと、評価器の`Values + (h − rest) × HostShare`が、60fps・30fps・不揃いなフレーム時間で許容差0.001以内で一致した
- 今までのケース（`gestureAction`・`actionMask`）はGestureの体のレイヤーにマスクが無く、体じゅうを覆うので、下の姿勢がどこにも出ない。そこで、多くのアバターと同じくGestureが左手だけを動かすケース`actionOverHands`を足した。Actionの待機は背骨を書き、Goで0.3秒かけて重み1、戻ると0.25秒で0にする。`HostShare`を掛けないと（下の姿勢を足さないと）このケースは3つのタイミングすべてで一致しなくなる（60fpsでフレーム0から213か所。背骨はUnityが0.15、評価器が0）ことを確かめた
- 評価器だけのテスト（`Tools/AvatarAnimatorEvaluatorTests.cs`の`HostShare`）: Actionの重み0で背骨の`HostShare`が1、ブレンド中は1 − 重み、重み1で0。重み1のGestureが書く指は0、どのコントローラーも書かないマッスルは1。半分の重みのGestureの下では0.5。FXだけのときはFXが書くマッスルで0、マスクの外で1。ブレンド中に下の姿勢を足した背骨が1フレームで跳ばないこと

## 測っていないこと

- Baseの姿勢がGestureの下で動くとき（歩きのクリップなど）も、マッスルごとに同じ式で混ざると見ている。実測は一定の姿勢だけ
- 足と脚のマッスルで、Actionの重みが上がり始めるフレームに出る小さなずれ（`2026-11-16-avatar-animator-muscle-empty-side.md`の測っていないこと）は、このケースには脚が無いので見ていない
- `Values`の扱いは変えていないので、書き出す内容と`Values`・`Written`は前と同じ

## アプリへの影響

アプリは、`HostShare`が1未満のマッスルだけを`Values + (h − rest) × HostShare`で書き、1のマッスルは自分の姿勢のまま残す。`Written`だけで上書きはしない。
