# FXのAnimator Controllerで重複するパラメーターは最初の宣言だけを使う

- 日付: 2026-10-19
- 対象: `Editor/ExpressionDependencies.cs`・`Editor/AdditionalParameterConstants.cs`・`Editor/ExperimentalExpressionControllerScope.cs`・`Editor/ExpressionEvaluationSession.cs`・`Editor/FixedExpressionContext.cs`・`Editor/HeldAutomaticExpressionLayers.cs`・`Editor/NativeTreeTrajectory.cs`・`Editor/NeutralAdditionalDataPreflight.cs`・`Editor/NeutralCurveConditions.cs`・`Editor/NeutralParameterDependencies.cs`・`Editor/NeutralRandomRest.cs`・`Editor/NeutralShapeSampler.cs`・`Editor/PoseMenuResolver.cs`・`Editor/VrChatAuthoredFaces.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/VrChatFxExpressions.cs`・`Tests/Editor/VrChatMenuExpressionTests.cs`・`Tests/Editor/DirectFxExpressionDiscoveryTests.cs`・`CHANGELOG.md`

## 背景

FXのAnimator Controllerに同じ名前のパラメーターが3回宣言されているアバターがある。アセットのYAMLでは、`m_AnimatorParameters` に `m_Name: TrackingType`・`m_Type: 3`（Int）・同じ初期値の要素が3つ並んでいる。UnityのAnimatorはパラメーターを名前のハッシュで引くので、同じ名前の宣言は1つのパラメーターとして動く。VRChatでもこのアバターは問題なく動く。

書き出しでは、表情の読み込みが `ExpressionDependencies.FixedNeutralValues` の `initial.Add(parameter.name, value)` で `ArgumentException: An item with the same key has already been added. Key: TrackingType` を投げ、ステージの保護処理がメニュー由来の表情をすべて省いていた。アバターと衣装の組み合わせをすべて書き出した検証では、499件中31件がこれに当たった。

`controller.parameters` を名前で引く辞書に入れている箇所はほかにもあり（`ExpressionEvaluationSession` の型の表、`VrChatFxExpressions.Discover`、`NeutralShapeSampler` の型の表、`NativeTreeTrajectory`・`VrChatAuthoredFaces`・`NeutralAdditionalDataPreflight` の宣言の検査など）、`FixedNeutralValues` を直すだけでは次の箇所で同じように止まる。重複を見つけると証明をあきらめる箇所（`FixedExpressionContext.TryReachableFxCommands` に渡す配列、`GroupBy` で1件の名前だけを残す型の表、`Where(...).Length != 1` の検査など）もあり、こちらは例外にはならないが、表情や固定値の証明が通らなくなる。

## 決定

- `ExpressionDependencies.UniqueParameters(RuntimeAnimatorController)` を足す。`Controller(runtime).parameters` から、同じ名前（ordinal）の2つ目以降の宣言を落とした配列を返す。名前が1つのパラメーターに解決されるUnityの動作に合わせ、最初の宣言を使う。`null` の要素はそのまま残し、これまでの検査に任せる。
- パラメーターを名前で引く・名前ごとにまとめる箇所は、`controller.parameters` の代わりにこの関数を使う。評価用に作るコントローラーの複製（`ExpressionEvaluationSession`・`ExperimentalExpressionControllerScope`・`VrChatExpressionSampler` のプローブ）も、重複を落とした宣言で作る。複製の側でも後続の処理が名前で引くため。
- 名前の集合を作るだけの箇所・`Any` で有無を見るだけの箇所・プレイアブルへ値を設定するだけの箇所は変えない。重複があっても結果が変わらない。
- `TryReachableFxCommands`・`TryFixedGateReads`・`PoseMenuResolver.Resolve` が受け取った配列の重複を拒む検査はそのまま残し、呼び出す側で重複を落としてから渡す。
- 重複のないコントローラーでは、返す配列は `controller.parameters` と同じで、動作は変わらない。元のコントローラーのアセットは書き換えない。

## 確認したこと

- Unity 2022.3.22f1のRoslynで、Editorのアセンブリと `Tests/Editor` のアセンブリをコンパイルできることを確かめた（`Tests/Editor/ExpressionAnimationDocumentTests.cs` は、手元のビルド成果物に新しいアセンブリの参照が無いため除いた）。このPCではUnityのテストを実行していない。
- `VrChatMenuExpressionTests.RepeatedFxParameterDeclarationsKeepMenuExpressions` で、`TrackingType`（Int）を3回宣言したFXについて、`FixedNeutralValues` が例外を投げずに `TrackingType=6` を返すこと、メニューの選択で表情が75の重みで取れること、元のコントローラーが変わらないことを確かめる。変更前は `FixedNeutralValues` の `initial.Add` で失敗する。
- `DirectFxExpressionDiscoveryTests.RepeatedFxParameterDeclarationsStillDiscoverFaces` で、同じFXからFXの遷移由来の表情が見つかり、エラーにならないことを確かめる。変更前は、パラメーターを名前で引く辞書を作るところ（`VrChatFxExpressions.Discover` の `ToDictionary` など）で `ArgumentException` になる。
