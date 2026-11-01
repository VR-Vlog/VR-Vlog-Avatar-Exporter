# 導入UI ローカル検証

2026-11-01 20:16–20:19 JST、Terra gpt-6.1-terra/high。Chrome専用タブ2079269420、http://127.0.0.1:8797/ 、日本語・ダーク、DPR 1。ブラウザversionとzoomは未確認。親から配信対象として指定されたcommit 2df7d93eaa5f1f2e9247e3ac38fbb51fec2d0eb1。exporter-site-sourceのHEADも同値と読取ったが、サーバープロセスとディレクトリの結合は独立に検証していない。

- ヒーローの「エクスポーターを導入する」およびヘッダー「導入する」で#installへ移動、PC上端23.5px、390幅23.6px。前回本番PCの41.5pxとは条件差がある。
- DOMで見出し「エクスポーターを追加」「アバターのプロジェクトに導入」、CTA「ALCOMで追加」「VCCで追加」、バックアップ・Unityを閉じる注意がCTAより前にあることを確認。
- ALCOMの「管理」・最新正式版・「適用」と画像、VCCの「Manage Project」・「Installed Version」と画像を切替表示。ダウンロードURLはALCOM https://vrc-get.anatawa12.com/alcom/ 、VCC https://vcc.docs.vrchat.com/ に切替。両CTA hrefは vcc://vpm/addRepo?url={{ listingInfo.Url }} 。ローカルの未展開は指定された想定内。本番での実URLは今回未確認。vccリンクはクリックしていない。
- 1440はカード2列・2枚目の本文と画像は横、900/1100はカード2列・2枚目本文と画像は縦、390/320はカード1列。document scrollWidthは各幅と一致し横overflowなし（1440、1100、900、390、320）。指定幅のうち600px自体と601–899pxの全範囲は未測定。
- 可読性の懸念: 390pxで2枚目の見出し末尾「入」が1文字だけ次行。320pxでは事前注意末尾「る」が1文字だけ次行、1枚目見出し「追加」が途中で改行。1440pxでは2枚目本文の「開く」の「く」が単独行。画像を参照。
- ALCOMボタンからTabでVCCへ移りEnterで選択でき、aria-pressedがALCOM true/VCC falseからfalse/trueへ変更。各managerの画像モーダルが開き、VCCは閉じるボタンとEscで閉じ、元の拡大ボタンにfocus復帰。ALCOMも開きEscで閉じた。
- fallbackを展開し、VCCとALCOMそれぞれの案内への切替、URL欄readonlyおよび値 {{ listingInfo.Url }} を確認。実際のURL展開・外部管理アプリ導入は未確認。
- 初期動画はsrc属性なし、preload=none、data-src=https://vrvlog.fun/guide/vrvlog-start-guide.mp4?v=be86956d2337。先頭再生（観測時6.39秒）、導入（33.27秒）、転送（145.74秒）はreadyState 4、paused false、media errorなし。close/Esc後はpaused trueで各起動ボタンにfocus復帰。初期時点のネットワーク通信有無は未検証。
- /guide/ と /guide/index.html#transfer はそれぞれ /#tutorial へ移動。ブラウザconsole error取得は空。旧URLのJS無効時・全環境は未確認。
- スクリーンショットはChromeのJPEG原本と、sipsで変換したPNGを保存。desktopは1440x1000、mobileは390x844、fullは390x2478。320/900/1100の追加画像とfallbackも保存。スクリーンショットは動作検証の一部であり、実機・外部アプリ導入は未実施。
- viewport override reset済み。専用tab 2079269420は後続本番確認のためhandoff保持。最後の検証後にclose/resetが必要。ソース編集なし。

## 追加確認: 最新head 92ca1ae6b907d6d3d5ef77bb941c03ef18fd33fa

2026-11-01 20:20 JST。親の指定head、exporter-site-sourceのHEADも同値。専用タブで通常reload後に確認。サーバープロセスと作業領域の結合は独立未検証。ローカル画面で新しい短縮文言とinline-blockの実際の表示を観測し、新版app.js/CSSの適用を確認。HTMLに記載のcache keyはapp.jsとstyles.cssとも `install-cards-20261101`。

- ALCOM選択の本文は「「管理」を開く」、VCC選択後は「「Manage Project」を開く」。VCC切替後aria-pressedはALCOM false/VCC true。
- 2枚目見出しのDOM textContentは番号を除き「アバターのプロジェクトに導入」。2つの語句はinline-block。390/320pxで「アバターの」／「プロジェクトに導入」の2行となり、以前の390pxの末尾1文字だけの改行は解消。DOM snapshot上のaccessible nameは語句境界に空白が入る「アバターの プロジェクトに導入」。
- 320・390・900・1100・1440pxでdocument横幅はviewport幅と一致。320・900・1100・1440pxでbody幅も一致。PCの短縮本文は単独文字の折返しなし。320pxの事前注意末尾「る」単独行と1枚目見出しの「追加」途中改行は残る。
- 新画面は `after-local-final-*` のJPEG原本とPNGに別名保存。以前のスクリーンショットとJPEG原本は保持。
- 今回は表示・切替と指定幅のoverflowのみを追加確認。上記の動画・ダイアログ・リンク等は2df7d93eaa5f1f2e9247e3ac38fbb51fec2d0eb1での実行結果であり、このheadで再実行していない。
- viewport reset済み。専用タブ2079269420は後続の本番確認のため保持。最終検証後close/resetが必要。

## 追加確認: 最終head f9fa63e6160c36cf0d2b362d089c82c0bbc45a51

2026-11-01 20:21 JST。親の指定head、exporter-site-sourceのHEADも同値。専用Chromeタブ2079269420で通常reloadし、指定の追加範囲のみ確認。サーバープロセスと作業領域の結合は独立未検証。

- PC 1440×1000でALCOM/VCC表示を撮影。切替後のVCCの本文「「Manage Project」を開く」とaria-pressed false/trueを観測。ALCOM本文は「「管理」を開く」。
- 390×844と320×844のALCOM表示を撮影、390はVCCと全ページも保存。320pxでは事前注意は「バックアップして、」／「Unityを閉じる」、1枚目見出しは「エクスポーターを」／「追加」で改行。前回の「る」単独行と「追加」途中の改行は解消。320/390のdocument横幅はviewportと一致。
- DOM textContentは番号を除いて1枚目「エクスポーターを追加」、2枚目「アバターのプロジェクトに導入」、注意「バックアップして、Unityを閉じる」。該当語句はinline-blockを確認。
- `after-local-approved-*` にJPEG原本とPNGを別名保存。92caおよび2dfの画像は保持。動画・ダイアログ・リンク等の一式はこのheadで再実行していない。
- viewport reset済み。専用タブ2079269420は後続の本番確認まで保持し、最終検証後close/resetが必要。
