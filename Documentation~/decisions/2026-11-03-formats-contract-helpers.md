# Formats の JSON 検査を 1 つにまとめ、schema 1 の書き込みと表情ボードの重複した通知を消す

- 日付: 2026-11-03
- 対象: `Formats/Runtime/ExpressionAnimationData.cs`・`Formats/Runtime/LipSyncData.cs`・`Formats/Runtime/ExpressionGestureData.cs`・`Formats/Runtime/ObjectToggleData.cs`・`Formats/README.md`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/FormatsContractBoundaryTests.cs`・`Tests/Editor/ExportGimmickTests.cs`・`Tests/Editor/ExpressionAnimationDocumentTests.cs`・`Tests/Editor/VrChatMenuExpressionTests.cs`・`Tests/Editor/Fixtures/AnimatedExpressionFixture.cs`

## 背景

Formats の契約型 4 つ（表情のアニメーション・リップシンク・ジェスチャーの表情・オブジェクトの切り替え）は、それぞれ `Obj`・`Arr`・`Text`・`Number` などの JSON の検査を自前で持っていた。中身はほぼ同じで、違うのは例外のメッセージと、ExpressionAnimationData の `Obj` がキーを確かめないことだけだった。

`ExpressionAnimationData.Write` は schema 1（point が書き出し用の `__VRVlog_Anim_` の変形を名前で指す形）を書く。書き出しは 2026-10-19 から schema 2 だけを書いており、`Write` を呼ぶのはテストだけになっていた。

表情ボードのセッションは、補助ギミックを省略・保持したときに「補助ギミックを省略: 」「自動除外せず保持: 」の警告を足していた。ボードの警告は保存時の書き出しの警告に足される。書き出しのほうは、省略したギミックを結果の一覧の「省略」の行に記録し、保持したギミックには同じ「自動除外せず保持: 」の警告を足す。そのため、省略したギミックは「省略」の行と通知の両方に出ていた。

## 決定

- 4 つの型の JSON の検査を `VRVlog.AvatarFormats.ContractJson`（internal）にまとめる。型ごとに今のメッセージを渡して持つので、例外のメッセージは 1 文字も変えない。
  - 置き場所は `ExpressionAnimationData.cs` の末尾にする。アプリのテストのスクリプトの 1 つが `ExpressionAnimationData.cs` だけを単独でコンパイルしているので、別のファイルに置くとそれが通らなくなる。Formats/Runtime にファイルも足さない。
  - 公開の `JsonAccess` は使わない。同じ理由（単独でコンパイルされるファイルに無い）と、`JsonAccess` にはキーの集合・整数・有限の数の検査が無いため。
  - `Obj` はキーを渡したときだけキーの集合が一致することを確かめ、渡さないときは型だけを確かめる。ExpressionAnimationData は今までどおりキーを渡さない（余分なキーを受け付け、足りないキーは `KeyNotFoundException` になる）。
- `ExpressionAnimationData.Write`（schema 1 の書き込み）と、それだけが使っていた `WriteName` を消す。`Read` と `ReadDocument` は schema 1 を読み続ける。テストで schema 1 の入力が要るときは、`AnimatedExpressionFixture.SchemaOne` で辞書を組み立てる。
- 表情ボードのセッションは補助ギミックの警告を足さない。省略は書き出しの「省略」の行だけ、保持は書き出しの「自動除外せず保持: 」の通知だけに出る。使われなくなった「補助ギミックを省略: 」の翻訳を 4 つの言語のファイルから消す。

## アプリ側でやること

- Formats の固定のコミットを、この変更を含むコミットへ上げる。
- `tests/unity/AnimatedExpressionBehaviorTests.cs` の `ExpressionAnimationData.Write(new[] { animation })`（2 か所）を、`AnimatedExpressionFixture.SchemaOne` と同じ辞書を組み立てる関数に置き換える。拒否を確かめる箇所は `ExpressionAnimationData.Read(SchemaOne(animation))` が例外になることを確かめる形にする。
- アプリが持つ `ObjectToggleData.cs` の写しは、`ContractJson` を使うようになったので単独ではコンパイルできない。写しを消して Formats のものを使い、写しを単独でコンパイルしているスクリプトには Formats の `ExpressionAnimationData.cs` も渡す。写しの SHA256 を確かめるスクリプトの基準のコミットも合わせて直す。

## 確かめたこと

- `FormatsContractBoundaryTests` を変更の前のコードで先に通し（5/5）、変更の後も同じく通ることを確かめた。数値の小数・NaN・無限大・文字列、1024 文字と 1025 文字・空文字、キーの過不足、配列の要素数の上下限、整数の ±1000000、0 以上の整数の上限で、受け付けるか拒むかと例外のメッセージが変わらないことを見る。
- `ExportGimmickTests.BoardSessionLeavesGimmickLinesToTheExport` は、変更の前のコードでは「補助ギミックを省略: gimmick — …」が残って失敗し、変更の後は通る。省略したギミックは結果の一覧の「省略」の行が 1 つだけで、同じギミックの通知の行は無い。
- `ExpressionAnimationDocumentTests` の `SchemaOneWriteRejectsMorphReferences` は消した `Write` のテストなので消した。
