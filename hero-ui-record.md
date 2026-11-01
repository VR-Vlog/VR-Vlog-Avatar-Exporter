# ヒーローの表示と構成の改善案

2026-11-01追加依頼。「自分のアバターを、VR Vlogへ。」が崩れて見え、見栄えが悪いとの指摘。今回まず現行実画面と表示条件・原因を確認し、改善案を画像比較で提示する。新しいヒーローデザインの採用前に本番を書き換えない。前の導入カード実装・公開の承認を新しいヒーロー案へ流用しない。

対象は既存Epic577・VR-Vlog/VR-Vlog-Avatar-Exporter/Website。sourceはexporter-site-sourceを読取再利用するのみ。新checkout、Unity/SDK/cache、比較用実装/ビルド/previewなし。公式サイトpublic/exporterは同期出力で直接編集しない。

saved: hero-ui-proposal/、owner本タスク、変更前の実画面・変更後モックアップ・撮影条件と生成prompt・最小観測。1画像10MiB未満、合計30MiB上限、本人判断まで保持。画像生成の既定出力は必要な最終案をworkspaceへコピーして原本を残す。今回scratch/cache新規生成なし。

Sol親はsource/CSSと実画面から原因とデザインを判断、操作・撮影は新規Terra/highがChrome専用tabで担当。PC1440x1000/390x844/320幅/900幅、日本語dark、hero先頭scroll0。ユーザーのambienttabは触らない。終了時ownedtab全closeとviewportreset。実スマホの証拠ではない。

## 変更前の観測と原因の切り分け

20:40JSTにTerra/highがChromeの4幅を撮影。public receiptはsource59e25075d1cecb69804e7e845c7fa0d9ec6ab70d/run41301819552。日本語dark/DPR1、font ready後/scroll0、版とzoomは未確認。専用2tabをclose、viewportreset、tabs空を読戻し担当終了。実機なし。observation.mdとmeasurements.jsonに数値を保持。
見出しの欠け・独立した余分な単語・文書横overflowは4幅で確認されなかった。PCでは見出し1行目486.4px/2行目290.9pxで大きな長さ差。左列670.8pxに対し動画カード447.2px、見出しと導入actionは別grid行。320幅の動画タイトルは「転送まで」の語途中で改行。見栄えの不均衡と文字の切れ・はみ出しを区別する。
computedのフォントはZen Kaku Gothic New/sans-serif、checkは和文true/英字falseだが実グリフの描画フォント・原因は確定していない。英字のfallback故障と断定しない。親のsource読取と計測から、大きさ/行長/余白/見出しの競合を改善対象と判断。

## 案A（未実装）

見出しを「アバターを、」「VR Vlogへ。」へ短縮し、2行の見た目の大きさと長さを揃える。2行目にbrand pink。短い説明「Unityのアバターを、QRコードでスマホへ。VR Vlogでいつものアバターを使えます。」と既存導入CTAを1blockへまとめる。PCは左右の幅を近づけ動画を大きくし、動画タイトルは「導入から転送まで」と短縮。スマホは説明→導入CTA→動画の順とし、動画は初期画面内で見つかる設計を狙う。サムネイル素材は動画冒頭を維持する方針。撮影イメージや別ガイドは復活させない。

image_gen内蔵で実画面を参照して生成。初稿desktopは見出しが大きすぎたため小さくする追加生成を行い、最終案はafter-A-desktop-v2-mockup.pngとafter-A-mobile-mockup.png。最終prompt3本を保存。親が目視し、本文・操作名と意図を確認した。desktop1505x1045/元1440x1000、mobileは元390x844に近い比率の拡大描画。生成画像なので文字/余白/ロゴ/動画poster/下のinstallの細部は近似で、実素材を修正した証拠ではない。実装時は既存assetをそのまま使う。フォント/初期画面の収まり/320幅の語句保持/動画dialog操作はこの画像で検証済みとしない。

確認点: 短いコピー、控えめな文字サイズ、動画との比率、スマホでCTAを動画前へ移す順序。実装・本番変更は今回なし。新しいCI・ビルド・試作・公開を行っていない。既存Epic577へ比較と閲覧リンクを追記し、案として判断を受ける。

mobile書出し853x1844、元390x844。desktop1505x1045、元1440x1000。前後同じ縦横比を目指した拡大描画だが、余白・文字・下部表示位置の一致は保証しない。ボタンの大きさやposter細部も生成上の差。初稿desktopは採用案ではなく最終v2と区別して保持。
AGENTS.mdの製品UIの採用判断に沿い、文言と並びも変わる案として選択肢「この案で実装・本番反映／元の見出しのまま整える／別の案を見たい」を提示。未回答や既定選択を承認としない。
比較をEpicで閲覧可能にするため、既存evidence branchへ必要画像・prompt・観測を保存。scratchは今回所有の一時git indexだけ、上限10MiB・保存処理中のみ保持、process終了後に削除。source workingtreeは変更しない。
