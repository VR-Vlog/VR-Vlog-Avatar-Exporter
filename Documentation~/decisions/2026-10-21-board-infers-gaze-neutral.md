# 表情ボードからの書き出しでも視線と neutral を名前から埋める

- 日付: 2026-10-21
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExpressionBoard.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`Tests/Editor/ExpressionBoardTests.cs`・`Documentation~/ExperimentalExpressionCapture.md`・`CHANGELOG.md`

## 背景

表情ボードを使わない通常の書き出しは `UniVrmOneClickExporter.Export` を `inferEmotionPresets: true` で呼ぶ。このとき `VrmExpressionBindings.AddMissing` が空の喜怒哀楽に加えて、空の `neutral` と視線（`lookUp`・`lookDown`・`lookLeft`・`lookRight`）を BlendShape の名前やメニューの項目名から埋める（`AddNeutral`・`AddGaze`）。

表情ボード（`ExperimentalExpressionCaptureSession.Export`）からの書き出しは、喜怒哀楽を使う人がボードで選ぶので、3 か所の呼び出しすべてで `inferEmotionPresets: false` を渡していた。この 1 つのフラグが `AddNeutral`・`AddGaze` も止めていたため、ボードから書き出すと視線と neutral が名前から埋まらなかった。ボード自身の自動の候補は喜怒哀楽・まばたき・口の形にしか出ず、視線と neutral は埋めない。29 体の書き出しの突き合わせで、ボードから書き出した 3 体の視線が消え、そのうち 1 体は neutral も消えていた。

## 決定

- `AddMissing` に `inferNeutralAndGaze` と `keepEmptyPresets` を足す。`inferEmotions` か `inferNeutralAndGaze` のどちらかが真なら `AddNeutral`・`AddGaze` を動かし、喜怒哀楽の推定（`AddEmotions`）は `inferEmotions` のときだけ動かす。`keepEmptyPresets` に入っている VRM 1.0 の表情名（`neutral`・`lookUp` など）は埋めない。
- `UniVrmOneClickExporter.Export` に `inferNeutralAndGazePresets`（既定 `false`）と `keepEmptyPresets`（既定 `null`）を足し、`ExportCore` を通して `AddMissing` に渡す。既定のままなら、これまでの呼び出しの動きは変わらない。
- 表情ボードの 3 か所の呼び出しで `inferNeutralAndGazePresets: true` を渡す。`keepEmptyPresets` には、ボードで使う人が自分で「なし」にした視線と neutral（`IsNone` かつ `Automatic` でない割り当て）を渡す。ボードで何も選んでいない枠は通常の書き出しと同じく名前から埋め、「なし」を選んだ枠だけ空のまま残す。
- ボードで視線や neutral に表情を割り当てた場合は、その表情が入った枠を `AddNeutral`・`AddGaze` がこれまでどおり空でないとして飛ばすので、上書きしない。元の VRM が持っていた視線・neutral も同じ理由で変わらない。
- ボードの操作でも、使う人が「なし」を選んだときだけ視線と neutral の枠に「なし」を書く。`ExpressionBoard.Move` で表情が枠を出たときと `ClearSlot`（カードの **×** で外したとき・追加したファイルが使えなくなって外したとき）は、ほかの枠では自動の候補が戻らないよう「なし」を書くが、視線と neutral の枠は割り当てを消して空けておく（`ExperimentalExpressionCaptureSession.FilledFromNames`）。「なし」を書くと記録に残って `keepEmptyPresets` に入り、使う人が空にしたつもりのない枠が書き出しでも空のままになるため。BlendShape を入れた枠のカードの **×** も同じく空けておく。視線と neutral にはボードの自動の候補が出ないので、空けた枠に候補が入ることはない。
- ボードの表示もこれに合わせる。何も入れていない視線と neutral の枠には「空のまま保存」ではなく「名前から埋める」と表示する。枠の見出しの一覧では「なし」の印を「なし」を選んだときだけに付け、「名前から埋める」の項目（割り当てを消して空ける）を足す。視線と neutral には自動の候補がないので、この 2 つの枠では「自動の候補に戻す」の代わりにこの項目を出す。元の VRM 設定にある枠はこれまでどおり「元のVRM設定」と表示し、この項目は出さない。

## 採らなかった案

- 表情ボードの自動の候補に視線と neutral を足す: ボードの表示と保存の仕組み（自動の候補・記録・まばたきとの関係）まで変わり、通常の書き出しと同じ名前の規則を 2 か所に持つことになる。書き出しの最後に同じ `AddMissing` で埋めれば、通常の書き出しと結果がそろう。
- 表情ボードからも `inferEmotionPresets: true` で書き出す: ボードで選んでいない喜怒哀楽まで名前から埋まり、使う人がボードで決めた喜怒哀楽と食い違う。
- ボードの操作で視線と neutral の枠に「なし」を書いたまま、書き出しのときに自動の「なし」と使う人の「なし」を見分ける: 「なし」の割り当てにはどの操作で書いたかが残らず、記録の形式まで変えることになる。枠を空けておけば、記録に残る「なし」は使う人が選んだものだけになる。
- 「なし」の視線・neutral を空の表情としてVRMに書く: 中身のない表情は `AddMissing` が空として埋めにいくので、埋めない表情名を渡すほうが単純で、ほかの表情の扱いも変わらない。

## 確認したこと

- VRChat SDK と UniVRM の入った Unity 2022.3.22f1 で、メニューの表情を残す修正と合わせた状態で `ExperimentalExpressionCaptureTests`・`ExpressionBoardTests`・`ExpressionNameInferenceTests`・`ExpressionPresetTests`・`NeutralShapeSamplerTests`・`SelectedExpressionAppearanceTests` を回した。596 件のうち 591 件が通った。失敗した 5 件は、外部のアプリを入れていないと動かない 4 件と、メニューの表情を残す修正の側のテスト 1 件（そちらの PR で直した）で、この変更とは関係しない。足したテストは次の 3 つで、どれも通った。
  - `NeutralAndGazeAloneLeaveEmotionsAlone`: `inferEmotions` を偽、`inferNeutralAndGaze` を真にすると、`neutral`・`lookUp`・`lookDown` が埋まり、`Fcl_ALL_Joy` があっても `happy` は埋まらない。既定の引数では何も埋めない。
  - `KeptEmptyPresetsAreNotFilled`: `keepEmptyPresets` に `lookUp` を入れると、`lookUp` だけ空のまま `neutral` と `lookDown` が埋まる。`inferEmotions` が真のときも `neutral` を入れればそれだけ空のまま残る。
  - `LeavingAGazeOrNeutralSlotLeavesItOpen`（`ExpressionBoardTests`）: 視線・neutral の枠から表情をその他の表情・書き出さない表情・ほかの枠へ動かすと枠に割り当てが残らず、BlendShape の視線を `ClearSlot` で外しても同じ。使う人が書いた「なし」の視線は自動の候補で消えない。喜怒哀楽の枠はこれまでどおり自動でない「なし」になる。
- 既存の `ConfirmClearAndSwapReachAllThirteenAndFiveSlots` は、neutral の枠から表情を動かしたあとと全部の枠を `ClearSlot` したあとの視線・neutral を「なし」から「割り当てなし」に直した。
- `Tools/test-compatibility-tooling.py` と `Tools/check-names.py` を回した。
- 29 体を通常の書き出しで書き出し直すと、視線の表情が名前から入るアバターが 3 体、neutral が入るアバターが 2 体増えた。メッシュの数・頂点数・材質は変わらない。
