# VRM 1.0の視線（lookAt）をボーン型で書き出す

- 日付: 2026-10-14
- 対象: `Editor/VrmLookAtExport.cs`・`Editor/UniVrmOneClickExporter.cs`

## 背景

これまでの書き出しは `VRMC_vrm.lookAt` を書いていなかった。UniVRM 0.131の `Vrm10Exporter.ExportLookAt` は、書き出し用コピーのルートにある `Vrm10Instance.Vrm`（VRM10Object）の `LookAt` だけを出力する。VRChat用アバターには通常 `Vrm10Instance` が無く、PhysBone変換で足す `Vrm10Instance` も `Vrm` を持たないため、lookAtは空のままだった。

lookAtが無いVRMを読み込むと、UniVRMのインポーターは `VRM10ObjectLookAt` の既定値（`type` は列挙の先頭の `bone`、原点は頭から上に6cm、4方向とも「視線90度で目を10度回す」）を使う（`Vrm10Importer.cs` 420-444行）。両目のボーンがあれば、この既定値のボーン型lookAtが毎フレーム目のボーンを書き換える。視線の向きを外から与えるアプリでは、目は既定の範囲でほとんど動かず、原点も目の位置とずれていた。

## 決定

- 両目と頭のボーンがHumanoidにあるときだけ、`type: bone` のlookAtを書く。
- `offsetFromHeadBone` は両目のボーンの中点を頭ボーンのローカル座標で表したもの（UniVRMの実行時は `Head.TransformPoint(offset)` を原点にする）。glTFの座標に合わせてXを反転して書く（UniVRMの `OffsetFromHead.ReverseX()` と同じ）。
- 可動範囲は、Avatar Descriptorで **Eye Look** が有効なとき、`customEyeLookSettings` の5つの目の向き（Straight／Up／Down／Left／Right）から測る。
  - UniVRMはルートの座標系で目にyaw・pitchを掛けるので、各向きの回転を「目の親の回転 × 向きの回転 × (目の親の回転 × Straightの回転)の逆」として前方ベクトルに掛け、yaw（+X向きが正）とpitch（上が正）を求める。
  - LeftとRightの向きは、名前ではなく実際に目が回る方向（-X／+X）で内側・外側に振り分ける。左目は-Xが外側。
  - 左右の目のうち小さい方の角度を使い、どちらの目もVRChatで設定した限界を超えないようにする。
  - VRChatのEye Lookは視線をそのまま追い、設定した向きで止まる。これに合わせて、各方向の `inputMaxValue` と `outputScale` を同じ限界角にする（限界までは視線と同じ角度だけ目を回す）。
  - linkedの向きは左目の回転を両目に使う（SDKのエディタが右目を左目と同じ値に保つため）。
- 測れないとき（Eye Lookが無効・Descriptorが無い・目のTransformがHumanoidの目と違う・回転が不正・いずれかの方向が1度未満か90度超）は、UniVRMの既定値（90度と10度）を書く。Eye Lookが有効なのに測れなかったときだけ、次の警告を出す。

  `Eye Lookの設定から目の可動範囲を測れないため視線は既定の範囲で書き出しました（Avatar DescriptorのEye Lookを確認）`

  Eye Lookを使っていないアバターは、lookAtが無かった頃とアプリ上の動きが同じ（UniVRMが同じ既定値を使う）なので警告しない。
- 書き出し用コピーの `Vrm10Instance.Vrm.LookAt`（元のアバターのVRM 1.0設定）が既定値から変更されているときは何もせず、UniVRMが書いたlookAtをそのまま残す。全項目が `VRM10ObjectLookAt` の既定値と同じとき（表情のためだけにVRM10Objectを持つアバターや、lookAtの無いVRMを読み込んだもの）は、アバター固有の情報が無いとみなし、UniVRMが書いた既定値のlookAtを上の値で置き換える。共有のVRM10Object自体は変更しない。
- 書き出し後のglTFのHumanoidに `head`・`leftEye`・`rightEye` が無いときは書かない。目のボーンが無いアバターは、これまでどおりlookAtなしで書き出す。どの場合も書き出しは止めない。
- lookAtはUniVRMで書き出したあとのglTF JSONへ足す。書き出し用コピーに新しくVRM10Objectを付けると、表情・一人称などの出力にも影響するため。

