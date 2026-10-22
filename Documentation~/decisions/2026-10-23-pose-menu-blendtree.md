# 体を動かさないBlendTreeのメニューをポーズの候補にしない

- 日付: 2026-10-23
- 対象: `Editor/PoseMenuResolver.cs`・`Tests/Editor/PoseRegistrationTests.cs`

## 背景

Modular Avatar 1.19.0-alpha.2 で `MaReactiveExpressionExportTests.MenuReactiveShapeSurvivesCanonicalBuildAndVrmReload` を回すと、書き出しの警告に次の「ポーズ未対応」が出ていた（1.18 では出ない）。

- メニューの Expression Parameter が Int・Bool のとき: `Expression ParametersとAnimatorパラメーターの型が一致しません: Menu`
- Float のとき: `複数レイヤーのWrite Defaultsによる暗黙の姿勢合成は未対応です。`

MA 1.19 は Reactive Component（Object Toggle・Shape Changer など）を、FX の 1 つの状態 `Apply` に置いた BlendTree で組む。Direct の重みが常に 1 の `$$MA/RC/AlwaysOne` の下に、メニューのパラメーターで切り替える 1D の BlendTree がある。メニューのパラメーターは Animator では Float になる。1.18 までは状態とクリップで組んでいた。

`PoseMenuResolver` は、メニューが体を動かすレイヤーに届かないなら「体のポーズではない」として警告を出さない（`CannotSelectBody`）。ここで BlendTree を持つ状態は中身を見ずに体を動かすものとして扱っていたので、1.19 の表情・衣装のメニューが体のポーズの候補として最後まで検査され、上の警告になっていた。

## 決定

- BlendTree は、その下（入れ子を含む）のクリップのどれかが体を動かすときだけ体を動かすものとして扱う。クリップは Animator Override Controller の置き換えを通して見る。クリップでも BlendTree でもない Motion は、これまでどおり体を動かすものとして扱う。中身が空の枝は数えない。
- `CannotSelectBody` と、ポーズを組み立てる側（`Read` の `controllerHasBody`・各レイヤーの `hasBody`）の両方を同じ判定にする。1.19 の Reactive Component の層が体のポーズのメニューに混ざって「BlendTreeによる合成は未対応です」になるのも避けるため。
- 体を動かすクリップを含む BlendTree は、これまでどおり候補として検査し、合成できないものは警告に出す。

## 確かめたこと

`PoseRegistrationTests.MenuThatCannotReachABodyLayerIsNotAnUnsupportedPose` に、MA 1.19 と同じ形（Direct の BlendTree・常に 1 の Float・Animator では Float、Expression Parameter は Bool のメニュー）の FX を足した。

- `BlendTreeClothingToggleIsNotReportedAsUnsupportedPose`: 衣装の切り替えだけの BlendTree は体のポーズではない扱いになり、「ポーズ未対応」の警告が出ない。
- `BlendTreeThatMovesTheBodyStillReportsThePose`: 同じ BlendTree のクリップが背骨の回転を動かすときは、これまでどおり候補として検査され、警告が出る。

## 残したこと

- 体を動かすメニューのパラメーターを MA 1.19 の Reactive Component も使うと、Animator では Float、Expression Parameter では Int・Bool になり、型の不一致として警告が出る。VRChat は Int・Bool の Expression Parameter で Float の Animator パラメーターを動かせるが、そのときの条件（Equals など）の評価を確かめていないので、型の判定は変えていない。
