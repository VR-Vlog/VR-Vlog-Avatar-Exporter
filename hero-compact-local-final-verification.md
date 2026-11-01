# Hero 最終版の追加表示確認

- 2026-11-01 JST、対象 `http://127.0.0.1:8798/`、親が提示した固定HEAD `1513ed86652137cd8bc522b6674b7ffad4da64aa`。checkout HEAD一致・差分なし。配信と対象ファイルの対応は親作成の `local-final-source-binding.json`（all_match true）を参照。
- 専用Chromeタブ 2079269442。日本語・暗色、スクロール0、DPR 1、font status loaded。Chromeバージョン・zoom・実描画フォントは未確認。実スマホではなくPC Chromeのviewport変更。画像は `after-local-final-width320.png`、`after-local-final-mobile.png`、`after-local-final-width600.png`、詳細矩形は `local-final-measurements.json`。旧画像は保持。
- 方法: 3幅とも高さ844でviewport設定、初期画面を撮影し、本文・CTA・動画カード・動画・両章ボタンの矩形とcomputed font、document scrollWidthを取得。画像を目視。
- 320×844: 本文14px、2行で末尾「す。」の孤立なし。CTA下端327.78、動画466.97〜607.59、章ボタン617.59〜661.59px。横幅320・scrollWidth320。
- 390×844: 本文15px、2行で孤立なし。CTA下端338.41、動画477.59〜657.59、章ボタン667.59〜711.59px。横幅390・scrollWidth390。
- 600×844: 本文15px、2行で孤立なし。CTA下端346.50、動画485.69〜783.81、章ボタン793.81〜837.81px、画面下に約6.19pxの余白。動画カード全体の下端852.81pxは画面外だが、動画と章ボタン全体は画面内。横幅600・scrollWidth600。
- 操作一式は旧SHA `3528012775cccc24ccbae67c1e42c8412069479b` の `local-verification.md` の実行結果を参照し、この追加確認では再実行していない。最終版の操作を新たに実施した証拠ではない。
- viewport overrideをreset。公開後の同担当確認のため、今回専用タブ 2079269442 をhandoff保持。公開後に再確認し、reset・close・専用tab一覧空を確認する必要がある。既存ユーザータブには触れていない。
