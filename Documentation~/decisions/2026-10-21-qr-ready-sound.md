# アプリに転送で QR コードができたら音を鳴らす

- 日付: 2026-10-21
- 対象: `Editor/LanTransfer/AppTransferFlow.cs`・`Editor/LanTransfer/AppTransferWindow.cs`・`Editor/LanTransfer/AppTransferWindow.uxml`・`Editor/LanTransfer/AppTransferWindow.uss`・`Editor/Locales/*.json`・`Tests/LanTransfer/AppTransferFlowTests.cs`・`Tests/LanTransfer/CloudTransferWindowTests.cs`・`CHANGELOG.md`

## 背景

**アプリに転送** では、**QRコードを作る** を押してから書き出しとアップロードが終わって QR コードが出るまで待ち時間がある。そのあいだに別の作業をしていると、QR コードが出たことに気づかず、QR コードの有効期限（3 分）を使ってしまう。QR コードが出たときに知らせてほしいという要望があった。

## 決定

QR コードが画面に出たときに、OS の通知音（`EditorApplication.Beep()`）を 1 回鳴らす。

- 鳴らすのは、転送の画面が QR の待ち（`AppTransferView.Waiting`）で、QR コードを描けたときだけ。書き出しやアップロードの失敗・キャンセル・待ち時間切れ・QR コードを描けなかったとき（名前が長すぎるなど）は鳴らさない。この判定は `AppTransferFlow.ShouldBeepForQr` にまとめ、EditMode テストで状態ごとに確かめる。
- **QRを作り直す** で新しい QR コードが出たときも 1 回鳴らす。同じ QR コードを表示しているあいだは鳴らさない（QR の画像を作ったときだけ判定する）。
- 転送の画面の下端、**有効期限3分** の行の左に **QRができたら音を鳴らす** のチェックボックスを置いた。最初はオン。選んだ値は `EditorPrefs` の `VRVlog.AppTransfer.QrReadySound` に保存し、この PC のすべてのプロジェクトで使う。
- チェックボックスは有効期限の行に入れ、QR の画面の高さ（`TransferChrome`）は変えていない。窓が狭いときは文言を省略して表示する。チェックボックスは書き出し中・アップロード中にも出しておき、QR コードが出る前に切り替えられるようにした。

## 入れなかったもの

- 音のファイル。OS の通知音で足りるため、パッケージに音のアセットは足していない。
- 開発用の転送の窓（`CloudVrmTransferWindow`）で鳴らすこと。利用者が使うのは **アプリに転送** の画面だけなので、そちらだけにした。
- 転送が終わったときの音。今回は QR コードが出たときだけにした。

## 確認したこと

- `Tools/run-lan-transfer-tests.ps1` で `AppTransferFlowTests` と `CloudTransferTests` を流し、すべて通ることを確かめた（鳴らす／鳴らさないの 10 通りと、最初はオンであることのテストを含む）。
- Unity 2022.3.22f1 の参照 DLL に対して `Editor/LanTransfer` と `Tests/LanTransfer` を C# コンパイラでビルドし、エラーが無いことを確かめた。Unity のエディタでの実行と EditMode テスト（`EditorPrefs` への保存のテストを含む）は行っていない。
- 足した文言 1 個は 4 つの言語のファイルにも足した。
