# M5のあとの12体の突き合わせと、Exit Timeの回避を外したテスト

- 日付: 2026-11-15
- 対象: `Tests/Editor/AvatarAnimatorCaptureTests.cs`
- Issue: VR-Vlog/VR-Vlog#3665（Epic VR-Vlog/VR-Vlog#3658 の段4-2c）

## 背景

`2026-11-15-avatar-animator-any-state-interruption.md`（M5）で、A9に残っていた5項目の原因と見ていた割り込みの決まりを直した。12体で0になるかはまだ数えていなかった。

`TheAppPlayerShowsWhatUnityShowsForEveryInput`は、Parameter Driverの値を遷移が次のフレームから見る（M8）前に書いたテストで、Driverを置いたステートからの遷移にExit Time（0.26）を付けて、値が立ってから遷移するように合わせていた。M8を入れたあとも回避が残っていて、条件だけの遷移でUnityと合うかを縛れていなかった（#804のレビューのP2 (d)）。

## 決定

- テストの遷移からExit Timeを外し、条件（Shown）だけで遷移させる。評価器もUnityも、Driverが立てた値を次のフレームから見るので、始めから合う
- 12体の突き合わせに残った1項目（下）は評価器の決まりの差なので、このPRでは評価器を変えない（評価器は並列の段4-13が触っている）。続きの作業にする

## 確かめたこと

- VRChat SDKの入ったプロジェクト（Unity 2022.3.22f1）で`AvatarAnimatorCrossCheckTests`と`AvatarAnimatorCaptureTests`を回し、71件すべて通った。`TheAppPlayerShowsWhatUnityShowsForEveryInput`は回避なしで「inputs 10 compared 10 mismatched 0」
- 回帰の確認と同じ手順（`-regressionShots none`）で、feature/full-reproductionの6ba81a4aの12体を書き出し、突き合わせの行を数えた。A9は5から0。A4〜A6は2D Directionalのツリーに対応した（#805）ので今回から比べられるようになり、A4・A5は0、A6は52項目中1項目が合わない。ほかの体は0のまま、A12は拡張を書かない

## 残ったもの

- A6の1項目: メニューのトグルで、Animator Layer Controlが瞬きのレイヤーの重みを0にする（ブレンド0秒）。重みが0になった次のフレームで、UnityはそのBlendShapeをrest（0）にするが、評価器は前のフレームの値（1.6969、重み1のときの瞬きのクリップの値）を残す。評価器の`Output`は重みが0のレイヤーを飛ばし（`if (!has || weight <= 0) continue;`）、ほかに書くレイヤーが無いターゲットは前のフレームの値のままになるため。重みが0より大きければ、M7で「下のレイヤーが書いていなければrest」と混ぜるので、0のときだけ続かない
- 直し方の候補は、重みが0のレイヤーも、Write Defaultsで書くターゲットは下の値（無ければrest）を書いたことにする。Write Defaultsが無いステートで同じかは測っていない
