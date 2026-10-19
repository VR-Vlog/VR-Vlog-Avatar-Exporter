# 既定の顔に動く筋肉のカーブがあるときは、従来どおり書き出し前の値に戻す

- 日付: 2026-10-20
- 対象: `Editor/VrChatExpressionSampler.cs`・`Tests/Editor/NeutralShapeSamplerTests.cs`・`Tools/run-unity-compatibility.py`・`Documentation~/decisions/2026-10-19-expression-settlement.md`

## 背景

2026-10-19 の変更（`2026-10-19-expression-settlement.md`）で、`ValidateFixedPose` は `neutral` のときヒューマノイドの筋肉のカーブを止まっているかの判定から外すようにした。目的は、Gesture の表情を組み立てるときの既定の顔の確認で、もう片方の手の待機ステートが筋肉をループさせていると表情ごと外れていたのを直すことだった。

ただし `neutral` は、書き出す既定の顔そのもの（`NeutralShapeSampler` から `preserveTemporalRest: true` で呼ばれる経路）でも立つ。この経路では、それまで同じカーブで `NeutralShapeSamplingException` になり、警告を付けて書き出し前の値（prepared rest）に戻していた。10-19 の変更で例外が出なくなり、既定の顔はアニメーターを再生した値（live rest）に変わっていた。表情を外さないという目的には要らない変更だった。

## 決定

`ValidateFixedPose` に `restFace` を足し、既定の顔そのものを読むとき（`preserveTemporalRest` のとき）は筋肉のカーブを判定から外さない。既定の顔は 10-19 より前と同じく、警告を付けて prepared rest に戻る。Gesture の表情を組み立てるときの確認では、10-19 のとおり筋肉のカーブを外したままにするので、ハンドサインの表情は書き出される。

## 違いが出る場合

prepared rest と live rest が違うのは、待機ステートがシーンの値と違う BlendShape の値を書いている場合（顔の形の調整や、まばたきを 0 に固定するクリップ）、加算レイヤー、重みが 1 未満のレイヤー、Expression Parameters の既定値で既定以外のステートが選ばれる場合など。これらのアバターでは、既定の顔が 10-19 より前の値に戻る。

ハンドサインの表情は、10-19 のとおりアニメーターを再生した既定の顔（live rest）を基準に値を決める。書き出す既定の顔だけが prepared rest になるので、両者が違うアバターでは、表情が持たない BlendShape の見え方がその差の分だけずれる可能性がある。10-19 より前はこの表情そのものを書き出していなかったので、以前より悪くなる組はない。

## 検証

- `NeutralShapeSamplerTests.ALoopingMuscleCurveInTheRestFaceKeepsThePreparedRest`: 既定のステートで開き 75 を書き、ループする手の筋肉のカーブを足す。結果は空（prepared rest）、警告は 1 件でカーブの名前を含み、シーンの重みは変わらない。`!restFace` を外すと失敗する
- `ExpressionSettlementTests.AGestureAboveALoopingHandIdleLayerIsExported` は引き続き通る
