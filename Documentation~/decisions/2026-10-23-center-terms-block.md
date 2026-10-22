# 転送画面の規約のまとまりを中央に寄せる

- 日付: 2026-10-23
- 対象: `Editor/LanTransfer/AppTransferWindow.uss`・`Editor/LanTransfer/AppTransferWindow.uxml`・`Editor/LanTransfer/AppTransferFlow.cs`・`Editor/LanTransfer/AppTransferPanel.cs`・`Tests/LanTransfer/AppTransferFlowTests.cs`・`CHANGELOG.md`

**アプリに転送** の画面下の「規約」と **利用規約を読む** の行、同意のチェックの行、「VRMの保存に同意は要りません」、「スマホのカメラでQRを読み取ると…」を 1 つのまとまりとして中央に寄せる。行は中身を寄せたまま中央に置き（チェックと文は離さない）、折り返した文は 1 行ずつ中央に揃える。「規約」の見出しは左の列の幅（64px）を外して **利用規約を読む** と並べ、プライバシーの行も空の列を外して同じく中央に置く。すぐ下のオレンジの「利用規約に同意すると作れます」だけが左に残るとまとまりから外れて見えるので、これも中央に揃える。入力欄・ボタンの行・文言は変えない。行の高さは今までどおり固定なので、文字の幅で下の行は動かない。

## 「QRを他の人に見せたり…」を書き出し中から出す

QRが出てから注意を読むのでは遅いので、`AppTransferFlow.ShowsDoNotShare` を足し、書き出し中・送信中・QRの表示中に出す。QRが来ない状態（完了・期限切れ・中止・失敗）では消すが、場所は残すので、QRが出たときも状態が変わったときも警告と下の行は動かない。文言は変えない。
