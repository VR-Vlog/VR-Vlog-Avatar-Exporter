# その他の表情を由来の順に書き、並びを文書に書いておく

- 日付: 2026-11-08
- 対象: `Editor/VrmMenuExpressions.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Formats/Runtime/ExpressionAnimationData.cs`・`Schema/VRVLOG_expression_animations.2.schema.json`

## 背景

アプリの表情の一覧を、喜怒哀楽 → ハンドジェスチャーで切り替わる表情 → FaceEmo の表情 → それ以外のボーンを動かさない表情 → それ以外のボーンを動かす表情の順にしたい。

今の並びは次のとおりで、由来では並んでいない。

- 喜怒哀楽は VRM のプリセット（happy・angry・sad・relaxed）に入り、UniVRM の列挙順でアプリの一覧の先頭に来る。
- カスタム表情は表情ボードの「その他の表情」の順（追加したファイル、アバターのクリップをクリップ名の順）に書く。
- アプリは 2026-11-07 の決定（`2026-11-07-bone-based-expressions.md`）で、ボーンを動かす表情を一覧の後ろへ送っている。

`VRVLOG_expression_animations` の `source` は、表情ボードからの書き出しでは FaceEmo に登録したクリップだけ `faceEmo`、それ以外はハンドジェスチャーで切り替わるかどうかに関係なくすべて `gesture` だった。ハンドジェスチャーで切り替わるかどうかは表情ボード（`IsGestureClip`）が知っているが、書き出しに渡していなかった。

## 決定

- 表情ボードは、ハンドジェスチャーで切り替わるクリップを `gestureFaces` として書き出しに渡す。`source` は FaceEmo に登録したクリップなら `faceEmo`、`gestureFaces` にあれば `gesture`、それ以外は `menu` にする。`gestureFaces` を渡さない呼び出しは、今までどおり FaceEmo 以外を `gesture` にする。
- `VrmMenuExpressions.Add` はカスタム表情を `gesture` → `faceEmo` → `menu` のボーン・表示を動かさないもの → `menu` の動かすもの、の順に書く。同じ順位の中は今の順のまま（安定ソート）。VRM の `custom` と拡張の `expressions` の順は同じ。
- 名前と ` (2)` の付け方は、並べ替える前の順で決めてから書く。アプリのお気に入りは表情の名前で覚えているので、並びが変わっても名前は変えない。
- 内容が同じでまとめた表情は、まとめた中でいちばん前の順位の場所に 1 回だけ書き、`source` もその表情のものにする。名前は今までどおり（喜怒哀楽を読める名前を優先）。
- FaceEmo に登録したクリップがハンドジェスチャーでも切り替わるときは `faceEmo` にする。FaceEmo の表情は FaceEmo が作るジェスチャーで切り替わるもので、アプリの「FaceEmo」の絞り込みにも残したい。
- 拡張の文書に任意のキー `order` を足し、この順で書いたときは `"order": "source"` と書く。アプリはこれがあればファイルの順のまま一覧に出し、無い古いファイルは今の並び（ボーンを動かす表情を後ろ）のままにする。
- `order` は任意の文字列として読む。後で別の値を書いても、今の読み取りが表情を捨てないようにする。

## 検討した代替案と不採用理由

- アプリだけで `source` とボーンから並べ替える: 古いファイルはその他の表情も `gesture` と書いてあるのでハンドジェスチャーの表情と見分けられない。古いファイルの並びまで変わる。
- Exporter だけで並べ、`order` を書かない: アプリがボーンを動かす表情を後ろへ送るので、ボーンを動かすハンドジェスチャーや FaceEmo の表情が最後に回る。
- `source` に新しい値（その他の表情を表す値）を足す: 今のアプリの読み取りは知らない `source` があると拡張全体を捨てるので、古いアプリで表情が出なくなる。`menu` は今の読み取りが知っている値なので、これを使う。

## 互換

- 今のアプリの読み取りは知らないキーを無視し、`menu` も知っているので、新しいファイルを読める。並びは古いアプリでもファイルの順にボーンを動かす表情を後ろへ送ったものになる。
- 古いファイルには `order` が無いので、新しいアプリでも並びは今のまま。

## 確かめたこと

- `ExpressionAnimationDocumentTests.OrderIsOptionalAndReadAsText`: `order` が無い文書・`source`・知らない値の読み書き。
- `ExpressionAnimationV2WriterTests.EveryRegisteredExpressionIsListedWithItsSource`・`FacesAreGroupedBySourceInTheListsOrder`: 由来の順、同じ順位の中の順、名前の ` (2)` が付く表情が変わらないこと、まとめた表情が前の順位に 1 回だけ並ぶこと、VRM の `custom` の順。
- `ExpressionBoneAnimationExportTests.CustomFacesAreWrittenBySource`: 書き出し全体で `gestureFaces` が `source` と順に効くこと、ボーンを動かすその他の表情が最後に来ること、`gestureFaces` を渡さないと今のまま。
