# 見た目の変更の一覧を言語の表から探す

- 日付: 2026-11-06
- 対象: `Editor/ExportAppearanceReport.cs`・`Editor/Compatibility/ExporterLocalization.cs`・`Tests/Editor/ExportAppearanceReportLanguageTests.cs`

## 背景

書き出しのあとの「見た目の変更」は、注記に「省略」「近似」「縮小」「調整して」「自動除外せず保持」のどれかが入っているかで拾っていた。注記は Editor の言語で書かれるので、英語・韓国語では拾えない。前回は訳した文を手で並べた一覧（縮小の注記と、省略・近似の一部）を足して埋めたが、一覧に無い注記（lilToon の構造の差など）は英語・韓国語・中国語で抜けたままだった。

## 決定

ほかの UI と同じく言語の表（`Editor/Locales/ExporterLocale_*.json`）に寄せる。表の元の日本語にこの 5 語のどれかが入っている文を集め、Editor の言語に訳した文を含む注記を拾う。訳した文の `{0}` や `{name}` のところは何が入ってもよいとして探す。5 語そのものも表に入っているので、日本語のときはこれまでと同じく語で探すのと同じになる。手で並べていた一覧は消した。

このため `ExporterLocalization` に今の言語（`Locale`）と、表の元の日本語の一覧（`Sources()`）を出した。

## 確かめたこと

R09（Unity 2022.3.22f1）で `ExportAppearanceReportLanguageTests`・`ExportFallbackConfirmationTests`・`QuickTransferMatchesDefaultNormalTests` の 39 件が通った。足したテストでは、5 つの言語で lilToon の構造の注記のうちマテリアルを MToon で書く注記が拾われ、プロパティを書かない注記は拾われない。
