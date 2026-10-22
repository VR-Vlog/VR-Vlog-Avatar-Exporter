# 表情ボードの一覧をそのまま VRM の表情として書き出す

- 日付: 2026-10-22
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/VrChatExpressionMenu.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Editor/FaceEmoMergedUnderlayTests.cs`

## 背景

これまでの書き出しは、表情メニュー・Gesture・FX をアバターの複製で再生して、どの操作でどの顔になるかを確かめてから表情にしていた。再生に時間がかかり（表情の多いアバターで1回の書き出しが数十秒〜5分）、メニューが一覧より少ないときは記録する経路に切り替える処理も要っていた。一方、表情ボードを開くと最初に「その他の表情」の一覧が出ていて、ユーザーはそれを見て書き出す表情を決めている。

一覧をそのまま表情にする試作を作って develop と同じアバター（9件）で比べ、オーナーが 2026-10-22 にこの方向で進めると決めた。

## 決定

- 書き出し（**アプリに転送（かんたん）**・**アプリに転送（通常）**・**VRMをPCに保存**）は、表情メニュー・Gesture・FX を再生しない。表情ボードの「その他の表情」の一覧にあるクリップを、そのまま VRM の custom 表情にする。喜怒哀楽などの枠と基本の顔は、これまでの表情ボードの既定どおり。
- 一覧の各クリップは、ジェスチャーの表情と同じ `VrChatGestureExpressions.ReadClip` で読む。そのあとは今までと同じく `VrChatExpressionBaker` → `VrmMenuExpressions.Add` で書く。時間で値が変わるクリップは `VRVLOG_expression_animations` にアニメーションとして入り、静止した表情は最後のコマになる。
- Radial Puppet のように Motion Time の状態だけが再生するクリップは、メニューの書き出しと同じく1コマ（表情ボードのコマ）で止める。Motion Time でない状態も同じクリップを再生するときは、動く表情のまま。
- `ReadClip` が受け付けないクリップ（表情以外の変更を含むものなど）は、表情ボードのコマの BlendShape の値で静止した表情にする。表情の BlendShape を1つも含まないものは書き出さない。
- Modular Avatar などのビルドツールが動かしたメッシュの表情は、表情ボードの喜怒哀楽の枠と同じく、動かした先のメッシュで読む。
- FaceEmo に登録されたクリップは、FaceEmo の表情と同じ読み方をする。FaceEmo の共通の DEFAULT FACE と常時の上書きが、これまでどおり下に入る。
- 一覧の表情は出どころを gesture（FaceEmo に登録されたクリップは faceemo）として拡張に書く。
- 表情ボードの書き出しの欄の文言は「この一覧をそのまま書き出し」にした。

## 比べた結果

develop と同じプロジェクトで、9件（同じアバターのプレハブとシーンを含む）と、比較に使っていない有償のアバターを含む8件を書き出して比べた。

- 両方にある表情は、すべて頂点の位置の差が 0.5 mm 未満。基本の顔もすべて一致した。
- 両方にあるアニメーションは、長さ・ループ・全チャンネルのキーと補間が develop と一致した。
- 書き出しの時間は、表情の再生がなくなった分だけ短くなった（最も長いアバターで 296 秒 → 43 秒）。
- develop では出ず、一覧にはある表情（衣装・髪の ON/OFF、頬・汗・涙など顔の部品、メニュー外の FX の表情）が増える。メニューと FX の組み合わせから develop が作っていて一覧にない表情は出なくなる。

## 残したこと

- 表情ボードの帯の「表情メニューの{0}項目はVRMに入らないことがあります」は、一覧に出ないメニューの表情は入らないので、正確には「入りません」になる。文言は別に直す。
- 一覧に同じ名前のクリップが複数あると、保存件数の表示が VRM の表情数より多くなる。
- 記録する経路と、メニューの再生の処理は残っている（使われない）。消すのは別の変更で行う。
