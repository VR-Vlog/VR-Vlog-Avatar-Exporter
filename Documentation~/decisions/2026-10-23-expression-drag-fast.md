# 表情ボードのドロップでは移動に要る処理だけを行う

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`（`MoveTo`・`ApplyAutomaticPresets`・`ApplySuggestions`・`ExpressionCaptureAssetPostprocessor`）・`Editor/ExperimentalExpressionCaptureSession.cs`（`RequireSourceUnlessUnedited`・`EditorEdits`・`hashedEdits`）・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`（`BuildPresetPose` の使い回し・`SuggestBlinkPresets`・`BlinkShapes`）・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

**アプリに転送（通常）** の表情ボードで表情のアイコンをドラッグアンドドロップすると、離したあとにエディタが固まることがあった。検証PC（Unity 2022.3.22f1・batchmode）で、手元の6体のアバターに、同じ手順（3回 × 7通りのドロップ、中央値）でドロップを計測した。ドロップを処理する更新の時間と、そのあと何もしなくなるまでの更新を足した時間を見た。

| | ドロップの更新（ms） | ドロップから落ち着くまで（ms） |
|---|---|---|
| アバター1 | 1194 | 2139 |
| アバター2 | 2789 | 3754 |
| アバター3 | 287 | 561 |
| アバター4 | 1691 | 2788 |
| アバター5 | 1675 | 3141 |
| アバター6 | 1605 | 3011 |

内訳を分けて計ると、ドラッグ中（`DragUpdated` 1回あたり 0.10〜0.23ms）は原因ではなかった。時間はドロップのあとの次の処理にかかっていた。

- `MoveTo` の `RequireSourceForBatch` が、元のアバター全体（メッシュ・テクスチャ・マテリアル・シリアライズされた値）の指紋を毎回取っていた。1回 0.27〜1.38秒。
- `MoveTo` が `ApplyAutomaticPresets` で瞬きの欄を古いことにしていたため、次の更新で `BlinkFallbackDescription` がもう一度、新しいバッチで指紋を取っていた。1回 0.27〜1.42秒。瞬きの欄は元のアバター・瞬きの設定・除外だけから決まり、スロットの並びには関係しない。
- 自動の割り当ての提案（`SuggestPresets`）が、候補ごとに `BuildPresetPose` で表情の形を作り直していた。0.01〜1.76秒。そのうち `BlinkExportSession.Resolve` で瞬きの候補を探すのに、アバター5では 159ms かかっていた。

## 決定

1. ドロップ（`MoveTo`）では瞬きの欄を古いことにしない。`ApplyAutomaticPresets` のうち提案の適用と注意の更新だけを `ApplySuggestions` に分け、ドロップはそちらを呼ぶ。元のアバターが変わって作り直すときは、これまでどおり `Prepare` が瞬きの欄も作り直す。
2. ドロップは、前に指紋を取ったあとにエディタからアバターやアセットの変更の知らせが来ていなければ、指紋を取らない（`RequireSourceUnlessUnedited`）。知らせは `ObjectChangeEvents.changesPublished`・`Undo.undoRedoPerformed`・`EditorApplication.projectChanged`・`EditorApplication.playModeStateChanged`・アセットのインポート（`OnPostprocessAllAssets`）を数えた `EditorEdits.Count` で見る。セッションは、指紋が一致した時点の数（`hashedEdits`）を覚えておく。数が変わっていればこれまでどおり指紋を取り、違っていれば元のアバターから作り直す。安い確認（`ChannelDifference`）は毎回行う。
3. `BuildPresetPose` の結果をセッションの中で使い回す。キーは表情・変形のパス・変形の名前・クリップと、クリップの `EditorUtility.GetDirtyCount` にする。返すのは複製なので、受け取った側が書き換えても使い回す値は変わらない。作れなかったときの理由も覚えておき、同じ文で `InvalidOperationException` を投げ直す。`SourceChangedException` は覚えない。`EditorEdits.Count`・値の制限の設定・Gestureのクリップの数が変わったときと、設定を読み込み直したときに捨てる。
4. 瞬きの候補（`BlinkExportSession.Resolve` で見つけた変形）は、`EditorEdits.Count` と瞬きの設定が同じあいだは使い回す。候補をスロットに入れてよいかの確認（`PresetError`）は、ほかのスロットとの重なりで変わるので毎回行う。

