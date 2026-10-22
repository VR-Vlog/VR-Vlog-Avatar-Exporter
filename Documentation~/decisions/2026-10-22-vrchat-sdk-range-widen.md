# VRChat SDK の対応範囲を 3.5.0 以上 3.11.0 未満にし、範囲内のプレリリースも受け付ける

- 日付: 2026-10-22
- 対象: `Compatibility/dependencies.json`・`Editor/Compatibility/DependencyPolicy*.cs`・`Editor/ExperimentalExpressionControllerScope.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tools/DependencyCompatibilityTests.cs`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`・README 3 種・`Compatibility/README.md`・`Compatibility/Evidence/VrchatSdkRange/`・`CHANGELOG.md`

## 背景

2026-10-18 に、表情の記録で音声再生（`VRCAnimatorPlayAudio`）を外してよい VRChat SDK を 3.5.2 以上 3.11.0 未満の正式版にした（`2026-10-18-vrchat-sdk-playaudio-range.md`）。プレリリースは「正式版と同じ DLL である保証がない」として止めていたので、VCC でベータ版の SDK（例: 3.10.6-beta.2）を入れたプロジェクトでは、音声再生を含む表情を記録できなかった。

オーナーから、VRChat SDK の対応範囲をできるだけ広げてベータリリースするよう指示があった。

## 決定

- 下限 `vrchatSdkMinimum` を 3.5.0 にする。3.5.0 は `package.json` に `"unity": "2022.3"` と書いた最初の SDK で、3.4.x は `"unity": "2019.4"`。Exporter は Unity 2022.3 が前提なので、これより下は入れられない。
- 上限 `vrchatSdkBelow` は 3.11.0 のまま。公開済みの最新の正式版は 3.10.5 で、3.11 はまだ出ていない。
- プレリリースは `X.Y.Z-<英数字とドット>` の `X.Y.Z` で範囲を判定する。3.10.6-beta.2 は通り、3.11.0-beta.1 は通らない。ビルドメタデータ付き（`+local`）や形の崩れた版、Base と Avatars が別の版は従来どおり止める。
- 範囲外で止めるときのメッセージを、導入済みの版・対応範囲・VCC／ALCOM で SDK を変えれば記録できること、の 3 つが分かる文にし、4 言語に訳す。これまでは内部の型名を出していた。
- 3.5.0 と 3.5.1 には `VRCAnimatorPlayAudio` がないので、音声再生を含む表情のテストはその SDK では理由を付けて除外する。

## 確認したこと

- 公式 VPM リストの 3.4.2 から 3.10.6-beta.2 までの 62 版（正式版 23・プレリリース 39）から DLL を取り出し、Exporter が型名で読む 287 項目（Avatar Descriptor・Expression Parameters・Expressions Menu・PhysBone・Contact・Animator の StateMachineBehaviour・VRC Constraint・Play Audio）の型・継承・フィールドの型と属性を 3.10.5 と比べた。ある版では全項目が 3.10.5 と同じだった。例外は次の 3 つ。
  - `VRCAnimatorPlayAudio` は 3.5.2-beta.1 から。継承・フィールド・メソッドはどの版でも同じ。
  - `VRCPhysBone.ignoreOtherPhysBones` は 3.7.7-beta.1（正式版は 3.8.0）から。ないときは `true` として扱う（従来どおり）。
  - VRC Constraint は 3.6.2-constraints.3（正式版は 3.7.0）から今の形。試験版の 3.6.2-constraints.1 と .2 は `Sources` が配列で、Exporter は sources を読めずにそのコンストレイントを書き出さない。版で分岐して読むほどの利用はないと判断し、制限として残す。
- Unity 2022.3.22f1 の実プロジェクト 4 つ（SDK 3.5.0・3.5.2・3.10.5・3.10.6-beta.2、どれも UniVRM 0.131.2・lilToon 2.3.4・NDMF/MA/AAO なし）で、表情の記録と PhysBone の揺れのテストを回した。結果は `Compatibility/Evidence/VrchatSdkRange/README.md` にある。音声再生を含む表情の 2 ケースは 3.5.2・3.10.5・3.10.6-beta.2 で成功し、3.5.0 では型がないため理由つきでスキップした。失敗した 18 ケースは 4 つとも同じで、MA・ポーズ用パッケージが入っていないことと IMGUI のドラッグが届かないことによるもので、SDK とは関係ない。3.5.0 だけで 1 回失敗した `LeavingTheObjectsPageAppliesItsEditsFirst` は、単独で回し直すと成功した。

## 検討した代替案と不採用理由

- 3.4.x 以下も受け付ける: 3.4.x は Unity 2019.4 向けで、Unity 2022.3 の Exporter と同じプロジェクトに入らない。
- 3.11 以降も受け付ける: まだ公開されておらず、型が変わったかを確かめられない。出たら同じ比較をしてから上げる。
- プレリリースを確かめた版の列挙で受け付ける: 39 のプレリリースで 3.10.5 と違ったのは上の 3 つだけだったので、範囲で受け付ける。列挙では新しいベータが出るたびに止まる。
- 3.6.2-constraints.1/.2 の配列形式も読む: 試験版のための分岐が増える。正式版と、それ以降のプレリリースは今の形だけ。

## 影響

- 3.11.0 未満のベータ版の SDK を入れたプロジェクトでも、音声再生を含む表情を記録できる。音声再生を外すのは記録用の複製の中だけで、元のアバターは変わらない。
- 3.5.0・3.5.1 では音声再生を含む表情ファイル自体が作れないので、これまでと動きは変わらない。
- 3.11.0 が出たら、`table.json` と同じ比較と実プロジェクトのテストをしてから `vrchatSdkBelow` を上げる。
