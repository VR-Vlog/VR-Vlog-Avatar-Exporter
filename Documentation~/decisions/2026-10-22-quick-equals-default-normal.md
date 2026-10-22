# アプリに転送（かんたん）と、何も変えない通常を同じ VRM にする

- 日付: 2026-10-22
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Editor/AppTransferQuickExportTests.cs`・`Tools/run-unity-compatibility.py`・`CLAUDE.md`・`Documentation~/CloudTransfer.md`・`CHANGELOG.md`・`Documentation~/decisions/2026-10-21-quick-transfer-same-path.md`

## 背景

**アプリに転送（かんたん）** は、**アプリに転送（通常）** で表情ボードの設定をすべて既定のまま書き出したものと同じでなければならない。これを、作り・テスト・書いておく決まりの 3 つで保証してほしい、という依頼があった。

`2026-10-21-quick-transfer-same-path.md` でかんたんも表情ボードの保存（`TransferExport` → `Save`）で書き出すようにしたが、まだ次の違いが残っていた。

- 失敗した段階の扱いを、確認を出すかどうか（`interactive`）で分けていた。かんたんは **失敗した段階を省いて続ける** の設定（`ExportStageGuard.ContinueByDefault`）に従い、通常は止まって対策画面を開く。材質の段階が失敗するアバターでは、かんたんだけが VRM を書き出す。
- 転送のためにボードを準備する処理が 2 か所にあった。通常（`OpenForTransfer`）は `ChangeAvatar` → `SelectSource` の順、かんたん（`PrepareHidden`）は `transferMode` を立てて `SelectSource` だけを呼ぶ。新しく作るボードでは同じ状態になるが、設定項目を足すときに片方だけへ足すと、かんたんと通常で既定値が変わる。
- 一致を確かめるテストは表情の名前と数だけを比べていて、メッシュ・材質・バイト列の違いは見ていなかった。

## 決定

- 転送のためにボードを準備する処理を `PrepareForTransfer(avatar)`（`transferMode = true` → `ChangeAvatar` → `SelectSource`）の 1 つにし、`OpenForTransfer` が積む処理と `PrepareHidden` の両方から呼ぶ。新しい設定項目の既定値は `ChangeAvatar` か `Prepare` に足し、転送ごとには書かない。新しく作ったボードでは `ChangeAvatar` が変える値（まばたきオン・利用条件・状態の文・1 ページ目）はもともと初期値と同じなので、かんたんの書き出す内容は変わらない。
- `Save` で転送（`written` がある）のときは、確認を出すかどうかにかかわらず `report.ContinueOnStageFailure = ExportStageGuard.ContinueByDefault` にする。書き出し画面（`ExportSettings.Export`）と同じ設定に従う。表情ボードの **VRMを保存**（`written` がない、ファイルへの保存）はこれまでどおり最初の失敗で止まる。
- `interactive` は、確認を出すかどうかと、何を Console や画面に出すかだけを決める。書き出す中身を変える分岐には使わない。かんたんだけに残る分岐は次の 2 つで、どちらも表示だけを変える。
  - 自動設定できなかった項目の一覧を、確認の画面ではなく Console の警告に出す。
  - ボードに直すところがあって止まるとき、ボードの理由（`BoardBlockReason`）を続けて返す。
- `AppTransferExport.SelectAvatar` の `TransferMode` の分岐は残す。開いている通常のボードのときに作者名と利用条件をどこから取るかを決めるだけで、書き出しの中身には関わらない。
- 一致を `QuickTransferMatchesDefaultNormalTests` で確かめる。同じアバターをかんたんと、表情ボードを開いて何も変えない通常で書き出し、次を比べる。
  - 書き出しが報告した内容（書き出し結果の一覧があればその行、無ければ Console の「書き出し詳細」の行。どちらも書き出し先のパスを除き、行を並べ替えて比べる）
  - VRM の標準の表情とカスタムの表情（名前と、変形・材質・画像の割り当ての数）
  - メッシュ（プリミティブ数・頂点数）と材質の名前
  - glTF の JSON 全体（キーを並べ替え、書き出しのたびに変わる乱数の接尾辞を伏せる）と、バイナリのチャンクの SHA-256
- この決まりをリポジトリ直下の `CLAUDE.md` に書く。

## 選ばなかった案

- かんたんも失敗した段階で止める。これまでかんたんで材質の段階を省いて書き出せていたアバターが書き出せなくなる（2026-10-21 の 29 体で 1 体）。書き出し画面も省いて続けるので、止まるのは通常だけになる。
- 通常は止まるままにし、違いを決まりの例外として書く。「何も変えない通常と同じ」が段階の失敗だけ成り立たなくなり、テストでも例外を扱うことになる。
- かんたんも画面に出したボードで書き出す。かんたんの画面の流れが変わり、通常のボードがすでに開いているかどうかで書き出す内容が変わりうる（`2026-10-21-quick-transfer-same-path.md` の「選ばなかった案」と同じ）。
- テストで表情の名前と数だけを比べる。メッシュの除外・材質の変換・段階の省略の違いを見落とす。バイト列まで比べれば、何が違っても検出できる。

## 変わること

- **アプリに転送（通常）** で書き出しの段階が失敗したとき、止まって対策画面を開くのをやめ、かんたんと書き出し画面と同じく、失敗した段階を省いて書き出し、書き出し結果の一覧にその段階を出す。通常では一覧を確認として出すので、**設定を変更する** でアップロードせずに止められる。
- Unity Editor の設定 `VRVlog.LilToonExporter.ContinueOnStageFailure` をオフにすると、かんたんも通常も失敗した段階で止まり、対策画面を開く。
- 表情ボードの **VRMを保存** は変わらない（失敗した段階で止まる）。
- かんたんの書き出す内容は変わらない。
- まだ揃っていないところ（この変更の前からある違い）:
  - 設定 `VRVlog.LilToonExporter.ContinueOnStageFailure` をオフにして段階の失敗で止まり、対策画面から保存し直すとき、かんたんは隠しボードをもう閉じているので、表情メニューから 1 件も書き出せなかったときに表情ボードの一覧を記録し直さない。通常はボードが開いたままなので記録し直す。既定（オン）では対策画面を開かないので起きない。
  - ノードの回転・拡大の値に 2e-6 以下の誤差が出るアバターがある（確認したことを参照）。

## 確認したこと

- 31 体の書き出し（検証環境、通常の転送と同じ `TransferExport` で書き出す回帰ランナー）を develop と比べた。29 体は差分 0。違う 2 体はどちらもこの変更で意図したもの。
  - 材質が足りない 1 体: develop の通常は材質の段階で止まり、この変更のあとは段階を省いて書き出す。develop のかんたんも省いて書き出していた。
  - シェーダーが見つからない 1 体: VRM は同じで、警告の並びだけが変わる。失敗を省いて続けるときは 1 回の書き出しの警告をまとめてから足すので（`UniVrmOneClickExporter.Export`）、「シェーダーが見つからない」の行が後ろへ動く。並べ替えると同じ。
- 同じ 31 体をかんたん（`AppTransferExport.Export`）でも書き出し、同じコミットの通常と比べた。バイナリのチャンクは 31 体とも同じ。JSON は 29 体で同じで、2 体でノードの回転・拡大の値が 2e-6 以下の誤差（と、同じ回転を表す符号の反転した四元数 1 件）だけ違う。develop のかんたんと通常でもまったく同じ値で違っていたので、この変更で入った違いではない。かんたんの書き出しは develop とこの変更のあとで 31 体とも同じ（警告を含む）。
- EditMode テスト 4163 件。develop の 4156 件と比べ、改名した 2 件（4 件に増えた）と足した 5 件のほかは 1 件ずつ同じ結果で、足したテストと改名したテストはすべて通った。
- 足したテスト（`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`、各 1 ケース）:
  - `TheSavedExpressionsMatch`: 喜びの枠に名前からは選ばれない形を記録したアバター。
  - `TheGimmicksAndTheSavedExclusionsMatch`: 自動で外すギミックと、記録した書き出さないオブジェクトを持つアバター。どちらの書き出しにもその 2 つのメッシュが無い。
  - `APlainAvatarMatches`: 何も記録していないアバター。
  - `AFailingStageMatches`: 材質の段階が失敗するアバター。設定がオンのとき、どちらも段階を省いて保存し、書き出し結果の一覧を出し、対策画面を開かない。
  - `ABoardReusedForAnotherAvatarStartsFromTheSameDefaults`: 別のアバターで開いて設定を変えた通常のボードを、このアバターで開き直したとき、ボードの既定値（まばたき・利用条件・状態の文・ページ・除外・ギミック・ポーズ・枠の割り当て）が `PrepareHidden` で準備したボードと同じで、書き出しも一致する。
- 書き換えたテスト（`Tests/Editor/ExperimentalExpressionCaptureTests.cs`）:
  - `NonInteractiveTransferFollowsTheStageChoice` を `TransferFollowsTheStageChoice` に改め、確認を出すかどうか × 設定のオン・オフの 4 ケースにした。オンならどちらも保存し、確認を出す転送だけが書き出し結果の一覧を 1 回出す。オフならどちらも保存せず、確認も出さずに対策画面を開く。
- `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` に `QuickTransferMatchesDefaultNormalTests` の 5 件を足し、`Tools/test-compatibility-tooling.py` が通ることを確かめた。