SoulFlame VRM Exporter 0.6.1（MIT）の `LookAtSetup.cs`・`EyeLookRange.cs` は考え方の参考にした。コードは移植しておらず、この実装はこのリポジトリで書いた。

## 検討した代替案と不採用理由

- 目のボーンが無いときに `type: expression` で書く: UniVRMはexpression型のときlookLeft等の重みを毎フレーム強制的に書く。これまでと違う表情の動きになり、VR Vlogの顔追跡の結果も変わる。
- 範囲を「視線90度で限界角」とする比例の写像: 近くのカメラを見るときに目が対象を向かない。VRChatのEye Lookは比例ではなく、限界まで視線を追う。
- Eye Lookが無いときもViewPositionなどから角度を推測する: 根拠のある値が無い。UniVRMの既定値の方が、lookAtが無かった頃の動きと同じになる。
- 測れないときに必ず警告する: Eye Lookを使っていない大半のアバターで、動きの変わらない警告が毎回出る。

## 影響

- VR Vlogアプリ（UniVRM 0.131.1のランタイム）での確認（コードを読んだ結果。実機では未確認）
  - 通常の顔追跡: アプリはlookAtへの入力を与えないので、視線は(0, 0)のまま。範囲の値に関係なく目は記録時の回転に戻るので、lookAtが無かった頃と同じ。瞬き・表情・EyeLook系のBlendShapeは目のボーン以外を書かないlookAtの影響を受けない。
  - 視線の向きを外から与える場面（AR配置でカメラや画面の点を見る視線など）は、目の振れ幅が変わる。これまでは既定値で視線30度に対して約3.3度しか回らなかったが、Eye Lookの範囲で回るようになる。アプリ側でこれを抑える処理は無い。
  - lookAtの型は `bone` に固定した。`expression` にするとアプリの目の動きが変わる。
- 元のアバターのVRM 1.0設定で視線を変更しているもの・目のボーンが無いものの出力は変わらない。
- 「表情を記録する」の書き出し（`ExperimentalExpressionCaptureSession`）は、書き出し用コピーに既定値のVRM10Objectを付けてから通常の書き出しを呼ぶ。これまでは既定値のlookAt（原点は頭から上に6cm）が書かれていたが、今回から原点は両目の中点になる。この経路は書き出し前にAvatar Descriptorを外すため、範囲は既定値のままで、警告も出ない。

## 未決

- アプリの顔追跡は目のボーン（control rig側）を回しているが、UniVRMのボーン型lookAtが同じフレームで目を記録時の回転に戻すため、画面には出ていない可能性がある。lookAtの有無に関係なく以前からの挙動で、アプリ側で対応するかはアプリの判断。
- AR配置の視線で目の振れ幅が大きくなることを、実機で確認する。
- 「表情を記録する」の経路でもEye Lookの範囲を使うか（Descriptorを外す前に測って渡す必要がある）。
- Eye Lookが無効なアバターで、VRChatがHumanoidの目のMuscle範囲で目を動かしている場合、UniVRMの既定値よりその範囲の方が近い可能性がある。VRChat側の挙動は未確認。
- 4方向のうち1つでも測れない（1度未満を含む）と全体を既定値にしている。片方向だけ動かさないEye Lookを、その方向0度として扱うかは未決。

## 出典

- UniVRM 0.131.1: `Runtime/IO/Vrm10Exporter.cs`（`ExportLookAt`）、`Runtime/IO/Vrm10Importer.cs`（lookAtの読み込み）、`Runtime/Components/Vrm10Runtime/Vrm10RuntimeLookAt.cs`、`Runtime/Components/LookAt/LookAtEyeDirectionApplicableToBone.cs`
- VRM 1.0仕様 `VRMC_vrm.lookAt`
- VRChat SDK3のAvatar Descriptor（`customEyeLookSettings` の `eyesLookingStraight`・`eyesLookingUp`・`eyesLookingDown`・`eyesLookingLeft`・`eyesLookingRight`）
- SoulFlame VRM Exporter 0.6.1（MIT）の視線設定の考え方
