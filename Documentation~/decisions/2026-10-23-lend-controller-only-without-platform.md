# 最適化の間に root へ controller を貸すのは、AAO が root の controller を読むアバターだけにする

- 日付: 2026-10-23
- 対象: `Editor/NdmfExportPreparation.cs`（`AvatarOptimizerParsesRootController`）・`Tests/Editor/NdmfAnimationModeSamplingTests.cs`・`Tests/NdmfPlugin/AnimationModeSamplingTestPlugin.cs`・`Tools/run-unity-compatibility.py`・`Tools/NdmfPreparationTestShim.cs`

## 背景

0.12.0-beta.8 から、メニューのアイコンをアニメーションで撮る他社の NDMF プラグインを入れた VRChat のアバターを書き出すと、Unity ごと落ちることがあった（`Animator::Sample` の中で SIGSEGV）。`2026-10-23-lent-controller-one-layer.md` で、貸す controller に空のレイヤーを 1 つ持たせて、知られている落ち方は止めた。

ただし、書き出しの間だけ root に controller が付いているという、通常の NDMF のビルドでは起きない状態を、ほかのプラグインに見せ続けていた。Optimizing で root の Animator を触るプラグインは多く、Unity がこの状態のどこかでまた落ちれば、同じ種類の不具合が起きる。この種類の不具合を出さないため、ほかのプラグインに見せる状態を通常のビルドと同じにする。

## 原因

- controller を貸しているのは、Avatar Optimizer（AAO）の `AnimatorParser` のため。AAO は、avatar root に VRChat の `VRCAvatarDescriptor`、UniVRM の `VRMBlendShapeProxy`、com.vrmc.vrm の `Vrm10Instance` のどれも無いと、root の Animator の controller を読み、null なら `ArgumentNullException` で止まる。同梱版の `Vrm10Instance`（`2026-10-22-vendor-univrm.md`）は AAO から見えない。
- AAO がアニメーションを読むのは、アバターに AAO のコンポーネントがあるときだけ。
- VRChat のアバターは書き出し用のコピーにも `VRCAvatarDescriptor` が残るので、AAO は root の controller を読まない。VRChat のアバターに貸した controller は誰にも使われておらず、ほかのプラグインに見えるだけだった。手元で落ちた 5 回は、どれもこの controller を貸した VRChat のアバターで起きていた。

## 決定

controller を貸すのは、AAO 自身が root の controller を読む条件のときだけにする。アバターに AAO のコンポーネント（`AvatarTagComponent` の派生）があり、root に上の 3 つのコンポーネントのどれも無く、同梱版の `Vrm10Instance` があるとき。

- VRChat のアバターでは何も貸さない。Optimizing のプラグインは、通常の VRChat 向けの NDMF のビルドと同じ root を見る。そこで落ちるなら、そのプラグインは通常のビルドでも落ちるので、書き出しが原因ではない。
- 貸すのは VRChat の Avatar Descriptor が無い（NDMF から見て VRChat 以外の）アバターだけ。NDMF 1.8 以降は、対応する platform を宣言していないプラグインをこのアバターでは動かさない（`[RunsOnAllPlatforms]` などで宣言したものだけが動く）。VRChat 向けのプラグインは貸した controller を見ない。
- 貸すときは、これまでどおり空のレイヤーを 1 つ持たせる。
- AAO の条件を AAO の型名で写す。AAO を参照しないパッケージなので、型は名前で見る。プラグインやアバターの名前では分けない。
- 貸すのをやめる案も試したが、AAO 1.9.20（最新版。master も同じ）は VRChat 以外のアバターで null の controller を読んで止まり、AAO を使う VRM 向けのアバターが書き出せなくなる（`AvatarOptimizerBoxIntegrationTests` の削除のテストが失敗する）。

## テスト

- `NdmfAnimationModeSamplingTests.ExportSurvivesAnOptimizingPassThatSamplesTheRootThroughAnimationMode`: VRChat のアバターで、テスト用の Optimizing のパスが見る root に controller が無いこと（`Layers == -1`）と、`AnimationMode.SampleAnimationClip` を呼んでも書き出せることを確かめる。修正前は貸した controller が見えて失敗する。
- `NdmfAnimationModeSamplingTests.GenericAvatarLendsAaoAControllerThatAnimationModeCanSample`: VRChat の Avatar Descriptor が無く AAO の Trace and Optimize を付けたアバターで、貸した controller にレイヤーがあり、サンプルしても書き出せることを確かめる。テスト用のパスは NDMF 1.8 以降で `[RunsOnAllPlatforms]` を付け、このアバターでも動くようにした。
- テスト用のパスの戻り（レイヤーが 0 ならサンプルしない）を外して、この 2 つのテストを繰り返した。レイヤーの無い controller を貸す修正前の版は 5 回中 5 回 `Animator::Sample` の SIGSEGV で落ち、修正後は 20 回中 0 回だった。

## 成り立たない場合

- AAO が root の controller を読む条件を変えたら（platform のコンポーネントを足す、AAO のコンポーネントが無くても読む、など）見直す。条件がずれて null のまま AAO が読むと、`AvatarOptimizerBoxIntegrationTests` が AAO の例外で失敗する。
- AAO が同梱版の `Vrm10Instance` を知るようになったら、貸す処理そのものを消す。
- VRChat 以外のアバターで動くプラグインが、レイヤーのある controller で落ちる条件があれば、このアバターでは防げない。
