# 書き出しの段階ごとに失敗を受け止め、省けるものを省いて必ずファイルを書き出す

- 日付: 2026-10-18
- 対象: `Editor/ExportStageGuard.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportFallbackConfirmation.cs`・`Editor/PoseExportSession.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/ExportRecovery.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/AppTransferExport.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExportStageGuardTests.cs`

## 背景

`UniVrmOneClickExporter.ExportCore` は、表情・瞬き・揺れ物・視線・同梱ポーズ・lilToon の材質情報などを順に処理し、どこか 1 か所で例外が出るとファイルを書き出さずに止まっていた。止まる理由の多くは、その機能だけを外せばアバター自体は書き出せるものだった。たとえば次のものがある。

- 公式の lilToon 材質が見つからない
- フェイストラッキングの経路が最適化で失われた（`LostTracking`）
- 揺れ物の出力の照合（`PhysBoneSpringExport.VerifyOutput`）に失敗した
- NDMF のビルドが失敗した
- アプリ側の上限（材質 64・画像 128・同梱ポーズ 128）を超えた

オーナーから、不完全でも必ず書き出す方向へ変えるよう指示があった。ほかの作業がこの仕組みの上に段階を足していくので、段階の扱いを 1 か所にまとめる。

## 決定

- 書き出しの処理を `ExportStage` の 23 段階に分け、`ExportStageGuard` で囲む。段階が失敗したらその段階を省略し、書き出しを続ける。省略した段階は `ExportFallbackReport` に記録し、警告に「書き出しの一部を省略しました: <段階名> — <理由>」を出す。
- 囲み方は段階の性質で 3 つに分ける。
  - `Run`: 元のアバターを読むだけの段階と、書き出したバイト列を加工する段階。失敗したら代わりの値（空の一覧・何もしないセッション・加工前のバイト列）で続ける。
  - `Step`: 書き出し用コピーを書き換える段階。途中まで書き換わったコピーは信用できないので、`ExportStageFailure` を投げて `Retry` が新しいコピーから書き出しをやり直し、その段階だけを省く。
  - `Watch`: `Step` と同じだが、省略したあとも本体を呼ぶ。前の段階の代わりの値（何もしないセッションなど）で、本体が無害になっているものに使う。
- やり直しは、省略する段階が増えるたびに 1 回ずつ行う。最大で段階の数までなので、無限には回らない。やり直す前に、前の試行の警告と記録を捨てる。
- NDMF は callback の中の例外を自分の例外で包んで投げ直すので、`InnerException` と `AggregateException` をたどって `ExportStageFailure` を探す。NDMF のビルド自体が失敗したときは、次の試行で NDMF の処理を飛ばし、Modular Avatar の処理を通さないコピーから書き出す。NDMF や最適化ツールの処理でヒューマノイドのルートの Animator や必須のボーンが失われたとき（`ValidatePreparedHumanoid`）も同じ段階の失敗として扱う。処理を通さないコピーでも欠けているなら、元のアバターに原因があるので止める。
- 取り消し（`OperationCanceledException`）は省略せず、そのまま止める。
- 上限の扱い:
  - 同梱ポーズは先頭の 128 件を残し、残りを省略する。警告と「上限を超えた項目」の記録を残し、件数を記録する。
  - 材質 64・画像 128 は GLB の中で一部だけを削れない。lilToon の材質情報（拡張）を省略し、標準の MToon で表示する VRM を書き出す。保存時の検査が通るよう、拡張のないファイルには「lilToon なし」の印を付ける。
  - 公式の lilToon 材質がない、または MToon への変換に失敗した材質は、色とメイン画像だけの MToon にして「近似」として記録する。
- `ExportFallbackReport` に、結果の一覧（`Results`）を足す。1 行ごとに、結果の分類（保持・焼き込み・近似・省略）、項目、名前、理由、件数を持つ。分類ごとの件数は `Summary()` で取る。画面はこの PR では作らず、データだけを置く。
- 続けるかどうかは `ExportRecoveryReport.ContinueOnStageFailure` で決める。書き出し画面とアプリへの転送では `ExportStageGuard.ContinueByDefault`（Unity Editor の設定 `VRVlog.LilToonExporter.ContinueOnStageFailure`、既定は続ける）を渡す。ほかの呼び出し元と既存のテストは従来どおり最初の失敗で止まる。
- 書き出し用コピーと一時の材質・メッシュ・画像は、やり直しのたびに `finally` で破棄する。シーンと元のアバターには書き込まない。

## 確認したこと

- `Tests/Editor/ExportStageGuardTests.cs` で、テストから各段階に例外を差し込み（`ExportStageGuard.FaultInjection`）、23 段階すべてについて次を確かめた。
  - 続ける設定では、その段階だけが 1 回だけ省略される。
  - VRM 1.0 として読めて、保存時の検査を通るファイルが書き出される。
  - 省略の記録と警告が 1 件ずつ残る。
  - シーンのルートの数と元のアバターの階層・材質が変わらない。
  - 止める設定では、差し込んだ例外が包まれずにそのまま届く。
- 取り消しが省略されないこと、別のツールに包まれた失敗を見つけられること、ポーズを 128 件で切ること、結果の一覧の数え方も同じファイルで確かめた。
- Unity 2022.3.22f1・UniVRM 0.131.3・lilToon 2.3.4・Modular Avatar 1.18.0・NDMF 1.11.0 のプロジェクトで、EditMode テストを回した。lilToon の画像を読む処理は `-nographics` では動かないので、グラフィックスありで回した。

## 検討した代替案と不採用理由

- 段階ごとに `try`/`catch` を書く: 段階が増えるたびに記録と警告の書き方がばらつく。省略の判定とやり直しを 1 か所に置く。
- コピーを書き換える段階も、その場で続ける: 途中まで書き換わったコピーから書き出すと、どこまで反映されたか分からないファイルになる。新しいコピーからやり直す。
- 材質・画像の上限を超えた分だけ削る: 標準 VRM の材質と画像は GLB の中で互いに参照していて、一部を削ると表示が壊れる。lilToon の拡張だけを省く。
- 既定で止める: 「不完全でも必ず書き出す」という指示に合わない。止めたい利用者のために Editor の設定を残す。

## 影響

- 書き出し画面とアプリへの転送では、失敗した機能を省いたファイルが保存される。省いた内容は警告と `ExportFallbackReport.Results` に残る。
- 新しい段階を足すときは、`ExportStage` に値を足し、`ExportStageGuard.Label` と `Outcome` に表示名と分類を書き、呼び出しを `Run`・`Step`・`Watch` のどれかで囲む。テストの差し込みは全段階を自動で回す。
- ファイルを作れないとき（保存先・UniVRM の書き出し自体の失敗・取り消し）は従来どおり止まる。
