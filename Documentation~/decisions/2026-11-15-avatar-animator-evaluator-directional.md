# 評価器で、2D Simple Directionalと2D Freeform Directionalのブレンドツリーを再生する

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/AnimatorComparison/AvatarAnimatorComparisonCases.cs`、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Tools/Fixtures/AvatarAnimator`、`Formats/README.md`

## 背景

評価器（`AvatarAnimatorEvaluator`）は2Dのブレンドツリーのうち2D Freeform Cartesianだけを再生し（[2026-11-15-avatar-animator-evaluator-freeform-cartesian.md](2026-11-15-avatar-animator-evaluator-freeform-cartesian.md)）、2D Simple Directionalと2D Freeform Directionalのツリーが1つでもあるFXは`Supports`で断っていた。断られたFXはアプリで丸ごと再生されない。

## 式の出どころ

Unityの重みの計算はネイティブにあり、UnityCsReferenceの`BlendTreeInspector`もネイティブの`BlendTreePreviewUtility.CalculateRootBlendTreeChildWeights`を呼ぶだけで、式は読めない。そこで次の2つを手がかりにして候補の式を作り、Unity 2022.3.22f1の`Animator`の記録と突き合わせて決めた。

- [Unityのマニュアルの2D Blending](https://docs.unity3d.com/Manual/BlendTree-2DBlending.html): Simple Directionalは方向ごとに1つの子を置き、(0, 0)の子は任意。Freeform Directionalは(0, 0)に子を置くことを勧めている
- [Rune Skovbo Johansenの修士論文（Aarhus University, 2009）](https://runevision.com/thesis/rune_skovbo_johansen_thesis.pdf)の6.3節（Gradient Band Interpolation）と6.3.1節（Gradient Bands in Polar Space）の式6.14・6.15。Freeform CartesianのM12は、この6.3節の直交座標の式と同じ形をしている

式6.14・6.15は、子iから子jへの差と、子iから点Pへの差を次のように置く。

- `v_ij = ((|p_j| − |p_i|) / ((|p_j| + |p_i|) / 2), ∠(p_i, p_j)·α)`
- `v_iP = ((|P| − |p_i|) / ((|p_j| + |p_i|) / 2), ∠(p_i, P)·α)`

論文は`α = 2`を勧めているが、Unityの記録に合うのは`α = 1`だった（下のD2）。論文は子が(0, 0)にあるときの角度を決めていないので、そこは記録から決めた（D3）。論文の式だけでは方向の間隔が広いツリーで合わず、子ごとの重みを`1 − |∠(p_i, P)| / 180度`から始めると合った（D7）。これも論文には無く、記録から決めた。

Simple Directionalの式は論文になく、記録から決めた。

## 測り方

比較のテスト（`Tests/AnimatorComparison`）に`simpleDirectional`と`freeformDirectional`のケースを足し、60 fps・30 fps・不均一なフレーム時間で記録した。重みを見るレイヤーの作り方は`freeformCartesian`と同じで、子ごとに「自分のターゲットに1、ほかの子のターゲットに0」を書く定数のクリップを置く。2つのパラメーターは、指定した点（子の位置、2つの子の中間、原点のすぐ近くなど）を通ってから、−2.5〜2.5の範囲を0.05秒ごとに疑似乱数で動かした（1記録あたり140点）。

時間の進み方のレイヤー（`Lengths`・`Scales`・`Inner`・`Missing`・`Direct`）は、(0, 0)と90度ずつの4方向に、長さとTime Scaleの違う子を置いた。`Missing`は(0.5, 0.5)にモーションのない子を足した。

## 測って分かったこと

### Simple Directional

| 番号 | レイヤー | 置き方 | 結果 |
|---|---|---|---|
| S1 | `Four` | (0, 0)と90度ずつの4方向 | 下の式と一致 |
| S2 | `NoCentre` | S1から(0, 0)を除く | 中心の分の重みを4方向で等分すると一致 |
| S3 | `Uneven` | 角度と距離のばらついた5方向と(0, 0)。子は角度順に並んでいない | 一致 |
| S4 | `Gap`・`GapNoCentre` | 隣同士が180度より離れた方向がある | その間の点では中心がすべてを取る（中心が無ければ方向で等分） |
| S5 | `One`・`OneNoCentre`・`Centre` | 方向が1つ、または(0, 0)だけ | 中心がすべてを取る（中心が無ければその方向が取る） |
| S6 | `Same` | (1, 0)と(2, 0)が同じ方向 | 先に並んだ(1, 0)だけが使われ、(2, 0)は重みを持たない |
| S7 | `Centres` | (0, 0)に2つ | 点がちょうど(0, 0)のときは先の子が1、それ以外の点ではどの子も0 |

重みの式（S1〜S3）: 子を角度順に並べ、点Pを挟む隣り合う2方向a・bを選ぶ（反時計回りにaからbへ180度未満のもの）。aとbと中心の三角形の中でのPの重心座標を、a・b・中心に渡す。Pがaとbを結ぶ線より外なら、a・bの重みをその合計で割り、中心は0にする。

### Freeform Directional

| 番号 | レイヤー | 置き方 | 結果 |
|---|---|---|---|
| D1 | `Ring` | (0, 0)と45度ずつの8方向 | 下の式と一致 |
| D2 | `Rings` | (0, 0)と、90度ずつの4方向に距離1と2の2つずつ | `α = 1`で一致。`α = 0.5・1.5・2`では合わない |
| D3 | `NoCentre`・`Uneven` | (0, 0)無しの4方向、角度と距離のばらついた6方向と(0, 0) | 一致 |
| D4 | `OneNoCentre` | 子が1つ | 重みは常に1 |
| D5 | `Same` | (1, 0)に2つ | 同じ位置の2つは互いを相手に数えなければ一致 |
| D6 | `Centres` | (0, 0)に2つ | D5と同じ扱いで一致 |
| D7 | `Radii`・`Wide`・`Gap`・`One` | (0, 0)無しで長さが1と10の方向が交互、135度の間隔、270度の間隔、(0, 0)と1方向 | 子ごとの重みを`1 − |∠(p_i, P)| / 180度`から始めると一致。これが無いと、`Radii`では点(−0.7071, 0.7071)などで最大0.10ずれる |
| D8 | `One`・`OneNoCentre`・`Radii` | 点が子の反対側にある、点が(0, 0)で(0, 0)の子が無い | どの子も重みが0になるときと、点が(0, 0)で(0, 0)の子が無いときは、モーションのある子で等分する |

重みの式（D1〜D8）: 子iの重みは、(0, 0)にない子なら`1 − |∠(p_i, P)| / 180度`（Pが(0, 0)なら1）、(0, 0)の子なら1から始め、ほかの子jごとのM12の式`1 − (v_iP·v_ij) / |v_ij|²`との最小を取る。0より小さければ0にして合計で割る。合計が0のときと、Pが(0, 0)で(0, 0)の子が無いときは等分する。`v_ij`・`v_iP`は上の式6.14・6.15で`α = 1`、角度は符号つき（−180〜180度）。子iか子jが(0, 0)のときは、`∠(p_i, p_j)`の代わりに(0, 0)でない方の子からPへの角度の絶対値を使い、`∠(p_i, P)`は子iが(0, 0)なら0、子jが(0, 0)ならその同じ角度にする。Pが(0, 0)のときは`∠(p_i, P)`を0にする。

最初は角度の項（D7）を入れずに合わせていた。そのときは270度離れた`Gap`、(0, 0)と1方向だけの`One`、135度離れた方向がある置き方で食い違い、角度の代わりに`sin`・`tan`・`2 sin(θ/2)`を使っても消えなかった。一度は隣同士が90度より離れたツリーを`Supports`で断ることにしたが、セルフレビューで角度の項の指摘を受けて測り直し、間隔が90度以内でも長さの違う方向がある`Radii`で食い違うことと、角度の項を入れると全部一致することが分かった。そこで断るのをやめた。

記録は`Tools/Fixtures/AvatarAnimator`の`simpleDirectional`と`freeformDirectional`の各3個。既存の69個の記録は、同じUnityで取り直してもJSONとして変わらなかった（違いは行末だけ）。

## 決定

- 評価器は2D Simple Directionalの重みを上の式で求める。これを評価器のM13とする
  - 同じ方向にある子は、先に並んだ子だけを使う（S6）
  - (0, 0)の子が無いときは、中心の分を方向で等分する（S2）
  - (0, 0)の子が2つ以上あるときは、点がちょうど(0, 0)なら先の子に1を渡し、それ以外では重みを渡さない（S7）。Unityの記録がそうなっている
- 評価器は2D Freeform Directionalの重みを上の式で求める。これを評価器のM14とする
- `Supports`は2D Simple Directionalと2D Freeform Directionalを断らない。知らない種類のブレンドツリーは今までどおり断る
- モーションのない子は、どちらの種類でもいないものとして扱う（M12と同じ）

## 検討した代替案と不採用理由

- 論文どおり`α = 2`にする: D2で合わない
- Freeform Directionalで隣同士が90度より離れたツリーを断る: 角度の項が無いときの回避策だった。角度の項を入れると広い間隔も一致し、断る理由が無くなった

## 既知の制限

- Simple Directionalで(0, 0)に子が2つあるとき、(0, 0)以外ではどの子も重みを持たない。Unityの記録では書くターゲットの値がすべて0で、ツリーが何も書かないのか0を書くのかは、既定値が0の記録からは見分けられない。評価器は何も書かない方にした
- 測った置き方は上の表のとおりで、子は9つまで。ごく近い（同じではない）方向の2つや、(0, 0)のすぐ近くの子は測っていない
- Simple Directionalで、隣同士がちょうど180度の方向と、(0, 0)の子が無く同じ方向に子が2つあるツリーは測っていない。評価器は前者を180度より広いときと同じに扱い、後者は中心の分を残った方向だけで等分する
- 同じ方向かどうかは、外積がちょうど0かで見ている。(0.3, 0.1)と(0.6, 0.2)のようにfloatの丸めで外積が0にならない2つは別の方向として扱う
- VRChatのクライアントで同じになるかは確かめていない。M1〜M7と同じく、Unityの`Animator`と比べている

## 確かめ方

- `AvatarAnimatorEvaluatorComparisonTests`をUnity 2022.3.22f1で回し、75ケース（25ケース×3通り）が評価器と一致すること
- `Tools/run-avatar-animator-evaluator-tests.ps1`で、評価器の単体の確認（手で計算した値を見る`DirectionalTrees`と`Supports`の確認を含む128件）と75個の記録の再生が通ること
- 評価器から次のどれか1つを戻すと、単体の確認か記録の再生で落ちることを手元で確かめた: 線より外で合計で割る、中心が無いときに等分する、(0, 0)の子を相手にするときのPの角度を0にする、(0, 0)の子が2つのときの扱い、同じ方向の子を1つにする、`α`を変える、角度の項（D7）を外す、重みが0のときの等分を外す、(0, 0)で(0, 0)の子が無いときの等分を外す
