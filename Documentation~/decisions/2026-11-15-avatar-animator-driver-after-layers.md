# 評価器のParameter Driverを、全レイヤーを進めたあとに当てる

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`、`Formats/README.md`
- Issue: VR-Vlog/VR-Vlog#3665（Epic VR-Vlog/VR-Vlog#3658 の段4-2）

## 背景

書き出しのあとの突き合わせ（`2026-11-14-avatar-animator-crosscheck.md`）で、VRChat SDKの入ったプロジェクトの12体のうち2体に、合わない項目が33個残っていた（A8の2項目とA9の31項目）。どれも前のレイヤーのステートのParameter Driverが変えたパラメーターで、後ろのレイヤーが遷移するところだった。評価器は同じフレームで後ろのレイヤーがその値を見ていた（M8の仮定）が、UnityでSDKの`OnStateEnter`から値を変えると、遷移がその値を見るのは次のフレームから。

## 決定

- Parameter Driverは、ステートに入ったときに当てる操作を積んでおき、全レイヤーを進め、トリガーを戻し、M11（クリップが動かすパラメーター）を書いたあとで、積んだ順に当てる。copyは当てる時点の値を読む。どのレイヤーの遷移も、その値を次のフレームから見る（同じレイヤーも後ろのレイヤーも）
- 当てる順番（外側のステートマシンから、ステートは最後）は今までどおり
- 評価器はアプリと同じソースなので、アプリの再生も同じだけ変わる。拡張の形・書き出しの出力は変えない

## 確かめたこと

- `pwsh Tools/run-avatar-animator-evaluator-tests.ps1`: 単体テスト115件と、録画69本の全フレームの一致が通る。足したテストのうち、後ろのレイヤーが同じフレームでは遷移せず次のフレームで遷移することを見るものは、評価器を前の版に戻すと落ちる。copyが遷移の使ったあとのトリガーを読むことを見るものも、前の版（4e32e60f）で落ちる。同じレイヤーが次のフレームで遷移することを見るものは前の版でも通る（遷移で入ったフレームには、M3でそのレイヤーが次の遷移を始めないため）。これを縛るものとして、既定ステートのDriverが自分の遷移の条件を立てるテストを2026-11-15に足し、前の版で落ちる（1回目のAdvanceで遷移してしまう）ことを確かめた
- 回帰の確認と同じ手順（Unity 2022.3.22f1、VRChat SDKの入ったプロジェクトの12体、`-regressionShots none`）で書き出し、各体の突き合わせの行を数えた。合わない項目はA8が2から0、A9が31から5になり、ほかの体は0のまま（A4〜A6は評価器が対応しない2Dのブレンドツリー、A12は拡張を書かない）

## 残ったもの

- 2026-11-15追記: 下の2つは`2026-11-15-avatar-animator-any-state-interruption.md`で測って直した
- A9の5項目は別の原因で、瞬きのレイヤーの`interruptionSource`が`source`の遷移の途中に、Any Stateの遷移が割り込むかどうかの差。評価器のM5は「Any Stateの遷移はレイヤーが遷移していない間だけ見る」で、`Tests/AnimatorComparison`で測ったのは割り込み元が`none`の遷移だけだった
- 割り込み元が`source`と`destination`の遷移をAny Stateが止めるケースをUnityで回すと、Any Stateを割り込み元の先頭に足しただけの評価器とも合わなかった（`source`では、Any Stateの条件が成り立つ前から差が出る）。決まりを測って直すのは続きの作業にする
