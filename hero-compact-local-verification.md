# Hero ローカル操作・表示確認

- 2026-11-01 20:40–20:49 JST。対象: `http://127.0.0.1:8798/`、source checkout HEAD `3528012775cccc24ccbae67c1e42c8412069479b`、作業ツリー差分なし。配信内容とGit SHAのファイルハッシュ照合は未実施。
- Chrome extension、専用タブID 2079269437、旧URL確認の専用タブID 2079269439。日本語・暗色表示、初期scrollTop 0、DPR 1、document.fonts.status loaded。Chromeバージョン・ズーム・実際の描画フォント未確認。実スマホなし、スマホ幅はPCのviewport変更。変更前は兄弟 `hero-ui-proposal/observation.md` と4画像を再利用（本番source 59e25075d1cecb69804e7e845c7fa0d9ec6ab70d / run 41301819552、20:40 JST）。元ページとpublication JSONのハッシュ照合はされていない。
- 保存: `after-local-desktop.png` 1440×1000、`after-local-mobile.png` 390×844、`after-local-width320.png` 320×844、`after-local-width900.png` 900×1000。詳細の矩形と計算スタイルは `local-measurements.json`。各表示で初期スクロール0、font status loaded。600×844は寸法のみ。

## 表示と期待結果

- h1は「アバターを、」「VR Vlogへ。」の2行、同サイズ、2行目 rgb(255,24,128)。1440では48px、390では32.76px、320では30px、900では34.2px。各spanは1行。
- 本文のcomputed margin-topは1440/900で20px、390/320/600で16px。主CTAは `.vrx-hero-copy` 内にあり、390/320のCTA下端342.4/363.2px、動画カード上端400/420.8pxでCTAが先。
- 1440のコピー幅522.5px、動画カード577.5px。動画見出しは全測定幅で1行。横overflowは各viewportのscrollWidthが幅と一致し観測されず。
- CTA/動画/2章ボタンが初期画面に収まるか: 1440×1000は最下端599.8px、390×844は719.6px、320×844は701.1px、900×1000は485.4pxで収まる。600×844は章ボタン下端845.8pxで約1.8px画面外。600付近で初期画面完全収容の期待を満たさない。
- **320幅の本文は画像上「す。」が最終行に孤立**。不自然な孤立なしの期待を満たさない。ソース修正なし。

## 操作

1. hero導入リンククリック → `#install`。header導入ボタン → 同 `#install`、install矩形上端82.8px。期待通り。
2. PC先頭再生 → ダイアログ、実再生 currentTime 2.15秒・paused false。閉じる → paused true、先頭再生ボタンへフォーカス復帰。導入章 → 30.79秒で再生、Escape → paused true・導入章へフォーカス復帰。転送章 → 143.23秒で再生、閉じる → paused true・転送章へ復帰。各観測はクリック後の経過を含む。
3. VCC / ALCOM切替後、各管理アプリのラベルと画像、拡大ダイアログが切替。VCCはEscape、ALCOMは閉じるボタンで閉じた。代替URL展開で値は `{{ listingInfo.Url }}`、CTAも `vcc://vpm/addRepo?url={{ listingInfo.Url }}`。ローカルsource templateとして想定どおり未展開。本番で実URLへの置換を確認する必要あり。外部管理アプリは起動していない。
4. 390幅で先頭動画再生 → ダイアログ表示・currentTime 0.129秒・paused false、閉じる → paused true・元ボタンにフォーカス復帰。動画全章の繰返しは省略。
5. `/guide/` に専用タブで移動 → `/#tutorial` に遷移。初期videoはsrc属性なし・preload `none`。通信が発生しなかったことは未検証。
6. 当該タブのconsole errorは取得結果0件。viewport override reset後、専用タブ2つを各自close、`browser.tabs.list()` は `[]`。既存ユーザータブには触れていない。

## 未確認

本番反映後の表示・本番URL置換・本番操作、実スマホ、別テーマ・文字サイズ、Chromeバージョン、描画フォント、配信ファイルとSHAの完全一致。動画とCTAの実機受入を証明しない。
