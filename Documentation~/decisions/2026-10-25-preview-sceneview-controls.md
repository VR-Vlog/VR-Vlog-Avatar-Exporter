# 全身プレビューの操作を Unity の Scene ビューに合わせる

- 日付: 2026-10-25
- 対象: `Editor/ExcludedObjectsPreview.cs`・`Editor/QuickTransferPreview.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/LanTransfer/AppTransferFlow.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExcludedObjectsPreviewTests.cs`・`README.md`・`CHANGELOG.md`

## 背景

**アプリに転送（かんたん）** の「プレビュー」は、左ドラッグでアバターの縦軸まわりに回すこととホイールでの拡大しかできず、上下から見る・顔に寄る・ずらすことができなかった。2026-10-25 13:18 に mk さんから「UnityのSceneビューと同じ操作体験にしてほしい」と指示があった。この欄は表情ボードの **除外するオブジェクト** のプレビューと同じ `ExcludedObjectsPreview` を使っている。

## 決定

- 割り当ては Unity 2022.3 の Scene ビューに合わせる。一次情報はマニュアルの Scene view navigation と、UnityCsReference 2022.3 の `SceneViewMotion.cs`（`ClutchShortcut` の割り当てと各操作の計算）。
  - Alt+左ドラッグ: pivot のまわりを周回する。横はワールドの上向きのまわり、縦はカメラの右向きのまわりに 0.003 rad/pt 回し、上下の制限は付けない。
  - 中ドラッグ・Alt+中ドラッグ・Alt+Action+左ドラッグ: 平行移動。Action は `EditorGUI.actionKey`（Windows は Ctrl、macOS は Command）。Shift で 4 倍。
  - 右ドラッグ: カメラの位置を変えずに見回す。
  - Alt+右ドラッグと macOS の Option+Control+左ドラッグ: 拡大縮小（`HandleUtility.niceMouseDeltaZoom` を使い、Shift で 3 倍）。
  - ホイール: 1 目盛りの delta あたり 1.5% 寄る。マウスの下の点へ向かって寄り、Alt+ホイールは中心へ寄る。
  - F: 全身が収まる距離と pivot に戻す。向きは変えない（Scene ビューの F は選択物を画面に収める操作で、向きを変えないため）。マウスがプレビューの上にあり、テキスト入力中でないときだけ受ける。
- 修飾キーなしの左ドラッグも周回にする。Scene ビューでは範囲選択だが、ここには選ぶものが無い。Alt+左ドラッグと同じ計算なので、上下にも回るようになった（これまでは縦軸まわりだけ）。
- ドラッグは `GUIUtility.hotControl` を取り、プレビューの外へ出ても続く。ボタンを決めるのはボタンを押したとき（Scene ビューと同じ）。
- カメラは向き・pivot のずれ・全身が収まる距離に対する倍率で持つ。欄の幅や高さが変わっても全身が収まる距離から計算し直す。除外の切り替えではカメラを動かさない。表情ボードとかんたんのプレビューはそれぞれ別にカメラを持つ。
- 欄の下の案内は「ドラッグで回転・中ドラッグで移動・ホイールで拡大」にする。修飾キーと F を含む一覧は、プレビューにマウスを重ねたときの tooltip に出す。キーの名前は macOS では Option・Command、それ以外では Alt・Ctrl にする。
- 表情ボードの **除外するオブジェクト** のプレビューも同じクラスなので同じ操作になる。分けると特例が増えるため分けない。1 ページ目の表情のプレビューは別のカメラで、顔を見るための左右 ±90° の回転だけなので今回は変えない。

## 確認

- `TheButtonsAreTheSceneViews`: ボタンと修飾キーの組み合わせが Scene ビューと同じ操作になること（Windows の Alt+Ctrl+左は移動、macOS の Option+Control+左は拡大）。
- `TheCameraMovesAsInTheSceneView`: 周回で pivot と距離が変わらないこと、見回しでカメラの位置が変わらないこと、平行移動で高さいっぱいのドラッグが pivot の距離での表示の高さだけ動くこと、Alt+ホイールでは pivot が動かずマウスの位置では寄る方へ動くこと、F で向きを保って全身に戻ること、除外の切り替えでカメラが動かないこと。
- 単純な図形だけで作ったテスト用のアバターで、表示倍率300%の修正前と修正後を撮って比べた。

## 検討して採らなかった案

- WASD・QE での移動（Flythrough）と矢印キー: 1 体のアバターを見るには周回・移動・拡大で足り、小さい欄でキーのフォーカスを取ると隣の入力欄と取り合う。案内の文言で「Sceneビューと同じ」と言い切ると、これらも使えると思わせるので書かない。
- F で正面に戻す: 背面を見ている途中で向きまで戻ると、見ていた向きを失う。Scene ビューの F も向きを変えない。
- 「正面に戻す」「操作方法」のボタンを欄に足す（Codex の提案）: 欄の高さと行の数が変わる。一覧は tooltip に置き、向きはアバターを選び直すと正面に戻る。
- 案内を 3 行に分ける（Codex の提案）: 欄の下の行の高さが変わり、プレビューの面積が減る。1 行で入らないときは省略し、全文は tooltip に出す（#499 と同じ）。
