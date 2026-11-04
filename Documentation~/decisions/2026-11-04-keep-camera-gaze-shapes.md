# カメラ視線が名前で探す BlendShape を書き出しで残す

- 日付: 2026-11-04
- 対象: `Editor/UnusedMorphTargets.cs`・`Tests/Editor/UnusedMorphTargetsTests.cs`・`Tests/Editor/Fixtures/UnusedMorphTargetsFixture.cs`・`Tests/Editor/ExportOptimizationBindingsTests.cs`

## 背景

アプリのカメラ視線（`CameraEyeGaze`）は、目を上下左右に動かす BlendShape を名前で探す。8 つの組（`look`・`eye`・`eyes`・`pupil`・`eyepupil`・`iris`・`eyeiris`・`eyelook` に `up`・`down`・`left`・`right` を付けた名前）があり、名前は英数字だけを小文字にして全体を比べる。`eye_up` と `Eye Up` は同じ名前として扱い、`Face.eye_up` は別の名前になる。4 方向のどれかが無い組は使わず、ボーンの視線に戻る。

書き出しの `UnusedMorphTargets.Strip` は、表情から参照されず、`AppDrives` に当たらず、重みが 0 の BlendShape を外す。視線の名前は `AppDrives` に無かったので、表情が 1 方向だけ参照していると、その 1 方向だけが残って残りの 3 方向が消え、アプリはボーンの視線に戻っていた。警告は出ない。

## 決定

- `AppDrives` に視線の名前を足す。比べ方はアプリと同じで、名前全体を英数字だけの小文字にして比べる。ほかの名前のように `.` や `/` の後ろだけを見ることはしない。アプリがその形では見つけないため。
- 4 方向がそろっているかは見ず、名前ごとに残す。ほかの `AppDrives` の名前と同じ扱いで、判定が単純なまま保てる。そろっていない組の形が残っても、ファイルが少し大きくなるだけで動きは変わらない。
- どの組を使うかはアプリが頂点を見て決めるので、書き出しでは選ばない。1 つのメッシュに有効な組が 2 つあるとアプリはボーンの視線に戻る。元のメッシュに 2 組ある場合のほか、別の組を持つメッシュを最適化ツールで結合した場合にも起こりうる。書き出しで片方を消して決めることはしない。
- この判断で動きが変わる入力が 1 つある。1 組は 4 方向とも表情が参照し、もう 1 組はどれも参照しないメッシュは、これまで参照されない組が消えてカメラ視線になっていたが、これからは 2 組が残ってボーンの視線になる。2 組とも有効になるメッシュはまれで、元のメッシュをアプリの判定どおりに扱う結果なので受け入れる。

`AppDrives` は最適化ツールに渡す経路の宣言と、名前を戻す処理（`RestoreAppDrivenNames`）でも使っている。視線の名前もこの 2 つの対象になるので、メッシュ結合で接頭辞が付いても元の名前に戻る。2 つのメッシュがどちらも同じ視線の名前を持っていて戻せないときは、結果一覧に既存の注意が「表情トラッキング」の項目で出る。

## 範囲外

- 別の組を持つメッシュの結合と、結合後にアプリが選ぶ組は試していない。
- 視線の一覧はアプリのコードの写しで、アプリが組を足しても自動では追従しない。`LegacyNames` と同じ扱い。
- アプリ側の同梱データの作り直しと、手順書に書かれた回避策の削除はこのリポジトリの外の作業。

## 確認

- 修正前のコードで `UnusedMorphTargetsTests` の新しい 2 件が失敗し、修正後に通ることを Unity 2022.3.22f1 で確かめた。
  - `KeepsTheShapesOfTheCameraGaze`: 名前の判定と `Strip` の結果。ホストの `Tools/run-behavior-tests.ps1` でも回る。
  - `AnExportKeepsTheCameraGazeShapesThatNoExpressionBinds`: `UniVrmOneClickExporter.Export` を通した書き出し全体。
- 手元のアバターを `Tools/run-export-regression.ps1` で修正前後に書き出して比べた。修正前は視線の形が 1 方向だけ残っていた。修正後は 2 つの組の 4 方向がすべて残り、BlendShape は 109 個から 116 個、ファイルは約 94KB 増えた。29 個の表情は BlendShape の名前で引くと修正前と同じ。
- その出力にアプリの判定（動く頂点が目の頂点と一致すること、向きが逆であること）を同じ式で当てると、修正前は有効な組が無く、修正後は 1 組だけが有効になる。アプリに読み込ませての確認はしていない。
- Avatar Optimizer 1.9.20 で `InstalledAvatarOptimizerMergeGivesAppDrivenShapesTheirNamesBack` に `eye_up` のケースを足し、メッシュ結合の後に名前が戻ることを確かめた。修正前のコードではこのケースだけが失敗する。
- EditMode 全体を Avatar Optimizer 1.8.16 で回すと 3981 件中 10 件が失敗する。すべて `ExportOptimizationBindingsTests.InstalledAvatarOptimizer*` で、修正前のコードでも同じ 10 件が失敗するので今回の変更とは関係しない。1.9.20 ではこのクラスの 39 件がすべて通る。
