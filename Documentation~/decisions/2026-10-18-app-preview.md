# 書き出したVRMをアプリの読み込みパッケージで開いて見え方を確かめる

- 日付: 2026-10-18
- 対象: `Editor/AppPreview/`（`VRVlog.LilToonExporter.AppPreview` アセンブリ）・`Editor/AppPreviewEntry.cs`・`Editor/AppPreviewWindow.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/ExportRecoveryComparisonWindow.cs`・`Editor/AssemblyInfo.cs`・`Editor/Locales/*.json`・`Tests/AppPreview/`・`Tests/Editor/AppPreviewWindowTests.cs`・`Tests/Editor/AssemblyInfo.cs`

## 背景

書き出し画面の比較表示は、UniVRM で読み直した VRM に lilToon の材質情報を当て直して描いている。アプリが実際にどの材質を lilToon で描き、どれを MToon へ落とすか、表情・まばたき・揺れものがアプリでどう動くかは、スマホへ送って読み込むまで分からなかった。

## 決定

- VR Vlog アプリの読み込みパッケージ (`com.vrvlog.avatar-runtime`) の `AvatarRuntimePreview` で VRM を開く。アプリと同じ読み込み処理・材質の判定・表情・揺れものの処理を通すので、アプリでの結果と同じ判定が出る。
- lilToon はアプリの URP 版シェーダーではなく、プロジェクトに入っている公式の lilToon Built-in 版シェーダーで描く。この拡張機能のプロジェクトは Built-in のレンダーパイプラインで、URP のシェーダーは描けない。描き方の細部（ライティングなど）はアプリと違うことがあるが、どの材質が lilToon で描かれるかの判定はアプリと同じになる。
- 材質の表示は 2 通りにする。
  - 「アプリと同じ」(`AvatarPreviewMaterialMode.App`): アプリの lilToon 専用表示と同じく、VRM の lilToon の材質情報を使う。
  - 「MToon のみ」(`AvatarPreviewMaterialMode.StandardMToon`): lilToon の材質情報を使わず、標準の VRM の MToon で描く。lilToon 専用表示をオフにしたアプリと同じ見え方になる。
  - 切り替えたら読み込み直す。パッケージが返す材質の結果 (`MaterialStatus`) は短い日本語にして表示し、元の値はツールチップに置く。lilToon の版が 2.3.4 でなければ警告色で知らせる。
- 読み込みはパッケージが読み込みのあいだだけ全体のシェーダーの差し替えを行うので、ウィンドウが複数あっても 1 件ずつ順に行う。中止したときやウィンドウを閉じたあとに届いた結果は、その場で破棄する。
- 自動のまばたき（4 秒ごとに 0.1 秒で閉じ、0.05 秒止めて 0.15 秒で開く）は、この画面だけの確認用の信号で、アプリのまばたきとは関係ない。`SetEyeBlink` で両目に同じ値を渡す。
- 表情・表情アニメーション・揺れものは Play モードを使わず、`EditorApplication.update` からエディタの経過時間で毎フレーム `Tick` を呼んで進める。経過時間は 1/15 秒で頭打ちにし、0 秒では呼ばない。ウィンドウが見えないあいだは止める。「揺れものを動かす」をオフにすると `SetSpringBonesEnabled(false)` で揺れものだけを止め、1 回だけ初期状態に戻す。表情は `Tick` で動き続ける。
- 読み込んだアバターと描画用の `PreviewRenderUtility` は、読み込み直し・ウィンドウを閉じたとき・ドメインの再読み込み・Play モードの切り替えで破棄する。アバターは読み込み結果（`IAppPreviewSession`）が持つので、先に読み込み結果を破棄してから `PreviewRenderUtility` を片付ける。
- 読み込みパッケージは任意の依存にする。このパッケージは一般には配布していない。
  - パッケージとの境界は 1 か所に置く。パッケージを参照するソースは `Editor/AppPreview/AvatarRuntimeBackend.cs` だけで、`VRVlog.LilToonExporter.AppPreview` アセンブリに入る。このアセンブリは `versionDefines` の `VRVLOG_AVATAR_RUNTIME` が無ければコンパイルしない。
  - 画面（`Editor/AppPreviewWindow.cs`）は常にコンパイルされる Editor アセンブリに置き、`Editor/AppPreviewEntry.cs` の `IAppPreviewBackend`・`IAppPreviewSession` だけを使う。`AvatarRuntimeBackend` は `[InitializeOnLoadMethod]` で `AppPreviewEntry.Backend` に自分を登録する。読み込みを 1 件ずつ順に行う処理と、材質の表示 2 通りをパッケージの `AvatarPreviewMaterialMode` へ対応づける処理もこのファイルに置く。
  - `AppPreviewEntry.Backend` が null のときは、メニューの項目を選べなくし、書き出し画面の保存後のボタンを出さない。画面が開いたままなら「VR Vlog アプリの読み込みパッケージがありません」と出して何も読み込まない。パッケージが無いプロジェクトでも書き出しは今までどおり動く。
  - パッケージの置き場所や名前が変わったときは、`AvatarRuntimeBackend.cs` とアセンブリ定義（`Editor/AppPreview/VRVlog.LilToonExporter.AppPreview.asmdef`）だけを直す。

## 確認すること

- `Tests/Editor/AppPreviewWindowTests.cs`（EditMode）で、偽の `IAppPreviewBackend` を登録して、パッケージが無くても次を確かめる。
  - `MaterialStatus` とエラーコードから表示する文言への対応、自動のまばたきの閉じ具合。
  - `AppPreviewEntry.Backend` が null のとき、メニューの項目が選べず、画面は何も読み込まない。
  - 読み込んだ結果を画面がつなぎ、揺れものを有効にし、閉じたときに破棄する。閉じたあとに届いた結果も破棄する。読み込みの失敗と例外はエラー表示になる。
- `Tests/AppPreview/AppPreviewTests.cs`（EditMode、パッケージがあるときだけコンパイル）で、登録された `AvatarRuntimeBackend` を通して、lilToon の材質・表情・手動のまばたき・揺れものを持つ小さなアバターを書き出し、パッケージで読み込んで次を確かめる。
  - 「アプリと同じ」で `MaterialStatus` が `full-liltoon:` で始まり、「MToon のみ」で `standard-mtoon` になる。
  - `SetExpression` と `SetEyeBlink(1, 1)` で BlendShape の値が変わる。
  - ルートを動かして `Tick` を呼ぶと、揺れもののジョイントがルートに対して動く。
  - ウィンドウを開いて読み込み、閉じたあとに隠しオブジェクトが残らない。
