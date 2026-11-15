# 評価器が Tracking Control の状態を出す

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`（`Animated`）、`Tools/AvatarAnimatorEvaluatorTests.cs`（`TrackingControl`）
- 関連: VR-Vlog#3666 の段5-5

## 背景

VRChat の VRC Animator Tracking Control は、ステートに入ったときに体の部位（頭・手・指・口など）をトラッキングで動かすかアニメーションで動かすかを切り替える。拡張 `VRVLOG_avatar_animator` には `trackingControl` の振る舞いとして書き出していて、評価器も読んでいたが、何も動かさないので捨てていた。

アプリは段5-5 で、`leftFingers`・`rightFingers`・`mouth` がアニメーションのとき、自分の指と母音の書き込みを止める。どのステートに入ったかは評価器の中でしか分からないので、評価器がその状態を持つ。

## 決めたこと

- 評価器に `Animated(string part)` を足す。部位の名前は拡張の `trackingControl` のキー（`head`・`leftFingers`・`mouth` など）
- ステートかステートマシンに入って `trackingControl` が走ると、`animation` の部位は true、`tracking` の部位は false になる。`noChange` の部位は前の値のまま
- どの `trackingControl` も触っていない部位は false（VRChat の既定のトラッキング）
- `Values`・`Written`・`HostShare` は変えない。書き出しの内容も変えない

## 決めなかったこと

- 同じフレームに複数のレイヤー・コントローラーが `trackingControl` を走らせたときは、評価器が回す順（gesture→action→fx、その中はレイヤーの番号順、ステートマシンの振る舞いはステートの前）で後のものが勝つ。VRChat の中の順は実測していない
- 評価器が再生しないコントローラー（gesture・action・fx 以外と、`SkipReason` で飛ばしたもの）の `trackingControl` は効かない。そのコントローラーの動きも出ないので、部位だけ止めることはしない

## 確かめたこと

- 評価器だけのテスト `TrackingControl`: 最初はどの部位も false、`animation` を持つステートに入ると true、`noChange` で残り、`tracking` で false に戻る
- 既存の評価器のテスト・Unity の記録の再生・落ち着いたフレームの照合が全部通る
