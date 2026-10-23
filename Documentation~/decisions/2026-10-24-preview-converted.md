# 表情ボードのプレビューで、書き出すVRMを表示できるようにする（試作）

- 日付: 2026-10-24
- 対象: `Editor/ExportedLookPreview.cs`（新規）・`Editor/ExperimentalExpressionCaptureWindow.cs`（`DrawPreview`・`BuildExportedLook`・`LoadExportedLook`・`ReleaseExportedLook`・`BoardListFaces`）・`Editor/ExportRecoveryComparisonWindow.cs`（`NormalizeForPreview` を `internal` に）・`Editor/Locales/*.json`・`Tests/Editor/ExportVrmLilToonPreviewTests.cs`・`README.md`

## 背景

表情ボード1ページ目のプレビューは、表情を確かめるための所有コピー（`ExperimentalExpressionCaptureSession.Copy`）をそのまま描いている。元のアバターと同じlilToonの材質とメッシュで、書き出しで行う処理（NDMF・AAO、ギミックの除外、Modular Avatarの見た目、未対応シェーダーの置き換え、メインテクスチャの焼き込み、瞬きと基準の顔の焼き込み）はどれも通っていない。そのため、lilToon以外のシェーダーを使った髪や服が書き出しでどう変わるかは、書き出してアプリかほかのビューアーで開くまで分からなかった。

書き出しの変換は `UniVrmOneClickExporter.ExportCore` の中で、書き出し用の別のコピーに対して行い、`Vrm10AppearanceExporter.Export` の直前で終わる。この時点のコピーの材質は標準のビューアー向けのMToonの近似で、アプリが使うlilToonの材質はVRMの拡張に別に入る。

## 決定

- プレビューの見出しの右に **アバター｜書き出すVRM** の切り替えを置く。**書き出すVRM** を押すと、**VRMを保存** と同じ呼び出し（`ExportAndSaveNormally` に渡す `Export` と同じ引数・`ExportOnTheAvatarAsItIs`・`PoseLimitChooser.Scope`）でファイルに保存せずに1回書き出し、そのバイト列を `Vrm10.LoadBytesAsync` で読み込み直して描く。書き出しの途中のコピーを横取りせず、書き出したバイト列を読む。アプリが受け取るのと同じファイルで、拡張に入ったlilToonの材質も含むため。
- 読み込んだVRMには `ExportVrmLilToonPreview.Apply` で拡張のlilToonの材質を戻す（書き出し結果の比較ウィンドウと同じ経路）。拡張が無い・戻せないときは、アプリと同じくMToonのまま描き、見出しに「・MToonで表示」と理由のツールチップを出す。
- 書き出しはボードの設定で1回分の時間がかかるので、開いたときには作らず、押したときだけ作る。作った時刻を見出しに出し、設定を変えたあとは **作り直す** で作り直す。
- 作ったあとに表情・設定が変わったら、見出しを「（01:40時点・変更は未反映）」にする。比べ方は転送が保存済みのファイルを送り直してよいか確かめるとき（`AppTransferExport.WrittenSource`）と同じで、2つの保存先の `Revision` とボードの `InputsKey` を作ったときの値と比べる。シーンのアバター自体の編集は見ない（毎回の描画で比べるには重いため）。
- 見出しは「基準の顔」から始め、「書き出すVRM」は繰り返さない（切り替えに出ているため）。
- 書き出しは転送と同じく、失敗した段を飛ばして続ける（`ExportStageGuard.ContinueByDefault`）。転送で送られるものに近い見た目を出すため。
- 表情を押すとアバターの表示に戻る。読み込んだVRMは基準の顔で、表情の再生はしない。書き出さないオブジェクトを反映したとき・ボードを閉じたとき・アバターを作り直したときは、読み込んだVRMを破棄する。
- 読み込んだVRMは `HideAndDontSave` で、シーンにもアセットにも残らない。元のアバターとアセットは書き換えない。

## 確認

- `ExportVrmLilToonPreviewTests.TheBoardsExportedLookShowsTheFilesLilToonMaterialsAndDestroysAllOfIt` で、読み込んだVRMの材質が元と同じlilToonのシェーダーと色になること、元の材質は変わらないこと、`Dispose` で読み込んだVRMと戻した材質が残らないことを確かめる。
- Unity 2022.3.22f1で、表情ボードのプレビューを切り替える前と後で撮って比べた。

## アプリとの差

- 戻したlilToonの材質は、Unityに入っているlilToon（Built-in版）で描く。アプリはlilToon 2.3.4のURP版で描くので、プロジェクトのlilToonが2.3.4でないときや、URPとBuilt-inで違う部分（ライトの扱い・影）は差が残る。
- ライトはプレビューの固定のライトで、アプリのAR（カメラ映像からの明るさの推定）とは違う。揺れものは動かさない。

## 検討して採らなかった案

- 開いたときに自動で書き出して常に表示する案は採らなかった。アバターによっては書き出しに数十秒かかり、表情の割り当てのたびに作り直すと操作が止まるため。
- `ExportCore` の途中（`Vrm10AppearanceExporter.Export` の直前）のコピーをそのまま見せる案は採らなかった。そこでの材質は標準のビューアー向けのMToonの近似で、アプリの見た目ではないため。
- 押したときに切り替える前に「生成」ボタンと待ち時間の注意を挟む案は採らなかった。書き出し中は進捗バーが出て操作を受け付けず、多重に走らないため。
- 表情を押すとアバターの表示に戻ることを表情一覧の近くに書く案は採らなかった。切り替えの選択がアバターに変わって見えるため、文言を増やさない。
- 別のウィンドウで開く案は採らなかった。書き出し結果の比較ウィンドウが既にあり、ボードの中で同じ角度・同じ拡大率のまま切り替えられるほうが違いが分かりやすいため。

## 欠点

- 表示中に表情や設定を変えても自動では作り直さない。見出しの「変更は未反映」と **作り直す** で分かるようにした。シーンでアバターを直接編集したときは「変更は未反映」が出ない。
- 書き出し1回分のメモリを読み込んだVRMが使う（アバターの表示に戻しても残り、ボードを閉じるか除外を反映するまで）。
