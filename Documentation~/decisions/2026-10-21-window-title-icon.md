# Exporter のウィンドウのタブにロゴを出す

- 日付: 2026-10-21
- 対象: `Editor/Compatibility/ExporterWindowIcon.cs`（`Editor/LanTransfer/AppTransferIcon.cs` から移動）・`Editor/LanTransfer/AppTransferWindow.cs`・タブの文字を設定している各ウィンドウ・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Compatibility/DependencyStartupTests.cs`・`Tests/Startup/ExporterStartupProbe.cs`

## 背景

**アプリに転送** のウィンドウだけ、タブに VR Vlog のロゴが付いていた。**VRM書き出し** をはじめ、ほかのウィンドウのタブは文字だけだった。並べて開くとどれが Exporter のウィンドウか分かりにくいので、同じロゴを付けてほしいという要望があった。

## 決定

タブの文字を設定しているウィンドウすべてに、アプリに転送と同じロゴを付けた。

- 対象: **VRM書き出し**・**アプリに転送（通常）**（同じウィンドウの転送用の表示）・**アプリでの見え方**・**瞬き**・**瞬きを確認**・**書き出すVRMを確認**・**VRMの書き出しを続ける**・**ポーズを確認・調整**・**VR Vlog VRM書き出し**・**VR Vlog 書き出し結果**・**VR Vlog 動作環境**・**スマホに送る**。**アプリに転送** はこれまでどおり。
- `GetWindow<T>(utility, title)` で開くウィンドウは文字だけが入るので、`ExporterWindowIcon.Restore(GetWindow<T>(...))` で包んでロゴを足す。
- ロゴの画像はアプリに転送のウィンドウが持っていたものを `ExporterWindowIcon` に移し、全ウィンドウで 1 枚のテクスチャを使い回す。どのウィンドウも参照できるよう、依存の無い `VRVlog.LilToonExporter.Compatibility` アセンブリに置いた（アプリに転送のアセンブリもここを参照している）。
- テクスチャはアセットではないので、Unity を再起動して保存済みのレイアウトからウィンドウが戻るとき、タブの文字は残るが画像は外れる。ドッキングできるウィンドウ（**VRM書き出し**・**アプリでの見え方**・**アプリに転送**・**スマホに送る**）は `OnEnable` で画像が無ければ付け直す。アプリに転送は画面の更新のたびにタブを設定し直しているが、クラウド転送が無効なときは更新が途中で戻るので、`OnEnable` でも付け直す。ほかは `ShowUtility` で開く補助のウィンドウで、レイアウトに保存されない。
- スクリプトの再読み込みでは `static` の参照が消えるが、テクスチャ自体は残る（`HideAndDontSave`）。名前で探して同じものを使い直し、再読み込みのたびに増えないようにした。
- 以前はアプリに転送のウィンドウを閉じるとロゴのテクスチャを破棄していた。共有にしたので破棄しない（128px の画像 1 枚をエディタの終了まで持つ）。

言語を切り替えたときは、これまでどおりウィンドウを開き直したときにタブの文字が新しい言語になる。ロゴはどの言語でも同じものを付ける。

## 入れなかったもの

- ロゴの画像の変更・Unity の標準アイコンへの差し替え。アプリに転送と同じものにそろえる要望なので、画像は変えていない。
- タブ以外の文言の変更。タブの文字も変えていない。
- タブ専用に図柄を簡略化した 16px・32px の画像。タブの実寸では細部が潰れるという意見があったが、今回の要望はアプリに転送と同じロゴにそろえることなので、画像は 1 枚のままにした。

## 確認したこと

- 検証PC の Unity 2022.3.22f1 で、変更前（develop）と変更後の **VRM書き出し** のタブを撮り比べた。変更前は日本語・英語・開き直し・アプリに転送（通常）・Unity の再起動後のどれも文字だけだった。変更後はどれもロゴが付いた。再起動後は、保存済みのレイアウトから戻ったウィンドウに `OnEnable` でロゴが付き直した。
- 同じ検証PC で EditMode テスト（`ExperimentalExpressionCaptureTests`・`DependencyStartupTests`・アプリに転送のテスト）を回した。389 件のうち 384 件が成功、1 件がスキップで、言語別のタブのテストと `WindowIconSurvivesLayoutRestoreAndScriptReload` は成功した。失敗した 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` で、検証用のプロジェクトに任意の SDK が入っていないことによるもので、今回の変更とは関係がない。
- `WindowIconSurvivesLayoutRestoreAndScriptReload` では、レイアウトから戻ったタブにロゴが付き直すこと、ウィンドウを閉じてもロゴが破棄されないこと、スクリプトの再読み込み後も同じテクスチャを使うことを確かめる。起動時の確認（`ExporterStartupProbe`・`DependencyStartupTests`）でも、メニューから開いたタブにロゴがあることを見る。
