# EditMode テストの待ち時間とアセットの取り込み回数を減らす

- 日付: 2026-11-03
- 対象: `Tests/LanTransfer/CloudTransferTests.cs`・`Tests/Editor/PoseRegistrationTests.cs`・`Tests/Editor/MergedFxDefaultsTests.cs`・`Tests/Editor/NeutralShapeSamplerTests.cs`

## 背景

時間のかかるテストクラスを測ると、確かめている処理そのものより次の 2 つに時間を使っていた。

- `CloudTransferTests` の再試行のテストは、セッションが再試行の前に本物の `Task.Delay` で 1 秒・2 秒待っていた。6 本で合わせて約 30 秒になる。
- `PoseRegistrationTests` は 1 ケースごとにコントローラー・メニュー・パラメーターと依存アセットを 10 個ほど `CreateAsset` して保存していた。アセット 1 個ごとに取り込みが走り、この部分だけで 1 ケース約 1 秒かかっていた。`MergedFxDefaultsTests` と `NeutralShapeSamplerTests` も、`SetUp` でフォルダーとコントローラーを作るたびに取り込みが 2 回走っていた。

## 決定

- 再試行の待ちは、セッションがもともと受け取れる `delay` 引数に、時間を置かずに呼び出し元の取り消しで終わる待ちを渡す。待ちの長さを確かめているテストは無く、どのテストも試行の回数と結果を見ている。
- アセットを作る部分を `AssetDatabase.StartAssetEditing` と `StopAssetEditing` で囲み、取り込みを最後の 1 回にまとめる。作るアセット・パス・サブアセットは変えない。比べる値（`before` の JSON など）は囲みを抜けてから取る。
- フィクスチャーをテスト間で共有することはしない。ケースごとにアセットの構成が違い、フォルダーのパスを使い回すとパスで引くキャッシュが前のテストの結果を拾うおそれがあるため。
- 製品コード（`Editor/`）とテストのメソッド名・ケースは変えない。

## 変えなかったもの

- `QuickTransferMatchesDefaultNormalTests` と `AnimatedManualPoseExportTests` の重いケースは、製品のポーズの標本化（1 フレームあたり約 8ms）に時間を使っている。上限は製品の定数で決まり、テストの側で短くすると確かめている内容が変わる。
- コントローラーやクリップをメモリ上のオブジェクトにすることはしない。製品は `IsPersistent`・`GetAssetPath`・GUID を見て処理を分けているので、テストの入力が変わる。
- `AppTransferFlowTests` の `FlowRun.Until` の 5ms の待ちは、1000 回の上限つきで別スレッドの処理を待っている。待ちを無くすと上限を先に使い切るおそれがあり、全体でも約 7 秒なので残した。

## 確かめたこと

Unity 2022.3.22f1・lilToon 2.3.4 のプロジェクトで、変更前と変更後を続けて回した（秒）。

| クラス | 件数 | 変更前 | 変更後 |
|---|---|---|---|
| CloudTransferTests | 250 | 80.7 | 57.4 |
| PoseRegistrationTests | 66 | 76.8 | 36.9 |
| NeutralShapeSamplerTests | 163 | 59.1 | 54.1 |
| MergedFxDefaultsTests | 85 | 85.0 | 93.1 |

テストの名前・件数・結果（成功 562・スキップ 2）は変更前と同じだった。`MergedFxDefaultsTests` の重いケースは書き出しに時間を使っていて、`SetUp` で減る 1 件あたり約 60ms は計測の揺れに埋もれた。
