# アバターのルートの拡縮をすぐ下の子へ移して書き出す

- 日付: 2026-10-19
- 対象: `Editor/RootScaleBake.cs`・`Editor/Vrm10AppearanceExporter.cs`（`Export`）

## 背景

UniVRMの `ModelExporter.Export(settings, arrays, avatar)` は、渡したアバターのルートの子をglTFのシーンのルートノードにし、ルート自身のTRSは書き出さない。VRChatのアバターではルートのScaleを0.7や0.325などにしてアバターの大きさを決めていることが多く、VRChatではこのScaleがアバターの見た目の大きさになる。書き出したglTFを調べると、シーンのルートノードには子のローカルTRSだけが入っていた（ルートのScale 0.325が失われ、子のArmatureのScale 100は残る）。このため書き出したVRMは元の大きさの1/ルートのScale倍になっていた。

## 決定

- `ModelExporter.Export` の直前にルートの `localScale` をすぐ下の子へ移し、ルートを(1, 1, 1)にする。子のワールド行列は変わらないので、メッシュ・ボーン・bindposeの関係も変わらない。`Vrm10Exporter` と `afterExport` も同じ階層を読むように、元に戻すのは `Vrm10AppearanceExporter.Export` を抜けるときにする（`using` による `finally`）。
- 均等な拡縮 s では、子の `localPosition` と `localScale` をそれぞれ s 倍する。
- 不均等な拡縮 S では、子の `localPosition` を `Vector3.Scale(S, localPosition)` にし、子の拡縮は `R⁻¹·S·R`（R は子の `localRotation`）の対角成分を掛ける。`R⁻¹·S·R` が対角にならない場合（回転で拡縮の違う軸が混ざる場合）はUnityのTransformでもglTFのTRSでも表せないシアーが要るので、ルートと子の名前を添えて書き出しを止める。90度単位の回転や、拡縮の等しい軸まわりの回転は表せるので止めない。
- ルートの拡縮がゼロ・NaN・無限大のときは書き出しを止める。
- ルートの位置と回転は移さない。アバターは従来どおり原点に正面向きで書き出す。
- 書き出し後にノードを参照する処理（`afterExport` の各 `Bind`、視線の `offsetFromHeadBone`、揺れ物・コライダーの値、書き出したバイト列の検査）は、ノードの番号か各ノードのローカル空間の値だけを使い、ワールド行列が変わらないので結果は変わらない。
- 同梱ポーズの `hipsOffset` は、仕様（`Documentation~/HumanoidPoses.md`・`HumanoidAnimations.md`）で「rootのサイズを含まない」と定めていて、この変更では扱いを変えていない。ルートのScaleが1でないアバターでは、VRMの座標の大きさと `hipsOffset` の大きさがルートのScale倍ずれる。扱いは別に決める。

## 検討した代替案と不採用理由

- glTFにルートノードを1つ足し、そこにルートのScaleを入れる: UniVRMの書き出し処理はルートを省く前提で、ノードの番号を使う既存の後処理（ポーズ・表情・最適化の対応付け）がすべてずれる。VRMのhumanoidの外側に拡縮を持つノードを置くことになり、ランタイムによって扱いが違う。
- メッシュの頂点とbindposeに拡縮を焼き込む: 子の階層が深いアバターでは全メッシュ・全ボーンを書き換えることになり、表情・揺れ物・コンストレイントの値も合わせて変える必要がある。子のTRSだけを変えれば済むので採用しない。
- 回転と不均等な拡縮が混ざる場合に近い拡縮へ丸める: 見た目が黙って変わる。既存の追従処理（`ExportAttachmentSession.ReparentPreservingPose`）と同じく、形を保てないときは止める。
