# AAO の対応範囲 1.7.0 以上 2.0.0 未満を据え置き、ヒューマノイドの骨が失われたら書き出し前に止める

- 日付: 2026-10-18
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/Compatibility/ExporterLocalization.cs`・`Editor/Locales/*.json`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`Tests/Editor/NdmfBlinkPreparationTests.cs`・`Tests/Editor/AvatarOptimizerBoxIntegrationTests.cs`・`Tools/run-unity-compatibility.py`・`Compatibility/README.md`・`CHANGELOG.md`

## 背景

Avatar Optimizer（AAO）の対応範囲を広げられるか確かめるため、1.7.13・1.8.15・1.9.20 の実プロジェクトで AAO の統合テストを回した。どの版でも、ばねの書き出しを全体で確かめるテストが失敗した。

- 1.8.15 と 1.9.20 では、AAO が腕と脚のボーンを消し、そのまま VRM が保存された。再読み込みで `LeftUpperLeg` が見つからずに失敗した。テストのアバターでは、腕と脚のボーンを使っているのが Animator だけだった。
- 1.7.13 では、Avatar Descriptor のプレイアブルレイヤーが空のため、AAO の中で `NullReferenceException` が出た。
- Descriptor のない box 削除のテストは、AAO 1.9 未満では AAO の処理自体が走らずに失敗した。

Exporter 側では、ボーンが欠けたヒューマノイドを UniVRM がそのまま保存してしまい、利用者は再読み込みするまで気づけなかった。

## 決定

- AAO の対応範囲は 1.7.0 以上 2.0.0 未満のまま変えない。1.6.x は VRChat SDK 3.6 未満と Unity 2019.4 を前提にしているので広げない。
- NDMF と最適化ツールの処理が終わった複製について、UniVRM で保存する前に確かめる。元のアバターがヒューマノイドで、複製のルートにヒューマノイドの Animator がなければ止める。Unity がヒューマノイドに必須とする 15 本のボーンのどれかが解決できない場合も止め、そのボーンの名前を表示する。元のアバターは変更しない。
- テストのアバターを実際のアバターに合わせた。VRChat の新規アバターと同じ既定のプレイアブルレイヤーを持つ Descriptor と、ルートのヒューマノイド Animator を付け、マップしたボーンすべてにウェイトを持つボディを足した。
- Descriptor のない box 削除のテストは、NDMF が generic プラットフォームを選ぶ。AAO がそこで動くのは 1.9.0 からなので、1.9 未満ではテストを理由付きでスキップする。

## 確認したこと

- AAO の公開ソースを 1.7.13・1.8.15・1.9.20 で読んだ（コードは写していない）。AAO は、Animator が解決したヒューマノイドボーンを名前の依存として残す。Descriptor はルートの Animator への依存を足す。
- 1.7.13 は、Descriptor のレイヤー配列が空だと Base のコントローラを null のまま処理する。1.9.20 は既定値を先に埋める。
- 実プロジェクト（Unity 2022.3.22f1）の結果は `Compatibility/Evidence/AaoRange/` にある。4 クラス 54 件で失敗は 0 件だった。1.9.20 は 54 件すべて通った。1.8.15 は 36 件、1.7.13 は 27 件が通り、残りはその版に無い API と SDK の版による理由付きのスキップだった。

## 検討した代替案と不採用理由

- テストのアバターだけを直す: 実アバターでも、ボーンの依存が付かない状況が起きると骨が統合されうる。そのときに黙って壊れた VRM を保存しないよう、Exporter 側でも止める。
- 欠けたボーンを Exporter が補う: AAO が消した理由を Exporter は知らないので、元の構造を推測で作り直すことになる。止めて利用者に知らせる。
- AAO 1.9 未満を対応から外す: Descriptor 付きの実アバターでは 1.7.13 と 1.8.15 でも書き出しが通った。generic プラットフォームの box 削除だけが動かないので、範囲は狭めない。

## 影響

- ヒューマノイドに必要な Animator やボーンが最適化で失われたアバターは、骨のない VRM を保存する代わりに、失われたボーンの名前を示して止まる。
- これまで書き出せていたアバターの結果は変わらない。
- AAO がボーンを残せなかった根本の理由は、Unity での確認がまだで確定していない。
