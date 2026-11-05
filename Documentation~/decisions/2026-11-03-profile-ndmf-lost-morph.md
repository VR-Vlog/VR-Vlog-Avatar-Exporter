# `NdmfLostMorphFollowTests` を統合プロファイルの一覧に載せる

- 日付: 2026-11-03
- 対象: `Tools/run-unity-compatibility.py`・`Compatibility/README.md`

## 背景

`NdmfLostMorphFollowTests` は `Tools/run-unity-compatibility.py` の一覧に無く、プロファイルを回してもこのテストを回したことを確かめていなかった。

## 決定

- インストールされた NDMF のパスを通すテストなので、`INTEGRATION_CASES` に 9 メソッド 12 ケースで載せる。

## 確かめたこと

Unity 2022.3.22f1 でクラス単位で回して 12 件が通り、メソッドごとのケース数が一覧と一致した。`Tools/test-compatibility-tooling.py` も通った。`--profile exporter-integration` そのものは回していない。
