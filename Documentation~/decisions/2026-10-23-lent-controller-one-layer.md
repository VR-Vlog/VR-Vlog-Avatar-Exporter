# 最適化の間だけ root に貸す AnimatorController に空のレイヤーを 1 つ持たせる

- 日付: 2026-10-23
- 対象: `Editor/NdmfExportPreparation.cs`（`PlaceholderController`）・`Tests/Editor/NdmfAnimationModeSamplingTests.cs`・`Tests/NdmfPlugin/AnimationModeSamplingTestPlugin.cs`・`Tools/run-unity-compatibility.py`

## 背景

0.12.0-beta.8 で、メニューのアイコンをアニメーションで撮る他社の NDMF プラグインを入れたアバターを書き出すと、Unity ごと落ちるようになった（`Animator::Sample` の中で SIGSEGV）。0.12.0-beta.7 までは落ちなかった。

## 原因

- 同梱版の UniVRM を入れたとき（`2026-10-22-vendor-univrm.md`）、同梱版の `Vrm10Instance` があるアバターでは、NDMF の Optimizing の間だけ root の Animator に空の `AnimatorController` を貸すようにした。この controller にはレイヤーが 1 つも無かった。
- 落ちるプラグインは Optimizing で、メニューのアイコンを撮るために `AnimationMode.SampleAnimationClip` を avatar root に対して呼ぶ。
- Unity 2022.3.22f1 は、レイヤーが 0 個の controller を持つ Animator で `AnimationMode.SampleAnimationClip` を呼ぶと、Animator が有効でも無効でも落ちる。controller が無いとき（null）と、空のレイヤーが 1 つあるときは落ちない。空のプロジェクトで 4 通りを試して確かめた。
- そのため、書き出し（Optimizing まで流す経路）でだけ落ち、表情ボードのプレビュー（Transforming まで）では落ちなかった。

## 決定

貸す controller に、空のステートマシンを持つレイヤーを 1 つ持たせる（`PlaceholderController`）。

- AAO から見た結果は変わらない。AAO はレイヤーにステートもモーションも無い controller を、レイヤーが無い controller と同じく何も動かさないものとして読む。
- レイヤーとステートマシンは `HideAndDontSave` で作り、最適化が終わったら controller と一緒に消す。
- プラグインの名前では分けない。Optimizing で `AnimationMode` を使うほかのプラグインにも同じように効く。

## テスト

`NdmfAnimationModeSamplingTests.ExportSurvivesAnOptimizingPassThatSamplesTheRootThroughAnimationMode`: MA の部品を付けて NDMF を流すアバターに同梱版の `Vrm10Instance` を付け、テスト用の Optimizing のパス（`AnimationModeSamplingTestPlugin`）が root に対して `AnimationMode.SampleAnimationClip` を呼んでも書き出せることを確かめる。パスはサンプルする前に貸された controller のレイヤー数を記録し、0 ならサンプルせずに戻るので、修正が戻ったときもテストランナーは落ちずに失敗する。主経路のテストには `AnimationMode` を使う NDMF のパスが無く、この不具合を見つけられないので足した。

## 成り立たない場合

- レイヤーのある controller で `AnimationMode` のサンプルが落ちる別の条件があれば、このレイヤーでは防げない。
- AAO が、レイヤーのある controller を、レイヤーの無い controller と違うものとして読むように変わったら見直す。
