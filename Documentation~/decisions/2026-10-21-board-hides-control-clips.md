# 顔を何も変えないアニメーションを表情ボードに出さない

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`CHANGELOG.md`

## 背景

表情ボードの表示とVRMの中身を突き合わせる一括検証（30体）で、5体のボードの **その他の表情** に、口パクや瞬きのオン・オフを切り替えるアニメーションが並んでいた。書き出したVRMにはどれも入っていない。中身を見ると、どれも口や目のBlendShapeを0にするカーブだけで、そのBlendShapeはアバターの元の値も0だった。顔を何も変えない。

ボードの候補（`RecommendClips`）は、SkinnedMeshRendererのBlendShapeのカーブを1本でも持つアニメーションをすべて候補にしていた。書き出しの側は、接触・外部入力で切り替わるFXのアニメーションのうち値がすべて0のもの（`VrChatAuthoredFaces`）を表情にしない。この差でボードにだけ並んでいた。

## 決定

- アバターのControllerにあるアニメーションのうち、次のすべてに当てはまるものは、ボードで対応していない表情として扱う（`ClipErrors`・`ClipError` が理由を返す）。
  - どのカーブも、すべてのキーの値と接線が0（ずっと0）。
  - ボードの基準の顔（準備後のアバター）に当てると、基準の顔と1つも変わらない。対象のBlendShapeが基準の顔で0ということ。
  - どのControllerでも、ハンドジェスチャーの条件で入るステートのアニメーションではない。
  - FaceEmoに登録された表情ではない。
- 候補の一覧（`RecommendClips`）からは外さない。対応していない表情はボードに出ないが、前に「書き出さない」へ移して保存した指定は数え続ける（`ExpressionBoard.UsesRecordRoute`）。候補から外すと、同じ保存内容のまま「この一覧を記録して書き出し」から「アバターの表情メニューをそのまま書き出し」へ切り替わってしまう。
- メッシュが見つからない・適用元が1つに決まらないものは、これまでどおりそちらの理由で対応していない表情になる。BlendShape以外のカーブやアニメーションイベントを持つものも、これまでどおりの理由で扱う。
- 理由の文言は4言語の訳を足した。ボードには出ないが、同じアニメーションをファイルとして足したときに表示される。

## 採らなかったもの

- 値がすべて0のアニメーションを一律に隠すこと。元の値が0でないBlendShapeを0に戻すアニメーション（標準の顔に戻すもの）は顔を変え、ハンドジェスチャーの表情としてVRMに入る。隠すとボードとVRMが逆向きにずれる。
- ハンドジェスチャーで入るアニメーションも隠すこと。ハンドジェスチャーの経路（`VrChatGestureExpressions`・`VrChatExpressionBaker`）は、顔を変えない表情も選べるように残してVRMに書く。隠すとボードとVRMが逆向きにずれる。
- 候補の一覧から外すこと（最初の版）。上の保存済みの指定の理由で採らなかった。
- 名前（LipSync・Blink など）で判定すること。名前の付け方はアバターごとに違い、表情の名前と区別できない。
- 書き出しの側でボードに合わせて0の表情を書き出すこと。顔を変えない表情をVRMに入れても使い道がない。

## 確認したこと

- EditMode: `ControllerClipsThatChangeNoFaceAreUnsupported` 4件（元が0のBlendShapeを0にするだけなら対応していない表情になる、元が0でないものを0に戻す・0でない値にする・ハンドジェスチャーの条件で入るものは表情のまま、どれも候補には残る）。`ExperimentalExpressionCaptureTests`・`DirectFxExpressionDiscoveryTests`・`GestureBlendTreeTests`・`VrChatMenuExpressionTests` のほかのテストの結果は修正前と同じ（失敗はModular Avatarなどが無い環境の7メソッドで、develop と同じ）。
- マージ後に一括検証で同じアバターを書き出し直し、ボードからこれらのアニメーションが消えることを確かめる。
