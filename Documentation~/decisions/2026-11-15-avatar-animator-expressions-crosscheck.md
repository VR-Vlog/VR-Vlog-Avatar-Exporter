# 評価器で出せる表情だけを`expressions`に書き、突き合わせでマテリアルのスロットとノードも比べる

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/AvatarAnimatorCrossCheck.cs`、`Formats/Runtime/AvatarAnimatorFaces.cs`（新規）、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/Editor/AvatarAnimatorCrossCheckTests.cs`

## 背景

拡張（`VRVLOG_avatar_animator`）の`expressions`には、表情ボードの表情のうち FX のクリップで出せるものを、ステートへの入り方つきで書いていた。アプリはこれを見て、表情を VRM の expression でなく評価器（FX の再生）で出すかを決める。ただ、書いた表情を評価器で出したときに VRM の expression と同じ見た目になるかは確かめていなかった。

突き合わせ（Unity の Animator と評価器を同じ入力で動かして比べる処理）は、BlendShape・オブジェクトの表示・レンダラーの有効・マテリアルの値だけを比べ、マテリアルのスロットの差し替え（`materialSlot`）と Transform（`translation`・`rotation`・`scale`）は比べていなかった。

## 決めたこと

- `expressions`には、次の A〜C を満たす表情だけを書く。満たさない表情は書かず、アプリは今までどおり VRM の expression で出す。外した表情は理由つきでコンソールに 1 行出す
  - A: 表情の VRM の bind がすべて拡張の対象に当たる。`morphTargetBinds`は同じノード・同じ morph の`morph`対象に当てる。`VRVLOG_expression_animations`の止まったトラックは、`active`を同じノードの`active`対象に、位置・回転・拡大率を同じノード・同じチャンネルの対象に当てる（glTF から Unity の空間へ、位置は x、回転は y と z の符号を戻す）
  - B: 評価器で表情を出したときの値が、A で求めた値と許容差の中で合う（`AvatarAnimatorCrossCheck.FaceMismatch`）。既定値から 30 フレーム進め、アプリと同じソルバーで表情を出すパラメーターの値を決めて入れ、150 フレーム進めてから比べる。許容差は BlendShape が 0.5、位置と拡大率が 1 mm、角度が 0.1°、マテリアルの色が 1/255
  - C: 表情に時間で進む動きがない。`VRVLOG_expression_animations`に BlendShape のアニメーションがある表情と、長さがあって値が変わるトラックがある表情は外す
- `materialColorBinds`と`textureTransformBinds`を持つ表情は A を満たさないものとして外す。Exporter が書くメニューの表情はこの bind を持たないので、対応づけは足さない
- 表情を出すパラメーターの値を決めるソルバーを、アプリの`AvatarAnimatorExpressions`から`Formats/Runtime/AvatarAnimatorFaces.cs`に中身を変えずに写した。アプリと Exporter が同じ値で表情を出すかを確かめるためで、アプリはこれに切り替えられる
- 突き合わせで`materialSlot`と Transform を比べる
  - `materialSlot`はスロットが持つマテリアルを glTF のマテリアルの番号に直して比べ、一致を求める。クリップが入れないマテリアルが入っているときは -2 として必ず違いにする
  - 空のサブメッシュと一緒に詰めた後ろのスロットは比べない。複製の Animator はクリップの元のスロットの番号に書くため、詰めた後の番号では別のスロットを読む
  - Transform はローカルの位置・拡大率・角度（インスペクターの値）・四元数を比べる。四元数は 4 つのチャンネルがすべて対象のとき、評価器の値を正規化し、Unity の四元数と同じ側の符号にそろえてから比べる。遷移中の線形の混ぜ合わせは長さが 1 より短くなるため
  - 待機の状態に戻すときは、1 つの Transform のチャンネルをまとめて入れる
- 評価器の遷移中の`materialSlot`の混ぜ方を Unity に合わせた。Unity 2022.3.22f1 で測ると、Write Defaults の状態どうしの遷移で片側のモーションだけがスロットを書くとき、Write Defaults の既定値は重みを持たず、書く側の値が重みのある間ずっと出る（入る遷移なら始まった次のフレームから、出る遷移なら終わるまで）。両側が書くときは重みが 0.5 を超えた側が出る。評価器はこれまで既定値にも重みを持たせ、どちらの場合も 0.5 で切り替えていた。直したのは測った場合（書かない側が Write Defaults のモーションを持つ状態）だけで、空の状態・Write Defaults なしの状態・レイヤーの重みは今までどおり

`schemaVersion`は 1 のまま。書く表情が減るだけで、古いアプリは`expressions`に無い表情を VRM の expression で出す。

## 採らなかった案

- `materialColorBinds`・`textureTransformBinds`を`materialColor`・`materialTextureST`の対象に対応づける: メニューの表情では出てこず、マテリアルの番号とプロパティ名の対応を新しく作ることになる
- 四元数をチャンネルごとに q と -q のどちらかに合えばよいとする: x は正、y は負のように選べてしまい、逆向きの回転を見逃す

## 確かめたこと

- Unity 2022.3.22f1 の EditMode で`AvatarAnimatorCrossCheckTests`と`AvatarAnimatorCaptureTests`を回した（結果は PR に書く）
- 検証用のアバターを書き出し、`Tools/compare-exports.py`で変更前と比べた（結果は PR に書く）

## 残したこと

- アプリ側（4-10）で`AvatarAnimatorFaces`に切り替え、`expressions`にある表情を評価器で出す。評価器の`materialSlot`の直しもアプリに写す
- `materialSlot`の混ぜ方のうち、空の状態・Write Defaults なしの状態との遷移、ブレンドツリー、レイヤーの重みは Unity で測っていない
