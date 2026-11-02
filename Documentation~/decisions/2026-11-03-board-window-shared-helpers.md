# 表情ボードと周りの画面が写し合っていたプレビューの設定・色・設定ファイルの読み書きを 1 か所にする

## 背景

表情ボードのウィンドウ（ExperimentalExpressionCaptureWindow）は 2,800 行あり、次のものが他の画面と同じ内容で書き写されていた。

- プレビューのカメラとライトの 9 項目（視野角 30・near .01・far 100・背景色・ライト 2 本・環境光）が、ボード・瞬きのプレビュー・除外オブジェクトのプレビュー・復旧の比較ウィンドウの 4 か所にあった。頭に寄せる式も瞬きとボードに 2 つあり、ボードだけ `animator.avatar.isValid` を見ていなかった。
- 画面ごとの赤・アクセント・補助文字の灰・罫線の色が 10 か所に書かれ、アプリに転送のスタイルシートにも同じ青が 3 か所あった。
- 書き出し設定（ExportSettingsStore）と表情ボードの選択（ExpressionPresetStore）のファイルの読み取り（存在・4 MiB 上限・空・版・壊れ）と書き込み（上限で古いものを捨てる・フォルダを作る・置き換える）が 2 つのストアに同じ形であった。
- ファイルを置き換える `AtomicWrite` がボードのウィンドウの静的メソッドで、ストアがウィンドウの型を通して呼んでいた。
- 常に真の `#if UNITY_5_3_OR_NEWER` と、どこからも読まれない `stepLayout` が残っていた。

## 決定

- `PreviewRendering.Apply` と `PreviewRendering.FrameHead` にまとめ、4 か所はそれを呼ぶ。頭の判定は瞬きに合わせて `isValid` を見る（無効な Avatar で `isHuman` が真のときだけ、頭でなく全体を写す）。ポーズの確認ウィンドウは値が違うのでそのまま。
- 色は `ExporterColors` の 5 つ（Accent・Error・TransferError・Muted・Hairline）にまとめる。値は 1 つも変えず、赤 2 種・アクセント 2 種は意図した違いなのでそれぞれ残す。スタイルシートの青は `--accent` の変数にする。
- 読み取りと書き込みの骨格を `UserSettingsFile.Read<T>` と `UserSettingsFile.Write` にまとめる。Save の中身・Revision の増やし方（ExportSettingsStore は書き込めたときだけ、ExpressionPresetStore は先頭で無条件）・警告の文言・ロック時の扱いはストアごとのまま。ExpressionPresetStore の書き込みを try で包む変更は挙動が変わるのでしない。
- `AtomicWrite` は `ExportSave` に移す。
- ボードの Save（約 130 行）を外に出す案は採らない。Save はウィンドウの状態 18 個を読み書きして Repaint まで呼ぶので、静的関数にしてもウィンドウのメンバーを internal に広げた結合が残り、行数も減らない。
- 常に真の `#if` と `stepLayout` は消す。ステップ表示の見た目は変えない（2026-10-22 の中心ずれ 0.5 px 以内の決定を守る）。

## 確かめたこと

- 色は、C# 側は変更前後で 10 か所の値が同じこと、スタイルシート側は Unity が解決した値がダーク・ライトとも同じことを見た。
- 全 EditMode と、ホストのハーネス、書き出しの回帰比較は PR 本文に書く。
