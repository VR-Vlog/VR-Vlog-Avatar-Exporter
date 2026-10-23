# 表情ボードの保存で元のアバターのハッシュを 1 回にする

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`（`Save`）・`Editor/ExperimentalExpressionCaptureSession.cs`（`RequireValid`・`Export`）・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

2026-10-21 の `2026-10-21-expression-board-open-speed.md` で、表情ボードを開くときの元のアバター全体のハッシュ（`ExportSourceFingerprint`）を 1 回にした。そのあと 2026-10-22 に、一覧だけで保存するときも編集されたアバターをふつうのエラーで止めるため、`Save` の最初に `RequireCurrentSource`（ハッシュを必ず取る確認）を足した。`Session.Export` も書き出しの最初に同じハッシュを必ず取るので、1 回の保存で同じハッシュを続けて 2 回取っていた。

40 体を同じ PC のバッチモードで書き出して測ると、`Save` の確認が合計 14.1 秒（1 体の中央値 239 ミリ秒、最大 1.4 秒）、`Export` の確認が合計 13.8 秒で、2 回目は 1 回目と同じ結果を出すだけだった。2 回のあいだはエディタのループに戻らず、ダイアログも出ないので、元のアバターは変わらない。

## 決定

- `Save` の確認はそのまま残す。編集されたアバターは、これまでどおり書き出す前にふつうのエラーで止まる。
- `Save` は、確認の直後に始める最初の書き出しにだけ、元のアバターを確かめ済みと渡す（`Export` の `sourceChecked`）。`Export` はそのときだけハッシュを取らず、レンダラーの有無・有効・メッシュの差し替えの軽い確認は続ける。
- 回復ウィンドウからのやり直しは、同じ書き出しの関数を後から呼ぶので、確かめ済みを渡さない。やり直しは時間がたってから行うので、`Export` がこれまでどおりハッシュを取り直す。
- `Export` の既定は変えない。ほかの呼び出しやバッチの中の書き出しは、これまでどおり必ずハッシュを取る（`ExportInABatchStillSeesAnEditOfTheSource`）。

## 確認

- `ATransferChecksTheSourceForTheRecordsAndTheExportOnly` で、1 回の転送でハッシュが 1 回だけ取られることを確かめる（これまでは 1〜2 回を許していた）。
- `ATransferAfterAnEditOfTheSourceStops` で、ボードを開いたあとに編集したアバターの転送が、これまでと同じメッセージで止まり、ファイルを書かないことを確かめる。
- 40 体の書き出しで、VRM のバイト列が変更前と一致することと、時間が短くなったことを PR に書く。

## 入れなかったもの

- ボードを開くときのハッシュ（40 体で 25.5 秒）は、開いたあとの編集に気づくための元になるので残す。
- 転送で書いたあとの `AppTransferExport.SourceStamp` のハッシュは、次の転送で前のファイルを使い回してよいかを決めるためのもので、測った書き出しの経路には入っていないので変えていない。
