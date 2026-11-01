# Exporter hero 本番確認

- 2026-11-01 20:58–21:01 JST、Chrome extensionの専用タブ 2079269442、`https://vrvlog.fun/exporter/`。日本語・暗色表示、scrollTop 0、DPR 1、font status loaded。Chrome version・zoom・実際の描画fontは未確認。スマホ幅はPC viewportであり実機ではない。
- 本番 `publication.json` のsource `046b4979ca81e078720026921f88b46c3668797e`、run `41304095976`、digest `da75489b25f0a42cec1b04fbfdbdaab0ccbe4a6ee71fa09d4b3d437bfa28ae44` は保存済み `publication.json` と3項目一致。JSONによる公開申告と画面の観察であり、受信した各ページファイルのハッシュを別途照合した証拠ではない。PR head `1513ed86652137cd8bc522b6674b7ffad4da64aa`、schema/listing成功、main採用、公式mirror Pages成功は親からの引継ぎ情報。
- 各幅で初期画面を撮影し、要素矩形・computed styleを `production-measurements.json` に記録。画像: `production-desktop.png`、`production-width900.png`、`production-mobile.png`、`production-width320.png`、`production-width600.png`。

## 表示

- h1「アバターを、」「VR Vlogへ。」は各幅で2行、各行同じサイズ、2行目 rgb(255,24,128)。本文margin-topは1440/900で20px、390/320/600で16px。CTAはhero-copy内。390/320/600はCTAの下に動画。
- 動画見出し「導入から転送まで」は5幅とも1行。1440のコピー幅522.5px、動画カード577.5px。初期CTAと動画・両章は全幅で画面内。章ボタン下端: 1440×1000 599.81、900×1000 485.39、390×844 711.59、320×844 661.59、600×844 837.81px。600ではカード枠下端852.81pxは画面外で、ローカル最終版と同値。
- 320本文14px・2行で「す。」の孤立なし。390/600本文15px。各幅scrollWidthはviewport幅に一致し横overflowなし。画面の目視で写真追加なし、posterのDOM srcは `assets/guide/tutorial-first-frame.png`、publication JSONのposter hashは保存JSONと一致。実際の画像bytesの別途ハッシュ照合は未実施。
- 初期video要素src属性なし、preload none。通信未発生を証明する確認は行っていない。

## 操作（PC 1440×1000）

1. hero導入リンク・header導入ボタン → 各 `https://vrvlog.fun/exporter/#install`、install上端82.81px。
2. 先頭動画 → dialog、読み込み直後はpaused true/time 0。再確認で1秒後 paused false/time 1.18秒。導入章は2.5秒後33.20秒・paused false。Escape直後にpaused falseが観測されたため、先頭で再確認し0.5秒後dialog closed・paused true・opener focusを確認。転送章は1秒後144.24秒・paused false、close後paused true・転送ボタンにfocus。初回Escape直後の値は非同期終了途中の可能性があるが、その章で遅延後の状態は別途観測していない。
3. VCCへ切替 → pressed true、CTA VCCで追加、対応画像ギャラリー表示・Escapeで閉じる。ALCOMへ切替 → pressed true、ALCOMギャラリー表示・閉じるボタンで閉じる。外部アプリ起動操作なし。
4. fallback展開 → readonly値 `https://vrvlog.fun/exporter/index.json`、CTA href `vcc://vpm/addRepo?url=https://vrvlog.fun/exporter/index.json`。リンクのクリックはしていない。
5. FAQ「QRコードはどこで読み取れますか？」を開き回答表示。旧 `/exporter/guide/` → `/exporter/#tutorial` に遷移。console error取得結果0件。
6. viewport reset、所有タブ2079269442 close、`browser.tabs.list()` `[]` を読戻し。ambientユーザータブは触れていない。

実スマホ・外部管理アプリへの導入・他言語やテーマ・実際の表示fontは未確認。端末受入を意味しない。source code編集・本番再deploy・ログイン変更なし。

## 導入章Escape後の追加確認

- 2026-11-01 21:02 JST。前回、導入章でEscape直後に `paused: false` を観測し、遅延後を測定していなかったため追加確認。source `046b4979ca81e078720026921f88b46c3668797e` として先に照合した本番URL、Chrome専用新規タブ2079269445、1440×1000。
- 導入章を開いて約2.5秒後、dialog.open=true、paused=false、currentTime=33.250477、readyState=4で実再生を確認。Escape後0.5秒でdialog.open=false、paused=true、currentTime=33.268770、activeElementは導入章button。Escape後1.5秒でもdialog.open=false、paused=true、currentTime=33.268770、activeElementは導入章button。動画停止とfocus復帰に収束し、1秒間currentTimeは変化しなかった。直後の前回観測は非同期close処理中と整合するが、内部原因の確定ではない。
- viewport reset・専用タブcloseを実施。直後の一覧には閉じたタブが一時残ったが、約1秒後に `browser.tabs.list()` が `[]` と読戻せた。ユーザーのambientタブは操作していない。他の動画・幅は再実行せず、source編集なし。
