# 表情のアニメーションを schemaVersion 2 で書き、元のBlendShapeを直接動かす

- 日付: 2026-10-19
- 対象: `Editor/VrChatExpressionBaker.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/AvatarFormatRequirements.cs`・`Editor/ExportFallbackConfirmation.cs`・`Editor/VrChatExpressionMenu.cs`・`Editor/VrChatFxExpressions.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/VrChatAuthoredFaces.cs`・`Editor/FaceEmoExpressions.cs`・`Editor/PoseExportSession.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionAnimationV2WriterTests.cs` ほか既存のテスト・`CHANGELOG.md`・`Formats/README.md`

## 背景

`2026-10-19-expression-animations-v2.md` で `VRVLOG_expression_animations` の schemaVersion 2 と `VRVLOG_requiredAppVersions` を足したが、書き出しはまだ v1 を書いていた。v1 では、アニメーションのある表情は静的な形に初期フレームを入れ、point ごとに「初期フレーム→knot」の差分を `__VRVlog_Anim_`／`__VRVlog_Menu_` の変形として焼き込み、名前で参照する。カーブで1つのBlendShapeを動かすだけの表情でも、point の数だけ変形が増える。

`2026-10-19-expression-direct-binds.md` で静的な表情は元のBlendShapeを重み付きで直接参照するようにした。書き出し用の複製のBlendShapeは「1フレームに `Evaluate(F) − Evaluate(初期値)` を持つ形」なので、カーブの値が初期値 `r` とそのフレームの重み `F` のあいだにある限り、アニメーションも同じBlendShapeを直接動かせる。

## 決定

- アニメーションするチャンネルは、表情の静的な形に入れない（静的な形ではそのチャンネルを初期値のままにする）。静的な形とそのハッシュには、カーブの無いチャンネルだけを使う。
  - 読み込み側（FX・ジェスチャー・FaceEmo）は、アニメーションするチャンネルの静的な値を `Evaluate(0)` として記録している。複数のクリップで同じチャンネルの値が合うかの確認（`Prepare`）はこの値のまま比べる。
- チャンネルごとに、次をすべて満たすときは焼き込まずに2つの point `{r, null}` と `{F, 元のBlendShape}` にする（`VrChatExpressionBaker.DirectAnimation`）。
  - 元のBlendShapeが1フレームで、静的な表情の直接参照と同じ条件（`DirectWeight` が重み `F` で1を返す）を満たす。
  - カーブの最小値・最大値が有限で、`r ≤ min`・`max ≤ F`・`F > r`。`PlayerSettings.legacyClampBlendShapeWeights` が有効なときは `r` を `AvatarBaseShape.ClampSourceWeight` で丸め、丸めで最小値・最大値が変わるカーブは直接にしない。
  - point の値が schema の範囲（±10000）に収まる。
- 条件を満たさないチャンネル（初期値より下げる・複数フレームの曲がった変形など）は従来どおり焼き込む。ただし差分の基準は `Evaluate(0)` ではなく初期値 `r` にし、値が `r` の point は変形を指さない。変形を指さない point は何も動かさないので、v2 の意味と合う。
- VRMのカスタム表情には、静的な形の参照に加えて、time = duration で `Channel.Weights` が選ぶ2つの point の変形を `1 − blend` と `blend` の重みで登録する（`Expression.FinalPoints`）。VR Vlog以外のアプリではアニメーションの最後のフレームの顔になる。同じ（node, index）が重なったときは最初の1つだけを残す（D1と同じ）。
- point の目標は書き出し後に `{node, index}` へ解決する。直接のものは `ExportOptimizationBindings`（`MenuBinding`）で追い、`Capture` に渡す `menuMorphs` にもその BlendShape を入れる。焼いたものは従来どおり mesh の extras.targetNames から引く。
  - 1つのアニメーションの中で2つの point が同じ（node, index）になった（最適化ツールが2つの BlendShape を1つにまとめた）ときは、アプリが2つを別々に動かせないので、そのアニメーションだけを省き、VRMの表情（最後のフレーム）は残す。警告に表情名を出す。
  - 静的なチャンネル（`Targets`・`Direct`）と point が同じ（node, index）になったときも同じに扱う。再生中はアプリがカーブの値を書くので、静的な値が警告なく消えるのを避ける。
