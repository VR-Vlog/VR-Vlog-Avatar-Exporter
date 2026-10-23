# 表情の割り当てのエラーを2回聞いても同じ文になることをテストで確かめる

- 日付: 2026-10-23
- 対象: `Tests/Editor/ExpressionPresetTests.cs`

## 背景

表情ボードのドラッグを速くした変更で、`BuildPresetPose` は作った表情とエラーの文を覚えておき、2回目からはそれを返すようにした。エラーの文は1回目に「表情の名前: 理由」の形で作るので、覚えた文を返すときに名前をもう一度付けると「名前: 名前: 理由」になる。この経路を確かめるテストが無かった。

## 決定

- `ExpressionPresetTests.APresetErrorReadsTheSameWhenAskedAgain` を足す。無いBlendShapeを割り当てたときの `PresetError` を2回呼んで同じ文になること、文の先頭に表情の名前が1回だけ付くこと、`BuildPresetPose` が同じ文で失敗することを確かめる。
- 製品のコードは変えない。

## 検討して採らなかった案

- 瞬きの候補が `EditorEdits.Note()` のあとに読み直されることを確かめるテストは足さなかった。ボードの外では `RequireValid` が呼ばれるたびに元のアバターとメッシュのハッシュを取り直し、変わっていれば `SourceChangedException` で止まるので、候補だけが古いまま残る状態をテストで作れない。瞬きの設定を変えたときに候補が変わることは、既存の `BlinkCandidatesRepeatTheAutomaticBlinkSetting` で確かめている。
