# かんたんの書き出しでリップシンクの枠を記述子の viseme で埋める

- 日付: 2026-10-20
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionNameInferenceTests.cs`

## 背景

10 体の書き出し計測で、かんたんの書き出しだけ VRM の口の形（`aa`・`ee`・`ih`・`oh`・`ou`）が空になるアバターがあった。詳細の書き出し（表情ボード）は、VRChat のアバター記述子の Lip Sync（`VisemeBlendShape` のときの `VisemeSkinnedMesh` と `VisemeBlendShapes`）から口の形を埋める。かんたんの書き出しは `VrmExpressionBindings.AddMissing` の名前の推定（`vrc.v_aa`・`あ` などの既知の名前）だけで埋めていたので、記述子の viseme の BlendShape 名が既知の名前に合わないアバターでは空のまま書き出していた。

## 決定

`AddMissing` が口の形を埋める順番を次の 3 段にする。

1. GLB に既にある口の形（UniVRM の設定・明示の割り当て）はそのまま残す。
2. 空の枠は、記述子の Lip Sync の viseme（`aa`=10・`ee`=11・`ih`=12・`oh`=13・`ou`=14 の枠）で埋める。表情ボードと同じ `DescriptorVisemes` を使う。
3. それでも空の枠だけ、今までどおり既知の名前で推定する。

viseme の BlendShape は、`VisemeSkinnedMesh` と同じ名前のノードのメッシュに割り当てる。その名前のノードが GLB に無いとき（最適化ツールがメッシュを結合・改名したとき）だけ、その BlendShape を持つメッシュが GLB の中に 1 つだけなら、それを使う。同じ名前のノードがあってもその BlendShape を持たないとき、候補のメッシュが複数あるとき、1 つのメッシュの中で名前が重複するとき、morph target の数が合わないときは、どれを動かすか決められないので記述子からは埋めず、3 の名前の推定に回す。

記述子から埋めた枠は、名前の推定と同じく書き出し詳細に警告を出す。文言は Codex への相談で「割り当て」「動作」が何を指すか曖昧という指摘を受け、使った BlendShape 名を添えて「書き出したVRMで口の動きを確認してください」とした。

名前でノードを探すのは、変換後のノードとレンダラーの対応（`converter.Nodes`）がこの段では渡っていないため。同じ名前のノードが複数あり、その BlendShape を持つものが 2 つ以上あるときは決めずに名前の推定に回すので、今までより悪くはならない。

## 確認

- `ExpressionNameInferenceTests` に、記述子の viseme 名が既知の名前に合わない場合・既存の口の形が記述子より優先される場合・記述子の viseme が使えないとき名前の推定に戻る場合・Lip Sync のメッシュかその BlendShape を持つ唯一のメッシュに割り当てる場合（Lip Sync のメッシュが BlendShape を持たないときに別のメッシュへ回さないことを含む）の 4 件を足した。
- EditMode で `ExpressionNameInferenceTests` と `ExportFallbackConfirmationTests`（97 件）、表情・メニュー・書き出し結果まわり（170 件）が通った。

## develop との統合（2026-10-21）

develop の #223（`2026-10-19-descriptor-visemes.md`）は、書き出し用のコピー（NDMF の処理後）の記述子を `VrChatVisemeExport` で読み、`converter.Nodes` で `VisemeSkinnedMesh` の出力のノードを決める。表情を記録して書き出すときも、記述子を外す前に `VrChatVisemeExport.Captured` に取っておき、最適化ツールの後まで追いかける。統合後はすべての書き出しでこちらだけを使い、この記録で足したメッシュの名前で探す割り当て（`visemeRenderer`・`visemeShapes` と、BlendShape を持つ唯一のメッシュへ回す処理）は消した。

- 口の形は Face Mesh の出力のノードだけに割り当てる。Face Mesh が出力に無いときは、その BlendShape を持つメッシュが 1 つだけあっても割り当てず、「Face Mesh が含まれていない」の警告を出す。
- この記録の決定 3 は残す。Face Mesh がその BlendShape を持たないとき（名前が重複するときを含む）は、記述子からは埋めず、既知の名前の推定に回す。それでも埋まらなければ develop の「指定した変形が書き出したメッシュにありません」の警告を出す。
- 記述子から埋めた枠には警告を出さない（Face Mesh に作者が指定した変形なので、確認を求める必要がない）。使った BlendShape 名を添える警告文と、その 4 言語の訳は消した。
- morph target の数が合わないときは、develop と同じく例外にする。
- `ExpressionNameInferenceTests` の 4 件は、メッシュの名前の代わりに Face Mesh の出力のノードを渡す形に直した。記述子から埋めたときの警告を確かめていた所は警告が出ないことを、Face Mesh が無いとき唯一のメッシュへ回すことを確かめていた所は割り当てず警告を出すことを確かめるように変えた。
- 寛容な書き出し（失敗しても続ける書き出し）で Lip Sync のメッシュを BlendShape の名前で追いかけるときは、ビルド処理の前に元のメッシュにあった名前だけを使う。古い名前が 1 つ残っているだけで 5 つの口の形がすべて外れないようにするため。
- 名前はビルド処理の後のメッシュではなく、処理の前に `NdmfExportPreparation` が記録した名前で確かめる。メッシュの名前を変えて Renderer を作り直す処理や、別のメッシュへ統合して元を消す処理の後では元の Renderer が破棄されていて、処理の後のメッシュで確かめると名前がすべて外れ、口の形が書き出されなかった。同じ理由で、破棄された Renderer も追いかけの対象に渡す。`NdmfLostMorphFollowTests` で、名前の変更・統合の後も口の形が移動先のメッシュに付くことと、Face Mesh に無い名前の口の形だけが外れることを確かめる。
