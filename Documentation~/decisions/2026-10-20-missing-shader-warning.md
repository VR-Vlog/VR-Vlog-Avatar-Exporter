# シェーダーが見つからないマテリアルがあるときは、保存したうえで強く警告する

- 日付: 2026-10-20
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportFallbackConfirmation.cs`・`Editor/ExportResultList.cs`・`Editor/ExportResultWindow.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/SavedExpressionVrm.cs`・`Editor/AppTransferExport.cs`・`Editor/LanTransfer/AppTransferWindow.cs`・`Editor/LanTransfer/AppTransferWindow.uxml`・`Editor/Locales/*.json`・`Tests/Editor/ExportFallbackConfirmationTests.cs`

## 背景

アバターのマテリアルが、プロジェクトに入っていないシェーダーを使っていると、Unityはそのマテリアルを `Hidden/InternalErrorShader` に置き換えてピンク色で表示する。書き出しはこのマテリアルをlilToonに置き換えて黙って成功していたので、保存したVRMの見た目が元のアバターと違うことに、アプリで開くまで気づけなかった。

## 決定

書き出しは止めない。シェーダーが入っていなくてもVRMそのものは欲しいことがあり、実際に見つかったのも、そのシェーダーを入れていないテスト用のプロジェクトで書き出したケースだった。

検出は書き出し用コピーに対して、材質保存の段（`ExportPhase.Materials`）の最初、`UnsupportedShaderSubstitution.Apply` の直前で 1 回だけ行う。この時点では「書き出さないオブジェクト」とギミックの自動除外がコピーから消え、NDMF・Modular Avatarのビルド処理も済んでいるので、実際にglTFのメッシュになるものだけが残っている。原本で数えると、除外したレンダラーやビルド処理で外れたレンダラーのマテリアルでも「警告あり」になってしまう。一方、この直後の近似と、そのあとのlilToonへの置き換えはシェーダーが無いマテリアルも置き換えるので、それより前でないと見分けられない。

`MissingShaderMaterials` が、書き出し対象のレンダラー（`ExportRendererSelection.Enumerate` と同じ選び方で、メッシュのあるもの）のマテリアルから、シェーダーが無いか `Hidden/InternalErrorShader` のものを集める。ビルド処理が複製したマテリアルは `ExportRecoveryReport.SourceMaterial` で元のマテリアルに戻し、同じマテリアルを 1 回だけ数え、元の名前で載せる。見つかったマテリアルは 1 つずつ結果の一覧に「マテリアル」の行（`ExportFallbackKind.MissingShader`）として載せ、警告の先頭に「シェーダーが見つからないマテリアルが○個あります。…」という 1 文を足す。

数は `ExportFallbackReport.MissingShaderCount` に入れ、見出し・保存後の案内・表情ボード・アプリに転送の画面はすべてこの数を使う。一覧の名前は文字列で重ねないので、別のマテリアルが同じ名前だと名前の数と合わなくなるため。

本文は原因（シェーダーが見つからない）→影響（見た目を正しく再現できない）→対処（必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出す）の順に書く。ピンク色に表示されることは、結果一覧の行の理由にだけ書く。

保存したあとの状態は、次の 4 か所で「警告あり」にする。

1. 書き出し結果の画面の見出しを「VRMを保存しました（警告あり）」にする。
2. 書き出し画面の保存後の案内を警告の表示にして、シェーダーが見つからないマテリアルの数と対処を書く。
3. 表情ボードの保存結果の見出しを 1 と同じにし、同じ文の警告を出す。下の状態の 1 行も同じ文にする。
4. アプリに転送の画面で、VRMを保存したあと（QRコードの上）に同じ内容の警告を出す。次の書き出しを始めたときとアバターを選び直したときに消す。警告が出るときは、保存した時点（アップロード中でQRコードはまだ出ていない）でウィンドウの高さを警告の分（`MissingShadersHeight`）だけ広げるので、表示中のQRコードは動かない。

### Codexへの相談の結果

採用＝保存の事実を残す見出し「VRMを保存しました（警告あり）」・原因→影響→対処の順の本文・「マテリアル」に統一・ピンク表示は結果一覧の理由にだけ書く／不採用＝保存前の見出しの下に別の行を足す案（一覧の行で分かるため）と、書き出しボタンの近くで事前に警告する案（今回の範囲外）

## 入れなかったもの

- 書き出しボタンの近くで、書き出す前に警告すること（上の相談のとおり今回の範囲外）。

## 確認したこと

- `ExportFallbackConfirmationTests` に次の 5 件を足した。
  - `AnExcludedRendererWithAMissingShaderIsNotCounted`: 「書き出さないオブジェクト」で外したレンダラーのマテリアルは数えない（外さなければ数に入る）。
  - `TwoMaterialsWithTheSameNameCountAsTwo`: 同じ名前の別のマテリアル 2 つは、警告の文でも `ExportResultList.MissingShaders` でも 2 個と数える。
  - `AMaterialWithAMissingShaderIsFoundOnceAndOtherSlotsAreIgnored`: 2 つのレンダラーが共有するマテリアルを 1 回だけ見つけ、普通のマテリアル・空のスロット・非アクティブなオブジェクトは数えない。
  - `ARealExportWithAMissingShaderSavesAndWarnsOnce`: 実際の書き出しでVRMが保存され、`MissingShader` の行が 1 つ、先頭の警告が 1 文だけ出る。
  - `TheSavedTitleWarnsOnlyWhenAShaderIsMissing`: 結果画面の見出しが、保存前・普通の保存・シェーダーが見つからない保存で切り替わる。
- 英語・韓国語・中国語（簡体・繁体）の訳を足した。
