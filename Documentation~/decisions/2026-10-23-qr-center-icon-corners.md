# アプリに転送のQRの中央にアプリのアイコン、四隅に読み取り枠の印を出す

- 日付: 2026-10-23
- 対象: `Editor/LanTransfer/AppTransferFlow.cs`（`TransferQrCode.Encode`・`CanMark`・`MarkShare`）・`Editor/LanTransfer/AppTransferPanel.cs`・`Editor/LanTransfer/AppTransferPanel.Done.cs`・`Editor/LanTransfer/PhoneGlyph.cs`・`Editor/LanTransfer/AppTransferWindow.uxml`・`Editor/LanTransfer/AppTransferWindow.uss`・`Editor/LanTransfer/CloudVrmTransferWindow.cs`（`RenderQr`）・`Tests/LanTransfer/AppTransferFlowTests.cs`・`Documentation~/CloudTransfer.md`・`CHANGELOG.md`

## 背景

**アプリに転送** のQRを見ても、スマホのカメラで読み取るものだと分かりにくかった。案内の「スマホのカメラで読み取る」はQRの下に小さく出ていて、その下の赤い注意のほうが目立っていた。

あわせて、書き出しを始めたあとの下のボタン列で **VRMを保存** が右寄りにあり、QRの表示中は **QRを作り直す** がボタン列にも出ていた。期限切れのときはQRの場所に **QRを作り直す** が出るので、ボタン列の分は要らない。

## 決定

- QRの中央にVR Vlogのアイコンを重ねる。大きさはQRの描画の一辺（クワイエットゾーン込み）の 20%（`MarkShare`）で、下地は白の角丸。
- アイコンで隠れる部分を読めるように、アイコンを出すQRは誤り訂正をレベルHで作る。ふつうの長さのリンク（317バイト）では、レベルMのバージョン13（69モジュール）がバージョン19（93モジュール）になる。1モジュールを画面の整数ピクセルに合わせる決まり（`DisplaySide`）は変えないので、1モジュールが細かくなり、QRの大きさも変わる。
- レベルHで1つのQRに収まらないリンクは、これまでどおりレベルMでアイコンなしにする（`CanMark`）。判定は、実際のリンクより短くならない仮のリンク（`PlaceholderLink`）で行い、QRの場所の大きさもその仮のリンクから決める。名前を「あ」だけで書くとアイコンが出るのは107文字まで、転送できる名前の長さ（224文字）は変わらない。英数字だけなら300文字でもアイコンが出る。
- QRの場所の四隅に、アクセント色のL字の印（カメラの読み取り枠の形）を出す。QRを隠しているあいだも出す。
- 「スマホのカメラで読み取る」の前にスマホの線画アイコン（`PhoneGlyph`）を付け、QRを隠しているあいだも出す。
- かんたんのウィンドウの下のボタン列は、左端に **VRMを保存**、右端に **閉じる** にする。表情ボードでは **戻る** のすぐ右に **VRMを保存** を移す。QRの表示中はボタン列に **QRを作り直す** を出さない。期限切れと中止のあとは、これまでどおりQRの場所に **QRを作り直す** を出す。
- 転送を始めていないパネル（`Begin` を通らない）がQRを出すときは、リンクからアイコンの有無を決める（`ShowQr`）。Unity のテストで、この場合にアイコンが出ていなかったのを見つけて直した。

## 確認

- `AppTransferFlowTests.MarkedQrReadsWithTheCentreCoveredAndLongNamesFallBackToUnmarked` で、レベルHのQRは中央の `MarkShare` 分を塗りつぶしても読めること、長い名前ではレベルMに戻ることを確かめる。CLI のテスト（`Tools/run-lan-transfer-tests.ps1`）は163件すべて成功した。
- Unity 2022.3.22f1 の EditMode テスト（`VRVlog.LanTransfer.Editor.Tests`）は226件中225件が成功した。失敗の1件（`CloudTransferWindowTests.AppTransferSavesToPcWithoutTheTransferConsentButNotWhileExporting`）は変更前の develop でも同じく失敗する。
- エディタの拡大率100%と300%、ダークとライト、日本語・英語・中国語で、変更前と変更後を撮って比べた（PR に画像）。QRの表示中・隠しているとき・期限切れ・作り直したあとを含む。
- 撮った画像のQRを zxing-cpp と OpenCV の2つで読んだ。かんたんのウィンドウ（拡大率100%・日本語・英語・中国語・作り直したあと）と表情ボード（300%）は、どちらでも元のリンクと同じ中身になった。表情ボードの100%（幅640・1モジュール3px）は zxing-cpp では読めたが、OpenCV では読めなかった。スマホの実機での読み取りはまだ確かめていない。

## 検討して採らなかった案

- QRの上にアイコンと「スマホのカメラで読み取る」の見出しを置く案（A）は採らなかった。小さいウィンドウでQRに使える高さが1行分減るため。
- QRの下に「1 カメラで読み取る」「2 アプリで取り込む」の手順を並べる案（B）は採らなかった。行が2つ増え、QRの場所が狭くなるため。2つ目の手順はカメラで読めばアプリが開くので、書かなくても困らない。
- 名前の長さにかかわらず常にレベルHにする案は採らなかった。今まで転送できた長い名前が転送できなくなるため。
- レベルMのままアイコンを重ねる案は採らなかった。中央のモジュールが隠れて、読めないQRが出ることがあるため。

## 欠点

- 同じリンクでもモジュールが細かくなるので、離れた場所や画面の小さいスマホでは読み取りにくくなるかもしれない。拡大率100%のふつうの長さの名前で、1モジュールはかんたんのウィンドウで5pxから4px（QRは385pxから404px）、表情ボード（640×760）で5pxから3px（QRは385pxから303px）になる。表情ボードでは高さに4pxで収まらないため、QRが小さくなる。
- 名前が長いとアイコンが出ず、名前の長さによってQRの見た目が変わる。
