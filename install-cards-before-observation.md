# 本番導入画面の変更前観測

2026-11-01 20:15–20:16 JST、Terra/highがChrome拡張を使用。専用タブ 2079269420 で https://vrvlog.fun/exporter/ を開き、画面内「エクスポーターを導入する」で #install へ移動。日本語・ダーク、DPR 1。Chromeのバージョンとズームは未確認。既存ユーザータブ、ログイン、外部アプリは操作していない。

- PC 1440×1000: ALCOM選択、セクション上端41.515625px、scrollY 602。VCC選択後はページ末尾で停止し上端71.515625px、scrollY 572。指定位置との差はページ末尾による。
- レスポンシブ 390×844: ALCOM選択、上端23.609375px、scrollY 761。全画面は390×2408。実機スマートフォンではない。
- 撮影はChromeのJPEG原本を保存し、sipsでPNGへ変換。各画像10MiB未満。原本とPNGを保存。
- https://vrvlog.fun/exporter/publication.json を同時刻に別の専用タブで読取。source_commit f544368a4e7bac51163c59acec6bf7e713e81bd7、run_id 41299621193、run_number 1451、digest 01f756800c0b0bf74c7150a10b9cf12b09e17c97d34c57f24f431249e65d7ef6。これは公開証跡の読戻しで、個別ファイルのhash再計算ではない。
- 公開証跡タブはclose済み。viewport overrideはreset済み。画面タブ2079269420は後続のlocalhost/本番確認用にhandoffとして保持。最終検証後にclose/resetする。
