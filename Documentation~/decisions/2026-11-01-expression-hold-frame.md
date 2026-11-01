# 動く表情の固定の顔は、顔を保っている区間の中ほどで読む

- 日付: 2026-11-01
- 対象: `Editor/ClipFaceTime.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureSession.Thumbnails.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Schema/VRVLOG_expression_animations.2.schema.json`・`Documentation~/ExperimentalExpressionCapture.md`・`Tests/Editor/ClipFaceTimeTests.cs`・`Tests/Editor/ExpressionPresetTests.cs`・`Tests/Editor/ExpressionAnimationV2WriterTests.cs`

## 背景

動く表情（ジェスチャー・一覧・FaceEmo から入り、値が変わるカーブを持つもの）は、カーブを表情のアニメーション（`VRVLOG_expression_animations` の schema 2）に書き、VRM の表情（`morphTargetBinds`）には `time = duration`、つまり最後のフレームの形を入れていた（[2026-10-19-expression-animations-v2](2026-10-19-expression-animations-v2.md)）。表情ボードでクリップをプリセット（sad など）に当てたとき、ボードの既定の時刻・サムネイル・クリップの検査も、同じく `clip.length` で読んでいた。

動くループのクリップには、無表情から始まり、途中で顔を作って保ち、最後に無表情へ戻るものがある。この形では最後のフレームはほぼ始まりの顔になる。実際に、12.8 秒のループの泣き顔のアバターでは、0.7–3.7 秒と 4.0–12.6 秒にジト目・悲しみ目・下まぶたを 60〜90 で保っているのに、最後のフレームでは下まぶたが 40 で他は 0 になる。この VRM の表情を固定の顔として使うと、ほぼ無表情に見える。

## 決定

クリップやカーブから固定の顔を読む時刻（顔の時刻）を `ClipFaceTime.Of` の 1 か所で決める。VRM の表情の焼き込み（`VrChatExpressionBaker`・`Expression.FinalPoints`）、プリセットの焼き込み、ボードの既定の時刻、サムネイル、クリップの検査はすべてこれを使う。決め方は次のとおり。

- 値が変わるシェイプキーのカーブだけを見る。値が変わらないクリップ（1 フレームのクリップを含む）は、どの時刻で読んでも同じなので最後のフレームのまま読む。書き出す VRM は変更前と同じになる。
- カーブが止まっている区間は、最初のキーより前と最後のキーより後（clamp のとき）、同じ値で接線が平らなキーの間、ステップのキーの後とする。カーブは `ExpressionAnimationData.Curve.Evaluate` と同じ読み方で見る。
- 長さの半分以上止まっているカーブを「保つカーブ」とする。まばたきのように短く動くカーブも、ずっと動き続けるきらめきのカーブも、止まっている時間が半分に満たなければ時刻の決定には使わない。保つカーブでないものは、決まった時刻の値で焼く。
- 保つカーブがすべて同じ値で止まっている区間を「保つ区間」とし、保つカーブの値の和が最も大きい区間を選ぶ。同じなら長いほう、さらに同じなら先のほうを選ぶ。無表情で待つ区間より、顔を作っている区間が選ばれる。
- 選んだ区間の中ほどの時刻で読む。保つカーブや保つ区間が無いときは最後のフレームで読む。

上の泣き顔では 3.967–12.583 秒の区間が選ばれ、8.275 秒で読む。区間の外で動くきらめきのカーブは、その時刻の値で入る。

schema 2 の description は「VRM の表情は 0 から duration のどこか 1 つの時刻の形を持つ（書き出しは顔を保つ区間の中ほど、無ければ duration）。再生側はどの時刻かを前提にしない」に改めた。再生はチャンネルの変形を毎フレーム上書きするので、どの時刻の形を VRM の表情に入れても VR Vlog での動きは変わらない。アプリの再生（`ExpressionAnimationPlayback.Compose`）も VRM の表情の重みとの差を足す作りで、時刻を前提にしていないことをアプリの develop で確かめた。サムネイルはアプリが別に duration の 2/3 で描く。

## 選ばなかったもの

- 先頭のフレームで読む: 無表情から始まるクリップでは同じ問題が起きる。
- 全体の平均や最大値で読む: どの時刻にも無い顔ができる。まばたきときらめきも混ざる。
- まばたきやきらめきのシェイプキーを名前で外す: アバターごとの名前に頼る特例になる。止まっている時間で決めれば名前を見ずに済む。
- プリセットの経路だけを直す: 固定の顔が経路で違う時刻になる。ジェスチャーから入った表情をアプリ側で別名に付け替えて使う場合にも、同じ問題が残る。

## 変えないもの

- 動くカーブは、これまでどおり先頭から最後までをアニメーションに書く。
- 「ループの最後が元の顔に戻る」の判定（`EndsNeutral`）は、顔の時刻の形が元の顔かで決める。顔を保つ区間が無いループは、これまでどおり最後のフレームで判定する。
- メニューの FX の表情（`ExpressionSettlement`）の読み方は変えない。

## 確かめたこと

- `ClipFaceTimeTests`: 0 から始まり途中で保つクリップは区間の中ほど、ループは保つ区間の中ほど、無表情で長く待つクリップは顔を作る区間、ステップのキーは保つ値の区間、値が変わらないクリップと 1 フレームのクリップは最後のフレーム、保つ区間の無いランプは最後のフレームを返す。
- `ExpressionPresetTests.PresetFromAMovingClipBakesTheFaceItHolds`: プリセットに当てた動くクリップが保つ顔で焼かれる。
- `ExpressionAnimationV2WriterTests.VrmExpressionOfAMovingFaceIsTheFaceItHolds`: 0→80 で保ち 40 で終わるカーブの VRM の表情が 80 の形になり、書き出した VRM をアニメーションつきで再生した形は元のアバターの同じ時刻の形と一致する。
- 書き出しの before/after は PR に書く。

## 影響

動く表情を持つアバターだけ、VRM の表情（VR Vlog 以外のアプリで見える顔と、アプリが固定の顔として使う形）が変わる。値が変わらない表情だけのアバターは、書き出す VRM が変わらない。
