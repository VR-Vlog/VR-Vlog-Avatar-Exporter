# 拡張に Transform のカーブを translation・rotation・scale の対象として書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/AvatarAnimatorDataTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`

## 背景

FX のクリップがノードの位置・回転・拡大率を動かすカーブは、これまで拡張（`VRVLOG_avatar_animator`）に書かず、`transform`として数えるだけだった。アクセサリーを出し入れするときに大きさや位置を動かすアバターでは、アプリで動きが欠ける。

## 決めたこと

- 対象の種類に`translation`・`rotation`・`scale`を足した。キーは`node`・`channel`・`rest`で、`channel`は`x`・`y`・`z`（四元数の回転だけ`w`も）。値は glTF の空間に直さず、Unity のローカルの値をそのまま書く
- 書くのは Unity の`m_LocalPosition`・`m_LocalRotation`・`localEulerAnglesRaw`・`m_LocalScale`の 4 つ。`localEulerAnglesRaw`（補間が Euler Angles）の回転は`euler: true`を付けて角度のまま書き、アプリは角度で混ぜてから`Quaternion.Euler`で回転にする
- 1 つのノードの回転を角度のカーブと四元数のカーブの両方で動かすコントローラーでは、そのノードの回転のカーブを`rotationMixed`として数えて書かない。読み込みも、1 つのノードで両方が混ざる回転を拡張ごと断る
- Humanoid のボーンを動かすカーブは`humanoidTransform`として数えて書かない。アプリのボーンの扱いが決まるまで、ほかの Transform と分けて数を残す
- Additive のレイヤーのクリップが動かす Transform のカーブは`transform`として数える。Unity はポーズに足すが、アプリは float として混ぜるため
- アバターのルートを動かすカーブと、補間が Euler Angles (Quaternion) の回転（`localEulerAnglesBaked`など）は、今までどおり`transform`として数える。ルートの位置はアプリが決める
- `rest`は書き出すときのローカルの値。Euler の回転はインスペクターに出る角度（`TransformUtils.GetInspectorRotation`）
- トラックの枠はマテリアルのスロットの後に使い、あふれた分は`transform`として数える。重なりの判定は`(node, channel)`を種類ごとに見る
- 評価器は対象ごとに 1 つの値を出すまま変えない。Transform のチャンネルは float と同じように混ざる
- 省略の種類の表示名を 4 言語のロケールに足した

`schemaVersion`は 1 のまま。古い読み手は新しい種類を知らない種類として受け、書かない対象にするので、新しいファイルも読める。

## 採らなかった案

- 値を glTF の空間（X を反転）に直して書く: カーブの角度と四元数の向きの直し方が分かれ、アプリの評価のあとに Unity の Transform へ戻すときにもう一度直すことになる
- Euler の回転を書き出しで四元数に直す: 角度で混ぜた結果と四元数で混ぜた結果は違い、Unity の再生と食い違う

## 確かめたこと

- Unity 2022.3.22f1 の EditMode で`AvatarAnimatorDataTests`・`AvatarAnimatorCaptureTests`・`AvatarAnimatorCrossCheckTests`を回した（結果は PR に書く）
- `Tools/run-avatar-animator-evaluator-tests.ps1`で評価器の確認と記録の再生を回した

## 残したこと

- レイヤーの Avatar Mask を Transform のカーブに効かせる（段 5）
- Transform のクロスチェック（4-9）
