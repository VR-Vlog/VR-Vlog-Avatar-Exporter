# 土台が終わりの重みで持つBlendShapeは、0へ戻す逆向きのモーフターゲットを足して書く

- 日付: 2026-11-14
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`、`Tools/compare-exports.py`

## 背景

書き出しはシーンのBlendShapeの重みを土台のメッシュに焼き込み、残りを差分にする（`2026-11-13-avatar-animator-morphs.md`）。シーンで最後のフレームの重み（多くは100）にしてあるBlendShapeは、差分が0になり、glTFのモーフターゲットではどの重みも表せない。FXがそれを0へ戻すと（衣装を着けたときに体型を戻すなど）、拡張には書けず`blendShape`に数えていた。92体の書き出しのうち89体を調べると、56体にこの動きがあった。

## 決定

- 土台が終わりの重みで持つBlendShape（`baked`が0でなく`end`と同じ）をFXが動かすときは、書き出しのコピーのメッシュに、土台をそのBlendShapeの重み0へ戻す差分を1つ足す。名前は`__VRVlog_Reverse_`とレンダラーのパスとBlendShapeの名前のハッシュ。差分は元のメッシュで重み0の形から`baked`の形を引いたもの
- 拡張のターゲットはそれを指し、`baked`はそのまま、`end`を0にする。アプリの式`(v − baked) / (end − baked)`は変えない。`baked`が100なら`1 − v/100`
- どの曲線も土台と同じ重みのまま動かさないときは足さない（見た目が変わらず、使われないBlendShapeとして消えるだけなので）。フレームが複数あるもの、リップシンクのもの、加算のレイヤーが動かすものは今までどおり`blendShape`に数える
- 足したBlendShapeは、FXが動かすBlendShapeと同じく最適化の後の行き先を追う。行き先が無い、拡張がサイズの上限を超えるなどで拡張が指さなかったときは、使われないBlendShapeとして最後に消える

## 既知の制限

- Avatar OptimizerのTrace and OptimizeがBlendShapeを最適化するとき、差分が0の元のBlendShapeは意味の無いものとして消され（`FreezeMeaninglessBlendShapes`）、FXの曲線から拡張のターゲットを作れない。足した逆向きはどこからも指されず最後に消えるので、書き出しはこの変更の前と同じになる。FXの曲線を逆向きのBlendShapeへ付け替える、または元のBlendShapeの差分を逆向きにする形は、Avatar Optimizer 1.9以降で動かして確かめてから入れる

## 採らなかった案

- glTFに直接書く: 最適化の後の頂点の対応と、アクセサーの追加を別に作ることになる
- 差分が0の元のモーフターゲットを逆向きに置き換える: それを指すVRMの表情が変わる
- 拡張に2つ目の行き先の項目を足す: スキーマとアプリの読み込みを変えることになる。`end`を0にすれば今の形で足りる

## 確かめ方

- EditModeの`AvatarAnimatorCaptureTests`で、逆向きを足す・足さない・行き先が無いときを確かめる
- 書き出しの回帰で、基準と比べた差が、足した拡張と、拡張が指すモーフターゲットだけであることを確かめる。比較ツールは`--allow-kept-morphs`で、拡張が指す逆向きのモーフターゲットも外して比べ、外した数を出す
