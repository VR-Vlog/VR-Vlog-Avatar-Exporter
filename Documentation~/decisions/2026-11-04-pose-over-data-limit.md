# 16MB を超える動くポーズを解析で失敗させず、上限の確認に渡す

- 日付: 2026-11-04
- 対象: `Editor/PoseSampling.cs`・`Editor/HumanoidAnimationData.cs`・`Tools/run-unity-compatibility.py`（テストの一覧）

## 背景

1 本で 16MB を超える動くポーズは、上限の確認（`PoseLimitPlan`）で「1本でデータの上限16MBを超えるため外しました」と案内して外す作りになっている。ところが解析の最後の `HumanoidAnimationData.Write` が同じ上限で先に例外を投げるので、実際の書き出しではこの案内に届かず、英語の汎用エラー `Animation extension is invalid, unsupported or exceeds its limits.` が警告に出ていた。

## 決定

- 解析の最後では値の検査だけを行い、大きさは見ない。大きさは上限の確認が見る。
- ファイルへ書く直前の `Write` の上限はそのまま残す。上限の確認を通らずに大きなデータが書かれることはない。

## 確かめ方

`AMovingPoseOverTheDataLimitIsLeftOutWithItsReason` が、136 秒のクリップを実際に解析し、8,192 フレーム未満で `Write` の上限を超えるポーズが解析で失敗せず、確認で外れて案内が警告に出ることを見る。

## 変わる動き

対話の書き出しでは、このポーズがあると上限の確認の画面が開く。これまでは解析で落ちていたので警告 1 行だけだった。ポーズの確認画面でも、無効な行でなく普通の行として並ぶ。どちらも上限は書き出し時に確かめるという元の設計どおりで、一括の書き出しでは画面を開かず外す。

`HumanoidAnimationData.cs` は `Validate` を `internal` にした 1 語だけ変えた。アプリ側のコピーは今回触っていないので、同じ内容に揃えるならアプリ側にも同じ変更が要る。動きには影響しない。
