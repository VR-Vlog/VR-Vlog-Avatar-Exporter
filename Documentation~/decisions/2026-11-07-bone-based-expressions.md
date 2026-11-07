# 表情が動かすボーンと表示の切り替えを、書き出しに戻す

- 日付: 2026-11-07
- 対象: `Editor/ExpressionTransformCurves.cs`・`CHANGELOG.md`・`Documentation~/ExperimentalExpressionCapture.md`・`Tools/ExportRegressionRunner.cs`・`Tools/run-export-regression.ps1`

## 背景

1.4.4で、表情のアニメーションが動かすボーンと表示の切り替え（表情の拡張の `transforms`）の書き出しを `ExpressionTransformCurves.Enabled = false` で止めた（[2026-11-01の決定記録](2026-11-01-expression-transforms-off.md)）。理由は、アプリがまだ `transforms` を再生しないことだった。

アプリは v1.2.0 から `transforms` を再生する。再生は Feature Flag `expression_bone_animation` の下にあり、本番の Remote Config でもオンになっている（2026-11-07に v1.2.4 の Android と iOS の値を読んで確かめた）。止めた理由はもう当てはまらないので、書き出しを戻す。

アプリでは、ボーンや表示を動かす表情を表情の一覧でほかの表情の後に並べる。並べ替えはアプリ側で行う。

## 決定

- `Enabled` を `true` にする。書き出しの中身は1.4.3と同じで、表情の拡張に `transforms` を足す。ボーンや表示だけを動かす表情も、その他の表情として書き出す。
- ボーンを使う表情を表す新しい項目はファイルに足さない。アプリは「`transforms` にその表情の名前がある」ことで見分ける。古いファイルには `transforms` が無いので、すべてボーンなしとして扱われる。項目を別に持つと、`transforms` の中身と食い違うおそれがある。
- 標準表情（感情・まばたき・口の形など）の枠には、これまでどおり BlendShape だけを入れる。ボーンや表示切替を含むクリップは標準表情の枠に割り当てない。これは `presetFace` が `Enabled` によらず決めているので、オンにしても標準表情の割り当ては変わらない。
- `Enabled` のコードと、オフのときの動きを確かめるテストは残す。アプリで問題が出たときに戻せるようにするため。
- `Tools/run-export-regression.ps1` に `-Transforms on|off` を足し、同じパッケージでオンとオフを書き出して比べられるようにする。

## 既定の状態を変えないことの確かめ

同じパッケージをオフ（develop と同じ書き出し）とオンで書き出して比べた。対象は、自作・有償・依頼者から受け取ったアバターと、衣装を着せた組み合わせの計159件。どちらの設定でも書き出せた156件を比べた。残りの3件（開けない2件・書き出しに失敗する1件）は、オンでもオフでも同じ結果だった。

一致したもの:

- ノードの静止姿勢（TRS）・スキン・メッシュの頂点データ・既定のモーフの重み・マテリアルと画像
- 両方にあるモーフのデータ。モーフは名前で突き合わせた
- VRMの拡張（Humanoid・標準表情・視線・一人称・揺れもの・コンストレイント）。表情が指すモーフも、番号ではなく BlendShape の名前で比べた
- オフでも書き出していたその他の表情の定義と、BlendShape のアニメーション
- 書き出したVRMを読み込んで撮った画像。全身（前・後ろ）と顔の既定の468枚、オフでも書き出していた表情の5204枚が、画素単位で一致した

オンで変わったものは、次の追加と名前だけだった。

- `transforms` と、オンで足した表情（ボーンや表示を動かす表情）
- 足した表情が使う BlendShape がメッシュに残る。モーフが増えたメッシュは名前の末尾に `(Clone)` が付き、前からあったモーフの番号が後ろへずれることがある。表情は同じ名前の BlendShape を指している
- ジェスチャーの割り当てで、足した表情が元の表情の後ろに並ぶ
- ノードと揺れものの名前の末尾の ID。書き出すたびに変わる値で、表情を足していないアバターでも変わる

`Tools/ExportRegressionRunner.cs` の撮影は、カメラの範囲を読み込んだときの頂点から取るように変えた。Unity はスキンメッシュの範囲を BlendShape の届く範囲まで広げるので、モーフが増えると同じ体でもカメラの位置がずれ、画像を画素単位で比べられなかった。

古いアプリ（v1.1.4）が読む `ExpressionAnimationData` は知らない項目を無視する。オンで書き出した139件（表情の拡張があるもの）を v1.1.4 の読み取りで読み、失敗しないことを確かめた。古いアプリでは、ボーンだけを動かす表情に BlendShape が無いので、一覧に出ない。

## 確認

- `ExpressionBoneAnimationExportTests`・`ExperimentalExpressionCaptureTests`・`FacialProjectionTests`・`MergedFxDefaultsTests`・`SelectedExpressionAppearanceTests`・`QuickTransferMatchesDefaultNormalTests` を Unity 2022.3 で回す。