## 確認

- 同じ手順で計り直した結果（ms）。

| | ドロップの更新（前→後） | 落ち着くまで（前→後） | 後の最大 |
|---|---|---|---|
| アバター1 | 1194 → 12.2 | 2139 → 12.2 | 60.9 |
| アバター2 | 2789 → 19.9 | 3754 → 19.9 | 46.5 |
| アバター3 | 287 → 1.9 | 561 → 1.9 | 3.1 |
| アバター4 | 1691 → 67.4 | 2788 → 67.4 | 151.6 |
| アバター5 | 1675 → 18.4 | 3141 → 18.4 | 78.4 |
| アバター6 | 1605 → 24.7 | 3011 → 24.8 | 130.2 |

- 全ドロップ（6体 × 7通り × 3回と、各アバターの最初の1回）のあとのボードの状態（各スロットの割り当て・除外・注意・瞬きの欄・プレビュー・顔の指紋）を変更前と変更後で突き合わせ、133件すべてが同じだった。
- 追加したテスト（`ExperimentalExpressionCaptureTests`）。
  - `ADropWithoutAnEditDoesNotHashTheSource`: 変更の知らせがないドロップでは指紋を取らない。
  - `ADropAfterAReportedEditFollowsTheSource`: Undoを記録してブレンドシェイプを変えたあとのドロップでは、指紋を取って元のアバターに合わせる。
  - `TheBoardsOwnWorkIsNotReportedAsAnEdit`: ウィンドウを表示して描画・プレビュー・ドロップを繰り返しても、ボード自身の処理は変更の知らせにならない（開いてからの知らせは0件で、ドロップは指紋を取らない）。
  - `ASlotReadsItsFaceAgainAfterTheFileChanges`: 使い回す表情の形は複製で、クリップの中身を変えると読み直す。
- 表情ボード・かんたん転送・表情の割り当てまわりの EditMode テスト8クラスを検証PCで回し、失敗はなかった。

## 検討して採らなかった案

- Codexの指摘のうち、変更の知らせの数は「変わっていない」ことの証明にならないので、指紋を取り直す合図にだけ使うという部分は、ドロップについては採らなかった。Undoを使わないスクリプトの書き換えは知らせが来ないが、その分も毎回指紋を取ると、ドロップの固まりがそのまま残るため。書き出しは `RequireCurrentSource` で毎回指紋を取り、違っていれば割り当てを残したまま作り直すので、書き出すVRMには影響しない。
- Codexが挙げた、ドロップを配置と後の評価に分ける案と、ボードを明示したスナップショットの上で動かす案は採らなかった。今回の原因は指紋の取り直しと表情の形の作り直しで、これらを使い回せば移動だけの時間に収まり、作りを大きく変える必要がなかったため。
- `EditorApplication.hierarchyChanged` は数えなかった。プレビュー用の複製を作るたびに来るおそれがあり、そうなるとドロップのたびに指紋を取ることになるため。Prefabモードでの編集は `ObjectChangeEvents` で知らされる。

## 欠点

- Undoを使わないスクリプトでアバターを書き換えたあとのドロップは、変更に気づかないまま前の複製の上で動く。次に割り当てを選んだとき・書き出すときに気づいて作り直す。
- まだ作っていない表情の形が要るドロップ（初めて置くクリップと表情の組み合わせなど）は、その形を作る分だけ遅い。計った中ではアバター4の 152ms がいちばん長かった。
- アバター4では、名前から表情が分からないクリップを形から見分ける処理に 1回 56ms ほど残っている。移動とは別の処理だが、これを外すと割り当ての提案が変わるため残した。
