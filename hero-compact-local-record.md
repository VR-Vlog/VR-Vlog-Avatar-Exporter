# コンパクトなヒーローUIの実装と本番反映

## ローカル最終結果

Terraが1513ed86652137cd8bc522b6674b7ffad4da64aaの320/390/600幅を追加確認。320の本文14px・2行で孤立行解消、390は15px・2行、600x844の両章下端837.81pxで画面内に6.19pxの余裕。動画カードの枠下端は画面外だが、動画本体と全章controlは表示される。3幅とも横overflowなし。latestのsourcebindingとlocal-final-verification.md/measurementsを保存、親も320/600画像を目視。重大未解決指摘なし。
PC/900の画像と操作一式は3528012での結果。1513の変更は600以下だけのCSSなので、その実行を最新headの新実行と扱わず明記して再利用する。新しいviewport写真のためだけに全操作を繰り返さない。本番では公開sourceを固定し改めて実画面を確認する。
操作担当はviewport reset、公開後用の専用tab1つを引継ぎ保持。localhost server71359はローカル確認終了後に停止してsource領域の切替え前にEOFを確認する。

## 狭幅の追加調整とレビュー

Terraは初稿3528012のPC/390/320/900と600近辺を実確認。主見出し/色/本文余白/CTA順序/動画幅は採用案どおり、動画/章/停止/focus・導入リンク・管理アプリ切替・画像gallery・旧guideの回帰なし。320本文の「す。」単独行と600x844の章下端約1.8px超過を検出。親も画像で確認した。
1513ed86652137cd8bc522b6674b7ffad4da64aaでCSS2行のみ調整し編集終了。360以下の本文14px、600以下heroの上paddingを4px/gapを4px短くした。自分で最新diffを敵対的再確認し、media scopeに限定、説明/ボタン/動画の順序・既存の操作/素材/他viewportへの非影響を照合。diff --check成功。最終5配信ファイルをバイト照合しlocal-final-source-binding.jsonへ別名保存、初稿証跡は維持する。
このソース変更により必須CIは最終headで改めて必要。旧headの結果を新headへ流用しない。Terraは変更した320/390/600の表示のみ追加確認し、イベント操作一式の古い実行を付け替えたり重複実行しない。

2026-11-01。本人は比較後の選択肢「この案で実装・本番反映」を明示選択。見出し「アバターを、VR Vlogへ。」へ短縮し、動画の拡大、短い説明とCTAの一体化、スマホは説明→導入CTA→動画の順を実装・公開する。今回承認済みなので再確認を求めない。

対象VR-Vlog/VR-Vlog-Avatar-ExporterのWebsiteのみ、既存Epic577。sourceはexporter-site-source1領域を再利用。現行mainからhotfix、既存必須CI→main採用→既存build-listing.ymlで公開とreceipt/本番照合→develop同期。新CI・版/タグ/Release・パッケージ・動画本体・素材・共有設定は変更しない。public/exporterは生成同期先で直接編集しない。

saved: hero-ui-implementation/、owner本タスク、最終画像・source binding・最小観測・review・公開receipt・管理結果。原本と復旧sourceは本人確認まで保持、各10MiB未満・合計30MiB上限、終了時実測。変更前はhero-ui-proposal/の4画像とsource59e/run41301819552の観測を再利用し、同タスクの撮影をむやみに繰り返さない。scratchは専用localhost serverログと一時evidence git indexだけ、process終了/EOF/利用終了後に不要分整理。SDK/Unity/cache/checkoutの複製なし。

親Sol/highが実装・自身の敵対的セルフレビュー。最終必須一式は既存PR CIのみ、ローカルで重複しない。操作・検証は新規Terra/highに委任し、PC1440x1000/390x844/320/900幅・日本語darkの専用Chromeで配置と動画/CTA/導入切替の回帰を確認。ユーザーambienttabに触らず、最終close/reset。実機導入・外部scheme起動なし。モックアップは実装証拠とせず実画面で比較する。

## 実装と自身のセルフレビュー

base59e25075d1cecb69804e7e845c7fa0d9ec6ab70d、実装3528012775cccc24ccbae67c1e42c8412069479b、hotfix/compact-hero。Website3ファイルのみ。hero-copyに既存CTAを移しDOM順を説明→CTA→動画に統一、2行の短いh1とpinkの2行目、本文短縮、動画カード幅を広げtitleを短縮。JS・poster・video/字幕/章値・導入2カード・fallbackは変更なし。CSSキー更新。
最新diffと直接影響するDOM/CSSを自分で敵対的にレビュー。反例: 移動でCTAが消える/二重になる、HTML閉じタグ不足、旧gridのrow指定が残る、nowrap見出しの幅不足、主動画が初期画面外、短縮で動画チャプター/ariaラベルが壊れる、CSS優先順位で見出しと説明の間隔が適用されない、導入カードへfont変更が波及する。
対処: heroの2childとCTA1つのnestingをHTMLParserで確認。旧copy/actionのgrid配置を除きminmax(0,fr)とmin-width0、スマホ1列にする。既存h1限定font48px上限/スマホ30-36で6字を収め、動画見出しを短くした。selector #exporter-page .vrx-site p のmargin0が旧.vrx-leadの余白指定を上書きする問題をsourceで発見し、全mediaのselectorを.vrx-hero-copy .vrx-leadへ揃えて必要な余白を優先させた。実フォント取得の原因は未確定で修正済みとは主張しない。
HTML nesting/CTA位置/ID一意/aria/相対assetとdiff --check成功、重大未解決指摘なし。試した最初の構築scriptは重複判定の誤りで編集前に停止し、 source未変更で補正。最終3ファイルはレビューした差分と一致。動作/配置の実確認はTerraへ渡し、既存CIをローカルで重複実行しない。
