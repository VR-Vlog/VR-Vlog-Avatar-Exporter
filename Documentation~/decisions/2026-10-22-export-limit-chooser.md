# アプリの上限を超えたポーズを書き出しの途中で 1 回だけ選ぶ

- 日付: 2026-10-22
- 対象: `Editor/PoseLimitPlan.cs`・`Editor/PoseLimitChooser.cs`・`Editor/PoseLimitWindow.cs`・`Editor/PoseExportSession.cs`・`Editor/PoseSampling.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/ExportSettingsStore.cs`・`Editor/ExperimentalExpressionCaptureSession*.cs`・`Editor/Locales/*.json`・`Tests/Editor/PoseLimitPlanTests.cs`・`Tests/Editor/ExportStageGuardTests.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`・`Tools/run-unity-compatibility.py`・`Documentation~/HumanoidPoses.md`・`Documentation~/HumanoidAnimations.md`・`CHANGELOG.md`・`README.md`・`README.en.md`

## 背景

アプリが読む同梱ポーズには上限がある（`HumanoidPoseData`・`HumanoidAnimationData` の契約）。

- ポーズと動くポーズを合わせて 128 件
- 動くポーズ 1 本 8,192 フレーム、全体 32,768 フレーム
- 静止ポーズの JSON 2MiB、動くポーズの JSON 16MiB（どちらも契約の `Measure` による保守的な見積もり）

これまでの書き出しは次のように扱っていた。

- 128 件を超えると、失敗した段階を省いて続ける設定のときだけ先頭の 128 件を残し、残りを黙って省いた。どれを残すかは選べなかった。
- 動くポーズのフレームの合計が 32,768 を超えた分は、その項目を「ポーズ未対応」にした。
- 動くポーズの JSON が 16MiB を超えると、ポーズの段階そのものが失敗し、段階を省いて続けると **ポーズがすべて** 入らなかった。骨の多いアバターでは 1 フレームが約 9.6KB になり、合計 1,700 フレーム前後（30fps で約 1 分）で届く。

案 A（書き出しの途中で 1 回だけ確認する）を mk が 2026-10-22 に選んだ。設計の画面案は案 A の画面で、結果の一覧に外した項目を残すところは案 B から採った。

## 決定

- 上限の判定と既定の選び方を `PoseLimitPlan` にまとめる。大きさは契約の `Measure` と同じ見積もり（値 1 つにつき 32、文字 1 つにつき 6 を足す）で数え、`PoseLimitPlanTests.TheSizesAreTheContractsBound` で契約の `Measure` と一致することを確かめる。計画が上限内なら必ず書ける。
- 既定はリストの末尾から外す。保存した選択（無ければ空）から始め、次の順で足りるまで末尾から外す。
  1. 動くポーズのフレームか動くポーズのデータが超えている間は、動くポーズだけを外す。
  2. 静止ポーズのデータが超えている間は、静止ポーズだけを外す。
  3. 件数が超えている間は、種類を問わず外す。
- 上限内なら確認を出さず、何も外さない。保存した選択があっても使わない。
- 1 本で 8,192 フレームを超える動くポーズと、1 本でデータが 16MB を超える動くポーズは書き出せないので、画面には出すが選べなくする。8,192 フレームを超えるだけでは確認を出さない（これまでどおりポーズ未対応）。16MB を超えるものは確認を出す。
- 確認は `PoseLimitWindow`（**アプリの上限を超えた項目**）で、書き出しの途中で 1 回だけ出す。書き出しの範囲を `PoseLimitChooser.Scope` で `LilToonExporterWindow.ExportAndSaveNormally` に置き、すべての書き出しの経路がここを通る。段階の再試行や記録した表情の書き出し直しは同じ範囲の中で答えを使い回す。
- 画面で選んだ内容はアバターの書き出し設定（`ExportSettingsStore` の `LimitExcludedPoses`）に保存し、次の書き出しの初期値にする。既定のままなら保存しない。保存していないシーンのアバターはエディタを閉じるまでメモリに持つ。
- **書き出しをやめる** は書き出しを取り消す（`OperationCanceledException`）。段階の失敗としては扱わない。
- **アプリに転送（かんたん）** とバッチ実行は確認を出さず、保存した選択か既定を使う。`interactive` は確認を出すかどうかだけを決める。通常で画面を開いて何も変えずに書き出したものと、かんたんが同じ VRM になることを `QuickTransferMatchesDefaultNormalTests.PosesOverTheAppLimitMatch` で確かめる。
- 外したポーズは書き出し結果の一覧に「同梱ポーズ」の行として名前と理由を出す（案 B の記録）。Console の警告は理由ごとに 1 行にする。
- 先頭 128 件を残す `TrimToLimit` と、その警告 `PoseLimitWarning` は消す。

