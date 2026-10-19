# VRMの表情名から入力元の接頭辞を外す

- 日付: 2026-10-19
- 対象: `Editor/VrChatExpressionMenu.cs`・`Editor/VrChatFxExpressions.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/VrChatAuthoredFaces.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/Fixtures/MenuExpressionFixture.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs` ほか既存のテスト・`CHANGELOG.md`・`Documentation~/ExperimentalExpressionCapture.md`

## 背景

VRMのカスタム表情の名前は、`VrmMenuExpressions.Add` が先頭に付ける `VRChat / ` と、表情を読んだ経路でできていた。

- メニュー: `VRChat / サブメニュー / … / 項目名`
- ジェスチャー: `VRChat / ジェスチャー / クリップ名`
- FX: `VRChat / FX / ステート名`（BlendTree なら値を付ける）
- FXのクリップ: `VRChat / FXクリップ / クリップ名`
- FaceEmo: `VRChat / 経路 / グループ / 表情パターン名 / 番号 / 基本・左トリガーなど / クリップ名`
- 表情・ポーズ指定UIで記録した表情: `VRChat / 記録 / 名前`

アプリの表情一覧にはこの名前がそのまま並ぶ。経路が長く、項目名やクリップ名が末尾に隠れて読みにくかった。

`2026-10-19-expression-animations-v2-writer.md` から、`VRVLOG_expression_animations` の schemaVersion 2 の `expressions[]` に、登録した表情ごとの入力元（`source`: `menu`・`gesture`・`faceEmo`）が入る。入力元を名前で見分ける必要はなくなった。

## 決定

- VRMへ書く名前は「メニューの名前、無ければクリップ名」にする。`VRChat / ` と経路は付けない。
  - `VrChatExpressionMenu.Entry` に `ExportName` を足し、表情を読む各処理で入れる。`Name` は書き出し画面の一覧・メッセージ・`Id` のハッシュ用に、これまでどおり経路付きで残す。
  - メニュー: 項目名だけ。サブメニューの経路は含めない。
  - ジェスチャー: クリップ名。クリップが無ければ経路の最後の要素。BlendTree の値で分けた表情は、それに ` (値)` を付ける。
  - FX: ステートの motion（Override Controller の差し替えを反映したもの）が AnimationClip ならクリップ名。BlendTree ならステート名に ` (値)` を付ける（1つのクリップが無いため）。
  - FXのクリップ: クリップ名。
  - FaceEmo: 表情パターンの既定の顔は表情パターンの表示名（表示名をアニメーション名にする設定のときはクリップ名）。基本・左右・両トリガーの分岐はメニューの項目ではないのでクリップ名。表示名が空のときもクリップ名。
  - `ExportName` が空か空白だけのときは、`Name` の最後の ` / ` より後を使う（`Entry.VrmName()`）。`VrChatExpressionBaker` はこの名前で `VrmMenuExpressions.Expression` を作る。
- `VrmMenuExpressions.Add` は名前に接頭辞を付けない。次のどちらかに当たるときは ` (2)`・` (3)`…を付ける（`VrmMenuExpressions.UniqueName`）。比べるときは大文字・小文字を区別する。
  - 出力の `custom` に同じ名前がある（作者がVRM設定に入れたカスタム表情・記録した表情・先に登録した表情）。後から入る方に番号が付く。
  - 予約された名前と同じ（`VrmMenuExpressions.Reserved`）。予約された名前は次の3つ。
    - VRM 1.0 の preset の名前（`happy`・`blink`・`aa` など、`UniVRM10.ExpressionPreset` の `custom` 以外）。UniVRM は読み込んだ表情を `SubAssetKey(VRM10Expression, 名前)` で区別するため、カスタム表情 `happy` は preset の `happy` と同じキーになる。
    - Unified Expressions に正規化できる名前（`JawOpen` など、`UnifiedExpressionRegistry.TryCanonicalize`）。アプリとフェイストラッキングの出力がそのチャンネルとして読むのを避ける。
    - ARKit のフェイストラッキングの名前（`VrmTrackingExpressions.Names`）。トラッキングの出力（`ExportOptimizationBindings.ApplyTracking`）は同じ名前のカスタム表情があると止まるため。
- v2 の `expressions[].expression` と、アニメーションの `expression` は番号を付けたあとの名前にする。
- 記録した表情（`ExperimentalExpressionCaptureSession`）は `pose.Name` をそのまま名前にし、同じ規則で番号を付ける。記録の表情は `Add` より前にVRMへ入るので、後から入るメニューなどの表情の方に番号が付く。
- 名前の接頭辞で「この書き出しが登録した表情」を見分けていた2か所を、v2 の表で見分けるようにした。
  - `VrmMenuExpressions.CountRegistered`: v2 の `expressions` に載っていて `morphTargetBinds` が空でない表情を数える。拡張が無ければ0。書き出し画面の「VRChat表情 {1}件」はこの数を出す。
  - `VrmExpressionBindings.AddEmotions`（空の喜怒哀楽を名前から埋める）: メニューなどの表情から埋める候補を、`AddMissing` の引数 `emotionCandidates` で渡した名前に限る。`UniVrmOneClickExporter` は `Add` のあとの v2 の表の名前（`VrmMenuExpressions.RegisteredNames`）を渡す。作者のカスタム表情は候補にしない。引数を渡さないとき（`LilToonGlbExtension`）は候補なしとして扱う。名前の判定（`ExpressionNameInference.MatchMenuPath`）には接頭辞の無い名前をそのまま渡す。
- 記録の流れ（表情・ポーズ指定UI）は `inferEmotionPresets: false` で書き出すので、`AddEmotions` は動かず候補も渡さない。

## アプリへの影響

- VR Vlog は v2 の表の `source` で入力元を見分ける。名前は表示にだけ使う。
- v2 の表を読まない古いアプリ・ほかのVRMアプリでは、表情一覧の名前から経路が消えて短くなるだけで、表情の動きは変わらない。同じ名前が重なった表情は ` (2)` などで区別される。

## 検討した代替案と不採用理由

- 名前に入力元だけを残す（`Menu / 笑顔` など）: 入力元は v2 の表にあり、名前に残すと一覧が読みにくいままになる。
- FX の表情にステート名を使う: ステート名は作者が Animator の整理に付けた名前で、メニューにも出ないため使わない。1つのクリップが無い BlendTree だけはステート名を使う。
- 予約された名前を preset 名だけにする: Unified Expressions・ARKit の名前と同じカスタム表情は、フェイストラッキングの出力で止まるか、アプリがトラッキングのチャンネルとして読むため、まとめて避けた。

## 確認したこと

- `Tests/Editor/Fixtures/MenuExpressionFixture.cs`（`Tools/run-behavior-tests.ps1` のホスト上の挙動テスト）で、同じ名前の表情・作者のカスタム表情・preset 名・Unified Expressions の名前に番号が付くこと（`Smile`・`Smile (2)`・`Smile (3)`・`Authored (2)`・`happy (2)`・`JawOpen (2)`）、v2 の表が同じ名前と入力元を持つこと、`CountRegistered` が v2 の表から数え拡張が無いと0になること、`UniqueName` が大文字・小文字を区別することを確かめる。
- `Tests/Editor/ExpressionNameInferenceTests.cs` で、接頭辞の無いメニューの表情・記録の表情から喜怒哀楽が埋まり、作者のカスタム表情からは埋まらないこと、候補が空のときと引数を渡さないときの出力が同じことを確かめる。
- 表情名を期待値に持つ既存のテストを新しい名前に直した。
