# 表情メニューが読み込みの上限を超えるとき、表情ボードの感情の行にそのことを出す

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`（`MenuClipPaths`・`MenuOverLimitNote`）・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`（`SuggestPresets`・`MenuOverLimitMessage`）・`Editor/ExperimentalExpressionCaptureWindow.cs`（`DrawGroupCaption`）・`Editor/Locales/ExporterLocale_{en,ko,zh-Hans,zh-Hant}.json`・`Tests/Editor/BoardMenuOverLimitTests.cs`・`CHANGELOG.md`
- [表情・ポーズ指定UIの喜怒哀楽の初期値に、VRChatメニューの項目名を使う](2026-10-20-board-menu-emotion-suggestions.md) の「読めない・上限を超えるなど、途中で例外が出たときは対応なしとして扱い」を補う。

## 背景

表情ボードは、喜怒哀楽の自動の候補を選ぶためにアバターの VRChat Expressions Menu を `VrChatExpressionMenu.Read` で読む。読み込みには上限があり（`VrChatExpressionMenu.MaximumCandidates = 256` 件の項目、`MaximumMenuVisits = 512` 回のサブメニュー、`MaximumControlVisits = 8192` 回の項目の訪問）、超えると `VrChatMenuImportLimitException` が出る。`MenuClipPaths(GameObject)` はこれをほかの例外と同じく握りつぶして空の対応を返していたので、項目が 256 件を超えるアバターでは、メニューの項目名からの感情の候補が 1 つも出ず、ボードにも理由が出なかった。利用者には、かんたんの書き出しでは埋まる喜怒哀楽がなぜ詳細では空なのか分からない。

## 決定

- `MenuClipPaths` は上限の例外だけを別に受け、そのときの `VrChatMenuImportSummary`（`Discover` が数えたメニュー全体の件数と、どの上限か）を `out` で返す。ほかの例外はこれまでどおり対応なしで、何も出さない。
- セッションは 1 回だけ読む対応と一緒にこの件数を持ち、`MenuOverLimitMessage` で表示する文を返す。
- 表示は、ボードの「感情　割当n/5」の見出しの行の右に、まばたきの見出しの「左右はアバターから見て」と同じ薄い小さい文字で出す。見出しの行は高さが決まっているので、文が出てもほかの部品は動かない。幅に収まらない分は省略し、ツールチップで全文を出す。
- 文は、利用者に起きたこと（項目名から感情を選んでいない）を先に置き、幅が狭くて後ろが省略されてもそこが残るようにした。
  - 項目の件数の上限: 「項目名から感情を選んでいません・表情メニューが{0}件で上限の{1}件を超えています」。数え切れなかった（`IsComplete` が偽）ときは「{0}件以上」。
  - サブメニューの訪問の上限（`menu-visits`・`control-visits`）: 「項目名から感情を選んでいません・表情メニューのサブメニューが多く上限を超えています」。このときは項目の件数が上限を下回ることがあるので件数を出さない。
- 選ぶ候補の動きは変えていない。上限を超えたときは今までどおり、メニューの項目名からは何も選ばず、クリップ名・BlendShape の名前・動かす形からは選ぶ。

## 入れなかったもの

- 上限の手前までに読めた項目（先頭の 256 件）だけで候補を選ぶこと。読み込みは上限で例外を出して途中の結果を返さない作りで、ほかの読み込み（書き出し時の取り込み）も同じ扱いのため、ボードだけ部分的に読むのはやめた。
- ボードから、取り込まない枝を選んで読み直す操作。書き出し時の取り込みには枝を外す対策があるが、ボードの初期値のために同じ画面を足すのは今回の範囲を超える。
- 状態の行（`status`）に出すこと。保存や追加の結果で上書きされて消えるので、アバターに付いて変わらない事実は見出しの行に置いた。

## 確認したこと

- `BoardMenuOverLimitTests`（VRChat SDK なしで、SDK と同じ形のフィールドを持つメニューを本番の読み込みでたどる）
  - `MenuOverTheItemLimitGivesNoPathsAndANoteWithTheCount`: 項目 300 件のメニューで対応が空になり、件数 300・上限 256 の文になること。
  - `MenuWithinTheLimitGivesNoNote`: 256 件ちょうどでは文が出ないこと。
  - `OtherReadFailuresGiveNoNote`: 上限以外の例外では文が出ないこと。
  - `TooManySubmenusSayItIsTheSubmenus`: 空のサブメニュー 520 個でサブメニューの文になること。
  - `AnIncompleteCountSaysAtLeast`: 数え切れなかったときは「以上」の文になること。
- 画面のスクリーンショットは撮っていない（ヘッドレスの実行ではボードを撮れない）。
