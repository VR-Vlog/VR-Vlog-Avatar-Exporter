# FXの動きの拡張に、FXが動かすBlendShapeの重みを書く

- 日付: 2026-11-13
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Editor/ExportOptimizationBindings.cs`、`Editor/AvatarBaseShape.cs`、`Editor/UnusedMorphTargets.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`

## 背景

`VRVLOG_avatar_animator`のクリップは`activeSelf`の曲線だけを書き、BlendShapeの曲線は`omitted`の`blendShape`に数えていた。表情ボードに入らない顔の切り替えや、衣装の体型合わせのようにFXがBlendShapeを動かすアバターは、アプリでその動きを再現できなかった。

書き出しはBlendShapeをそのまま写さない。表情ボードの既定の顔（FXの既定値）を土台のメッシュに焼き込み、残った分を差分にして（Rebase）、Avatar Optimizerが統合・削除し、どこからも使われないBlendShapeは最後に消す。FXの曲線はUnityの重み（0〜100）なので、アプリがglTFの重みに直すには、焼き込みと差分の取り方が分からないといけない。

## 決定

- ターゲットに`property`が`morph`の形を足す。`node`はメッシュを持つノード、`morph`はそのメッシュのモーフターゲットの番号（`morphTargetBinds`と同じ数え方）。`rest`・`baked`・`end`の3つの重みを持つ
- アプリは、レイヤーを混ぜた後のUnityの重み`v`を`(v − baked) / (end − baked)`でglTFの重みにする。0〜1に丸めない。`baked`は土台に焼き込んだ重み（焼き込めずにそのまま写したときは0）、`end`は差分が1で表す重み（最後のフレームの重み）
- `rest`は、どのレイヤーも書かないときの重みで、シーンに置いたときの値（ビルドの後、書き出しが重みを変える前）にする。`activeSelf`の`rest`と同じ考え方
- `rest`のままの曲線も書く。FXの中では何も変えなくても、FXがそのBlendShapeを書いていることは、VRMの表情やまばたきとどちらを優先するかを読む側が決めるのに要る。ただし、曲線がどれも`rest`のままで土台も`rest`を表す（`baked`が`rest`と同じ）BlendShapeは、それだけではモーフターゲットを残さない。表情など別の理由で残るときだけ書き、消えるときはそのターゲットとトラックを拡張から外す（見た目は変わらず、表情も動かさないので優先を決める相手もいない）。全部を残すと、92体の書き出しでVRMの合計が15%、最大で2.5倍に増えたため
- 次のものは書かずに`blendShape`に数える。フレームが複数あるBlendShape（直線とは限らず、どの重みでも土台に戻らないことがある）、リップシンクのBlendShape（土台を作るときに、ほかのリップシンクのBlendShapeと既定の口の形も差し引くので、式1つでは重みを出せない。統合でこれが混ざった出力も数える）、加算（Additive）のレイヤーのクリップが動かすBlendShape（加算の基準のポーズを拡張に持たない）、BlendShapeが出力に無い、書き出しが読んだ元が無い、統合で元が複数になり値が食い違う、`baked`と`end`が同じ（差分が何も表さない）、クリップのキーやトラックの上限を超える。ファイルに無いメッシュの曲線は`missingNode`に数える。上限は、1つのクリップでキーが16384個（`activeSelf`を先に取り、残りをBlendShapeに使う）、トラックが4096本
- BlendShapeを足すと拡張が4MBを超えるときは、BlendShapeを全部`blendShape`に数えて、`activeSelf`だけで書く
- Avatar Optimizerの統合の後でも出力のメッシュと名前が分かるように、FXが動かすBlendShapeにも表情と同じ道筋を用意する。ただし使える端点は求めず、無くしても書き出しは止めない（その曲線を書かないだけ）
- 使われないBlendShapeを消すとき、拡張が指すモーフターゲットは残し、番号を振り直した後の値を拡張に書く（上の`rest`のままのものを除く）
- VRMの表情と同じモーフターゲットを動かすときにどちらを優先するかは、読む側（アプリ）が決める

## 理由

- `baked`と`end`を拡張に書けば、アプリはメッシュを作り直さずに式1つで重みを出せる。焼き込みの仕組みをアプリが知らなくてよい
- `rest`をシーンの値にするのは、VRChatでFXが何も書かないときに見える重みがそれだから
- FXの既定値（ステートが書く値）は曲線の側にあるので、`rest`と別に持たなくてよい
- 値の分からない曲線を推測で書くと、アプリで見た目が崩れる。数えておけば、どれだけ再現できていないかが分かる

## 変わるもの

FXがBlendShapeを動かすアバターでは、拡張に`morph`のターゲットとトラックが増え、`omitted`の`blendShape`が減る。拡張が指すBlendShapeはファイルに残るので、メッシュのモーフターゲット・`targetNames`・`weights`と、それより後ろの番号（表情の`morphTargetBinds`など）が変わる。FXの無いアバター、BlendShapeを動かさないFXのアバターの書き出しは変わらない。

## 別のメッシュの同じ名前のBlendShape

Avatar Optimizerは同じ名前のBlendShapeを、メッシュの統合で1つにまとめることがある。FXが動かさない別のメッシュの同じ名前のBlendShapeが混ざると、1つの重みでFXが動かしていない頂点まで動く。FXが動かさないBlendShapeの行き先まで追うと、書き出しの最適化の道筋が増えて書き出す中身が変わりうる。そこで、FXが動かさない同じ名前のBlendShapeを別のメッシュが持つときは、最適化のあとに別のレンダラーへ移ったものを`blendShape`に数えて書かない。統合されずに元のレンダラーに残ったものは書く

## 確かめ方

- EditMode: `AvatarAnimatorCaptureTests`・`AvatarAnimatorDataTests`・`UnusedMorphTargetsTests`・`ExportOptimizationBindingsTests`・`QuickTransferMatchesDefaultNormalTests`
- 書き出しの回帰: 基準と比較ツールの`--allow-kept-morphs`で比べる。拡張が指すために残ったモーフターゲットを外して番号を戻し、それ以外が同じことを確かめる。メッシュのモーフターゲットが全部外れたときは、空になった`targets`・`weights`・`extras`も外す（書き出しは空のものを書かない）。残したモーフターゲットの分だけ頂点データを詰めて減った量が変わるので、その警告のMBの数値は比べない
- 測った結果: 手元の検証用アバター92体（3体は変更の前後とも同じ理由で書き出せない）を比べて、92組とも一致した。71体で拡張がBlendShapeを書き、拡張が指すために残したモーフターゲットは合わせて536個（1体で最大30個）。VRMの大きさは合計で約4%、1体あたりの中央値で約1%、最大で約22%増えた。拡張のJSONは中央値が約70KB、最大で約1.6MB
