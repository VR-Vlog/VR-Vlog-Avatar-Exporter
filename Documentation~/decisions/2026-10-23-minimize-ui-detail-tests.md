# UI の細かい文言・配置・見た目だけを確かめるテストを消す

- 日付: 2026-10-23
- 対象: `Tests/Editor/StepIndicatorTests.cs`（削除）・`Tests/Editor/ExpressionSlotLayoutTests.cs`（削除）・`Tests/Editor/AppPreviewWindowTests.cs`・`Tests/Editor/ExpressionBoardTests.cs`・`Tests/Editor/ExportGimmickChoicesTests.cs`・`Tests/Editor/ExportDetailsTextTests.cs`・`Tests/Editor/VrVlogMenuExportTests.cs`・`Tests/LanTransfer/VrVlogMenuTests.cs`・`Tests/Editor/PoseReviewWindowTests.cs`・`Tests/Editor/RecordRouteMenuLossTests.cs`・`Tests/Editor/PoseLimitPlanTests.cs`・`Tests/LanTransfer/AppTransferFlowTests.cs`・`Tests/LanTransfer/CloudTransferWindowTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。残すのは、書き出しの主経路を通す少数の結合テスト、主経路のテストで代わりに守れない過去の不具合の回帰テスト、壊れたら気づきにくい純粋なロジックの代表値に絞る。この記録は、そのうち UI の細かい文言・配置・見た目だけを確かめるテストを扱う。

利用者を守る UI の振る舞いは残した。同意や作者名が無いあいだ書き出しや転送のボタンが押せないこと、データを失う前の確認、失敗を黙って先へ進めずに画面に出すこと、ファイルの上書きと一時ファイルの後始末、QR に秘密を入れないことがこれに当たる。翻訳は、ある画面の文言がすべての言語にあることを確かめるテストを 1 本残し、文言 1 つずつの言い回しの検査は消した。

## 決定

- `StepIndicatorTests.cs`・`ExpressionSlotLayoutTests.cs`: ファイルごと消した（ステップ表示の名前と並び、表情スロットの配置と大きさ）。
  主経路で代わりに守れるか: 守れない。ただし見た目だけで、書き出す VRM の中身には関わらない。
- `AppPreviewWindowTests.cs`: マテリアルの要約・詳細の文言、エラー文、まばたきの開閉、読み込みの状態遷移の 9 本を消した。修正と一緒に入った 3 本（表情を選んでも手動の目の閉じ具合を保つ・1 回再生の終わりでボタンが再生に戻る・スクリプトの再読み込み後にもう一度読み込みを出す）は残した。
  主経路で代わりに守れるか: 守れない。プレビューの画面は書き出しの経路の外にある。
- `ExpressionBoardTests.cs`: カードのボタン、見出しの数、提案の並び、名前の変更と表示の 14 本を消した。書き出す表情の一覧を決める 6 本（入れ替え、対応しない表情はスロットに入らない、書き出しの一覧が盤面と経路に従う、視線・無表情のスロットを空けたまま残す、64 個を超えても全部記録する、書き出せない名前を拒む）は残した。
  主経路で代わりに守れるか: 消した分は一覧の見せ方で、書き出す中身は残した 6 本と主経路で守れる。
- `ExportGimmickChoicesTests.cs`: グループの並び、見出しの数、部品の要約・説明文、行のトグルの 6 本（9 ケース）を消した。保存、コンポーネント単位の選択、親の選択の継承、選択の比較（プレビューを作り直すかの判定に使う）は残した。
  主経路で代わりに守れるか: 消した分は一覧の文言と並びで、書き出しには関わらない。
- `ExportDetailsTextTests.cs`: 段階の行の文言、クリップボードとファイルの一致、回復の文言、診断の無い例外の 4 本を消した。ファイルの上書き・キャンセル・書き込み失敗・一時ファイルの後始末の 5 本は残した。
  主経路で代わりに守れるか: 消した分は詳細の文面で、書き出しには関わらない。
- `VrVlogMenuExportTests.cs`: アバター無しでもメニューから書き出しが開く 1 本を消した。
  主経路で代わりに守れるか: `DependencyStartupTests` が同じ入口を開いている。
- `VrVlogMenuTests.cs`: メニューの並びと区切り線の 2 本（3 ケース）を消した。アバターに対する項目がアバターのルート 1 つのときだけ押せることと、アプリでの見え方を開くメニューが無いことは残した。
  主経路で代わりに守れるか: 並びは見た目だけで、書き出しには関わらない。
- `PoseReviewWindowTests.cs`: .anim を 1 つ置く場合と、対応しないファイルの拒否・閉じたときの取り消しの 2 本を消した。20 個置いても使える（不具合 #61 の回帰）と、少しずつ集めた結果が書き出しと一致する 2 本は残した。
  主経路で代わりに守れるか: 1 つ置く場合は 20 個の場合に含まれる。拒否と取り消しは画面の振る舞いで、書き出しには関わらない。
- `RecordRouteMenuLossTests.cs`: 帯の表示と件数の上限、帯の翻訳、同じ分岐を別の形で確かめる 4 本の計 6 本を消した。修正と一緒に入った 2 本（共有のリセット層、Float のトグルを層に分けた場合）は残した。
  主経路で代わりに守れるか: 消した分は帯の見せ方か、残したテストと同じ分岐。
- `PoseLimitPlanTests.cs`: 上限の種類ごとに既定の外し方を見る 3 本、上限の画面の注意書きと選べるかどうかの 2 本、フレーム数の上限を超えた行の表示、16MB を超える 1 本を聞く場合、画面の翻訳の計 8 本を消した。上限の計算、保存した選択の扱い（修正 45e3a2d の回帰を含む）、画面でやめたら書き出しも止まること、かんたんと一括では聞かずに既定を使うことは残した。
  主経路で代わりに守れるか: 上限の種類ごとの外し方は、件数を超える場合と保存した選択の場合で同じ末尾から外す処理を通る。画面の見え方は書き出しには関わらない。
- `AppTransferFlowTests.cs`: 開始の確認の翻訳、保存と QR のどちらが青いか（3 ケース）、戻るの表示（3 ケース）、通知音の既定値の 4 本を消し、通知音の判定の 10 ケースを 4 ケースにした。転送の画面の文言の検査は、句読点・依頼の形・空白の検査を消して、全言語にあることと `{0}` の数の検査だけにした（名前を `WindowTextExistsInEveryLocale` に変えた）。同意・利用規約・QR の余白・アップロードの失敗・閉じたときにコピーを消すことは残した。
  主経路で代わりに守れるか: 消した分は見た目と言い回しで、転送する中身には関わらない。通知音の既定値は `CloudTransferWindowTests.QrReadySoundDefaultsOnAndIsSaved` が見ている。
- `CloudTransferWindowTests.cs`: 転送のメニュー項目のパス・優先度とアバターのルートの判定の 1 本（2 ケース）を消した。プライバシーの同意、他の VRM を消さないこと、ウィンドウを閉じたときに自分のコピーだけを消すことは残した。
  主経路で代わりに守れるか: ルート 1 つのときだけ押せることは `VrVlogMenuTests.EveryEntryActingOnAnAvatarIsGreyWithoutExactlyOneAvatarRoot` が全項目で見ている。
- `ExperimentalExpressionCaptureTests.cs`: 見出しのロゴ、転送をやめる確認の文言と翻訳の 2 本を消し、ロケール表の読み込みの 5 ケースを日本語と英語の 2 ケースにした。書き出し・再読み込み・保存・修正の回帰はすべて残した。カードの × の control id のテストは修正 a8eade7 の回帰なので残した。
  主経路で代わりに守れるか: 消した分は見た目と文言で、書き出しには関わらない。
- `Tools/run-unity-compatibility.py`: `BEHAVIOR_CASES` から `ExportDetailsTextTests` と `PoseReviewWindowTests` の消したメソッドを外した。

## 消さなかったもの

- 同意・利用規約・プライバシーの同意と、その同意が無いあいだ書き出しや転送を止めるテスト。
- ファイルの上書き・一時ファイル・回復と、他の VRM を消さないことのテスト。
- 書き出しを止める理由を黙らずに画面に出すテスト（`BottomBarNamesWhatBlocksTheSave`・`AWindowWithoutABoardBlocksTheSaveAndSaysWhy`）。
- 修正と一緒に入った回帰テストのうち、主経路のフィクスチャでは起きない形の入力を使うもの。
- 転送の画面の文言が全言語にあることを確かめるテスト 1 本。

## 件数

メソッド数／ケース数（`[TestCase]` が N 行のメソッドは N ケース）。

| ファイル | 前 | 後 |
| --- | --- | --- |
| StepIndicatorTests | 13 / 22 | 0 / 0 |
| ExpressionSlotLayoutTests | 15 / 22 | 0 / 0 |
| AppPreviewWindowTests | 12 / 12 | 3 / 3 |
| ExpressionBoardTests | 20 / 21 | 6 / 6 |
| ExportGimmickChoicesTests | 10 / 13 | 4 / 4 |
| ExportDetailsTextTests | 9 / 9 | 5 / 5 |
| VrVlogMenuExportTests | 2 / 2 | 1 / 1 |
| VrVlogMenuTests | 4 / 5 | 2 / 2 |
| PoseReviewWindowTests | 4 / 4 | 2 / 2 |
| RecordRouteMenuLossTests | 13 / 13 | 7 / 7 |
| PoseLimitPlanTests | 21 / 22 | 13 / 14 |
| AppTransferFlowTests | 31 / 75 | 27 / 61 |
| CloudTransferWindowTests | 23 / 29 | 22 / 27 |
| ExperimentalExpressionCaptureTests | 84 / 176 | 82 / 171 |
| 合計 | 261 / 425 | 174 / 303 |
