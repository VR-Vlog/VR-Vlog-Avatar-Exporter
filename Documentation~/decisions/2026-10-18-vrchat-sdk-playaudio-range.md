# 表情の追加で音声再生を省略できる VRChat SDK を 3.5.2 以上 3.11.0 未満にする

- 日付: 2026-10-18
- 対象: `Compatibility/dependencies.json`・`Tools/generate-compatibility.py`・`Editor/Compatibility/DependencyPolicy*.cs`・`Editor/ExperimentalExpressionControllerScope.cs`・`Tools/DependencyCompatibilityTests.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`・README 3 種・`Compatibility/README.md`・`Compatibility/Evidence/VrchatSdkRange/`・`CHANGELOG.md`
- 置き換え: 下限とプレリリースの扱いは `2026-10-22-vrchat-sdk-range-widen.md` で変えた（3.5.0 以上 3.11.0 未満、範囲内のプレリリースを含む）。

## 背景

Exporter が VRChat SDK の版を見ているのは 1 か所だけで、表情を記録するためにアバターを複製したとき、Animator にある音声再生（`VRCAnimatorPlayAudio`）を外してよいかの判定だった。ここは `com.vrchat.avatars` と `com.vrchat.base` の版が文字列で `3.10.5` と一致するときだけ通し、3.10.4 や 3.10.6 では「このSDKの音声処理は表情への影響を確認できません」で止まっていた。ほかの VRChat の型は型名とシリアライズ名で読んでいて、版の検査はない。

オーナーから、VRChat SDK の対応範囲をできるだけ広げるよう指示があった。

## 決定

- `dependencies.json` に `vrchatSdkMinimum: "3.5.2"`・`vrchatSdkBelow: "3.11.0"` を足し、生成した `DependencyPolicy.Generated.cs` の定数で判定する。判定は `DependencyPolicy.SupportsVrchatSdkAudio` に置き、Base と Avatars が同じ版で、`X.Y.Z` の数字だけの正式版で、3.5.2 以上 3.11.0 未満のときに通す。プレリリース・ビルドメタデータ付き・Base と Avatars が別の版は従来どおり止める。
- 範囲外で止めるときのエラー文に対応範囲を足す。
- 下限は `VRCAnimatorPlayAudio` が入った最初の正式版の 3.5.2 にする。上限は、まだ出ていない 3.11 で型や挙動が変わるかを確かめられないので 3.11.0 未満にする。
- `PhysBoneSpringExportTests` のうち `ignoreOtherPhysBones` を書き換えるケースは、そのフィールドがない SDK（3.7.x 以前）では理由を付けて除外する。Exporter 本体はフィールドがないとき `true` として扱っていて、SDK 側の既定の動きと同じ。

## 確認したこと

- 公式 VPM リストの全版から DLL を取り出して調べた。`VRC_AnimatorPlayAudio` は 3.5.1 以前の DLL になく、3.5.2-beta.1 で初めて入った。3.5.2 以降 3.10.5 まで、`VRCAnimatorPlayAudio` → `VRC_AnimatorPlayAudio` → `StateMachineBehaviour` の継承、フィールド、メソッドが同じで、3.5.2・3.6.0・3.7.0 の `VRC_AnimatorPlayAudio` の IL はメタデータトークンを除いて 3.10.5 と一致した。SDK の DLL には再生処理がなく、VRChat のクライアントが差し込む作りになっている。
- 3.5.0・3.5.2・3.6.1・3.7.0 の `package.json` はどれも `"unity": "2022.3"` で、3.5.2 も Unity 2022.3 のプロジェクトに入る。
- Unity 2022.3.22f1 の実プロジェクトで、SDK 3.5.2（UniVRM 0.131.2・lilToon 2.3.4・NDMF/MA/AAO なし）、3.7.0（UniVRM 0.131.0・lilToon 2.0.0・NDMF 1.8.3・AAO 1.7.13）、3.10.5（UniVRM 0.131.3・lilToon 2.3.4・NDMF 1.8.3・MA 1.13.4・AAO 1.8.15）の 3 つで、音声再生を含むメニューの表情（`audio`・`audio-reactive`）を記録するテストを回し、3 つとも通った。結果と、音声以外で失敗したケースの理由は `Compatibility/Evidence/VrchatSdkRange/README.md` に書いた。

## 検討した代替案と不採用理由

- 3.7.0 以上にする: 3.5.2 と 3.6.x も同じ型で、Unity 2022.3 に入り、実プロジェクトで音声の表情を記録できた。狭める理由がない。
- 確かめた版を列挙して完全一致を見る: VCC が入れるパッチごとに止まる。型と IL が 3.5.2 から 3.10.5 まで変わっていないので、範囲で受け付ける。
- 3.11 以降も受け付ける: まだ公開されておらず、型が変わったかを確かめられない。
- プレリリースも受け付ける: 正式版と同じ DLL である保証がない。

## 影響

- 3.5.2〜3.10.x の SDK のプロジェクトで、音声再生を含む表情ファイルを追加できるようになる。音声再生を外すのは表情を記録する複製の中だけで、元のアバターは変わらない。
- VRChat のクライアントで音声再生の動きが変わっても、SDK の版からは分からない。Exporter は記録のあいだ音声再生を外すだけなので、表情の値には影響しない。
- 3.11.0 が出たら、型と IL を確かめてから `vrchatSdkBelow` を上げる。
