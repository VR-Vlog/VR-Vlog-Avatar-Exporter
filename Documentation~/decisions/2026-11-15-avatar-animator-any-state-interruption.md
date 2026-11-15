# 評価器で、遷移の途中にAny Stateの遷移が割り込む決まりをUnityに合わせる（M5）

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/AnimatorComparison/AvatarAnimatorComparisonCases.cs`、`Tools/Fixtures/AvatarAnimator`、`Tools/AvatarAnimatorEvaluatorTests.cs`
- Issue: VR-Vlog/VR-Vlog#3665（Epic VR-Vlog/VR-Vlog#3658 の段4-2の残り）

## 背景

M8を直したあと（`2026-11-15-avatar-animator-driver-after-layers.md`）、12体の突き合わせで1体（A9）に5項目が残った。瞬きのレイヤーの`interruptionSource`が`source`の遷移の途中で、UnityはAny Stateの遷移に移り、評価器は移らなかった。評価器のM5は「Any Stateの遷移はレイヤーが遷移していない間だけ見る」で、測っていたのは割り込み元が`none`の遷移だけだった。前の作業でAny Stateを割り込み元の先頭に足すだけの直しを試したが、Unityの録画と合わなかった。

## Unityで測ったこと

`Tests/AnimatorComparison`に、遷移の途中にAny Stateの条件を立てるケースを足し、Unity 2022.3.22f1のAnimatorで60fps・30fps・揺れのある刻みの3通りを録った。割り込み元（`source`・`destination`・`sourceThenDestination`）、Ordered Interruptionのあるなし、遷移の条件を立てたままにするか途中で戻すかを組み合わせた12通りと、Any Stateから始まった遷移の6通りを録り、次の決まりがすべてに合った。

1. 割り込み元が`none`の遷移の途中は、Any Stateの遷移も見ない（今までどおり）
2. 割り込み元が`none`以外なら、割り込み元が`destination`でも、まずAny Stateの遷移を見る。行き先のステートの遷移より先に見る
3. Ordered Interruptionが無いと、走っている遷移自身も割り込める。条件が成り立ったままの間、Unityは毎フレームその遷移を始め直し、レイヤーの値は割り込まれた時点の値で止まる。条件が戻った次のフレームから進む。前の作業で「Any Stateの条件が成り立つ前から差が出る」と書いたのはこれだった
   - 割り込まれたあとの元の側は止まった写しで、元のステートの時間も止まる。Exit Timeのある遷移（ループする1秒のクリップから、Exit Time 0.9・長さ2秒）は、元のステートがもう一度Exit Timeを越えた1.9秒で1回だけ自分を始め直し、そのあとは越えないので3.9秒で終わる
4. Ordered Interruptionがあると、どのリストも走っている遷移の手前で止まる。Any Stateから始まった遷移では、Any Stateのリストのうちそれより前の遷移だけが割り込む
5. Any Stateの遷移の Can Transition To Self は、遷移の途中では元の側のステートと比べる。録画で確かめたのは、Any StateからBへの遷移が、Can Transition To Self が無くても、Aから移る途中なら自分を始め直すこと（行き先と比べるのは誤り）。逆に、AからBへ移る途中にAへのAny Stateの遷移が止められることは録っておらず、元の側と比べる決まりからの推定

## 決定

- 評価器の割り込みを上の決まりにする。割り込み元が`none`でなければAny Stateの遷移を先に見て、そのあと今までどおり割り込み元の側と行き先の側を見る。Any Stateから始まった遷移では、割り込み元の側（Any State）を2回は見ない
- 走っている遷移を割り込みの候補から外していたのをやめ、Ordered Interruptionが無いときは候補に入れる
- 割り込まれて元の側が止まった写しになっている間は、元のステートの時間を進めない
- 遷移が自分を始め直したときは、行き先のステートのBehaviour（Parameter Driverなど）をもう一度は走らせない。Unityで始め直しのたびにOnStateEnterが呼ばれるかは測っていない（比較ケースの仕組みでBehaviourを録れない）ので、今までと同じ「本当に割り込んだときに1回」にした
- 足したケースのうち9つ（27本の録画）を`Tools/Fixtures/AvatarAnimator`に入れ、Unityを使わない確認（`Tools/run-avatar-animator-evaluator-tests.ps1`）でも毎回見る。今までの75本の録画は変えた評価器でもそのまま合う
- 評価器はアプリと同じソースなので、アプリの再生も同じだけ変わる。拡張の形・書き出しの出力は変えない

## 確かめたこと

- `pwsh Tools/run-avatar-animator-evaluator-tests.ps1`: 単体テスト136件と、録画102本の全フレームの一致が通る
- Unityで録った54本（上の18通り×3）と、Exit Timeのある遷移の2ケース（`exitTimeRestart`・`exitTimeRestartGo`、6本）が、変えた評価器と全フレームで合う。後の2ケースは、元の側の時間を止める前は2.9秒から合わなかった（評価器はもう一度始め直していた）。Fixturesに入れた新しい7ケースのうち6ケース（18本）は、変える前の評価器では合わない。合うのは`anyStateOrdered`（Any Stateから始まった遷移をそれより前のAny Stateの遷移だけが割り込む形で、前の評価器も同じ決まりだった）
- 単体テストの`Interruption`は、Ordered Interruptionの無い遷移の条件を立てたまま進めていたので、条件を戻してから半分まで進める形に直し、始め直すこと・Ordered Interruptionがあれば始め直さないこと・割り込み元ごとにAny Stateが割り込むか、始め直してもDriverが1回しか走らないかを足した。Ordered Interruptionのときの確かめは前の評価器でも通るので、それを縛るのは`anyStateInterruptionSourceOrdered`の録画

## 残ったもの

- 12体の突き合わせ（A9の5項目が0になるか）は、この変更では回せていない。12体の一覧と書き出し用の複製が別の環境にあるため
- 割り込み元が`destinationThenSource`の遷移でAny Stateが先に来ることは、`destination`と同じ形のケースで1回録って合ったが、Fixturesには入れていない
- 始め直しのたびにUnityが行き先のBehaviourを走らせるか（上の決定の2つ目）。VRChat SDKの入ったプロジェクトで、行き先にaddのParameter Driverを置いて条件を立てたままにし、値を見て確かめる