- 同じ（node, index）への2つ目の bind の重みが最初の bind と1e-4より違うときは、D1と同じ文言で「最初の値だけを使った」と警告する。静的な直接参照と最後のフレームの bind の両方がこの処理を通る。
- 「ループの最後が元の顔に戻る」は、全チャンネルで `Curve.Evaluate(duration)` が初期値に等しいかで決める。最後のフレームが2つの焼いた point の打ち消し合う bind になる場合も元の顔として保存前の確認に出す（その bind で選べるので、ゼロのマーカーは足さない）。
- 値が変わらないカーブで直接動かせないもの（焼くと point が1つになり `Channel.Weights` が使えない）は、`InvalidOperationException` で止める。直接動かせる一定のカーブ（同じ BlendShape の静的な別名と一緒に来る場合）は従来どおり2つの point にする。
- 拡張は `ExpressionAnimationData.WriteDocument`（schemaVersion 2）で書き、`expressions` に登録した表情をすべて `source` 付きで載せる。登録する表情が0件なら拡張を書かない。`extensionsUsed` にだけ足し、`extensionsRequired` には足さない（古いアプリは標準の表情として読める）。
  - `source`: VRChatのメニュー（FXのメニュー由来を含む）は `menu`、ジェスチャー・FXのジェスチャー・接触や外部入力・AFKの顔は `gesture`、FaceEmo は `faceEmo`。
- ループの最後が元の顔に戻る表情（直接参照・残りの差分が無く、最後のフレームの変形が無いか全チャンネルが初期値で終わる）は、メッシュごとに共有するゼロのマーカーで選べる状態を保ち、保存前の確認に「ループの最後が元の顔に戻る表情」として出す（`ExportFallbackKind.ExpressionEndsNeutral`、結果は「近似」）。VR Vlogでは動くが、ほかのアプリでは選んでも顔が変わらない。
- asset.extras の `VRVLOG_requiredAppVersions` は、書き出しの最後のバイト列を `AvatarFormatRequirements.Write` に通して、`extensionsUsed` にある `VRVLOG_` 拡張ごとに `AvatarFormatVersions` の表の版を書く。対象の拡張が無ければキーを書かず、残っていた古いキーは消す。要件が変わらないときは同じバイト列を返す。

## 確認したこと

- `Tests/Editor/ExpressionAnimationV2WriterTests.cs` で、1フレームのBlendShapeをカーブで動かす表情が焼き込みなしの2つの point になり、VRMの表情が最後のフレームの重みを持つこと、初期値より下げるカーブと複数フレームのBlendShapeが初期値を基準に焼き込まれること、書き出したVRMを v2 の意味で再生した形が元のアバターの同じ時刻の形と t = 0・duration/4・duration/2・duration で位置の差1e-5以下で一致すること、ループの最後が元の顔に戻る表情が登録され保存前の確認に出ること、`source` が menu・gesture・faceEmo で入ること、`VRVLOG_requiredAppVersions` が拡張に合わせて書かれ拡張が無いと書かれないこと、統合・改名されたBlendShapeを point が追うこと、1つの morph にまとめられたときはアニメーションを省いて警告を出すこと、`ReadDocument` で読めて v1 の `Read` は拒むことを確かめる。統合は最適化ツールを入れずに、`ExportOptimizationMarker` の経路を書き換えて再現している。
- 同じ morph にまとめられた静的な BlendShape とアニメーションの point（`ExportOptimizationMarker` の経路の書き換えで再現）、初期値で終わるが最後の区間の途中で止まるカーブ、直接動かせない一定のカーブを、それぞれテストで確かめる。直接参照の修正（同じ morph に違う値が来たときの警告）を取り込み、その警告は `Add` の重複の処理（`Bind`）に移した。
- Unityのテスト（EditMode、Unity 2022.3.22f1）は全3833件のうち3649件が通過、31件が失敗、153件がskip。失敗した31件は変更前の基準（3817件のうち3633件が通過、31件が失敗）と同じで、テスト用のプロジェクトに入っていない外部パッケージやモデルを要するもの。
- `Tools/run-behavior-tests.ps1` のホスト上の挙動テストは通過した。
