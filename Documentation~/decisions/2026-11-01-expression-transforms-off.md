# 表情のボーンと表示の切り替えの書き出しを、アプリの対応まで止める

- 日付: 2026-11-01
- 対象: `Editor/ExpressionTransformCurves.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`CHANGELOG.md`・表情のボーンと表示の切り替えを確かめるテスト

## 背景

1.4.3（#546）から、表情のアニメーションが動かすボーンと表示の切り替えを、表情の拡張の `transforms` に書き出している。アプリ側はまだこれを再生しないため、依頼者の判断で、Exporterは1.4.2と同じ書き出しに戻してパッチリリースし、アプリ側の対応が済んでから新しい書き出しに切り替える。

## 決定

- コードは消さず、`ExpressionTransformCurves.Enabled`（既定は `false`）で入口を止める。設定項目は足さない。オフのとき `Read` は空を返し、読む側は1.4.2の動きに戻る。
  - シェイプキーも動かす表情はシェイプキーだけを書き出し、ボーンや表示の切り替えだけを動かす表情は書き出さない。
  - 表情ボードは、ボーンと表示の切り替えの曲線を1.4.2と同じく未対応として扱う。ジェスチャーの表情は、これまでどおりTransformの曲線を無視する。
- アプリの対応が済んだら `Enabled` を `true` にするか、フラグごと消して元に戻す。オンのときの動きを確かめるテストは、テストの中でオンにして残している。
- 口が重なっていた修正（#552）は `transforms` を使わないので、そのまま残す。1.4.3のVRCFuryなどの反映、デカールの修正、未リリースのMesh Cutterとスケールの修正も残す。
- オフのとき、静止位置と同じ値に置いただけのボーンの曲線を持つ表情も、1.4.2と同じく変化として扱う。#546で入った許容差による判定を残す案もあったが、1.4.2と同じ書き出しに揃えることを優先した。

## 確認

- `ExpressionBoneAnimationExportTests.WhileOffNoFaceWritesBonesOrVisibility` で、オフのとき拡張に `transforms` が無く、ボーンだけを動かす表情が書き出されないことを確かめる。
- `ExperimentalExpressionCaptureTests.WhileTheExportLeavesBonesOutTheBoardTreatsThemAsBefore` で、表情ボードの扱いが1.4.2と同じであることを確かめる。
- 初期状態で非表示の部品が古いアプリで表示される件は、1.2.0からの `VRVLOG_object_toggles` が部品のメッシュを書き出すことによるもので、この変更とは関係しない。この段階では直さない。
