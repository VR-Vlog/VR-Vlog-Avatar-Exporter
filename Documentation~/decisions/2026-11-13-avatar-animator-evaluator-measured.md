# FXの評価器の決まりを、UnityのAnimatorの録画に合わせる

- 日付: 2026-11-13
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/AnimatorComparison/`、`Tools/AvatarAnimatorComparisonReplay.cs`、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Tools/Fixtures/AvatarAnimator/`、`Tools/run-avatar-animator-evaluator-tests.ps1`、`Tools/run-validation.py`、`Formats/README.md`

## 背景

アプリは`VRVLOG_avatar_animator`を読み、Unityを使わずにFXの動きを再生する（`AvatarAnimatorEvaluator`）。アバターの見た目がVRChatと同じになるには、遷移の始まる時刻・ブレンドの進み方・レイヤーの重ね方がUnityのAnimatorと同じでないといけない。最初の版は、Unityで測っていない動きを仮定M1〜M10として書いていた。

## 測り方

`Tests/AnimatorComparison`のEditModeテストが、16種類のコントローラー（トグル・フェード・Exit Time・ループ中のExit Time・トリガー・Any State・割り込み・Any Stateの割り込み・Write Defaults off・3レイヤー・ブレンドツリー・サブステートマシン・ステートの設定・連続遷移・syncedレイヤー・activeSelf）をコードで組み、Unity 2022.3.22f1の`Animator.Update`を60fps、30fps、不均一な間隔の3通りで回して、毎フレームの値をJSONに書く。録画は48本で、`Tools/Fixtures/AvatarAnimator`に置く。`Tools/run-avatar-animator-evaluator-tests.ps1`は同じケースを評価器で再生し、全フレームの値とステートを録画と比べる。Unityの無い環境（CI）でも比べられる。

## 測って分かったこと（評価器の決まり）

1. M1: アニメーションされたactiveSelfは0以外の値（負の値を含む）で表示になる
2. M2: Exit Time eは、ステートの正規化時間がeを越えたフレームで成り立つ。eが1以上なら1回だけ、1未満なら開始後のループごとに成り立ち、開始時点は数えない（eが0なら最初のループの終わり）。越えた後のフレームの残りは、遷移の進みと行き先のステートの時間をそのフレームのうちに進める。条件だけの遷移はフレームの終わりから始まる
3. M3: 遷移が終わったフレームには、そのレイヤーで次の遷移を始めない。長さ0の遷移は始めたフレームで終わる
4. M4: 遷移が使ったトリガーは、全レイヤーが進んだ後に戻す。同じフレームの後ろのレイヤーはそのトリガーを見る
5. M5: Any Stateの遷移は、レイヤーが遷移していない間だけ見る。割り込まれた遷移は、割り込まれた時点の値から混ざる
6. M6: 遷移の途中で、モーションの無いステートは、元の側なら遷移の始まりの値を保つ。行き先の側なら、Write Defaultsがあれば下のレイヤーの値（どれも書いていなければRest）、無ければそのレイヤー自身の前のフレームの値になり、その値へ毎フレーム近づく。Write Defaultsの無いステートを持つレイヤーは、コントローラーがアニメーションする全部の対象について自分の値を持ち、モーションの無いステートにいる間もその値を自分の重みで当て続ける。モーションが書かない対象は、Write DefaultsならRest、無ければ下のレイヤーの値になる
7. M7: 重みが1未満のレイヤーは下のレイヤーの値に、どれも書いていなければRestに重ねる
8. ブレンドツリーの子はツリーの正規化時間を共有し、子のTime Scaleはツリーの長さだけを変える。Directの重みの負の値は0として読む。重みの合計が0のモーションは、モーションの無いステートと同じに扱う
9. ステートの正規化時間はfloatで足していく。ループの境目の値は倍精度で計算すると合わない

## 決定

- 評価器のM1〜M7を、上の測った決まりに書き換える。仮定ではなく、録画との一致をテストで保つ
- M8（ステートのビヘイビアを走らせる順番と時刻）とM10（レイヤーとFXの重みのブレンド）はVRChatのビヘイビアで、素のUnityでは録画できないので、仮定のまま残す。M9（モーションの無いステートを1秒とする）は録画が無いので仮定のまま残す
- 録画はリポジトリに入れ、`Tools/run-validation.py`のホスト側の検査に評価器のテストを足す。ケースを変えたら、`VRVLOG_ANIMATOR_RECORDINGS`に出力先を入れて比較テストを回し、録画を入れ替える

## 確かめ方

- `pwsh Tools/run-avatar-animator-evaluator-tests.ps1`: 評価器の単体テスト80件と、録画48本の全フレームの一致
- `uv run --with pyyaml python Tools/run-validation.py --report <path>`: 既存の検査と合わせて全部通る
