# アプリに転送の QR の注意書きを依頼の形にする

- 日付: 2026-10-14
- 対象: `Editor/LanTransfer/AppTransferFlow.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/LanTransfer/AppTransferFlowTests.cs`・`CHANGELOG.md`・`Documentation~/CloudTransfer.md`

## 背景

**アプリに転送** の QR の下の注意書きは「QRを他の人に見せたり配信に映したりしない」だった（[2026-10-14-app-transfer.md](2026-10-14-app-transfer.md)）。このウィンドウの文言は「ください」などの依頼の形を使わない規則で書いていて、テスト（`WindowTextFollowsTheWordingRulesAndExistsInEveryLocale`）でも検査している。オーナーが QR の画面を見て、この行を「しないでください」にするよう指示した。

## 決定

- 注意書きを「QRを他の人に見せたり配信に映したりしないでください」にする。
- テストの「ください」の禁止は、この 1 行（`AppTransferText.DoNotShare`）だけ外す。句点・「できます」などの他の規則と、訳語の側の禁止（Please・주세요・请・請）はこの行にも残す。
- 訳語は訳語の側の規則の中で依頼の形にそろえる。韓国語は名詞形の「내보내지 않기」を「내보내지 마세요」にした。英語の「Do not show this QR to anyone or on a stream」と中国語の「不要…」は、Please・请・請 を使えない規則の中でもともと依頼として読めるので変えない。

## 検討した代替案と不採用理由

- 規則どおり元の文言のまま残す: オーナーの指示と合わない。
- 「ください」の禁止をウィンドウ全体で外す: 他の行の語調まで変わり得る。指示はこの 1 行だけ。
- 英語を「Please don't show…」、中国語を「请勿…」にする: 訳語の側の規則（Please・请 を使わない）に反する。

## 影響

- 注意書きが 4 文字長くなる。QR の下の 1 行に収まることを Unity Editor で確認した（PR 本文の比較画像）。
- 既存の **QRコードでスマホに送る** の文言は変わらない。

## 未決

- なし

## 出典

- 2026-10-14 オーナーの指示（QR の画面のスクリーンショットへの「しないでくださいにして」）
