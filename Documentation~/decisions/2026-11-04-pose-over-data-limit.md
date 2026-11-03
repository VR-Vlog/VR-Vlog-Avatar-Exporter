# 16MB を超える動くポーズを解析で失敗させず、上限の確認に渡す

- 日付: 2026-11-04
- 対象: `Editor/PoseSampling.cs`・`Editor/HumanoidAnimationData.cs`

## 背景

1 本で 16MB を超える動くポーズは、上限の確認（`PoseLimitPlan`）で「1本でデータの上限16MBを超えるため外しました」と案内して外す作りになっている。ところが解析の最後の `HumanoidAnimationData.Write` が同じ上限で先に例外を投げるので、実際の書き出しではこの案内に届かず、英語の汎用エラー `Animation extension is invalid, unsupported or exceeds its limits.` が警告に出ていた。

## 決定

- 解析の最後では値の検査だけを行い、大きさは見ない。大きさは上限の確認が見る。
- ファイルへ書く直前の `Write` の上限はそのまま残す。上限の確認を通らずに大きなデータが書かれることはない。

## 確かめ方

`AMovingPoseOverTheDataLimitIsLeftOutWithItsReason` が、136 秒のクリップを実際に解析し、8,192 フレーム未満で `Write` の上限を超えるポーズが解析で失敗せず、確認で外れて案内が警告に出ることを見る。
