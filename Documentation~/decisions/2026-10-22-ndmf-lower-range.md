# NDMF の下限を 1.8.3 から 1.6.0 に下げる

- 日付: 2026-10-22
- 対象: `Compatibility/dependencies.json`・`Editor/Compatibility/DependencyPolicy.Generated.cs`・`Editor/NdmfExportPreparation.cs`・`Tests/Editor/NdmfPreparationTests.cs`・`Tools/run-unity-compatibility.py`・README 3 種・`Compatibility/README.md`・`Compatibility/Evidence/NdmfRange/`・`CHANGELOG.md`

## 背景

Exporter は NDMF の処理を書き出し用の複製に対して自分で回す（`NdmfExportPreparation.Bridge`）。下限 `ndmfMinimum` は 1.8.3 で、それより古い NDMF の入ったプロジェクトでは「アバターの準備に必要な NDMF API を利用できません」で止まっていた。Modular Avatar の下限を下げる作業（`2026-10-22-ma-older-range.md`。1.12.2 に決まった）と並べて、オーナーから NDMF の下限も下げられる限り下げるよう指示があった。

## Exporter が使う NDMF の API

Bridge が型名で読むもの:

- `AvatarProcessor.ProcessAvatar(BuildContext, BuildPhase, BuildPhase)`（internal）
- `BuildContext` のコンストラクタ、`Finish()`（internal）、`Successful`、`AssetSaver`、`ObjectRegistry`
- `BuildPhase` の `Resolving`・`Generating`・`Transforming`・`Optimizing`、1.8.0 以降は internal の `First`
- `OverrideTemporaryDirectoryScope(string)`（internal）
- 1.8.0 以降だけ: `platform.INDMFPlatformProvider`・`PlatformRegistry.GetPrimaryPlatformForAvatar(GameObject)`・`GenericPlatform.Instance`

ほかに `IObjectRegistry.GetReference(Object, bool)` と `ObjectReference.Object`（AAO と表情の由来の追跡）、`INDMFEditorOnly`（書き出し対象の部品の判定）、`runtime.components.NDMFAvatarRoot`（MA の部品があるときに複製へ付ける印）を読む。

## 決定

- `ndmfMinimum` を 1.6.0 にする。上限は 1.x のまま（`ndmfMajor` 1）。対象は正式版だけで、プレリリースは確かめていない。
- 1.8.0 より前の NDMF にはプラットフォームの仕組みがない。`INDMFPlatformProvider` が無いときは、`BuildContext(GameObject, string, bool)` で作り、最初のフェーズを `Resolving` にする。1.8.0 以降で増えた FirstChance と PlatformInit は、それより前の版には無いフェーズなので飛ばすものはない。
- プラットフォームの型が一部だけある形（`INDMFPlatformProvider` はあるが `PlatformRegistry` か `GenericPlatform` が無い）は、従来どおり処理を始める前に止める。

## 1.6.0 から 1.8.2 で確かめたこと

公式の NDMF リポジトリのタグ 1.6.0・1.7.0・1.7.10・1.8.0〜1.8.3 のソースを読んだ。

- `ProcessAvatar` の 3 引数版・`Finish`・`Successful`・`AssetSaver`・`OverrideTemporaryDirectoryScope`・`ObjectRegistry`・`IObjectRegistry.GetReference(Object, bool)` は 1.6.0 から同じ形。
- `BuildContext` のコンストラクタは、1.8.0 で `INDMFPlatformProvider` を取る版に変わった。1.6.0〜1.7.10 は `(GameObject, string, bool)`。
- `INDMFEditorOnly` は 1.7.0 から。1.6.x には無いが、その版の MA と AAO の部品は基底クラス（`AvatarTagComponent`）で判定できるので困らない。
- `NDMFAvatarRoot` は 1.8.0 から。無い版では印を付けずに進む。

## 1.6.0 より下げない理由

- 1.5.x 以前の `BuildContext` には `AssetSaver`（`IAssetSaver`）が無い。生成したアセットは 1 つの `AssetContainer` に入れて `Serialize()` で保存する作りで、1.6.0（NDMF の #479）で今の形になった。Exporter は書き出しごとに専用の一時フォルダを用意し、最後に `AssetSaver` を閉じてそのフォルダの中身を確定させる。1.5.x で同じことをするには保存と後片付けを別に書く必要があり、そのための利用者も見込めないので見送る。
- MA で NDMF 1.6.0 を受け付けるのは 1.11.0 以降（1.11.x は 1.6.0 以上、1.12.2 は 1.7.2 以上）。1.6.0 より古い NDMF と組み合わさるのは MA 1.10.x 以前で、表示の固定の対応範囲（1.12.2 以上）の外にある。

## 実プロジェクトでの確認

Unity 2022.3.22f1 の実プロジェクトで、NDMF の 1.6.0・1.7.2・1.7.10・1.8.0・1.8.3 と、それぞれを受け付ける MA（1.11.2・1.12.2・1.12.5・1.13.0）や MA なしを組み合わせて NDMF まわりのテストを回した。NDMF 1.6.0 と 1.7.x で、1.8.0 以降と違う結果になったのはテストの土台（NDMF のアバターの印も VRChat の記述子も無いアバター）だけで、テストを直した。組み合わせと結果は `Compatibility/Evidence/NdmfRange/README.md` にある。

## 検討した代替案と不採用理由

- 1.5.x 以前も読む: 上のとおり、アセットの保存と後片付けを版で分ける必要がある。MA の下限より古い版のための分岐になる。
- 1.7.2 を下限にする（表示の固定に対応する MA 1.12.2 の要求に揃える）: MA を使わない NDMF のツールや、表示の固定を使わないアバターの MA 1.11.x は NDMF 1.6.x で動く。Exporter から見た 1.6.x と 1.7.x の差は `INDMFEditorOnly` だけで、無くても困らないので、その組み合わせを止める理由が無い。MA 1.12.2 以上を入れたときは、MA 自身の依存で NDMF 1.7.2 以上になる。

## 影響

- NDMF 1.6.0〜1.8.2 のプロジェクトでも、MA などの NDMF のツールを使ったアバターを書き出せる。1.8.3 以降の動きは変わらない。
- 1.5.x 以前は、これまでどおり下限（1.6.0 以降の 1.x）を示すメッセージで処理の前に止まる。
