# ハンドジェスチャーの表情 / VRVLOG_expression_gestures 1

Exporter と VR Vlog の共通仕様。VRChat の左右のハンドジェスチャーの組ごとに、アバターの FX が出す顔を VRM のカスタム表情の名前で持つ。
任意の GLB root extension として保存し、extensionsUsed に宣言する。extensionsRequired には入れない。
読み書きと検査のコードは `Formats/Runtime/ExpressionGestureData.cs`、スキーマは `Schema/VRVLOG_expression_gestures.1.schema.json`。

## データ

```json
"VRVLOG_expression_gestures": {
  "schemaVersion": 1,
  "cells": [
    [], [{ "expression": "Blink", "drive": "right" }], [{ "expression": "Smile" }], ...,
    [{ "expression": "Angry" }, { "expression": "HalfClosed" }], ...
  ]
}
```

- `cells` はちょうど 64 要素で、`cells[left * 8 + right]` が左 `left`・右 `right` のときの顔。ジェスチャーの値は VRChat と同じで、0 中立・1 拳・2 開き・3 指差し・4 ピース・5 ロックンロール・6 銃・7 サムズアップ。
- マスは `null` か 0〜8 個の参照の配列。`[]` はジェスチャーの表情なし（中立の顔）、`null` はその組の顔を Exporter が決められなかったことを表す。
- 参照の `expression` は `VRMC_vrm.expressions.custom` の名前（1〜1024 文字）。同じマスで同じ名前を 2 回指さない。
- 配列は適用の順。同じ BlendShape は後ろの参照の値で上書きする（FX の層の順）。2 つ以上入るのは、別の層の顔を重ねるアバターだけ。
- `drive` は `"left"` か `"right"`。その表情の重みをその手のトリガー（`GestureLeftWeight`／`GestureRightWeight`、0〜1）にする。無ければ重み 1。
- ルートのキーは `schemaVersion` と `cells` だけ、参照のキーは `expression` と `drive` だけ。違反があれば拡張全体を拒む。1 マスも表情が無い表は書かない（読み込みでも拒む）。
- 表に出る名前が `custom` に無いときは、その参照だけを無視する。

## アプリでの引き方

```
cell = cells[gestureLeft * 8 + gestureRight]
target = {}
if cell != null:
  for ref in cell:                  // 後ろの参照が前の参照を上書きする
    target[ref.expression] = ref.drive == "left" ? weightLeft : ref.drive == "right" ? weightRight : 1
表に出るすべての表情 e について SetWeight(e, target.get(e, 0))
```

1 マスに 2 つ以上の参照があるときは、VRM の表情の重みを足すのではなく、後ろの表情の BlendShape で前の表情の同じ BlendShape を上書きする。`null` のマスはアプリが決める（前の顔を保つ、中立にする等）。

## Exporter が表を作る方法

書き出しの直前、NDMF などの前処理を済ませたコピーで、FX を左右 64 通りの組ごとに新しいグラフで再生する。

- パラメーターはすべて既定値から始め、`GestureLeft`／`GestureRight` を入れて 2 秒（120 フレーム）進める。トリガーは拳（1）の手だけ 1、ほかは 0。`IsLocal`・`Grounded` などの組み込みパラメーターは立って静止したローカルの値に固定する。
- 入った状態の Parameter Driver（Set・Add・Copy）と、FX の層の重みを変える Animator Layer Control を再現する。Random は値を決められないので、その組のマスは `null` にする。
- 重みのある層で再生中の、顔の BlendShape を動かすクリップのうち、中立 (0,0) のときに再生していないものを層の順に集める。後ろの Override 層が重み 1 で再生しているクリップが、下の層のクリップの動かす BlendShape をすべて動かすときは、その下の層の参照は落とす（一部だけ重なるときは両方残す）。中立のときと BlendShape が変わらないクリップも落とす。
- 約 1.67 秒（100 フレーム）と 2 秒で再生中のクリップが違う（遷移や時間で顔が変わり続ける）組は `null` にする。
- 拳の手がある組は、その手のトリガーを 0 にしてもう一度再生し、顔が変わったクリップの参照に `drive` を付ける。両手のトリガーで変わるクリップの組は `null` にする。
- クリップは表情ボードで書き出す表情（その表情のクリップ・前処理で複製した元・NDMF が記録した元のアセット）に対応させ、VRM に書いた名前で参照する。同じ顔として別の名前にまとめた表情はまとめ先の名前になる。表情ボードに無いクリップだけのマスは `null`、ほかの表情と一緒に出るときはその参照だけを落とす。

決められないもの:

- メニューのトグルや表情セットの切り替えは既定値の表だけを書く。既定でジェスチャーの表情を止めているアバターは、1 マスも表情が無いので拡張を書かない。
- Write Defaults がオフで前のジェスチャーの顔が残る構成は、中立から入った場合の顔になる。
- トリガーを Motion Time に使う構成も `drive` で 0〜1 の重みにする近似になる。遷移の時間は書かない。
- 再生に失敗したときは拡張を書かずに書き出しを続け、Unity のコンソールに警告を出す。表を VRM に足す工程で失敗したときは、書き出しの注意に工程「ハンドジェスチャーの表情」として出る。

## 版

必要なアプリの版は `AvatarFormatVersions` の `("VRVLOG_expression_gestures", 1)`。アプリのリリースまで仮の値を入れている（`decisions/2026-10-23-expression-gestures.md`）。
