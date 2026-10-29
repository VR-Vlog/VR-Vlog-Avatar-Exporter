# 表情ボードの顔が 0 にするシェイプキーを、VRM の表情でも 0 に戻す

- 日付: 2026-10-29
- 対象: `Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/AuthoredExpressionEndpoints.cs`・`Editor/PreparedExpressionBindings.cs`・`Tests/Editor/NeutralShapeEndpointTests.cs`・`Tests/Editor/PreparedRendererProvenanceTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

普段の顔（基準形）でシェイプキーが 0 以外のアバターでは、FX の表情アニメーションがそのシェイプキーを 0 にして顔を作ることがある。VRChat ではその表情の間、そのシェイプキーは 0 になる。

表情ボードで喜怒哀楽などのプリセットに入れた顔は、`AuthorPresets` で VRM の表情の bind に書く。ここで重みが 0 の値を書かずに捨てていた（`face.Morphs.Where(morph => morph.Weight > 0)`）。VRM の表情は基準形からの差分として足されるので、捨てた 0 のシェイプキーは基準形の値のまま残り、表情に重なっていた。

例: 基準形が A=35・B=0 で、表情が A=0・B=100 の顔。修正前の VRM の表情は B だけを動かし、アプリでは A=35 と B=100 が重なった顔になっていた。

0 を捨てていたのは、VRM で作者が書いた重み 0 の bind が「そのチャンネルを使わない」ことを表す、という書き出しの決まり（`AuthoredZeroOnlyRemainsDisabledWhileExplicitFxZeroCanReopen` など）に合わせるためだった。

## 決定

- 表情ボードがプリセットに入れた顔は、アニメーションが明示的に 0 にしたシェイプキーも bind に書く（`AuthorPresets`）。
- 書き出しは、表情ボードが作ったプリセットの枠（`authorCopyExpressions` の後に新しく入った表情。custom は除く）を `clipZeroPresets` として持つ。この枠の表情の重み 0 は「基準形の値を 0 に戻す」として扱う。
  - 基準形が 0 以外のシェイプキー: 表情の終点（`__VRVlog_Endpoint_<hash>`）に 0 として入れる。終点の差分は `0 − 基準形` になる。
  - 基準形が 0 のシェイプキー・メッシュが無いもの・範囲外のもの: 何も変えないので書かない。終点を作れなかった表情からも 0 の bind は外す。VRM の重み 0 の bind としては残さないので、使わないチャンネルとして予約されることも、最適化で消さないシェイプキーとして扱われることもない。
  - NDMF の処理後に付け直すとき（`RebindAuthoredExpressions`）は、ほかの bind と同じく追従する。シェイプキーが処理で消えたときは、0 に戻す対象が無いので黙って外す（厳密モードでも例外にしない）。
- アニメーションが触れていないシェイプキーは、これまでどおり基準形の値を残す。
- VRM で作者が書いた表情の重み 0 は、これまでどおり「使わないチャンネル」として扱う。

## 確認

- `NeutralShapeEndpointTests.ClipZeroResetsTheDefaultFaceAndUnkeyedShapesKeepIt`: 基準形 A=35・C=50 で、表情が A=0・B=100 の顔。表情ボードの顔では A が 0 に戻り（A の差分が `−0.35×dA`）、B は 100、触れていない C は 50 のまま。VRM で作者が書いた同じ表情では、これまでどおり A は 35 のまま。
- `NeutralShapeEndpointTests.ClipZeroAtAZeroDefaultFaceAddsNoBind`・`ClipZerosThatChangeNothingAreLeftOut`: 何も変えない 0 は bind に残らない。
- `PreparedRendererProvenanceTests.ClipZeroOnARemovedShapeIsLeftOut`: 処理で消えたシェイプキーへの 0 は、厳密モードでも例外にせず外す。
- `ExperimentalExpressionCaptureTests.APresetKeepsTheZerosItsClipKeys`: プリセットの bind に 0 が入る。

## 欠点

- 基準形が 0 以外のシェイプキーを 0 にする表情では、メッシュごとに終点のシェイプキーが 1 つ増える（これまでも 0 以外の値を持つ表情では作っていたもの）。
- VRM で作者が書いた表情の重み 0 は、基準形が 0 以外でも 0 に戻さない。VRM の作者が「使わない」の意味で 0 を書いている場合と見分けられないため。
