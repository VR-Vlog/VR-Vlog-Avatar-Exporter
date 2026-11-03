# PhysBoneからSpringBoneへの換算を、骨の長さと実測した式に置き換える

- 日付: 2026-11-03
- 対象: `Editor/PhysBoneMotionMapping.cs`・`Editor/PhysBoneSpringExport.cs`・`Editor/Compatibility/ExportNoteText.cs`・`Editor/LanTransfer/AppTransferPanel.Conversion.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/PhysBoneMotionMappingTests.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`Tools/run-unity-compatibility.py`・`Compatibility/PhysBone.md`・`Compatibility/Evidence/SpringFidelity/`・`README.md`
- 置き換える決定: [2026-10-20 PhysBoneのImmobile（All Motion）を揺れの強さと減衰に反映](2026-10-20-physbone-immobile-allmotion.md)

## 背景

様々なモデルで揺れものの再現度を上げる。これまでの式（stiffness = 4p、dragForce = 0.6(1-s)、gravityPower = |g|(1-f)、All Motionに定数加算）を元のPhysBoneと比べると、次の差が残っていた。

1. 硬さが骨の長さによらない。VRMのソルバーは先端を距離で動かすので、同じ曲がり方には骨の長さに比例した力が要る。短い骨は硬く、長い骨は柔らかくなりすぎていた
2. 減衰の式が実測と合わない
3. PhysBone 1.1の重力は静止方向の比率で、1.0の重力はPullとの競合で効く。Pullを掛けるだけでは釣り合いの角度が合わない
4. Gravity Falloffは角度で重力が変わる。`|g|(1-f)`は下向きの骨でしか合わない
5. Advancedの式が別
6. Immobile（All Motion）の定数加算は、親の動きを打ち消す効果と比例しない
7. VRMのソルバーは最後のジョイントの回転を更新せず、取得済みの値を書き戻す。最後のボーンとその下が親の揺れに付いてこない

## 決めたこと

式は [Compatibility/PhysBone.md](../../Compatibility/PhysBone.md) に書いた。要点だけ挙げる。

- stiffnessとgravityPowerは骨の長さに比例させ、dragForceは長さによらない値にする。式は `Editor/PhysBoneMotionMapping.cs` に分けた
- PhysBone 1.1と1.0の重力を別々に換算する
- Gravity Falloffは釣り合う角度で解いた重力を書き出す。揺れている間の重力の変化は、静止点を保ったままstiffnessとgravityPowerに同じ倍率を掛けて補う。倍率は釣り合いから45度振れた位置の復元力の比（30度と45度を試して45度を採用）
- Immobile（All Motion）は力を1/(1-i)倍にする。遅れは「動き÷力」に比例するので、動きを隠す割合と力を上げる割合をそろえれば遅れが同じになる。dragForceは変えない
- 実在するボーンで終わる連鎖には、長さ1/4の追従用の末端を足す
- 静止姿勢を変えないため、既存のボーンの位置と回転は触らない

## 2026-10-20の決定を置き換える理由

10-20の決定はGravity Falloffの角度依存・Advanced・骨の長さを1項目ずつ試して不採用にした。今回は全部を同時に入れて、同じタイムラインと誤差の定義で評価した。1項目だけ直すと他の項目の誤差と打ち消し合う組み合わせが崩れるので、10-20の結果と今回の結果は矛盾しない。

10-20はアプリの読み込み状態（Centerをモデルのルートに、dragを安定化係数で底上げ）を主な評価にした。今回は素のVRMとアプリの読み込み状態の両方を出す。動きはアプリに合わせてHipsを動かす方を主にした。アプリはHipsを動かしてモデルのルートを置いたままにするので、ルートを動かす比較は歩きの慣性をCenterで消してしまい、アプリの見え方を表さない。

## 採らなかった案

- アプリの安定化で上がるdragForceを、書き出し時に先回りして下げる: 4体の平均で6.51度から5.98度に下がるだけで、素のVRMは悪化する。アプリの定数に書き出し側が結合するので入れない。素のVRMでは1.13度のモデルがアプリの読み込み状態で5.81度になっており、差の原因はアプリの安定化の既定値にある
- 速度に応じて重力を変える等、VRMのソルバーに無い動きを再現する: glTFに保存できない
- 骨のカプセルとしての当たり判定: VRMのソルバーは先端の球だけで判定するので再現できない。コライダーがあるモデルの静止の差は1.5度ほど残る

## 再現できないもの

- 伸縮、動的なIs Animated、Grab / Pose
- 揺れの途中で変わるGravity Falloffの重力（倍率で近づけるだけ）
- フレームレートによる違い。式は60Hzで合わせた。VRMのdragForceはフレーム単位なので、フレームレートが変わると揺れ方も少し変わる

## ライセンスと参考にした考え方

公開されている書き出しツールのうち、MPL-2.0とMITのものの換算の考え方を読んだ。コードは写していない。GPL-3.0のものは読んでいない。

- 骨の長さに比例する力、PhysBone 1.1の重力が静止方向を変えること、Falloffが角度で変わることは既に公開されている考え方だった
- 式は実測で確かめて決めた。合成の連鎖を元のPhysBoneとVRMのソルバーで同時に動かし、静止角と振れ幅から同定した。上限の表（`CapPull`・`CapSpring`）、1.0の重力の式、Falloffの倍率、Immobileの換算、追従用の末端はこの計測から設計した

## 確かめたこと

結果の表と手順は [Compatibility/Evidence/SpringFidelity](../../Compatibility/Evidence/SpringFidelity/README.md) に置いた。

手元で組み立てられた300体（8558本）で、元との骨の向きの差は素のVRMで平均11.01度から4.87度になった。悪化したモデルはない。アプリの読み込み状態では9.11度から6.47度で、36体は悪化した（最大1.27度）。悪化はアプリの安定化がdragを上げるためで、Exporter側では補わない。

書き出したVRMファイルを読み戻した5体は、同じプロセスで換算した値とほぼ一致した。VRMのソルバーを30Hzで回すと4体の平均は5.33度から5.67度になった。

最悪の1体は新旧とも47度台のまま。Hipsをrootにした全身のPhysBoneがIs Animatedでアニメーションに従う構成で、VRMの連鎖には動く静止姿勢がないのが原因と見ている（切り分けの計測はしていない）。
