# 評価器のレイヤーのマスクにTransformのマスクを足し、マッスルをUnityと比べる

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/AnimatorComparison/AvatarAnimatorComparisonCases.cs`、`Tests/AnimatorComparison/AvatarAnimatorEvaluatorComparisonTests.cs`、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Tools/Fixtures/AvatarAnimator/`（`hands`・`maskParts`・`nodeMask`）

## 背景

評価器（`AvatarAnimatorEvaluator`）はレイヤーのマスクのうち、Humanoidの部位だけを当てていた。部位を外したレイヤーはその部位のマッスルに書かない。書き出しはマスクが名前を持つTransformを`mask.nodes`に書くが、評価器はこれを読んでいなかった。また、マッスルの値がUnityと同じになるかを比べるケースが無かった。

## 決めたこと

- `mask.nodes`があるマスクは、そこに無いノードの`translation`・`rotation`・`scale`にそのレイヤーを書かせない。`nodes`が無い（Transformを名前に持たない）マスクはTransformを絞らない
- 上の3つのほか（GameObjectの有効・BlendShape・Animatorのパラメーター）はTransformのマスクで絞らない。Unityで測ると、マスクから外したTransformの上でも`m_IsActive`・`blendShape.*`・パラメーターのカーブは書かれた（`nodeMask`のケース）
- マスクがTransformを1つも持たない（根だけを名前に持ち、それを外した）ときも、どのTransformも動かさない。`nodeMask`の`None`のレイヤーをこの形でUnityに渡して確かめた
- マッスルとマスクの比較ケースを3つ足した
  - `hands`: 空のFXの後のGestureで、`GestureLeft`・`GestureRight`による手の形の切り替え、手だけのマスク、Write Defaultsを切ったレイヤー、腕のマッスルが手のマスクで止まること
  - `maskParts`: 部位1〜8をそれぞれ1つだけ持つマスクのレイヤーで、95のマッスルがどの部位に当たるか（`MuscleBodyPart`）
  - `nodeMask`: 上のTransformのマスク
- 比較テストのUnity側は、1つのAnimatorに1つのコントローラーを再生する。ケースのレイヤーはFXだけか、空のFXの後のGestureなど1つのコントローラーに置く。コントローラーを重ねる順（M15）の計測は段5-3の担当
- Humanoidのリグはコードで組む（`AvatarBuilder.BuildHumanAvatar`）。ねじれの配分（twist）は0にする。0.5のままだと、上脚のねじれのマッスルの読み戻し（`HumanPoseHandler.GetHumanPose`）が書いた値から1e-3ほどずれる
- マッスルの許容差は設計書の1e-3、ほかの値は今までどおり1e-4

## 古い出力との関係

書き出しは、表情ごとの値を評価器で確かめる（`AvatarAnimatorCrossCheck.FaceMismatch`）。FXのレイヤーがTransformのマスクを持ち、表情のクリップがそのマスクの外のTransformを動かすときだけ、評価器の値が変わる。

- VRMの表情のTransformの値（`ExpressionTransformCurves`）はマスクを見ずにクリップから作る。この形の表情は、評価器（＝Unityの再生）では動かないTransformをVRMの表情が動かすので、`FaceMismatch`が不一致を返し、`VRVLOG_avatar_animator`の`expressions`から外れる（ログに「left out」と出る）。VRChatで動かない値をアプリが表情として出さない方向の変化
- VRChatの手のマスク（Humanoidの部位だけを持ち、Transformを持たない）は`nodes`が無いので、今までと変わらない
- それ以外のアバターの書き出しは変わらない

## 採らなかった案

- Transformのマスクで、そのノードのすべての値（有効・BlendShapeも）を止める: Unityはそうしない（`nodeMask`で測った）
- 1つのAnimatorに複数のコントローラーを重ねてUnityと比べる: VRChatの重ね方は1つのAnimatorでは再現できず、計測は段5-3で行う
