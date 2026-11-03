# QR モジュールと書き出し側の参照を片方向にし、名前で引く結び付けをやめる

## 背景

QR で転送するモジュール（Editor/LanTransfer）は転送なしのパッケージから外せる作りで、そのために書き出し側とモジュールが互いを型名の文字列で引いていた。

- AppTransferPanel は `Type.GetType("...AppTransferExport, VRVlog.LilToonExporter.Editor")` で書き出し側を引き、27 個のメンバーを名前で委譲する ExportEntry を持っていた。メンバーの食い違いは実行時にしか分からず、テストが名前の一覧を照合していた。
- LilToonExporterWindow は `Type.GetType("...CloudVrmTransferWindow, VRVlog.LanTransfer.Editor")` で表情ボードの 3 ページ目を引き、Tools/build-package.py はこの文字列だけを例外として通していた。
- LilToonExporterWindow はもう開かないウィンドウなのに、書き出しの入口（ExportAndSaveNormally・PackageVersion・RequireLilToon など 80 行）が載っていた。
- 転送の書き出し 2 つ（Export・ExportDetailed）の前置き、コピー失敗の例外の絞り込み 2 か所、結果の画面と転送後のページのボタンの文言、LanTransferProtocol（55 行）と CloudTransferProtocol が、同じ内容を 2 か所に持っていた。
- QR を描く失敗は例外の種類を問わず「名前を短く」にしていた。

## 決定

- 参照は転送側から書き出し側への片方向にする。LanTransfer の asmdef が Editor を参照し、AppTransferPanel は `IAppTransferExport` を通して AppTransferExport を直接呼ぶ。テストの差し替え口は同じインターフェースのスタブにする。
- 表情ボードの 3 ページ目は Editor の `AppTransferBoardPage`（Create・Transferring・SetChangeAvatar）に転送側が `[InitializeOnLoadMethod]` で登録する。登録が無い、または転送が無効のときは null のままで、ボードは今までどおり「QR対応のパッケージを導入してください。」を出す。転送を外せる構成は残す。
- インターフェースとフックは public にする。InternalsVisibleTo を使うと Editor のファイルに転送のアセンブリ名が入り、転送なしのパッケージの検査（Editor のどのファイルも転送の型名・アセンブリ名・GUID を持たない）と両立しないため。
- 書き出しの入口は新しい `ExportEntry`（static）へそのまま移し、LilToonExporterWindow は開いたら閉じる殻だけにする。Tools/ExportRegressionRunner.cs がリフレクションで引く AppTransferWindow.OpenDetailedFromMenu と ExporterLocalization.T は変えない。
- 前置きは `Export(..., bool detailed)` に、例外の絞り込みは述語 `CopyFailed(exception, fromTransfer)` にまとめる。ObjectDisposedException は転送の写しを読むときだけ起きるので引数で残す。ボタンの文言は `ExportResultText` の定数 1 か所にし、値は変えない。LanTransferProtocol は CloudTransferProtocol へ移してファイルを消す。
- QR を描く失敗は CanMark と同じ 2 つ（InvalidOperationException・ZXing.WriterException）だけを「名前を短く」にし、それ以外は上げる。
- 転送の結果 6 つ（LastMissingShaderCount など）を 1 つのオブジェクトで返す変更はしない。読む側が多く、行数も減らないため。

## 確かめたこと

- 転送あり／なしの 2 つのパッケージを組み立て、転送なしでは Editor/LanTransfer が 0 件で、Editor のどのファイルにも転送の型名が無いことを test-package.py で見た。
- 全 EditMode、ホストのハーネス、書き出しの回帰比較は PR 本文に書く。
