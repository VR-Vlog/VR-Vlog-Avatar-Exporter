# 表情のボーンと表示の切り替えを、表情の拡張の transforms に書く

- 日付: 2026-10-31
- 対象: `Editor/ExpressionTransformCurves.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/FacialProjectionScope.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExpressionBoneAnimationExportTests.cs`・`Tests/Editor/ExpressionAnimationDocumentTests.cs`・`Tests/Editor/FacialProjectionTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`
- アプリ側の設計: VR Vlog の `Docs/decisions/2026-10-31-expression-bone-animation.md`（Epic #2760）

## 背景

ジェスチャー・メニュー・FaceEmo の表情クリップが BlendShape 以外のカーブ（Transform・GameObject の `m_IsActive`・マテリアル）を 1 本でも持つと、その表情を警告付きで書き出していなかった。検証に使ったアバターは表情クリップの全部が目と口のボーンのカーブを持つので、表情がほとんど書き出されなかった。そのボーンのカーブの大半は書き出し時の姿勢と同じ値を持つだけで、実際にボーンを動かす表情は数本だった。動かすボーンには揺れ物（PhysBone から書き出す SpringBone）のジョイントも含まれる。

アプリ側では、`VRVLOG_expression_animations`（schemaVersion 2）のルートに省略できるキー `transforms` を足し、ボーンと表示の切り替えを表情と同じ時刻で再生することにした。データの形と読み書き（`ExpressionAnimationData.WriteDocument`・`ReadTransforms`）は `ebb8606e` で足してある。ここでは書き出し側を決める。

## 決定

表情クリップのカーブを種類ごとに扱う。

1. BlendShape のカーブは今までどおり読む。
2. Transform のカーブ（`m_LocalPosition`・`localEulerAnglesRaw`／`m_LocalEulerAngles`・`m_LocalRotation`・`m_LocalScale`）は `ExpressionTransformCurves.Read` で読む。
   - 書き出し用の複製の姿勢から動かない成分は捨てる。オイラー角は 0.05 度、位置はアバターの実寸で 0.0005 m（親の拡大のいちばん大きい軸で割ったローカルの値。100 倍のアーマチュアの下では 0.000005）、拡大は 0.0005 の差までを同じ値とみなす。四元数は成分ごとに sin(0.025 度) までの差を同じとみなし、q と −q を同じ回転として比べる。オイラー角は 360 度の整数倍の違いを同じ値として比べ、3 成分とも一定のときは回転としても比べる。
   - 「動かない」は、キーの値とベジェの制御点を含めた範囲（`Curve.Range`）が許容差に収まることで判定する。接線が 0 でないキーは範囲が広がるので残る。
   - Humanoid のボーン（視線に使う目を含む）のカーブは捨てて警告する。アプリの姿勢・視線・エモートが毎フレーム書くため。
   - アバターのルート、ルートの拡大が 1 でないときのルート直下の位置と拡大（書き出しで拡大を焼き込み直すため）、同じパスのオブジェクトが複数あるものは、ノードに結び付かないとして捨てて警告する。クリップのパスにオブジェクトが無いカーブは何も動かさないので黙って捨てる。
   - 揺れ物のジョイントは残す。アプリはジョイントの基準の向きとして使う。
   - 同じオブジェクトにオイラー角と四元数の両方、または 2 通りのオイラー角の綴りがあるクリップは、今までどおり非対応として表情ごと落とす。
3. `m_IsActive` のカーブは 0 から 1 の範囲のものだけを表示の切り替えとして読む。書き出し時の表示と同じ値で一定のものは捨てる。書き出し後に `ExpressionTransformCurves.Resolve` が、書き出すオブジェクト（`ExportRendererSelection.ActiveUnder` で表示されるもの）の切り替えだけを `active` のトラックにする。書き出さないオブジェクトを表示する表情は、その表情の表示の切り替えを全部捨てて警告する。片方だけ切り替わる見た目を避けるため。書き出さないオブジェクトを隠すだけの切り替えは何も変えないので黙って捨てる。
4. マテリアル・オブジェクト参照・ほかのカーブは今までどおり非対応で、表情ごと落として警告する。
5. 値は UniVRM の書き出しと同じ X 反転で glTF の空間にする。位置は (−x, y, z)、オイラー角は (x, −y, −z)、四元数は (x, −y, −z, w) で、符号を変える成分は値と接線の両方を反転する。ノード番号は書き出しの `afterExport`（`converter.Nodes`）で決める。
6. 変化が何も残らない表情は今までどおり書き出さない。ボーンか表示の切り替えだけの表情は、bind の無い VRM のカスタム表情にして `expressions` に名前を入れ、`transforms` に入れる。`animations` には入れない（古い読み取りは 0 チャンネルの `animation` を拒否するため）。bind の無い表情の `overrideBlink`・`overrideMouth`・`overrideLookAt` は `none` にする。アプリはこの値で選んだ表情の間のまばたき・口・視線のトラッキングを止めるので、顔を変えない表情では止めない。BlendShape を持つ表情は今までどおり `block`。書き出しでボーンと表示の切り替えを全部捨てた表情は書き出さず、残る表情が 1 つも無ければ拡張を足さない（空の拡張は読み取りが拒否する）。
7. `transforms` の時間は、BlendShape かボーン・表示のどれかが時間で変わるときクリップの長さとループの設定を使い、`animations` の同じ表情と同じ値にする。BlendShape が一定で、ボーンが書き出し時と違う値で一定のときは時間 0・ループなしにする。
8. ボーンと表示を持つ表情は、同じ BlendShape の値を持つ別の表情とまとめない。
9. schemaVersion は 2 のままにする。`AvatarFormatVersions` と `VRVLOG_requiredAppVersions` は変えない。拡張は今までどおり `extensionsUsed` だけに入れ、`extensionsRequired` には入れない。表情クリップにボーンと表示のカーブが無い（または書き出し時の姿勢と同じ値しか持たない）アバターの出力は、`transforms` を書かず今と同じバイト列になる。

