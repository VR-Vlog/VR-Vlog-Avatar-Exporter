# 書き出すVRMを確認の画面で読み込んだ VRM を、シーンを開いても消さず、閉じたら残さない

- 日付: 2026-11-03
- 対象: `Editor/ExportRecoveryComparisonWindow.cs`・`Tests/Editor/ExportRecoveryComparisonTests.cs`

## 背景

アプリに転送の画面のフォントがシーンを開くと壊れた件（2026-11-02-app-transfer-font-lifetime）を受けて、Exporter がエディタで作って C# の変数からだけ参照しているオブジェクトを全部洗い出した。シーンを開く・`Resources.UnloadUnusedAssets`・ドメインの再読み込み・Play モードで消えるもの、閉じても残るものを探した。

Unity 2022.3.22f1 で確かめた前提:

- シーンを開くと、`HideFlags.DontSave` を付けていないオブジェクトのうち、保存されたアセットでもなく、開いているシーンからも参照されていないものが全部消える。`ScriptableObject.CreateInstance`・`Instantiate` で作ったもの、プレビュー用のシーン（`PreviewRenderUtility`）のオブジェクトだけが参照しているもの、HideAndDontSave のオブジェクトが持っているだけのものも消える。
- NDMF を通す書き出しの下準備（表情ボード・まばたき・ポーズの確認）は、複製を処理したあと NDMF が最後に、複製が参照する保存されていないオブジェクトを一時フォルダ（`Assets/VRVlogExportTemp-<guid>`）のアセットに保存する。このため、その下準備の中で作ったメッシュ・マテリアルはシーンを開いても消えない（まばたきの確認・ポーズの確認・表情ボードの撮影・書き出さないオブジェクトの確認で確かめた）。

書き出すVRMを確認の画面だけが当てはまらなかった。書き出した VRM を UniVRM で読み直すと、アバター（Avatar）・VRM の設定（VRM10Object）・表情（VRM10Expression）ができる。これらには DontSave が付かず、プレビュー用のシーンのモデルから参照されるだけなので、画面を開いたままシーンを開くと消えていた（24 件中 20 件）。見た目のエラーは出なかった。描いているメッシュとマテリアルは別に作った HideAndDontSave のもので残るからである。ただし画面の中のモデルは VRM の設定と Avatar を失った状態になる。

もう一つ、画面を閉じても読み込んだものが残っていた。UniVRM は読み込んだメッシュ・マテリアル・テクスチャ・Avatar などを `RuntimeGltfInstance` の `OnDestroy` で消す。このクラスには `[ExecuteAlways]` が無いので、エディタ（Play モードでないとき）では `OnDestroy` が呼ばれず、ルートを消しても中身は残る。これまでは DontSave が無かったので、次にシーンを開くか使われていないアセットを片付けたときに消えていた。DontSave を付けるだけにすると、エディタを閉じるまで残る漏れになる。

さらに `ReleasePreview` は先に `PreviewRenderUtility.Cleanup` を呼んでいた。`AddSingleGO` で渡したルートはこのとき消えるので、後から読み込んだものの一覧（`RuntimeResources`）を辿れなかった。

## 決定

- 読み込みが終わったら、`RuntimeResources` にあるもののうち保存されたアセットでないものすべてに DontSave を付ける。この画面が読み込んだものであり、画面が閉じるか読み込み直すまで持つ。
- 片付けるときは `RuntimeResources` の一覧を先に取り、ルートを消してから一覧のものを消す（`DestroyLoaded`）。UniVRM が Play モードで `OnDestroy` に行うことを、エディタで画面が代わりに行う。読み込みを途中で止めたときも同じ関数を通す。
- `ReleasePreview` の順番を、lilToon の見た目の差し替えを戻す → 読み込んだものを消す → `PreviewRenderUtility.Cleanup` に変える。

## 検討した代替案と不採用理由

- DontSave だけ付けて消すのは UniVRM に任せる: エディタでは `OnDestroy` が呼ばれないので、閉じた画面の分がエディタを閉じるまで残る。
- `RuntimeGltfInstance` に `[ExecuteAlways]` を足す: 同梱している UniVRM のコードを変えることになり、更新のたびに直し直す必要がある。
- `TransferOwnership` で一覧を画面に移す: 一覧が `RuntimeGltfInstance` から消えるだけで、画面に一覧を持つ処理が増える。今の一覧を読んで消すほうが短い。
- シーンを開く前（`EditorSceneManager.sceneOpening`）に読み込み直す: 片付けの経路ごとにフックが要る。読み込んだ時点で付ければ経路を問わない。

## 影響

- 画面を開いたままシーンを開いても、読み込んだ VRM の Avatar・VRM の設定・表情が残る。
- 画面を閉じる・読み込み直すと、読み込んだものがすべて消える（テストで確かめる）。
- 書き出す VRM は変わらない。この画面は保存前の確認だけを行い、書き出す内容には関わらない。

## 洗い出しで残したもの

- `com.vrvlog.avatar-runtime` を使うプレビュー（AppPreviewWindow）は、パッケージが配られておらず検証環境に入れられなかったので確かめていない。
- 他の画面のテクスチャ（QR・アイコン・撮影の画像など）は HideAndDontSave か、作った画面が自分で消していた。

## 出典

- 同梱の UniGLTF `RuntimeGltfInstance.cs`（`OnDestroy` で `_resources` を消す・`[ExecuteAlways]` なし・`TransferOwnership`）
- Unity 2022.3 `PreviewRenderUtility.Cleanup`（`AddSingleGO` で渡したオブジェクトを消す）
