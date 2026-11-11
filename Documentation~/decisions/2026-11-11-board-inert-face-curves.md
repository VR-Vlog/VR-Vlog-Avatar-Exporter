# 表情ボードでも、パーティクルの設定と PhysBone の切り替えの曲線を、どの顔でも無視する

- 日付: 2026-11-11
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`

## 背景

表情のクリップに、BlendShape と一緒にパーティクルの設定（放出量など）や PhysBone・Contact・VRCHeadChop のオン・オフの曲線が入っていることがある。表示切替でエフェクトを出す顔によくある形で、これらは VRM に入るものを何も変えない。

書き出し（`VrChatGestureExpressions.ReadPose`）は、どの顔でもこれらの曲線を読み飛ばす（`NeutralShapePlan.IsInertDynamicsBinding`）。表情ボードは、手の形で入る顔だけ読み飛ばし（`IgnoredBesideFace`・9e1708c5）、メニュー・FaceEmo・ボードに直接入れた顔では「顔の変形・ボーン・表示切替以外の変更を含みます」で断っていた。そのため、書き出せる顔が表情ボードに入らなかった。

## 決定

- 顔の横の曲線の共通の条件（`BesideFace`）で、`IsInertDynamicsBinding` に当たる曲線を、手の形で入るかどうかに関係なく外す。`IgnoredBesideFace` からは外し、手の形で入る顔だけが無視するボーンと Humanoid の曲線を残す。

表情ボードが受け付ける顔が増えるだけで、すでに受け付けていた顔の読み方と書き出す中身は変わらない。標準の表情の枠と自動の割り当ても同じ条件から始まるので、これらの曲線を含む顔を同じように扱う。

## 検討した代替案と不採用理由

- 書き出しの側でもこれらの曲線を断る: 手の形で入る顔はすでに書き出せていて、壊れる。VRM に影響しない曲線を理由に断る理由が無い。

## 確かめたこと

- 結果は PR に書く。
