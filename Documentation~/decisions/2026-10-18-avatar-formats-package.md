# VRM の独自拡張の共有コードを `Formats/` のパッケージに切り出す

- 日付: 2026-10-18
- 対象: `Formats/`（新規。`Editor/ExpressionAnimationData.cs`・`Editor/LilToonFullContract.cs`・`Editor/LilToon234Catalogue.cs` を移動）・`Editor/VRVlog.LilToonExporter.Editor.asmdef`・`Tests/Editor/VRVlog.LilToonExporter.Editor.Tests.asmdef`・`Tools/build-package.py`・`Tools/test-package.py`・`Tools/liltoon_catalogue.py`・`Tools/generate-liltoon-catalogue.py`・`Tools/test-liltoon-catalogue.py`・`Tools/generate-compatibility.py`・`Tools/test-compatibility-tooling.py`・`Tools/run-behavior-tests.ps1`・`Tools/run-compatibility-tests.ps1`・`README.md`・`CONTRIBUTING.md`・`Compatibility/README.md`

## 背景

書き出した VRM の独自拡張は、Exporter が書き、アプリ（VR Vlog 本体）が読む。読み書きと検査のコードは次の 3 ファイルで、Exporter とアプリの両方のリポジトリに同じ内容を手で写していた。

- `ExpressionAnimationData.cs`（`VRVLOG_expression_animations` の読み書きと曲線の評価）
- `LilToonFullContract.cs`（`VRVLOG_materials_liltoon` の検査）
- `LilToon234Catalogue.cs`（契約が照合する lilToon 2.3.4 のシェーダーとプロパティの表。`Tools/generate-liltoon-catalogue.py` が生成する）

写しが 2 つあると、片方だけを直したときに Exporter が通す VRM をアプリが拒否する（またはその逆の）ずれが起きうる。ずれはどちらのリポジトリのテストでも見つからない。どの 3 ファイルも `System` だけを使い、Unity の API に依存していない。

## 決定

- 3 ファイルを、このリポジトリの `Formats/Runtime/` に移し、独立した UPM パッケージ `com.vrvlog.avatar-formats`（`Formats/package.json`・1.0.0）にする。写しを保つのをやめ、同じソースを両方から使う。`git mv` で移し、`.meta` の GUID は変えていない。
- アセンブリは `VRVlog.AvatarFormats`（`Formats/Runtime/VRVlog.AvatarFormats.asmdef`）。`noEngineReferences: true` で UnityEngine も参照させない。プラットフォームの制限は付けず、`autoReferenced: false` にして、使う側の asmdef が名前で参照する。参照を足したのは `VRVlog.LilToonExporter.Editor` と `VRVlog.LilToonExporter.Editor.Tests` の 2 つ（3 つの型を使うのはこの 2 つだけ）。
- アプリはこのパッケージを `https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter.git?path=/Formats#<commit>` で取り込み、commit を固定する。共有コードを変えたら、アプリ側で固定した commit を上げる PR を出すまでアプリには反映されない。
- 3 つの型と、Exporter とテストが使うメンバーを `internal` から `public` にした。入れ子の型とフィールドも揃えて `public` にし、`Fail` などの補助だけ `private` のまま残した。挙動は変えていない。
- `LilToon234Catalogue.cs` は生成物なので、生成スクリプト（`Tools/liltoon_catalogue.py`）の出力と読み戻しの正規表現も `public` に変え、出力先を `Formats/Runtime/LilToon234Catalogue.cs` にした。lilToon 2.3.4 のタグから生成した結果は、移動後のファイルとバイト単位で一致した。
- VPM の配布物（`Tools/build-package.py`）には `Formats/` のソース・asmdef・`.meta`・`README.md` を入れる。入れ子の `Formats/package.json` とその `.meta` は入れない。VCC／ALCOM で入れた Exporter のフォルダに、パッケージの定義が 1 つだけになるようにする。

## 検討した代替案と不採用理由

- 写しを続け、両リポジトリでバイト一致を CI で確かめる: 一致の検査には相手のリポジトリを読む手段が要り、このリポジトリの CI だけでは確かめられない。ずれを見つけられても、写しを直す手間は残る。
- `InternalsVisibleTo` でアプリのアセンブリに見せる: アプリのアセンブリ名をこのリポジトリが知ることになり、アプリ側の名前を変えるたびにこちらの変更が要る。別リポジトリから使う以上、公開の API として扱う。
- 別のリポジトリに切り出す: Exporter の変更と共有コードの変更が別々の PR になり、Exporter のテストと同時に確かめられない。同じリポジトリに置けば、Exporter の検証がそのまま共有コードの検証になる。
- Exporter の配布物から `Formats/` を外し、VPM の依存として別に配る: VPM の掲載先と公開の手順が 1 つ増える。Exporter の利用者には区別する理由がない。

## 影響

- Exporter の書き出しと検査の挙動は変わらない。型の見える範囲だけが広がった。
- VPM で入れた Exporter には `VRVlog.AvatarFormats` のアセンブリが含まれる。同じプロジェクトに `com.vrvlog.avatar-formats` を別に入れると、アセンブリ名が重なってコンパイルできない（`Formats/README.md` に書いた）。
- このリポジトリをそのまま Unity に入れる開発環境（Add package from disk）では、`Formats/package.json` がパッケージの中に残る。Unity のパッケージとして登録されるのは `Packages/` 直下のパッケージだけなので、入れ子の定義は使われない想定だが、警告が出ないかは Unity での確認がまだ。

## 未決

- アプリ側の切り替え（写しを消して git URL で参照する PR）は、このリポジトリの変更が取り込まれたあとに別に行う。
