# 感情・口・まばたきの枠に入れたクリップのボーンと表示切替を書き出す

- 日付: 2026-11-10
- 対象: `Formats/Runtime/ExpressionAnimationData.cs`・`Schema/VRVLOG_expression_animations.2.schema.json`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/Locales/*.json`・`Formats/README.md`・`Documentation~/ExperimentalExpressionCapture.md`・`CHANGELOG.md`

## 背景

[2026-11-09の決定記録](2026-11-09-preset-slot-drops-visibility.md)で、自分で標準表情の枠に入れたクリップは顔の変形だけを使い、ボーン・表示切替・指のポーズを外して書き出すようにした。VRM の表情はモーフ・マテリアル・テクスチャしか持てないため。

笑顔に頬の飾りを出す、口を開けると牙のボーンが動く、のように、感情や口の形と一緒に動くボーンや表示切替を作っているアバターは多い。外すと VR Vlog のアプリでは顔の形だけが変わり、作者の意図と違う見た目になる。オーナーの判断は、標準の表情でもボーンと表示切替を書き出し、アプリはボーン付き表情のフラグ（`expression_bone_animation`）の中で対応する、というもの。

## 決定

- `VRVLOG_expression_animations`（schema 2 のまま）に任意のキー `presetTransforms` を足す。要素は `transforms` と同じ形で、`expression` に標準の表情名、`duration` は 0、`loop` は false。名前は `ExpressionAnimationData.PresetTransformNames`（happy・angry・sad・relaxed・surprised・aa・ih・ou・ee・oh・blink・blinkLeft・blinkRight）に限り、1 名 1 回。読むのは `ReadPresetTransforms` だけで、`ReadDocument`・`ReadTransforms` の結果は変わらない。
- 標準の表情の名前は `expressions`・`transforms` に入れない。カスタムの表情の一覧と再生に混ざらないようにするため。
- 書くのは、利用者が自分で感情・口・まばたきの枠に入れたクリップ（`Automatic` でない）だけ。値は表情ボードの時刻（`FaceTime`）で止めた値で、アプリは表情の実際の重みで混ぜる。自動の候補は今までどおり BlendShape だけのクリップに限るので、アプリに転送（かんたん）の書き出しは変わらない。
- クリップの読み取りはその他の表情と同じ `ExpressionTransformCurves.Read`・`Resolve` を使う。Humanoid のボーン、見つからないパス、元から非表示のオブジェクトを表示する表示切替は、その他の表情と同じ理由で外し、枠の名前を付けて書き出し結果の一覧に出す。
- 指のポーズは今までどおり外し、「指のポーズは書き出しません: <パス / プロパティ>」を一覧に出す。枠の下は「確定」のままにする。
- 視線とneutralの枠は今までどおり顔の変形だけを使い、外した変更を一覧に出す。視線はアプリが目のボーンを回しているので、表情のボーンと二重に動かすと破綻しやすい。neutralは単独で表示しない。
- マテリアルの変更は書き出さない（今までどおり対応外）。
- `presetTransforms` だけを持つ文書（カスタムの表情が無い）を許す。`ReadDocument` の空の判定はこのキーがあれば通す。

## 古いアプリとの互換

- カスタムの表情もある文書: 古いアプリは `presetTransforms` を知らないので読まず、カスタムの表情・`transforms` は今までどおり動く。標準の表情は顔の変形だけになる（この変更の前と同じ）。
- `presetTransforms` だけの文書: 古いアプリの `ReadDocument` は空の文書として拒み、警告を出して標準の VRM の表情で続ける（`ExpressionAnimationPlayer.Attach` が例外を捕まえる）。カスタムの表情は元から無いので失うものは無い。
- 拡張は今までどおり `extensionsRequired` に入れない。必要なアプリの版（`VRVLOG_requiredAppVersions`）も schema 2 のまま上げない。読まない版でも表示は今までと同じため。

## 選ばなかった案

- 標準の表情をカスタムの表情として `expressions`・`transforms` に書く: アプリはカスタムの表情を一覧に出し、トラッキングを止めて再生するので、標準の表情の重み（トラッキング・リップシンク）に追従しない。
- 新しい拡張を作る: 読み取りの制約（ノード・トラック・キーの検査）が `transforms` と同じなので、同じ拡張の中に置いて検査を共有する。
- 視線の枠も書く: アプリの視線は目のボーンを直接回す。表情のボーンと順番の取り決めが要り、この変更の範囲を超える。

## 確かめたこと

- `ExpressionAnimationDocumentTests`: `presetTransforms` の往復、`presetTransforms` だけの文書、キーを読まない読み手でカスタムの表情と `transforms` が変わらないこと、壊れた要素（視線・neutral・カスタムの名前・大文字違い・重複・時計つき・ループ・トラック無し・空・配列でない）を拒むこと。
- `ExpressionPresetTests.AChosenEmotionSlotWritesItsBonesAndShownObjectsUnderThePreset`: happy の枠に入れたクリップのボーンと表示切替が `presetTransforms` の happy に入り、`expressions`・`transforms` には入らない。
- `ExpressionPresetTests.AChosenPresetThatAlsoShowsAnObjectKeepsItsShapesAndSaysWhatItLeftOut`: 視線の枠は今までどおり顔の変形だけで、`presetTransforms` は書かれない。
- `ExperimentalExpressionCaptureTests.GestureFacesIgnoreBoneCurvesBesideTheFace`: happy の枠では指のポーズだけを外し、lookUp の枠では今までどおりボーン・表示切替・指のポーズを外す。
