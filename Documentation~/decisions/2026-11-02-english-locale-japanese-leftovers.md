# 英訳に残っていた日本語を直す

- 対象: `Editor/Locales/ExporterLocale_en.json`・`Tests/Compatibility/OptimizationLocalizationTests.cs`
- 経緯: 英訳 91 件に日本語や崩れた英語が混じっていた（例: 表情が 0 件のときの注意文が "If theアプリ expression is not VRM"）。
- 決めたこと: 91 件すべてを英語に書き直した。内訳は仮名・漢字が残っていた 42 件と、日本語の字が欠けただけで英語も壊れていた 49 件（例: " へ縮小しました。" の訳が "Contact Us"）。原文の改行・前後の空白・`{...}` の差し込み位置は変えない。
- 確かめたこと: `TransferNotes` は `T(template)` で英訳した文も照合するが、元の文（`ExportNoteText`）と書き出し側が同じ文字列を翻訳するので、訳の中身には依存しない。テストも日本語の元の文だけを使っているので変更なし。
- 防ぎ方: 英訳に仮名・漢字・全角記号が残ったら `EnglishTranslationsHoldNoJapaneseCharacters` が落ちる（字が欠けただけの崩れは検出できない）。
- テスト: `Tools/run-lan-transfer-tests.ps1` が通った（Cloud 209 件ほか失敗 0）。
