# NDMF の下限を 1.8.3 から 1.6.0 に下げる

- 日付: 2026-10-22
- 対象: `Compatibility/dependencies.json`・`Editor/Compatibility/DependencyPolicy.Generated.cs`・`Editor/NdmfExportPreparation.cs`・`Tests/Editor/NdmfPreparationTests.cs`・`Tools/run-unity-compatibility.py`・README 3 種・`Compatibility/README.md`・`Compatibility/Evidence/NdmfRange/`・`CHANGELOG.md`

## 背景

Exporter は NDMF の処理を書き出し用の複製に対して自分で回す（`NdmfExportPreparation.Bridge`）。下限 `ndmfMinimum` は 1.8.3 で、それより古い NDMF の入ったプロジェクトでは「アバターの準備に必要な NDMF API を利用できません」で止まっていた。Modular Avatar の下限を 1.11.2 まで下げる作業と並べて、オーナーから NDMF の下限も下げられる限り下げるよう指示があった。

## Exporter が使う NDMF の API

Bridge が型名で読むもの:

- `AvatarProcessor.ProcessAvatar(BuildContext, BuildPhase, BuildPhase)`（internal）
- `BuildContext` のコンストラクタ、`Finish()`（internal）、`Successful`、`AssetSaver`、`ObjectRegistry`
- `BuildPhase` の `Resolving`・`Generating`・`Transforming`・`Optimizing`、1.8.0 以降は internal の `First`
- `OverrideTemporaryDirectoryScope(string)`（internal）
- 1.8.0 以降だけ: `platform.INDMFPlatformProvider`・`PlatformRegistry.GetPrimaryPlatformForAvatar(GameObject)`・`GenericPlatform.Instance`

ほかに `IObjectRegistry.GetReference(Object, bool)` と `ObjectReference.Object`（AAO と表情の由来の追跡）、`INDMFEditorOnly`（書き出し対象の部品の判定）、`runtime.components.NDMFAvatarRoot`（MA の部品があるときに複製へ付ける印）を読む。

## 決定

- `ndmfMinimum` を 1.6.0 にする。上限は 1.x のまま（`ndmfMajor` 1）。
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
- Modular Avatar 1.11.2（今回の MA の下限）が受け付ける NDMF も 1.6.0 以上で、1.6.0 より古い NDMF と組み合わせる MA の対応版は無い。

## 実プロジェクトでの確認

Unity 2022.3.22f1 の実プロジェクトで、NDMF の 1.6.0・1.7.10・1.8.0 と、それぞれを受け付ける MA を組み合わせて NDMF まわりのテストを回した。組み合わせと結果は `Compatibility/Evidence/NdmfRange/README.md` にある。

## 検討した代替案と不採用理由

- 1.5.x 以前も読む: 上のとおり、アセットの保存と後片付けを版で分ける必要がある。MA の下限より古い版のための分岐になる。
- 1.7.0 を下限にする: 1.6.x と 1.7.x の差は `INDMFEditorOnly` だけで、Exporter は無くても困らない。MA 1.11.2 が 1.6.0 を受け付けるので、下限をそこに揃える。

## 影響

- NDMF 1.6.0〜1.8.2 のプロジェクトでも、MA などの NDMF のツールを使ったアバターを書き出せる。1.8.3 以降の動きは変わらない。
- 1.5.x 以前は、これまでどおり下限（1.6.0 以降の 1.x）を示すメッセージで処理の前に止まる。
