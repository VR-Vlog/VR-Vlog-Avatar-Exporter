# FXの動きの拡張で、BlendShapeの曲線とパラメーターの曲線を一緒に書く

- 日付: 2026-11-13
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Editor/AvatarAnimatorCapture.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`

## 背景

`VRVLOG_avatar_animator`のターゲットに、2つの変更がそれぞれ別の種類を足していた。BlendShapeの重みを書く変更（2026-11-13の「FXの動きの拡張に、FXが動かすBlendShapeの重みを書く」）は`morph`を、floatのパラメーターの曲線を書く変更（2026-11-13の「FXのパラメーターを動かす曲線と、Prevent Repeatsを拡張に書く」）は`parameter`を足した。後者が先に入ったため、前者を取り込むときに、ターゲットの種類、クリップのトラックとキーの上限の分け方、評価器が受け付けるターゲットがぶつかった。

## 決定

- ターゲットの`property`は`active`・`morph`・`parameter`の3つにする。`active`と`morph`はノードを持ち、`parameter`はノードを持たない（読み込むと`Node`は-1）。`morph`はメッシュのモーフターゲットの数で範囲を確かめる
- 同じターゲットは種類ごとに1つ: `active`はノードごと、`morph`はノードとモーフターゲットの組ごと、`parameter`はパラメーターごと。ほかの種類のキー（`parameter`に`node`や`morph`、`morph`に`parameter`）があれば拒む
- ターゲットは`active`、`morph`、`parameter`の順に並べる。ノードのターゲットを先に置くという、その決定記録の並びを保ち、その中で`active`の後に`morph`を置く。クリップのトラックも同じ種類の順に並べる
- 1つのクリップのトラック（4096個）とキー（16384個）は、`activeSelf`の曲線、パラメーターの曲線、BlendShapeの曲線の順に割り当てる。入らなかった曲線は後ろから落とし、種類ごとに`omitted`の`other`・`parameter`・`blendShape`に数える。後から足した種類が、前からある種類を押し出さないようにするため
- 拡張が4MBを超えるときにBlendShapeを全部`blendShape`に数えて書き直す決まりはそのまま。パラメーターの曲線は残す
- `AvatarAnimatorEvaluator`は`morph`のターゲットも再生する。ブレンド（`rest`、レイヤーの重み、Write Defaults）はターゲットの種類に依らないので`active`と同じ計算でよく、`Supports`が拒むのは`parameter`のターゲットだけにする。`morph`の値はUnityの重みで返し、glTFの重みへは読む側が`baked`と`end`で直す

## 確かめ方

- `AvatarAnimatorDataTests.AllTargetKindsRoundTripTogether`: 3種類のターゲットと、1つのクリップに各種類のトラックを持つデータを書いて読み、同じJSONに戻ることと、種類違いのキーを拒むことを確かめる
- `AvatarAnimatorCaptureTests.ActiveParameterAndShapeCurvesOfOneClipRoundTripThroughTheExtension`: 1つのクリップで`activeSelf`・パラメーター・BlendShapeを動かすFXから拡張を作り、書いて読み直して並びと値を確かめる
- `AvatarAnimatorCaptureTests.ShapeCurvesTakeWhatParameterCurvesLeaveOfTheClip`: トラックの残りが2つのとき、パラメーターの曲線が先に入り、BlendShapeの曲線は1つだけ入って残りが`blendShape`に数えられることを確かめる
- `Tools/AvatarAnimatorEvaluatorTests.cs`の`Morphs`: `morph`のターゲットを`Supports`が受け付け、レイヤーの重みと遷移の途中で`active`と同じようにブレンドすることを確かめる
