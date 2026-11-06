# 視線・顔の範囲・接続・表情のボーン・プレビューの枠も HumanoidBones.Resolve でボーンを引く

- 日付: 2026-11-06
- 対象: `Editor/VrmLookAtExport.cs`・`Editor/FacialProjectionScope.cs`・`Editor/ExportAttachmentSession.cs`・`Editor/ExpressionTransformCurves.cs`・`Editor/PreviewRendering.cs`・`Editor/PoseExportSession.cs`

## 背景

[揺れものと拘束の除外](2026-11-05-constraint-humanoid-bones.md)を `HumanoidBones.Resolve` に寄せたとき、書き出しのコピーなどを `Animator.GetBoneTransform` で直接読む処理が 5 か所残った（あとでポーズの書き出しの 2 か所も見つけた）。一度も表示していない Animator では `GetBoneTransform` が null を返すことがあるとされ、そのときは次のようになる。

- `VrmLookAtExport.Create`: 頭か目が null だと視線の設定を作らず、警告なしで視線が落ちる
- `FacialProjectionScope.Create`: 顔の範囲を推定しない
- `ExportAttachmentSession`: 人型のボーンの一覧と接続先の候補から抜ける
- `ExpressionTransformCurves`: 表情が動かすボーンから人型のボーンを除けない
- `PreviewRendering.FrameHead`: 顔でなく全体を枠に入れる
- `PoseExportSession`: ポーズのボーンが書き出しのコピーから失われたとみなし、書き出しを止めるかそのボーンを省く

## 決定

7 か所とも `HumanoidBones.Resolve` で引く。`Resolve` は `GetBoneTransform` が答えたときはその値を返すので、結果が変わるのは未初期化の Animator で null が返ったときだけになる。`ExpressionTransformCurves` の中の同じ名前のメソッド `HumanoidBones` は、クラス名と重なるので `HumanoidTransforms` に変えた。

書き出しの中では、先に `ValidatePreparedHumanoid` が必須ボーンを `GetBoneTransform` で確かめ、取れなければ書き出しを止める。このため書き出しの中の視線・顔の範囲・接続・ポーズは、必須ボーンについては今までも null で黙って落ちることはなかった。今回の変更で挙動が変わりうるのは、必須でないボーン（目など）と、書き出しの外でシーンのアバターや確認用のコピーを読む処理（表情ボードと瞬きの確認画面の `FrameHead`、VRChat のジェスチャーの表情を読むときの `ExpressionTransformCurves.Read`）になる。`ExpressionTransformCurves` は、Humanoid の Avatar が有効でない（`isValid` が false の）とき、以前は `GetBoneTransform` の例外が外に出たが、今は人型のボーンが無いものとして読む。

## 再現できていないこと

前の決定と同じく、この null は Unity 2022.3.22f1 の batchmode で再現していない。修正前に落ちるテストは作れていないので、既存のテストが変わらず通ることだけを確かめる。

`UniVrmOneClickExporter` の必須ボーンの確認（`ValidatePreparedHumanoid`）は、取れなければ書き出しを止めるので変えていない。ポーズの確認画面（`PoseReviewWindow`）など、書き出しのコピーでなくプレビュー用のコピーを読む処理は、今回の対象にしていない。

## 確認

`VrmLookAtExportTests`・`FacialProjectionTests`・`FacialDepthEnvelopeTests`・`AttachmentConnectionTests`・`ExpressionBoneAnimationExportTests`・`InactiveRootExportTests`・`AnimatedManualPoseExportTests`・`HumanoidPoseTests`・`PoseRegistrationTests`・`RootScaleExportTests` を Unity 2022.3.22f1 で回す。
