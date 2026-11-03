# 韓国語・中国語の崩れた訳を直し、画像縮小の注記をEditorの言語で書く

- 対象: `Editor/Locales/ExporterLocale_*.json`・`Editor/Compatibility/ExporterLocalization.cs`・`Editor/ExportAppearanceReport.cs`・画像縮小の注記を書く4か所（`LilToonFullSnapshot`・`MobileTextureEncoder`・`LilToonMainTextureBaker`・`AlphaMaskBaker`）・`Tests/Compatibility/OptimizationLocalizationTests.cs`・`Tests/LanTransfer/TransferNotesLanguageTests.cs`・`Tests/Editor/ExportAppearanceReportLanguageTests.cs`

## 韓国語・中国語の訳

- 経緯: 英訳と同じく、ko・zh-Hans・zh-Hantにも日本語の残りと、字が欠けて別の文に化けた訳があった（例: " へ縮小しました。" が ko で "기타 제품"、zh-Hans で "联系我们"。"不正" が ko で "불법"（違法））。
- 決めたこと: 3言語の全1593件を原文と英訳を見比べて読み、意味が違う・崩れている・途中で切れている・日本語や英語が残っている訳だけを書き直した。言い回しが違うだけの訳は変えていない。用語は各表で多く使われている語にそろえた（例: ko のマテリアルは 머티리얼、書き出しは 내보내기）。原文の改行・前後の空白・`{...}` は変えない。例外は ko の " 秒を静止姿勢として採用（再生しません）。" で、前後の " 件省略しました。" などと同じく数字に続けるため先頭の空白を外した。中国語は ": " で終わる断片を、表のほかの訳と同じく全角の "：" で受け、後ろの空白を付けない。表情ボードは zh で「看板」と訳した4件を、表のほかの訳と同じ「表情面板」にそろえた。ko の「・」は韓国語の中点「·」に置き換えた。
- 防ぎ方: `TranslationsHoldNoJapaneseCharacters` を4言語に広げた（旧 `EnglishTranslationsHoldNoJapaneseCharacters`）。言語ごとに許す字が違うので、言語ごとに正規表現を持つ。英語は仮名・漢字・全角記号のすべて、韓国語は仮名と漢字、中国語は「・」を除く仮名と半角カナを禁じる。中国語は漢字・「」・「・」を自分の字として使うので禁じない。字が欠けて別の文になった崩れはこのテストでは見つけられない。

## 画像縮小の注記

- 経緯: 画像縮小の4文（`ExportNoteText.LilToonTextureResized`・`OutputTextureResized`・`MainTextureResized`・`AlphaMaskResized`）は日本語のまま書き出していた。訳の表には昔の連結で使っていた断片（" から "、" へ縮小しました。" など）しか無く、文全体の訳が無かった。英語などのUIでは結果の一覧に日本語の文が出ていた。`TransferNotes` は日本語のテンプレートで読めていたが、訳したテンプレートで読む経路はこの4文では通っていなかった。
- 決めたこと: lilToonと表情の注記と同じく、Editorの言語で書く。`ExporterLocalization.Format(template, …)` が `ExporterLocalization.T(template)` に値を入れ、4か所の書き出しはこれを使う。4文の全文の訳を4言語の表に足し、使われていない断片6件を消した。`ExportNoteText` には置かない。`ExportNoteText.cs` はCLIのビルド（`VRVLOG_LAN_TRANSFER_CLI`）と `Tools/run-compatibility-tests.ps1` にも `ExporterLocalization` 無しで入るため。CLIは日本語のテンプレートだけで読む。
- テスト: `TransferNotesLanguageTests.TextureNotesWrittenInTheEditorLanguageAreReadBack` を4言語で回す。4文が訳されていること、書き出しと同じ `ExporterLocalization.Format` で作った文から名前と大きさが読めること、Editorを日本語に戻すと同じ文が読めない（訳したテンプレートで読んだ）ことを確かめる。PR #604 の `TransferNotesReadTheConversionWrittenInTheEditorLanguage` と同じ作りで、別のファイルに置いた（同じファイルに足すと #604 と衝突するため）。
- 「見た目の変更」の一覧: `ExportAppearanceReport.Changes` は書き出しの警告から "縮小" などの語を含む文を拾う。縮小の注記をEditorの言語で書くと英語では拾えなくなるので、"縮小" は `ExporterLocalization.T("縮小")` でも探す。このために "縮小" の訳を英語で "Downscaled"、ko で "축소"、zh で「缩小」「縮小」にした（英語は "Close" だった）。"近似" の英訳も "Close" だったので "Approximated" にした。テストでは4文がすべて一覧に入ることも確かめる。
- 残したもの: PhysBoneの注記（`SpringSentences`）は日本語のまま書く。末尾の文で読み分けているので、Editorの言語で書くには読み取り側も変える必要があり、今回は扱わない。
