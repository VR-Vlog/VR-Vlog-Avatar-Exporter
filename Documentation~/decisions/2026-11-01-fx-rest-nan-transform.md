# 通常時の顔・体形を読むとき、Transform の曲線は値を調べない

- 日付: 2026-11-01
- 対象: `Editor/VrChatExpressionSampler.cs`（`ValidateNativeParameterCurve`・`IsValidNativeCurve`・`HarmlessAppearance`）・`Editor/NeutralShapePlan.cs`（`ChecksCurveValues`・`ValidateBindings`・`Create`）・`Editor/SelectedExpressionAppearance.cs`（`IsUnboundTransform`・`ValidateClipData`・`IsUnchangedTransform`・`IsMovingTransform`）・`Editor/ExpressionDependencies.cs`（`CanEvaluateNeutralRelay`）
- Issue: #566

## 背景

Modular Avatar の Mesh Cutter はビルド時に `NaNimatedBuffer…/NaNimatedBone` を作り、その `m_LocalScale` を NaN にするアニメーションで頂点を消す。`NeutralShapePlan.ValidateBindings` は FX のクリップのすべての float 曲線を `ValidateNativeParameterCurve` で調べ、値が有限でなければ例外にしていたので、`ExportStage.FxRest` 全体が近似に落ち、FX の既定の状態のシェイプキーが VRM に焼かれなかった。

## 決定

- 通常時の顔・体形を読むプローブ（書き出し用コピーの複製で Animator を評価し、`GetBlendShapeWeight` と Animator のパラメーターだけを読む）では、Transform の曲線は時刻の並びと有限の時刻だけを調べ、値・接線・重みは調べない。判定は `NeutralShapePlan.ChecksCurveValues`（Transform 以外なら値も調べる）の 1 か所に置いた。
- `ValidateNativeParameterCurve` に `values` を足した。`false` のときはキーの時刻だけを調べる。既定は `true` で、ほかの呼び出しは変わらない。
- 値を調べないのは次の箇所で、どれもプローブの中で使う。
  - `NeutralShapePlan.ValidateBindings`（`Create`・`CreateProjection` から呼ぶ。通常時の顔・体形と、表情の評価用のプランの両方）
  - `HarmlessAppearance` の中の `IsUnboundTransform` と、動的コンポーネントの有効・対象の無い Transform のときの `ValidateClipData(…, probe: true)`
  - `CanEvaluateNeutralRelay` の `StaticMotion`。NaN の曲線は `IsConstant` が偽になるので、中継は証明されず従来の準備済みの値に戻る
- マテリアル・表示の切り替え・有効状態・Constraint などの値は、見た目の持ち主の判定に使うので、これまでどおり値まで調べる。
- NaN の Transform の曲線は、準備済みの姿勢を保つもの（`IsUnchangedTransform`）とも、そのチャンネルの動き（`IsMovingTransform`）とも見なさない。どちらも例外を出さずに偽を返す。`Create` で、クリップ自身の Transform の曲線が不正なら動きとして扱わない。NaN の大きさは頂点を消すので見た目の変更であり、同じクリップのシェイプキーは保持する側に倒す。

## 変えなかった箇所

- `NeutralAdditionalDataPreflight` は FX 以外の Playable のクリップを調べる。今回の経路ではない。
- `VrChatExpressionSampler` のシェイプキーだけのクリップと、評価中に Animator とシェイプキーの曲線を調べる箇所は、値を読む曲線だけを調べている。
- `ValidateClipData` の `ExpressionTransformCurves`・`VrChatGestureExpressions` からの呼び出しは、Transform の値を書き出すので値まで調べる。
- `HarmlessAppearance` の最後の分岐（表情の評価で、マテリアルなどが準備済みの値と同じクリップ）は、そのクリップを表情として書き出すので値まで調べる。

## 成り立たない場合

- NaN の Transform の曲線を持つクリップのシェイプキーは、見た目の持ち主として保持されるので、そのクリップが通常時にも再生されるなら、そのシェイプキーは焼かれない。Mesh Cutter のクリップは頂点を消す曲線だけを持つので、今回のアバターではこの場合に当たらない。