## 選ばなかった案

- 案 B（書き出しは既定で外して進め、結果の一覧から選び直す）。選び直すにはもう一度書き出すことになる。案 B の「結果の一覧に外したものを残す」だけを採った。
- 表情ボードの **ポーズを確認・調整** の中で上限を見せる。動くポーズの大きさは書き出し用のコピーで採取するまで決まらず、ボードを開く前の見積もりは外れる。
- 画面案にあった材質・画像（10 枚・1024×1024）の欄。アプリの schema 1 の読み込みにある上限で、この Exporter は schema 2 を書くので当たらない。書き出す GLB の 64 材質・128 画像の確認は別の作業の候補にした。
- 画面案にあった並べ替えのメニュー。既定は「末尾から」の 1 通りで、並べ替えると既定の意味が変わる。

## 変わること

- 128 件を超えたとき、通常の書き出しでは確認を出す。かんたんとバッチは既定で末尾から外す（これまでは先頭の 128 件を残していたので、外れる項目は同じ）。
- 動くポーズのデータが 16MB を超えるアバターで、ポーズがすべて落ちていたのが、既定で末尾の動くポーズから外して残りを書き出すようになる。
- 動くポーズのフレームの合計が 32,768 を超えたとき、超えた項目を「ポーズ未対応」にしていたのが、上限の画面で選べるようになる。

## 画面案との差

- 材質・画像のカードは無い（上の「選ばなかった案」）。並べ替えのメニューと行ごとの「確認」ボタンも無い。
- 下の行は「あと{0}件外す」で、まだ足りない件数だけを出す。左のカードは外す件数を出す。
- 登録元の列は **ポーズを確認・調整** と同じく元の登録元をそのまま出す。
- 統計はラベルと値の 2 段にした。言語ごとにラベルの長さが違うため。
- 種類の欄の「8,192フレーム超え」「16MB超え」は短い形で、文はツールチップに出す。

## Codex に見せて直したところ

採った指摘:

- 上限内に戻したあとも見出しの「上限を超えています」が赤いままだった。今の選択が超えているときだけ赤にした。
- 「あと2件外す」がカードと下の行の両方に出ていた。カードは外す件数にした。
- 見出しのチェックに一部だけ選んだ状態が無かった。`EditorGUI.showMixedValue` で出す。
- 「動くポーズのデータ」を「データ量」にした。

採らなかった指摘:

- 「既定に戻す」を「初期選択に戻す」に。保存した選択があると画面はその選択で開くので、開いたときの選択と既定は別物になる。
- 「保存」の列名を「書き出す」に。承認した画面案が「保存」で、72pt の列に 4 文字は入らない。
- 左の欄を狭くする。上限の種類が増えたときのために残す。
- 「外した項目のみ」の絞り込み。今回の範囲の外で、画面は外した行が見える位置までスクロールして開く。
- 種類の欄を長い文にする。ツールチップに全文を出している。

## 画面

`Documentation~/Verification/export-limit-*.png`（3 倍で撮影。名前は「アバター A」「ポーズ 1」などの仮の名前）

- `export-limit-asis-result.png`: 変更前の結果の一覧（128 件を超えた分は警告 1 行だけ）
- `export-limit-count.png`・`export-limit-over.png`: 件数が超えたとき、既定で開いた直後と、既定から 2 件戻して超えた状態
- `export-limit-data.png`: 動くポーズのデータ量が超えたとき
- `export-limit-result.png`: 変更後の結果の一覧
- `export-limit-ko-820.png`・`export-limit-en.png`・`export-limit-font16.png`: 最小幅の韓国語、英語、エディタの文字を大きくしたとき

## 確かめたこと

- `PoseLimitPlanTests`・`PoseLimitChooserTests`（既定の決定性、種類ごとの上限超過、保存した選択の使い回し、確認が 1 回だけ、取り消し、かんたんとバッチで確認を出さない、4 言語の翻訳）
- `ExportStageGuardTests.OverTheLimitPosesKeepTheFirst128`
- `QuickTransferMatchesDefaultNormalTests.PosesOverTheAppLimitMatch`
- EditMode テスト全体（Unity 2022.3）
