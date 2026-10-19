# 書き出しの段階を 7 つの工程にまとめて表示する（処理の順番は変えない）

- 日付: 2026-10-19
- 対象: `Editor/ExportStageGuard.cs`・`Editor/ExportRecovery.cs`・`Editor/ExportRecoverySession.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportFallbackConfirmation.cs`・`Editor/ExportResultList.cs`・`Editor/ExportDetailsText.cs`・`Editor/ExportFailureWindow.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExportStageGuardTests.cs`・`Tests/Editor/ExportFallbackConfirmationTests.cs`・`Tests/Editor/ExportDetailsTextTests.cs`

## 背景

書き出しの処理は `ExportStage` の 24 段階に分かれている（`2026-10-18-stage-guard.md`）。省略の警告・結果の一覧・失敗の詳細には段階の名前（「瞬き」「視線」「スキンの修復」など）がそのまま並び、どの段階が書き出しのどのあたりの処理なのかが使う人から見て分かりにくかった。進み具合を示す `ExportRecoveryReport.Stage` も「原本検査」「ビルド処理」「材質保存」などの文字列で、段階の名前とは対応していない。

書き出しの流れを、事前確認・骨格・表情・マテリアル・揺れ物・書き出し・結果一覧の 7 つの工程として説明する案があり、表示もこの 7 つで揃える。

## 決定

- A. `ExportStage` の 24 個の値はそのまま残す。7 つの工程は別の `ExportPhase` として足し、`ExportStageGuard.Phase(ExportStage)` で段階から工程を引く。
  - 事前確認: `GimmickDetection`・`SourceValidation`・`CopyValidation`・`MaAppearance`
  - 骨格: `BuildProcess`・`Attachments`・`SkinRepair`・`Constraints`
  - 表情: `ExpressionMenu`・`BlinkCapture`・`PoseCapture`・`FaceEmo`・`Expressions`・`ExpressionOutput`・`BlinkOutput`・`TrackingOutput`・`LookAt`・`PoseOutput`
  - マテリアル: `LilToonSnapshot`・`TextureBake`・`OptimizerEvidence`・`LilToonExtension`
  - 揺れ物: `PhysBone`・`PhysBoneVerify`
  - 書き出し・結果一覧: 囲まれた段階はなく、進み具合（`ExportRecoveryReport.Phase`）にだけ使う。
- B. 工程は表示（名前の前置き）・結果の一覧のまとまり・進み具合の帰属にだけ使う。`guard.Run`・`Step`・`Watch` の呼び出しは 1 つも動かさず、処理の順番も変えない。
- C. 省略や失敗の文言では段階の名前を残し、「<工程>: <段階>」の形で出す（例: 「表情: 瞬き」）。工程と段階はそれぞれ別に訳してからつなぐので、組み合わせごとの訳は持たない。
- D. 進み具合の文字列 `ExportRecoveryReport.Stage` は残し、`ExportRecoveryReport.Phase` を足す。`Stage` を「揺れ物変換」と比べて対処の文言を選ぶ処理と、共有用の診断に書く工程の符号は変えない。進み具合は工程の間を行き来する（マテリアル → 揺れ物 → 骨格 → マテリアルなど）。処理の順番に合わせた結果なので、そのままにする。

## 段階を 7 つの値にまとめなかった理由

省略は `ExportStage` の値ごとに記録し（`ExportStageGuard` の `omitted`）、`Retry` のやり直しの回数も値の数で決まる。`UniVrmOneClickExporter` には `IsOmitted` で段階ごとに分かれる処理がある。24 個を 7 個にまとめると、1 つの段階の失敗で同じ工程のほかの処理まで省かれるようになり、書き出したファイルの中身が変わる。この変更では書き出すファイルをバイト単位で変えないことを条件にしたので、段階はそのままにして工程を別に持たせた。

## 使う人から見て変わる点

- 段階を省略したときの警告が「書き出しの一部を省略しました: 瞬き — <理由>」から「書き出しの一部を省略しました: 表情: 瞬き — <理由>」になる。
- 書き出し前の確認と結果の一覧で、省略した段階の行が「表情: 瞬き」のように工程付きの名前でまとまる。警告の文と行の名前は同じ形なので、同じ省略が「そのほかの知らせ」に二重に出ることはない。
- 同梱ポーズが上限を超えたときの行の名前が「表情: 同梱ポーズ」になる。
- 失敗の詳細と失敗画面の「工程: 」の行が、工程が分かるときは「工程: 表情: 表情メニュー読込」のようになる。
- 工程の名前のうち、事前確認・骨格・書き出し・結果一覧と、これまで訳がなかった「基準形評価」の英語・韓国語・簡体字・繁体字の訳を足した。表情・マテリアル・揺れ物は既存の訳を使う。

## 確認したこと

- `ExportStageGuardTests.EveryStageBelongsToAPhase`: すべての段階が例外なく工程に対応する。
- `ExportStageGuardTests.EveryStageCanFailAndTheFileIsStillWritten`: 24 段階それぞれについて、確認の一覧に出る名前が工程付きの名前になり、結果の一覧で省略の行が工程付きの名前のまとまりに入り、省略の警告が別の知らせとして出ないことを確かめた。
- `ExportFallbackConfirmationTests.EveryShippedLocaleTranslatesTheResultWords`: 7 つの工程の名前と「基準形評価」が 4 言語の訳にある。
- `ExportFallbackConfirmationTests.AStageIsNamedWithItsPhase`: 日本語で「表情: 瞬き」になる。
- `ExportDetailsTextTests.TheStageLineNamesItsPhaseWhenKnown`: 工程が分かる行には工程が付き、分からない行は従来どおり。
- Unity 2022.3.22f1 で、段階・省略・復旧・結果の一覧に関わる EditMode テスト 649 件を回した。失敗は APL が入っていないテスト用プロジェクトで APL を前提にする 4 件だけだった。
- 構成の違う手元のアバター 6 体を変更の前後で書き出し、6 体とも VRM ファイルの sha256 が一致した。同じコードで 2 回書き出しても一致することを先に確かめてあるので、この変更は書き出すファイルを変えない。
- `揺れ物` のように段階名と工程名が同じときは「揺れ物: 揺れ物」と重ねず「揺れ物」と出す（`AStageIsNamedWithItsPhase`）。
