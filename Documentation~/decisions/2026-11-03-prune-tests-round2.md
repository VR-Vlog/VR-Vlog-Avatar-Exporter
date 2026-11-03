# テストの削減の 2 巡目で、定数を写すだけのテストと同じ行の重複を消す

- 日付: 2026-11-03
- 対象: `Tests/Editor/PreviewRenderingTests.cs`・`Tests/Editor/ExpressionPresetTests.cs`

## 背景

10/23 に一巡削減したあと、EditMode のテストを必要な分だけにする作業をもう一度行った。今回は消す PR と速くする PR を分け、これは消す PR にあたる。速くする対象のクラス（`QuickTransferMatchesDefaultNormalTests`・`MergedFxDefaultsTests`・`PoseRegistrationTests`・`CloudTransferTests` など）は別の PR で扱うので、ここでは触っていない。

残すものと消すものは次の基準で決めた。迷うものは残す。

- 残す: VRM・GLB の中身を確かめるもの、保存形式・ロケールの表・パッケージの中身、`git log -S` で不具合の修正と一緒に足したと分かり同じ断言が他に無いもの、利用者に見える契約（入口・エラーや警告や注記の文言と出る条件・個人情報を出さないこと・キャッシュの誤りで誤った VRM を書かないこと・CLAUDE.md で固定している一致）、依存パッケージの版の範囲のテスト
- 消す: 出力・警告・ファイル・利用者の操作のどれも確かめていないもの（D1）、同じ入力・同じ経路・同じ断言のテストが他にあるもの（D2）、skip の理由の環境がどこにも無いもの（D3）
- 赤のテスト（`ExportOptimizationBindingsTests`）、アプリ側の検証プロジェクトで回る skip（`LilToonFull*`・`AvatarOptimizerBoxIntegrationTests`）、`Tools/validate.py` が固定するファイル、10/23 に「その経路の唯一の守り」として戻したテストは対象にしない

## 決定

- `PreviewRenderingTests.ApplySetsTheSharedCameraAndLights` をファイルごと消す（D1）。`PreviewRendering.Apply` のプレビュー用の画角・クリップ面・背景色・ライトの向きと強さ・環境光の定数を写して比べているだけで、書き出し結果も警告も確かめていない。このテストは 4 つのプレビューの設定を 1 か所にまとめる整理で足したもので、不具合の修正ではない。
- `ExpressionPresetTests` の `[TestCase]` を 4 行消す（D2）。どれも `ExpressionNameInferenceTests` に同じ呼び出し・同じ入力・同じ期待値の行がある。
  - `NamesSuggestOneEmotion("Smile", happy, 0)`・`("joy", happy, 1)` → `ExpressionNameInferenceTests.MenuStyleNamesNameOneEmotion` の同じ行
  - `PartialAmbiguousOrUnrelatedNamesSuggestNothing("eye_smile_L")`・`("mouth_smile")` → `ExpressionNameInferenceTests.PartialOneSidedOrMixedNamesNameNothing` の同じ行。こちらは結果全体が null であることと `rawShape: true` も確かめるので、消した行より強い
- 消したことで使われなくなったテスト用のツールや本体の入口は無い。本体の動作は変えていない。

## 消さなかったもの

- skip の 156 件は、どれも環境がある（互換の検証環境・任意パッケージを入れたプロジェクト・アプリ側の検証プロジェクト・プロジェクトに入れた UniVRM）。D3 は 0 件だった。
- 本文の似ているテストの組（類似度 0.9 以上が 18 組）は、どれも入力が違う別の場面だった。本文が同じメソッドの間で同じ `[TestCase]` 行を持つのは、上の 4 行だけだった。
- キャッシュの回数やヒット数を確かめるテストは残した。`HeldAutomaticExpressionTests.HeldProbeRunsTheSelectionAnalysesOnce` は、読み込み時間の半分近くを占めていた 2 回の解析を 1 回にした修正の再発防止で、本番と同じく解析を渡す経路で値を確かめるのもこれだけ。`ExpressionInspectCacheTests`・`StationaryLayerCacheTests` の残りは、キャッシュが別の入力や保存していないコントローラーで誤った結果を返さないことを守る。
- `ExpressionChannelMergeTests` は、表情の変形の食い違いをエラーにする共通の手順を直接守る唯一のテストなので残した。
- 10/23 の一巡で残すと決めたもの（`ExpressionSettlementTests.NativeClocksMatchTheSettlementConversion` など）はそのまま残した。

## 件数

- EditMode のケース数: 3,985 → 3,980（−5）。メソッドは 1 本、ファイルは 1 本（と `.meta`）減った。Unity 2022.3.22f1 の EditMode 全件で計測し、赤は変更前と同じ 10 件、新しい赤は 0 件
- 行数: `PreviewRenderingTests.cs` の 31 行と `ExpressionPresetTests.cs` の 4 行
- 10/23 に一巡して絞ったあとなので、方針で見込んだ本数の 1〜2 割減には届かない。残りの時間の短縮は速くする PR で行う
