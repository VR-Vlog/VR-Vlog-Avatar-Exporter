# Exporter hero: 変更前の観察

- 2026-11-01 20:40 JST、Chrome extension経由の専用タブ、https://vrvlog.fun/exporter/ 。日本語、暗色、スクロール位置0。DPR 1。Chromeバージョンとズームは未確認。スマートフォン幅はPC Chromeのviewport変更であり実機ではない。
- 同時刻の https://vrvlog.fun/exporter/publication.json は source_commit `59e25075d1cecb69804e7e845c7fa0d9ec6ab70d`、run_id `41301819552`、digest `5251f840f79a00b6468c9117806c3c7af40ca96e58c5918838e6991c46675ce7`。ページ自体のファイルハッシュと照合したわけではない。
- 再現: 新規タブでページを開き、viewportを各値に設定、`document.fonts.ready`を待ち500ms後、表示画面とDOMを取得。動画の再生はしていない。
- 画像: `before-desktop.png` 1440×1000、`before-mobile.png` 390×844、`before-width320.png` 320×844、`before-width900.png` 900×1000。完全な矩形・計算スタイルとテキストRange矩形は `measurements.json`。

## 観測

- 見出しは2つのblock spanで、それぞれ `white-space: nowrap`。各行は各幅で1行。1行目・2行目のテキスト幅はそれぞれ、1440: 486.4 / 290.9px、900: 367.4 / 219.8px、390: 277.8 / 166.1px、320: 217.1 / 129.9px。文字列は2行目が短く、行末に空きがある。撮影画像上で見出し文字の欠けや独立した余分な単語は確認されなかった。
- h1は700、font-family指定 `Zen Kaku Gothic New, sans-serif`。1440: 56px / 行高78.4px / 字間-1.96px、900: 42.3px / 59.22px / -1.4805px、390: 31.98px / 44.772px / -1.1193px、320: 25px / 35px / -0.875px。
- heroは1440で左140〜右1300、見出し領域左140〜右810.8、動画カード左852.8〜右1300。900では見出し領域左40〜右475.7、動画カード左517.7〜右860。390/320では見出し、動画カード、操作ボタンの縦積み。390の見出し領域幅350px、320は280px。ボタン下の補足は320で2行。
- viewport幅とdocument scrollWidthはそれぞれ1440/1440、900/900、390/390、320/320。取得した状態では文書全体の横はみ出しなし。画像上では320の動画カード見出しが「転送まで」の途中で改行。
- フォント: Google Fonts stylesheetはDOMに存在し、取得時 `document.fonts.status` は `loaded`。`document.fonts.check('700 25px "Zen Kaku Gothic New"', ...)` は見出しの日本語文字でtrue、英字 V/R/l/o/g でfalse。checkは実際のグリフ描画フォントを確定しないため、日本語・英字それぞれ実際にどのフォントが描画されたか、また英字のfalseが失敗か未使用フォントの状態かは未確定。computed font-familyは指定値であり実描画フォントの証明ではない。取得直後の要素一覧は空だったがFontFaceSet sizeは369という観測もあり、この環境のDOM取得による制約を否定できない。
- 前後比較・デザイン案・動作や他のテーマ/文字サイズ/実機は未確認。寸法はCSSピクセルであり、スクリーンショットの見え方は取得環境に限定する。
- 終了処理: この作業で作成した2タブをcloseし、viewport overrideをreset、`browser.tabs.list()` が `[]` と読戻し。ユーザーの既存タブは取得・操作していない。
