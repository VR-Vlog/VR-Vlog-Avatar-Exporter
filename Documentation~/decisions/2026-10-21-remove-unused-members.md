# 表情ボードのセッションから、どこからも使われていないメンバーを消す

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`

## 背景

表情ボードのセッション（`ExperimentalExpressionCaptureSession`）に、どこからも使われていないメンバーが 3 つ残っていた。

- `ClipInput.SelectionBeforeError`: 初期値 `true` を入れるだけで、読む箇所も書く箇所もない。
- `ClipInput.Selected`: 同じく初期値 `true` を入れるだけで、読む箇所も、オブジェクト初期化子を含めて書く箇所もない（`poses.Selected()` は別の型のメソッド）。
- `SelectCandidateByDefault`: 表情メニューの候補を最初から選んでおくかを決めるメソッド。呼び出している箇所がない。

## 決定

3 つとも消す。

## 書き出す内容が変わらない理由

- `Editor/`・`Runtime/`・`Tests/`・`Tools/`・`Website/` を名前で検索し、宣言以外に出てくる箇所がないことを確かめた。`nameof` や文字列での参照（`Tools/ExportRegressionRunner.cs` のリフレクションなど）もない。
- `ClipInput` は `[Serializable]` ではなく、表情ボードの画面でもシリアライズされない。フィールドを消しても、保存されたデータや画面の状態の読み込みには影響しない。
- `SelectCandidateByDefault` は副作用のない判定だけのメソッドで、呼ばれていないので、消しても実行される処理は 1 つも変わらない。
- 2026-10-08 以降に変更した `Editor/` のファイルをほかにも調べたが、宣言だけで参照のないメンバーは、Unity から呼ばれるもの（`[InitializeOnLoadMethod]`・`GetWindowSize` の override）を除いてこの 2 つだけだった。`ClipInput.Selected` は、最初の調査で見落としていたものをレビューで指摘されて足した。

## 確認したこと

- 確認: 検証用の PC での書き出し突き合わせと EditMode テスト（PR 本文に記載）
