# 表情ボードからの書き出しでも視線と neutral を名前から埋める

- 日付: 2026-10-21
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`CHANGELOG.md`

## 背景

表情ボードを使わない通常の書き出しは `UniVrmOneClickExporter.Export` を `inferEmotionPresets: true` で呼ぶ。このとき `VrmExpressionBindings.AddMissing` が空の喜怒哀楽に加えて、空の `neutral` と視線（`lookUp`・`lookDown`・`lookLeft`・`lookRight`）を BlendShape の名前やメニューの項目名から埋める（`AddNeutral`・`AddGaze`）。

表情ボード（`ExperimentalExpressionCaptureSession.Export`）からの書き出しは、喜怒哀楽を使う人がボードで選ぶので、3 か所の呼び出しすべてで `inferEmotionPresets: false` を渡していた。この 1 つのフラグが `AddNeutral`・`AddGaze` も止めていたため、ボードから書き出すと視線と neutral が名前から埋まらなかった。ボード自身の自動の候補は喜怒哀楽・まばたき・口の形にしか出ず、視線と neutral は埋めない。29 体の書き出しの突き合わせで、ボードから書き出した 3 体の視線が消え、そのうち 1 体は neutral も消えていた。

## 決定

- `AddMissing` に `inferNeutralAndGaze` と `keepEmptyPresets` を足す。`inferEmotions` か `inferNeutralAndGaze` のどちらかが真なら `AddNeutral`・`AddGaze` を動かし、喜怒哀楽の推定（`AddEmotions`）は `inferEmotions` のときだけ動かす。`keepEmptyPresets` に入っている VRM 1.0 の表情名（`neutral`・`lookUp` など）は埋めない。
- `UniVrmOneClickExporter.Export` に `inferNeutralAndGazePresets`（既定 `false`）と `keepEmptyPresets`（既定 `null`）を足し、`ExportCore` を通して `AddMissing` に渡す。既定のままなら、これまでの呼び出しの動きは変わらない。
- 表情ボードの 3 か所の呼び出しで `inferNeutralAndGazePresets: true` を渡す。`keepEmptyPresets` には、ボードで使う人が自分で「なし」にした視線と neutral（`IsNone` かつ `Automatic` でない割り当て）を渡す。ボードで何も選んでいない枠は通常の書き出しと同じく名前から埋め、「なし」を選んだ枠だけ空のまま残す。
- ボードで視線や neutral に表情を割り当てた場合は、その表情が入った枠を `AddNeutral`・`AddGaze` がこれまでどおり空でないとして飛ばすので、上書きしない。元の VRM が持っていた視線・neutral も同じ理由で変わらない。

## 採らなかった案

- 表情ボードの自動の候補に視線と neutral を足す: ボードの表示と保存の仕組み（自動の候補・記録・まばたきとの関係）まで変わり、通常の書き出しと同じ名前の規則を 2 か所に持つことになる。書き出しの最後に同じ `AddMissing` で埋めれば、通常の書き出しと結果がそろう。
- 表情ボードからも `inferEmotionPresets: true` で書き出す: ボードで選んでいない喜怒哀楽まで名前から埋まり、使う人がボードで決めた喜怒哀楽と食い違う。
- 「なし」の視線・neutral を空の表情としてVRMに書く: 中身のない表情は `AddMissing` が空として埋めにいくので、埋めない表情名を渡すほうが単純で、ほかの表情の扱いも変わらない。

## 確認したこと

- この変更を作った PC には Unity がなく、C# のコンパイルと EditMode テストは回していない（件数と結果は PR 本文と CI）。足したテストは次の 2 つ。
  - `NeutralAndGazeAloneLeaveEmotionsAlone`: `inferEmotions` を偽、`inferNeutralAndGaze` を真にすると、`neutral`・`lookUp`・`lookDown` が埋まり、`Fcl_ALL_Joy` があっても `happy` は埋まらない。既定の引数では何も埋めない。
  - `KeptEmptyPresetsAreNotFilled`: `keepEmptyPresets` に `lookUp` を入れると、`lookUp` だけ空のまま `neutral` と `lookDown` が埋まる。`inferEmotions` が真のときも `neutral` を入れればそれだけ空のまま残る。
- `Tools/test-compatibility-tooling.py` と `Tools/check-names.py` を回した。
