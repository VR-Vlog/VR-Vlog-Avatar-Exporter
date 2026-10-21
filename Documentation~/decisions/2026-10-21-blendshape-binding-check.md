# 曲線が BlendShape かを見る判定を `PoseSampling.IsBlendShape` にまとめる

- 日付: 2026-10-21
- 対象: `Editor/PoseSampling.cs` ほか、BlendShape の曲線を判定していた `Editor/` の 16 ファイル

## 背景

Clip の曲線（`EditorCurveBinding`）が BlendShape の曲線かを見る式 `binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)` と、その否定 `binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith(...)` が、`Editor/` の 31 か所に同じ形で書かれていた。`NeutralShapePlan` と `VrChatGestureExpressions` には、同じ式の private な `IsMorph` がそれぞれあった。

## 決定

- `PoseSampling.IsHumanoidChannel` の隣に `internal static bool IsBlendShape(EditorCurveBinding binding)` を足す。中身は上の式そのまま。
- 型の確認と `blendShape.` の接頭辞の確認が、同じ条件式の中で隣り合って書かれている 30 か所を `PoseSampling.IsBlendShape(binding)`（否定は `!PoseSampling.IsBlendShape(binding)`）に置き換える。
- `NeutralShapePlan` と `VrChatGestureExpressions` の `IsMorph` を消し、呼び出し（`Where(IsMorph)` のようなメソッドグループも含む）を `PoseSampling.IsBlendShape` に替える。

## 置き換えなかった箇所

- `MaAppearanceSnapshot`（`name.StartsWith("blendShape.")`）と `ExportOptimizationMarkerInformation`（`property.Property.StartsWith("blendShape.")`）。`EditorCurveBinding` ではなく、型の確認も別の形で書いているので、そのままにした。
- `ExperimentalExpressionCaptureSession.SelectCandidateByDefault` の中の 1 か所。このメソッドは使われていないので、別の変更（`2026-10-21-remove-unused-members.md`）で丸ごと消す。両方の変更が同じ行に触れないように、ここでは触らない。
- テスト（`Tests/Editor/FacialProjectionTests.cs`）の、接頭辞だけを見ている 1 か所。

## 書き出す内容が変わらない理由

- `IsBlendShape` の中身は置き換えた式と文字単位で同じで、型の確認を先に、接頭辞の確認を後に行う。型の確認が偽なら接頭辞を見ない点（短絡評価）も変わらない。どちらの確認も副作用がない。
- 否定の形 `A' || B'`（`A' = 型が違う`・`B' = 接頭辞が違う`）は `!(A && B)` と同じで、評価する順番も同じなので、`!IsBlendShape(binding)` に置き換えても結果も評価の順番も変わらない。
- 置き換えた式の前後の `&&`・`||` は、置き換え前から `A && B` や `A' || B'` をひとかたまりとして評価する並び（`X && A && B` は `(X && A) && B`、`X || A' || B'` は `(X || A') || B'`）になっている箇所だけを選んだ。`A' || B' && C` のように優先順位で分かれる並びはなかった。
- 置き換えた箇所の `binding` は、すべて `EditorCurveBinding`（`AnimationUtility.GetCurveBindings` の結果、`HashSet<EditorCurveBinding>`、`Func<..., EditorCurveBinding, bool>` の引数）だった。

## 確認したこと

- 確認: 検証用の PC での書き出し突き合わせと EditMode テスト（PR 本文に記載）
