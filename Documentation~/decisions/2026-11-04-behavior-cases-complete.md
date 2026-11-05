# 回帰テストの表に、結合テストと共有するクラスの全メソッドを載せる

- 日付: 2026-11-04
- 対象: `Tools/run-unity-compatibility.py`、`Tools/test-compatibility-tooling.py`

## 背景

`exporter-behavior`は、`INTEGRATION_CASES`にも載っているクラスをメソッド名で選んで実行する。`PhysBoneSpringExportTests`はこのクラスに当たり、`BEHAVIOR_CASES`に無い6本がこのプロファイルで実行されていなかった。

表とテストファイルを突き合わせる`test_required_case_names_and_counts_match_checked_in_fixtures`は`INTEGRATION_CASES`に載るクラスを網羅の検査から外していたので、抜けを検出できなかった。

## 決定

- 抜けていた6本を`BEHAVIOR_CASES`に足す。
- 網羅の検査はすべてのクラスに掛ける。2つの表を合わせたメソッドがテストファイルと一致しないと落ちる。

全クラスを突き合わせた結果、抜けはこの6本だけだった。
