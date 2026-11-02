# 表情のセッションから、本番で通らない経路と同じ表の写しを消す

- 日付: 2026-11-02
- 対象: `Editor/ExperimentalExpressionCaptureSession*.cs`・`Editor/ExpressionPresetStore.cs`・`Editor/Locales`・`Tests/Editor`

## 背景

大規模リファクタリング（[2026-11-02-big-refactor.md](2026-11-02-big-refactor.md)）の監査で、表情ボードのセッションにテストからしか呼ばれない経路が見つかった。書き出す VRM の中身・警告の文言・表情の集合と順番は変えずに、その経路を消す。

## 決定

- 表情ボードの記録設定の保存・読み込み（`SaveSettings`・`LoadSettings` と、それだけが使う `Settings`・`PoseReference`・`PoseNameReference`・`SavePoseReference`・`LoadPoseReference`・`SavePresets`）を消した。表情ボードの画面から呼ぶ所が無くなったあと、テストだけが呼んでいた。表情の割り当てはアバターごとに `ExpressionPresetStore` が保存する（[2026-10-14-emotion-presets.md](2026-10-14-emotion-presets.md)）ので、利用者から見える保存は変わらない。
  - `ExpressionPresetStore` が 1 件ずつ読むのに使う検査は `LoadPreset` として残した。同じ表情の重複は、これまでどおり `ExpressionPresetStore.Load` が飛ばす。
  - 消した経路だけが出していた訳文 10 件を 4 言語から消した。
  - 保存・読み込みの往復だけを確かめていたテストは消し、`LoadPreset` が壊れた割り当てを拒むテストを足した。

## 確かめたこと

- `Editor`・`Tests`・`Tools` を名前で検索し、消したメンバーの呼び出しが本体に無いこと。
- EditMode テストの全件と、Unity を使わないスクリプトのテスト。
