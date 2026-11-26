# 「自動で軽量化する」と「メッシュをまとめて表示を速くする」を「軽量化」の 3 段にまとめる

- 日付: 2026-11-23
- 対象: `Editor/ExportSettings.cs`（`LightweightLevel`・`LightweightLevels`）・`Editor/ExportSettingsStore.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`（`DrawLightweight`）・`Editor/Locales/*.json`・`Tests/Editor/ExportSettingsTests.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`・`Tests/Editor/ExportMeshMergeTests.cs`・`Tests/Editor/TexelDensityTests.cs`

## 背景

表情ボードの 1 ページ目に、軽量化の項目が 2 つ並んでいた。

- 「自動で軽量化する」: テクスチャ・頂点データを小さくする（`2026-11-09-auto-lightweight-default-on.md`）。
- 「メッシュをまとめて表示を速くする」: Avatar Optimizer（AAO）を使うアバターだけに出し、切り替える物と顔のほかのメッシュを結合する（`2026-11-17-aao-auto-mesh-merge.md`）。

2 つは「どこまで軽くするか」という 1 つの選択で、メッシュの結合だけを選ぶ使い方は無い。AAO の有無で行が増えたり減ったりもする。

## 決定

「軽量化」の 1 行にし、「なし・標準・最大」から選ぶ（段の名前は mk が決めた）。

- なし: 何もしない。テクスチャもメッシュも元のまま書き出す。
- 標準: 今までの「自動で軽量化する」がオンのときと同じ。
- 最大: 標準に加えてメッシュを結合する。結合は AAO を使うアバターでだけ効く。
- 既定は標準（当初は最大にしたが、2026-11-26 に mk の指示で標準に変えた。下の「既定を標準に変える」）。結合に失敗したときに結合だけを外して書き出し直す仕組みは変えない。

見た目は、ラベルの右に同じ幅の 3 つのボタン（`GUILayout.Toolbar`・240pt 固定・ボタンは 80pt）を置き、下に選んだ段の説明を 1 行出す。mk が 3 案（ボタン・ドロップダウン・ラジオボタン）から選んだ。ボタンとラベルの幅は文字の長さとフォントで変えず、入らない文字は `Fit` で切る。

### AAO を使わないアバターの「最大」

行の数と位置は AAO の有無で変えず、最大も選べるようにする。選ぶと説明に「このアバターでは標準と同じです（Avatar Optimizerなし）」と出す。

- 選べなくする（グレーにする）と、最大を選んだまま押せない段ができ（最大を既定にしていたときは、AAO の無いアバターがみなそうなる）、どの段で書き出すのかが分からなくなる。
- 書き出しで結合するかどうかは `ExportMeshMerge.Apply` が AAO の有無を見て決めるので、AAO の無いアバターで最大を選んでも書き出す中身は標準と同じになる。
- 最大を選んだアバターは、AAO を後から入れても選び直さずに結合が効く。

### 保存と読み込み

`ExportSettings.json` のアバターごとのエントリに `Lightweight`（0: なし・1: 標準・2: 最大）を足す。保存形式のバージョンは上げない（上げると古いファイルが「別のバージョンのファイル」として保存を止める）。

- `Lightweight` があり 0〜2 のとき: その段。
- 無いか範囲の外のとき: 前のバージョンの `AutoLightweight`・`MergeMeshes` から 1 回だけ読み替える。`AutoLightweight` が false ならなし、true で `MergeMeshes` が false なら標準、どちらも true なら最大。キーが無いものは誰も選んでいないものとして読む: `AutoLightweight` は true、`MergeMeshes` は false で、標準になる。
- 書くときは `Lightweight` と一緒に `AutoLightweight`・`MergeMeshes` も段に合わせて書く。前のバージョンに戻しても同じ選択で書き出せる。

アプリに転送（かんたん）は、これまでどおり表情ボードの既定値（`ChangeAvatar` と `Prepare`）を通るので、表情ボードを開いたまま書き出すのと同じ段で書き出す（`QuickTransferMatchesDefaultNormalTests`）。

## 既定を標準に変える（2026-11-26）

1.16.1 のリリースの前に、mk の指示で既定を最大から標準に変えた。

- 新しいアバター（エントリが無い）と、表情ボードを別のアバターに替えたとき（`ChangeAvatar`）は標準で始まる。かんたんも同じ既定を通る。
- 前のバージョンからの読み替えは、キーが無いときの扱いだけを変えた。`MergeMeshes` の既定を false にし、キーが無いエントリを標準として読む。1.15.2 までにリリースしたバージョンは `AutoLightweight` だけを書いていたので、そこから上げたアバターは、オフにしたものはなし、それ以外は標準で始まる。
- `AutoLightweight` と `MergeMeshes` がどちらも true で `Lightweight` の無いエントリは、リリースしていない develop のバージョンだけが書く。最大を選んだものとして最大のまま読む。

## 考えたが選ばなかったもの

- ドロップダウン 1 つ: 行は短いが、開くまで他の段が見えない。
- 3 行のラジオボタンと各段の説明: 段の違いは読みやすいが、1 ページ目が 2 行ぶん長くなる。
- AAO の無いアバターで最大を隠す・グレーにする: 行の見た目がアバターで変わる。選んである最大を押せない状態が生まれる。
