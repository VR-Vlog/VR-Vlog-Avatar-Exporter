# Exporter導入UI 本番検証

2026-11-01 20:29 JST頃。操作担当 gpt-6.1-terra/high、Chrome拡張、専用タブ2079269420、日本語・ダーク、DPR 1。Chromeバージョンとzoomは未確認。実機・外部管理アプリのインストール、ログインや他のタブの操作はなし。

## 公開証跡

別の専用タブで https://vrvlog.fun/exporter/publication.json を読戻し、作業フォルダ `publication.json` と照合。source_commit `59e25075d1cecb69804e7e845c7fa0d9ec6ab70d`、run_id `41301819552`、run_number `1456`、digest `5251f840f79a00b6468c9117806c3c7af40ca96e58c5918838e6991c46675ce7` は指定・期待と一致。repositoryは `VR-Vlog/VR-Vlog-Avatar-Exporter`、workflowは `.github/workflows/build-listing.yml`。これはJSON同士の比較であり、配信された全ファイルのdigest独立再計算ではない。checkoutのHEADを本番根拠に使っていない。公式mirror commitとPages runは親からの引継ぎで、今回独立にCI状態を確認していない。

## 期待と実際

- 通常reload後、2カードの新UIを確認。#installへのヒーローリンクをクリックして移動。PCのセクション上端23.5px、390px幅23.6px。前回変更前PCは41.5pxのため撮影位置に約18pxの差がある。
- 見出しはDOM textContentで番号を除き「エクスポーターを追加」「アバターのプロジェクトに導入」。accessible snapshotでは語句境界に空白が入る。CTAは「ALCOMで追加」／「VCCで追加」。PCは2列、390幅は1列。事前注意「バックアップして、Unityを閉じる」はCTAより上。
- 320pxでは注意が「バックアップして、」／「Unityを閉じる」、1枚目見出しが「エクスポーターを」／「追加」、2枚目が「アバターの」／「プロジェクトに導入」と語句単位で改行。document scrollWidthは320、body scrollWidthも320。390とPCのdocument scrollWidthも各viewport幅と一致。
- ALCOMは「管理」を開く・最新正式版を選び「適用」、ALCOM画像、download https://vrc-get.anatawa12.com/alcom/ 。VCCは「Manage Project」を開く・「Installed Version」で正式版を選ぶ、VCC画像、download https://vcc.docs.vrchat.com/ 。表示とaria-pressedの切替を確認。
- 両CTAのhrefは `vcc://vpm/addRepo?url=https://vrvlog.fun/exporter/index.json`、両fallbackのreadonly URLは `https://vrvlog.fun/exporter/index.json`。各管理アプリのfallback文章に切替。外部schemeはクリックしていない。
- VCC画像galleryは開き、Escで閉じ拡大ボタンへfocus復帰。ALCOM画像galleryは開き、閉じるボタンで閉じて同様にfocus復帰。画像・説明は各選択に対応。
- PCで動画先頭、導入、転送を起動し観測時は各2.64秒、33.16秒、145.69秒、いずれもpaused=false、readyState=4、media errorなし。先頭をclose、各章をEscで閉じるとpaused=trueとなり、それぞれの起動ボタンへfocus復帰。
- FAQの3項目を表示、QRの項目を展開して回答を確認。旧 `/exporter/guide/` と `/exporter/guide/index.html#transfer` は `/exporter/#tutorial` へ移動。JS無効時、他のFAQの展開、他ブラウザは未確認。
- `tab.dev.logs` のerror一覧は空。画面に関係しない全環境のエラー不在を保証するものではない。
- 画像はChromeのJPEG原本とPNG変換を保存。PC1440×1000、モバイル幅390×844、fullは390×2478、320×844。ブラウザのレスポンシブ表示であり実スマートフォンの結果ではない。
- 証跡タブ2079269424をclose、最後にviewport resetと画面タブ2079269420のcloseを実行。直後の一覧は遅延して旧タブを示したが、1.2秒後の再読取りは空。ソース変更や再deployなし。
