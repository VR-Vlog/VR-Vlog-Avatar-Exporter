# 導入カードUIの実装・公開

## 最終表示の調整

320幅の追加観測を受け、f9fa63e6160c36cf0d2b362d089c82c0bbc45a51でstage1見出しと事前注意にも語句inline-blockを適用。文字列は同じ。自分で最新HTML2行差分を再レビューし、文字/aria/順序と320幅で語句が収まる設計を確認、diff --check成功。編集終了。配信5ファイルをバイト照合しlocal-approved-source-binding.jsonへ保存。旧headのbinding/画像は保護し操作結果を付け替えない。

旧Validate41301296259/41301414246は新しい同PRの実行でcancelled（既存concurrency）。成功ではない。monitor52280はexit1/EOF、10525もAPI timeoutでexit1/EOF。最終必須CIは41301508893/listing41301508921。ブラウザの追加確認は改行・本文・切替・overflowに絞り、未変更の動画一式は重複しない。Terraが最終headのPC/390/320を確認し孤立文字が解消、横overflowなし、DOM文字列は本人指定どおりと報告。local-verification.mdの各commit別の実行範囲を正とする。

2026-11-01。同タスクの案Aに対し、本人は見出しを「エクスポーターを追加」「アバターのプロジェクトに導入」、ボタンを「ALCOMで追加」／「VCCで追加」に訂正し、「その文言で実装して、本番にも反映して」と承認。2カード構成・アプリ選択の上部集約・事前条件・対処折りたたみを実装し、既存経路で公開する。

対象VR-Vlog/VR-Vlog-Avatar-Exporter Websiteのみ。既存Epic577、既存source領域exporter-site-sourceを再利用。main由来hotfixからPR、既存必須CI後に採用、既存build-listing.ymlでWebsiteのみ公開しsource/run/receipt/本番を照合、developへ同期。新CI・版/タグ・Release・SDK・Unity・共有設定は変更しない。public/exporterは生成同期先で直接編集しない。

saved: install-ui-implementation/、owner本タスク、source binding・前後画像・最小ログ・公開receipt・review・管理結果。各10MiB未満、合計30MiB上限、本人確認まで保持。scratchは今回専用localhost serverログと証跡indexのみ、sourceは復旧用に保持。新cacheなし。既存cache/SDK/別checkoutのコピーなし。不要scratchは停止/EOF/利用終了確認後に限り整理。

親Sol/high: 実装・原因分析・自身の敵対的セルフレビュー。最終schema/listing一式は既存PR CIのみ、ローカル重複なし。軽い構文/差分/HTML整合を親が確認。Terra/high新規コンテキスト: 編集前本番撮影、その後同じ専用Chromeでローカル・本番PC1440x1000/390x844/320幅、ALCOM/VCC、導入CTA・画像dialog・fallback・動画/章/close/ESC/focus・overflowを確認。外部アプリ実起動・実機導入なし。ユーザーambienttabを操作せず今回専用tabを終了する。

共通完了contextは初回UI案input1566380639534448701:824:v0を返す。既存immutable completion.jsonとcanonical repository scope不整合を保護、管理完了を偽造・設定で回避しない。

## 実装と自身の敵対的セルフレビュー

base main f544368a4e7bac51163c59acec6bf7e713e81bd7。hotfix/install-cards-ui、Website4ファイルのみ。実装前にTerraが同revision/runをpublication.jsonで読戻し、PC/390幅のALCOM/VCCを撮影。既存sourceを再利用しコピーなし。

最新未コミットdiffとapp.js/guide.css/残存DOM参照を自身でレビュー。反例：カード移動で管理アプリのquerySelectorが欠落し全JS停止、ALCOMの適用をVCCへ表示、defaultHTMLと切替後の文言が不一致、URL templateを壊す、captionの旧画像注釈喪失、850-1100幅の2列内で本文が狭すぎる、先に押せてバックアップ指示を見落とす、動画と画像dialog/導入anchorを壊す、存在しない導入成功状態を表示する。
対処：3つのdata-manager-copyキーと2managerのキーを一致、見出しは選択に依らず本人訂正の2つ、CTAはmanager名＋で追加。URL template/画像素材とcaption/画像dialog/動画codeはそのまま。注意をCTA前へ移動。1100幅以下はカード2の本文と画像を縦配置し、600以下は2カードも縦配置。準備完了は従来の条件文を保持し外部アプリ導入を判定しない。重複の導入動画リンクだけ除きトップ動画/章/headerを保持。JS/CSS cacheキー更新。
最初の2回のapply_patchは文脈一致/ハンク順序の誤りで未適用・source無変更を確認して補正した。Node通常PATHなしを成功扱いせず、配備済みworkspace runtimeを用いて構文確認。diff --check、HTMLの一意ID/aria/scroll/相対asset/外部link保護、2カード/managerキー確認が成功。新しい実装形状だけのテスト追加なし。既存必須一式はPR CIに任せ重複せず、実ブラウザの配置・操作はTerraで別途確認。

初稿commit2df7d93eaa5f1f2e9247e3ac38fbb51fec2d0eb1をPR587へ提出し本チャットへattach。localhostのHTML/JS/CSS5ファイルをバイト照合しlocal-source-binding.jsonへ保存。sandboxのbind拒否はexit1で停止、権限付きの配備済みPythonでlocalhost限定serverを起動（session23665）。新buildなし。
Terra取得画像を自身で確認し、PC本文「開く」の末尾と390幅見出し「導入」の最後1字の孤立を発見。92ca1ae6b907d6d3d5ef77bb941c03ef18fd33faで重複するproject説明を短縮、見出しを「アバターの」「プロジェクトに導入」のinline-block語句へ分割（見出し文字列は同一）。自身で最新3ファイルの6追加4削除を再レビューし、manager初期/切替の一致・ariaの文字列維持・320幅で最長語句が収まること・他挙動の非変更を確認。Node構文/diff成功。最終source bindingは別名へ保存し初稿証跡を保護。編集終了。CI再実行はソース差分のため、旧headの成功を新headへ付け替えない。Terraも既検証の挙動を重複せず変更した表示・文言・折返し・overflowだけ追加確認する。