顔のメッシュを隠す `m_IsActive` も表示の切り替えとして書くようにした。これまでは顔の BlendShape への置き換えができないとして表情ごと落としていた。

表情ボード（`ExperimentalExpressionCaptureSession`）の「顔の変形以外の変更を含みます」の案内は、ボーンと表示の切り替えを含めない「顔の変形・ボーン・表示切替以外の変更を含みます」にした。BlendShape を元の値のまま持つだけのクリップを「顔を何も変えないアニメーション」として外す判定は、ボーンか表示を書き出し時の姿勢から動かすクリップを外さないようにした。BlendShape のカーブを 1 本も持たないクリップは、表情ボードでは今までどおり全身ポーズとして扱い、表情にしない。

## 確かめたこと

- EditMode テスト（`ExpressionBoneAnimationExportTests`）で、浮動小数の誤差を含む書き出し時と同じ値のカーブ（オイラー角・360 度回った角度・符号を反転した四元数・位置・拡大・表示）が捨てられること、キーの間だけ姿勢から外れるボーンが残ること、Humanoid のボーンが警告付きで捨てられることを確かめた。
- 同じテストで VRChat SDK の FX の表情を書き出し、オイラー角・位置・表示のトラックの値と接線が X 反転されること、ノードに結び付かないボーンと書き出さないオブジェクトを表示する表情が警告付きで捨てられること、揺れ物のジョイントが SpringBone のジョイントのまま `transforms` に入ること、ボーンだけの表情が bind の無いカスタム表情になり `animations` に入らないこと、時間が `animations` と一致することを確かめた。
- 同じテスト（`TransformsReadBackAsTheClipPose`）で、位置・オイラー角・四元数・拡大のトラックをアプリと同じ式（位置 (−x, y, z)、`Quaternion.Euler(x, −y, −z)`、四元数 (x, −y, −z, w)、拡大はそのまま）で Unity の値に戻すと、クリップを Unity で再生したときの姿勢と複数の時刻で一致することを確かめた。
- ボーンと表示のカーブが無いアバターと、書き出し時と同じ値のカーブだけを持つアバターの出力が、`transforms` を持たず同じバイト列になることを確かめた。
- 同じテストで、100 倍の親の下で 1 cm 動くボーンが残ること（`SmallMovesUnderAScaledParentAreKept`）、bind の無い表情だけ 3 つの override が `none` になること、書き出さないオブジェクトを表示するだけの表情しか無いとき拡張を足さずに書き出せること（`AFaceThatLosesEveryTrackLeavesTheFileAlone`）を確かめた。
- `ExpressionAnimationDocumentTests` で `transforms` の書き込みと読み込みが往復することを確かめた。
- 書き出し結果の比較（`Tools/run-export-regression.ps1`・`Tools/compare-exports.py`）で、変更前（`7ef5e417`）と変更後を検証に使ったアバター 2 体で比べた。結果は下の「書き出しの比較」に書く。

## 書き出しの比較

変更前（`7ef5e417`）と変更後を、検証に使ったアバター A・B で 2 回ずつ書き出して比べた（Unity 2022.3.22f1・UniVRM 0.131.0）。

| | A 変更前 | A 変更後 | B 変更前 | B 変更後 |
| --- | --- | --- | --- | --- |
| 拡張の `expressions` | 13 | 47 | 15 | 26 |
| 拡張の `animations` | 0 | 11 | 10 | 10 |
| 拡張の `transforms` | なし | 12 | なし | 9 |
| 書き出さなかった表情 | 39 | 3 | 17 | 6 |

- A の `transforms` は、オイラー角・位置・拡大のトラックを持つ表情が 11、表示の切り替えだけの表情が 1。B は揺れ物のジョイントの回転を動かす表情が 6、表示の切り替えの表情が 3。
- 残った書き出さなかった表情は、Humanoid のルートの移動（`RootT`）を持つ全身のアニメーション（A 3・B 3）と、元から 0 の BlendShape を 0 にするだけのクリップ（B 2）など。どれも変更前から書き出していなかったもの。
- B では、書き出さない PhysBone 用のオブジェクトを動かす 2 つの表情で、そのボーンを捨てる警告が出た。
- `extensionsRequired` は両方とも無いまま。`VRVLOG_requiredAppVersions` の既存のキーの値は変わらない。A は変更前に書き出せるジェスチャーの表情が無かったので、既存の `VRVLOG_expression_gestures`（1.0.8）が新しく入った。
- 同じ版の 1 回目と 2 回目の比較（`compare-exports.py --runs`）は両方の版で一致した。A は 2 回の出力が同じバイト列になった。B は変更前から 2 回の出力のバイト列が違う（比較で除く値だけが違う）。

## 分かっている制限

- 最適化系の非破壊ツールがボーンをまとめたり消したりすると、そのボーンはノードに結び付かないとして警告付きで捨てる。
- 中身の無いオブジェクトを書き出しで取り除く設定のとき、そのオブジェクトのボーンと表示の切り替えも同じく捨てる。
- 古いアプリ（`transforms` を読まないもの）は、bind の無い表情（ボーンか表示の切り替えだけの表情）を選択肢に出さない。BlendShape とボーンの両方を持つ表情は BlendShape の部分だけを再生する。
