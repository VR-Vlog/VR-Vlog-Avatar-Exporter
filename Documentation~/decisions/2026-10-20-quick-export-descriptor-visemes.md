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

viseme の BlendShape を持つメッシュは、`VisemeSkinnedMesh` と同じ名前のノードが 1 つだけあり、そのメッシュが BlendShape を持っていればそれを使う。無ければ、その BlendShape を持つメッシュが GLB の中に 1 つだけのときに限ってそれを使う。複数のメッシュが同じ名前の BlendShape を持つときや、1 つのメッシュの中で名前が重複するときは、どれを動かすか決められないので記述子からは埋めず、3 の名前の推定に回す。

記述子から埋めた枠は、名前の推定と同じく「動作を確認してください」の警告を書き出し詳細に出す。

## 確認

- `ExpressionNameInferenceTests` に、記述子の viseme 名が既知の名前に合わない場合・既存の口の形が記述子より優先される場合・記述子の viseme が使えないとき名前の推定に戻る場合・Lip Sync のメッシュかその BlendShape を持つ唯一のメッシュに割り当てる場合の 4 件を足した。
- EditMode で `ExpressionNameInferenceTests`（85 件）と表情・メニュー・書き出し結果まわり（170 件）が通った。
