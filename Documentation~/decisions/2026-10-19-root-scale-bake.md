# アバターのルートの拡縮をすぐ下の子へ移して書き出す

- 日付: 2026-10-19
- 対象: `Editor/RootScaleBake.cs`・`Editor/Vrm10AppearanceExporter.cs`（`Export`）・`Editor/PoseExportSession.cs`（`Inject`）

## 背景

UniVRMの `ModelExporter.Export(settings, arrays, avatar)` は、渡したアバターのルートの子をglTFのシーンのルートノードにし、ルート自身のTRSは書き出さない。VRChatのアバターではルートのScaleを0.7や0.325などにしてアバターの大きさを決めていることが多く、VRChatではこのScaleがアバターの見た目の大きさになる。書き出したglTFを調べると、シーンのルートノードには子のローカルTRSだけが入っていた（ルートのScale 0.325が失われ、子のArmatureのScale 100は残る）。このため書き出したVRMは元の大きさの1/ルートのScale倍になっていた。

## 決定

- `ModelExporter.Export` の直前にルートの `localScale` をすぐ下の子へ移し、ルートを(1, 1, 1)にする。子のワールド行列は変わらないので、メッシュ・ボーン・bindposeの関係も変わらない。`Vrm10Exporter` と `afterExport` も同じ階層を読むように、元に戻すのは `Vrm10AppearanceExporter.Export` を抜けるときにする（`using` による `finally`）。
- 均等な拡縮 s では、子の `localPosition` と `localScale` をそれぞれ s 倍する。
- 不均等な拡縮 S では、子の `localPosition` を `Vector3.Scale(S, localPosition)` にし、子の拡縮は `R⁻¹·S·R`（R は子の `localRotation`）の対角成分を掛ける。`R⁻¹·S·R` が対角にならない場合（回転で拡縮の違う軸が混ざる場合）はUnityのTransformでもglTFのTRSでも表せないシアーが要るので、ルートと子の名前を添えて書き出しを止める。90度単位の回転や、拡縮の等しい軸まわりの回転は表せるので止めない。
- ルートの拡縮がゼロ・NaN・無限大のときは書き出しを止める。
- ルートの位置と回転は移さない。アバターは従来どおり原点に正面向きで書き出す。
- 書き出し後にノードを参照する処理（`afterExport` の各 `Bind`、視線の `offsetFromHeadBone`、揺れ物・コライダーの値、書き出したバイト列の検査）は、ノードの番号か各ノードのローカル空間の値だけを使い、ワールド行列が変わらないので結果は変わらない。
- 同梱ポーズと動くポーズの `hipsOffset` は、採取・保存・ポーズ確認画面ではこれまでどおりルートのScaleを1にした値を使い、VRMへ書き込むとき（`PoseExportSession.Inject`）だけ `Vector3.Scale(ルートのScale, hipsOffset)` にする。VRMのモデル空間はルートのScaleを含むようになったので、書き込む値もそれに合わせる。掛けるScaleは `RootScaleBake` が移すのと同じ、書き出し用コピーのルートの `localScale` で、書き出しの直前に一度だけ取る。仕様（`Documentation~/HumanoidPoses.md`・`HumanoidAnimations.md`）にもそう書いた。
- `hipsOffset` 以外に、ルートのScaleを含まない位置をVRMへ書く処理は見つからなかった（ポーズの回転はScaleの影響を受けず、視線・揺れ物はノードのローカル空間の値）。

## 検討した代替案と不採用理由

- glTFにルートノードを1つ足し、そこにルートのScaleを入れる: UniVRMの書き出し処理はルートを省く前提で、ノードの番号を使う既存の後処理（ポーズ・表情・最適化の対応付け）がすべてずれる。VRMのhumanoidの外側に拡縮を持つノードを置くことになり、ランタイムによって扱いが違う。
- メッシュの頂点とbindposeに拡縮を焼き込む: 子の階層が深いアバターでは全メッシュ・全ボーンを書き換えることになり、表情・揺れ物・コンストレイントの値も合わせて変える必要がある。子のTRSだけを変えれば済むので採用しない。
- ポーズの採取でルートのScaleを残す: 保存済みのポーズデータ・ポーズ確認画面・「rootのスケールを無視して採取する」既存テストの前提がすべて変わる。書き込み時に1か所で掛ければ足りる。
- 回転と不均等な拡縮が混ざる場合に近い拡縮へ丸める: 見た目が黙って変わる。既存の追従処理（`ExportAttachmentSession.ReparentPreservingPose`）と同じく、形を保てないときは止める。
