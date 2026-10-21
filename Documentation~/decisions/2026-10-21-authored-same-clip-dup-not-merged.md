# 同じクリップを顔の範囲に絞った違いだけの「名前 (2)」を消す変更は入れない

- 日付: 2026-10-21
- 対象: なし（この記録だけ）
- 関連: #310（閉じた）・b73dd69

## 背景

30体の一括検証（b944be9）で、4体のVRMに、手のジェスチャーの表情と同じアニメーション付きの顔が「名前 (2)」としてもう1つ入っていた。手のジェスチャーの経路（`VrChatGestureExpressions`）はBlendShapeだけのクリップを顔の範囲（`FacialProjectionScope`）に絞らず、接触・外部入力の経路（`VrChatAuthoredFaces`）は必ず絞る。顔の範囲の外のBlendShapeが1本あると、`Equivalent` がアニメーションのチャンネル数の違いだけで別の表情と判定していた。

#310 で、前の表情が同じクリップを再生する手のジェスチャーの表情なら、顔の範囲の中だけで比べるようにした。

## 決定

#310 は入れずに閉じる。

develop 0027a3a と、develop に #310 を足したものの2回で30体を書き出して比べると、30体ともVRMの中身が同じだった。「名前 (2)」は develop 0027a3a ではすでに出ない。b73dd69 で、接触・外部入力の経路は手のジェスチャーとして読み終えたFXのステートを読み直さなくなったためである。重複は同じステートを2回読んでいたもので、このステートを読まなくなった。

#310 が直すのは、別のステートが同じクリップを再生する場合だけである。30体に当てはまるものはなく、実例のない分岐は足さない。

## 実例が出たとき

別のステートが同じクリップを再生して「名前 (2)」が出るアバターが見つかったら、#310 の変更から作り直す。

- コミット: e7f26e6（`Editor/VrChatAuthoredFaces.cs` の `Equivalent` と `Tests/Editor/FacialProjectionTests.cs` のテスト `AContactFaceCutFromTheSameHandGestureClipIsNotAddedTwice`）、dd5987d（`Tools/run-unity-compatibility.py` に新しいテストを固定する1行）
- ブランチは消したが、閉じた PR #310 から取り出せる（`git fetch origin pull/310/head`）
