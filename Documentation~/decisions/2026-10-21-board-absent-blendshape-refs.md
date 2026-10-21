# 元のメッシュに無いBlendShapeへの参照は、表情ボードでも書き出しと同じく省く

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Documentation~/ExperimentalExpressionCapture.md`・`CHANGELOG.md`

## 背景

表情ボードの表示とVRMの中身を突き合わせる一括検証を手元のアバターで回したところ、ジェスチャーの表情を持つアバターで、ボードの **その他の表情** が7件、VRMのカスタム表情が12件になった。差の5件は、顔のメッシュに今は無いBlendShape（作者が消した変形）を指すカーブが残っている表情だった。

- 表情メニューを読む通常の書き出し（`PreparedExpressionBindings`）は、元のメッシュにも出力のメッシュにも無いBlendShapeへの参照を「元モデルにも出力モデルにもないBlendShape参照を省略しました」として省き、残りの変形で表情を書き出す。参照が全部そうなら表情ごと省く。
- ボードの候補の判定と一覧を記録する書き出し（`BuildClipPoseUnchecked`）は、同じ参照を「候補のBlendShapeが表示中のメッシュにありません」として表情全体を対象外にしていた。#273 以降はボードに出さない。

VRChatでも存在しないBlendShapeへのカーブは何も動かさないので、見た目の上では書き出しのほうが元のアバターと合っている。

## 決定

- ボードの判定と記録でも、書き出しの前の処理より前のメッシュ（`NdmfExportPreparation.HadShape`。書き出しの `OriginalShapes` と同じ控え）にも表示中の書き出し用メッシュにも無いBlendShapeへの参照は省き、残りの変形で表情を作る。
- 省いた結果、適用する変形が1つも無いときは対応外にする。理由の文は書き出しが表情ごと省くときの文（「参照を解決した結果、有効な顔のBlendShapeがないため、この表情を省略しました。」）を使い、新しい訳は足さない。
- 元のメッシュにはあって書き出しの前の処理で消えたBlendShapeは、これまでどおり対応外にする。書き出しも同じ場合を `LostAfterPreparation` で止めている。処理後の Renderer の `sharedMesh` では見分けられず（処理はコピーをその場で書き換え、統合では元の Renderer が壊される）、処理前に控えた名前で判定する。

## 採らなかったもの

- 書き出し側を厳しくして、無い参照を含む表情を外すこと。VRChatで動いている表情が書き出しから消えるので、元のアバターから遠ざかる。
- 省いた参照をボードに表示すること。書き出し結果の一覧には通常の書き出しが出す行が残る。ボードの判定は表情単位で、参照ごとの理由を出す場所が無い。

## 確認したこと

- EditMode: `ShapesTheAvatarNeverHadAreSkippedLikeTheMenuExport`（無い参照と有る参照を持つ表情が、判定・プレビュー・記録で有る参照だけを使う）と、`ExplicitClipErrorsNeverPartiallyRecordOrAlterThePreview("wrong-shape")`（無い参照だけの表情は対応外のまま）、`ShapesLostByPreparationStayUnsupportedOnTheBoard`（処理で消えた参照は有る参照と混ざっても対応外のまま）。
- マージ後に一括検証で同じアバターを書き出し直し、ボードの件数とVRMの件数を比べる。
