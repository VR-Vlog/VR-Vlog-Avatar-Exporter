# VRMの表情名から入力元の接頭辞を外す

- 日付: 2026-10-19
- 対象: `Editor/VrChatExpressionMenu.cs`・`Editor/VrChatFxExpressions.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/VrChatAuthoredFaces.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/TrackingExpressionNames.cs`・`Editor/VrmTrackingExpressions.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/Fixtures/MenuExpressionFixture.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs` ほか既存のテスト・`CHANGELOG.md`・`Documentation~/ExperimentalExpressionCapture.md`

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
  - 予約された名前と同じ（`VrmMenuExpressions.Reserved`）。アプリは v2 の `expressions[]` か `animations[]` の名前が1つでも次のどれかに当たると、表情アニメーションの拡張をまるごと捨てる。予約された名前はその上位集合にする（アプリが拒む名前はすべて予約し、予約の方が広いのは構わない）。
    - VRM 1.0 の preset の名前（`happy`・`blink`・`aa` など18個、`UniVRM10.ExpressionPreset` の `custom` 以外）。大文字・小文字を区別した完全一致。アプリも同じ比べ方をする。UniVRM は読み込んだ表情を `SubAssetKey(VRM10Expression, 名前)` で区別するため、カスタム表情 `happy` は preset の `happy` と同じキーにもなる。
    - ARKit（Perfect Sync）のチャンネル名（`TrackingExpressionNames.IsPerfectSync`）。アプリの判定と同じく、前後の空白を除いた名前が、大文字・小文字を区別せずにチャンネル名と一致するか、`.`・`/`・`\`・`_`・`-`・`:` の直後でチャンネル名で終わるとき（`Fcl_ALL_JawOpen`・`Face.jawOpen` など）。アプリは `UE/` と `VRChat /` で始まる名前をこの判定から外すが、予約ではそれも含める。トラッキングの出力（`ExportOptimizationBindings.ApplyTracking`）も同じ名前のカスタム表情があると止まる。チャンネル名の一覧は `VrmTrackingExpressions` から `TrackingExpressionNames.PerfectSync` へ移し、ホストのテストでも使えるようにした。
    - Unified Expressions に正規化できる名前（`JawOpen`・`UE/JawOpen` など、`UnifiedExpressionRegistry.TryCanonicalize`）。アプリと同じ表を持ち、アプリの判定（名前そのもの・`UE/` 付きの完全一致、それ以外はこの関数）はこの関数で当たる名前に含まれる。
    - アプリが Unified Expressions のために足す静止用の表情の名前（`UnifiedExpressionRegistry.RestPrefix`、続けて数字だけが付いたものも）。
  - 番号を付けた名前（`happy (2)` など）は `)` で終わるので、上のどれにも当たらない。`UniqueName` は番号を付けた名前も予約と照らし、当たれば次の番号にする。
- v2 の `expressions[].expression` と、アニメーションの `expression` は番号を付けたあとの名前にする。
- 記録した表情（`ExperimentalExpressionCaptureSession`）は `pose.Name` をそのまま名前にし、同じ規則で番号を付ける。記録の表情は `Add` より前にVRMへ入るので、後から入るメニューなどの表情の方に番号が付く。
- 名前の接頭辞で「この書き出しが登録した表情」を見分けていた2か所を、v2 の表で見分けるようにした。
  - `VrmMenuExpressions.CountRegistered`: v2 の `expressions` に載っていて `morphTargetBinds` が空でない表情を数える。拡張が無ければ0。書き出し画面の「VRChat表情 {1}件」はこの数を出す。
  - `VrmExpressionBindings.AddEmotions`（空の喜怒哀楽を名前から埋める）: メニューなどの表情から埋める候補を、`AddMissing` の引数 `emotionCandidates` で渡した表情に限る。引数は「番号を付けたあとのVRMの名前 → 喜怒哀楽の判定に使う文字列」の対応にする。作者のカスタム表情は候補にしない。引数を渡さないとき（`LilToonGlbExtension`）は候補なしとして扱う。
- 喜怒哀楽の判定（`ExpressionNameInference.MatchMenuPath`）は、これまでと同じ文字列を読む。短くなるのはVRMの名前だけにする。
  - メニューなどの表情は経路付きの `Entry.Name`（`Smile / 1` など）。`VrChatExpressionBaker` が `VrmMenuExpressions.Expression.InferenceName` に入れ、`Add` が登録した名前を `RegisteredName` に書き、`VrmMenuExpressions.EmotionCandidates` が対応を作る。`UniVrmOneClickExporter` はこれを渡す。サブメニュー `Smile` の項目 `1` は、VRMの名前が `1` になっても happy を埋める。
  - 結果の一覧と警告に出す名前はVRMの名前にする（書き出したファイルの中で探せる名前のため）。
  - 記録した表情でこれまで判定に使っていたのは `VRChat / 記録 / ` を外した名前で、今の名前（`pose.Name`）と同じ。記録の流れ（表情・ポーズ指定UI）は `inferEmotionPresets: false` で書き出すので、`AddEmotions` は動かず候補も渡さない。

## アプリへの影響

- VR Vlog は v2 の表の `source` で入力元を見分ける。名前は表示にだけ使う。
- v2 の表を読まない古いアプリ・ほかのVRMアプリでは、表情一覧の名前から経路が消えて短くなるだけで、表情の動きは変わらない。同じ名前が重なった表情は ` (2)` などで区別される。

## 検討した代替案と不採用理由

- 名前に入力元だけを残す（`Menu / 笑顔` など）: 入力元は v2 の表にあり、名前に残すと一覧が読みにくいままになる。
- FX の表情にステート名を使う: ステート名は作者が Animator の整理に付けた名前で、メニューにも出ないため使わない。1つのクリップが無い BlendTree だけはステート名を使う。
- 予約された名前を preset 名だけにする: Unified Expressions・ARKit の名前と同じカスタム表情は、フェイストラッキングの出力で止まるか、アプリがトラッキングのチャンネルとして読むため、まとめて避けた。

## 確認したこと

- `Tests/Editor/Fixtures/MenuExpressionFixture.cs`（`Tools/run-behavior-tests.ps1` のホスト上の挙動テスト）で、同じ名前の表情・作者のカスタム表情・preset 名・Unified Expressions の名前に番号が付くこと（`Smile`・`Smile (2)`・`Smile (3)`・`Authored (2)`・`happy (2)`・`JawOpen (2)`）、v2 の表が同じ名前と入力元を持つこと、`CountRegistered` が v2 の表から数え拡張が無いと0になること、`UniqueName` が大文字・小文字を区別することを確かめる。
- 同じフィクスチャで、ARKit のチャンネル名（そのまま・先頭小文字・全部大文字・`Fcl_ALL_` などの前置き付き・前後に空白）、Unified Expressions の名前（そのまま・`UE/` 付き）、preset 名、静止用の表情の名前がすべて ` (2)` になり、番号を付けた名前は予約に当たらないこと、`Smile`・`Blink`・`Happy`・`SmileJawOpen` などはそのままであることを確かめる。
- 同じフィクスチャで、`Add` → `EmotionCandidates` → `AddMissing` の順に通し、サブメニュー `Smile` の項目 `1`（VRMの名前は `1 (2)`）が happy を埋めることを確かめる。
- `Tests/Editor/ExpressionNameInferenceTests.cs` で、接頭辞の無いメニューの表情・記録の表情から喜怒哀楽が埋まり、作者のカスタム表情からは埋まらないこと、候補が空のときと引数を渡さないときの出力が同じこと、判定が経路を読む（葉が決め、下が何も言わないときだけ親が決める）ことを確かめる。
- アプリの判定のコードと手元で突き合わせ、約6万6千の名前（チャンネル名・preset 名の大文字小文字の違い・前置き・後置きの組み合わせ）で、アプリが拒む名前がすべて予約に当たり、番号を付けた名前をアプリが拒まないことを確かめた（この突き合わせはリポジトリに入れていない）。
- 表情名を期待値に持つ既存のテストを新しい名前に直した。
