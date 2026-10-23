# 転送後の画面で、変換の内容を種類ごとの行にまとめる

- 日付: 2026-10-23
- 対象: `Editor/LanTransfer/AppTransferPanel.Conversion.cs`（新規）・`Editor/LanTransfer/TransferNotes.cs`・`Editor/LanTransfer/AppTransferPanel.Done.cs`・`Editor/LanTransfer/AppTransferFlow.cs`（`AppTransferText.OtherNotes`）・`Editor/Compatibility/ExportNoteText.cs`（新規）・注意を書く側（`DependencyPolicy`・`UniVrmOneClickExporter`・`VrChatExpressionBaker`・`LilToonFullSnapshot`・`MobileTextureEncoder`・`LilToonMainTextureBaker`・`AlphaMaskBaker`・`PhysBoneSpringExport`）・`Editor/Locales/ExporterLocale_*.json`・`Tests/LanTransfer/TransferNotesTests.cs`・`Tools/run-lan-transfer-tests.ps1`

## 背景

**アプリに転送（通常）** の転送後の画面で、「そのほか」の下に書き出しが出した文が1行ずつ並んでいた。lilToonのバージョン、テクスチャの縮小（1枚1行）、表情の登録、FXの初期状態、PhysBoneの近似（PhysBone 1つにつき3〜7行で、同じ文がPhysBoneの数だけ繰り返される）が混ざり、PhysBoneが8つのアバターで40行を超えた。文はほかの言語の画面でも日本語のままだった。

## 決定

- 見出しを「そのほか」から「変換の内容」にし、種類ごとに1行（エディタ内蔵のアイコン・短い名前・右端に件数）を並べる。開くと中身を1行ずつ出す。開いた種類は「書き出し時の注意」と同じく、エディタを開いている間だけ覚える。
  - lilToon: 「lilToon 2.3.4で描画」と右端に「導入済み 2.4.1」。開く中身はない。
  - 表情: 「VRChatから表情を登録」「13個」。開くと瞬き・口・視線を止める旨。登録できた表情が0のときは警告のアイコンで「VRChatから登録できた表情はありません」。
  - FX: 「FXの初期状態の復元を省略」。開くと保存した内容と、見た目が異なる場合がある旨の2行。
  - テクスチャ: 「テクスチャを縮小」「5枚」。開くと名前と「2048×2048 → 1024×1024」を1枚1行。
  - PhysBone: 「PhysBoneをVRMの揺れものに変換」「8本」。開くと、すべてのPhysBoneに付く項目を「すべて」として1回ずつ、続けて「設定による変更」の項目を本数つきで出し、項目を開くと対象のPhysBoneの名前を並べる。最後に「揺れものの一覧」で全PhysBoneの名前。項目は結果まで含めた短い名前（「伸縮は保存せず元のボーン長を保持」）にし、元の文はツールチップで出す。
  - 9件目からは今と同じ「残りN件を表示」で開く。
- 種類の見分けは文の形で行う。書く側と読む側が同じ文を使うよう、文を `ExportNoteText`（Compatibility）の定数にまとめ、書く側はそれを使う（書き出す文は変えていない）。読む側は定数の `{n}` を値として読み、日本語の文と訳した文の両方に合わせる。見分けられない文は今までどおり1行ずつ並べる。
- 画面の文言と、PhysBoneの文の訳を英語・韓国語・簡体字中国語・繁体字中国語に足した。

## 確認

- `TransferNotesTests`（CLI・`Tools/run-lan-transfer-tests.ps1`）: 種類ごとの読み取り・表情0件・見分けられない文。167件すべて通った。
- Unity 2022.3.22f1 の EditMode テスト（`VRVlog.LanTransfer.Editor.Tests`・`VRVlog.LilToonExporter.Compatibility.Tests`・`VRVlog.LilToonExporter.Editor.Tests`）: 3417件中 2280件成功・34件失敗。変更前（develop）の同じ環境でも 3414件中 32件が失敗し、増えた2件（`ExperimentalExpressionCaptureTests` のウィンドウのクリック操作の2件）はこのクラスだけを回し直すと通った。失敗するテストは回すたびに入れ替わり、VRMの読み戻しと表情キャプチャのウィンドウ操作に集中しているため、この変更とは関係ない。`TransferNotesTests` はすべて通った。
- Unity 2022.3.22f1 で、ダーク・ライト、エディタの拡大率 300%・100%、幅 480・600・860、日本語・英語・簡体字中国語・繁体字中国語で撮って崩れがないことを確かめた（PhysBone 8本・テクスチャ5枚・表情13個の検証用アバター）。
- 比較画像: `Documentation~/Verification/export-notes-graphical.png`（左から今の形・A・B）、`Documentation~/Verification/export-notes-graphical-after.png`（今の形と変更後）。

## 検討して採らなかった案

- 案 B（種類ごとに見出しを常に開き、テクスチャは縮小の割合の横棒、PhysBoneは行＝PhysBone・列＝設定の ✓ の表）は採らなかった。列の見出しが訳すと長くなり、幅 480 や拡大率 300% で列が収まらないため。PhysBoneが多いと表が長く、ほかの種類が画面の外に出る。
- 案 C（上に種類ごとの件数のボタンを並べて絞り込む）は作らなかった。種類が5つ程度では A の行と同じ情報を二重に出すことになるため。
- 文を短くせずそのまま並べる案は採らなかった。PhysBoneの文が2行に折り返し、行の高さが揃わないため。

## 欠点

- 種類の見分けは文の一致に頼る。書く側の文を `ExportNoteText` を通さずに変えると、その文は見分けられず1行ずつの表示に戻る。
- 「ポーズ未対応」などの「書き出し時の注意」側の種類名と対象は、今までどおり日本語のまま出る。
