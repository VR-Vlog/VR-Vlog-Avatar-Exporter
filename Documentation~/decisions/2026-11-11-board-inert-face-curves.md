# 表情ボードでも、パーティクルの設定と PhysBone の切り替えの曲線を、どの顔でも無視する

- 日付: 2026-11-11
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`

## 背景

表情のクリップに、BlendShape と一緒にパーティクルの設定（放出量など）や PhysBone・PhysBone のコライダー・Contact・VRCHeadChop のオン・オフの曲線が入っていることがある。表示切替でエフェクトを出す顔によくある形で、これらは VRM に入るものを何も変えない。

書き出し（`VrChatGestureExpressions.ReadPose`）は、どの顔でもこれらの曲線を読み飛ばす（`NeutralShapePlan.IsInertDynamicsBinding`）。表情ボードは、手の形で入る顔だけ読み飛ばし（`IgnoredBesideFace`・9e1708c5）、メニュー・FaceEmo・ボードに直接入れた顔では「顔の変形・ボーン・表示切替以外の変更を含みます」で断っていた。そのため、書き出せる顔が表情ボードに入らなかった。

## 決定

- 顔の横の曲線の共通の条件（`BesideFace`）で、`IsInertDynamicsBinding` に当たる曲線を、手の形で入るかどうかに関係なく外す。`IgnoredBesideFace` からは外し、手の形で入る顔だけが無視するボーンと Humanoid の曲線を残す。

表情ボードが受け付ける顔が増えるだけで、すでに受け付けていた顔の読み方と書き出す中身は変わらない。標準の表情の枠と自動の割り当ても同じ条件から始まるので、これらの曲線を含む顔を同じように扱う。そのため、新しく受け付けるようになった顔が名前で標準の表情に自動で割り当たるアバターでは、何も変えずに書き出した VRM の標準の表情が変わる（かんたん転送も同じ既定値から決めるので、通常と一致したまま）。

書き出しで別の理由（パスが一意に決まらない・値が有限でない）で止まる曲線は、ボードでは見ない。手の形で入る顔では前からこうで、今回はそこを広げない。

## 検討した代替案と不採用理由

- 書き出しの側でもこれらの曲線を断る: 手の形で入る顔はすでに書き出せていて、壊れる。VRM に影響しない曲線を理由に断る理由が無い。

## 確かめたこと

- 検証用の PC の EditMode で、足したテストと `ExpressionBoneAnimationExportTests`・`QuickTransferMatchesDefaultNormalTests` が通る。`ExperimentalExpressionCaptureTests` の失敗 15 件は直す前の develop と同じ（`-nographics` で GPU から読み戻せないものと、検証用のプロジェクトの別のツールの初期化エラー）。
