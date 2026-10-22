# UniVRMの書き出しに使う部分を名前を変えて同梱する

- 日付: 2026-10-22
- 対象: `Editor/Vendor/UniVRM/`（新規・UniVRM 0.131.4 から生成）・`Tools/vendor-univrm.py`（新規）・`Editor/ForeignUniVrm.cs`（新規）・`Editor/VRVlog.LilToonExporter.Editor.asmdef`・`Editor/Compatibility/DependencyPolicy*.cs`・`Editor/Compatibility/DependencyDiagnostics.cs`・UniVRM の型を使う `Editor/*.cs`・`package.json`・`Compatibility/`・`ThirdPartyNotices/UniVRM.md`・`Tools/*.py`（互換性・パッケージ・一覧の検査）・`Editor/NdmfExportPreparation.cs`・`Editor/AvatarOptimizer/ExportVrm10Information.cs`（新規）・`Editor/AvatarOptimizer/VRVlog.LilToonExporter.AvatarOptimizer.asmdef`・`Tests/Editor/*.cs`・`Tests/Editor/ForeignUniVrmTests.cs`（新規）

## 背景

Exporter は VRM 1.0 の書き出しにプロジェクトに入っている UniVRM（`com.vrmc.gltf`・`com.vrmc.vrm`）をそのまま使っていた。そのため次の制約があった。

- `package.json` の `vpmDependencies` で UniVRM 0.131.x を要求していたので、別の版の UniVRM を要求するパッケージと同じプロジェクトに入れると VCC／ALCOM の依存の解決がぶつかった。
- Exporter のアセンブリは asmdef の `versionDefines` で Package Manager の UniVRM 0.131.x を見つけたときだけコンパイルしていた。Git URL で別の版を入れたプロジェクトや、unitypackage で `Assets` の下に入れたプロジェクトでは、書き出しのメニューが出ないか、書き出しの前に「対応しない UniVRM」で止まっていた。
- UniVRM の版が上がるたびに、Exporter 側で受け付ける範囲を広げて確認し直す必要があった（`DependencyPolicy`）。

オーナーから 2026-10-22 に「UniVRM を内蔵して、ほかに VRM を VPM などで入れていても競合しない形にできないか。どの入れ方で入れていても問題が出ないようにしたい」と依頼があった。

## 決定

### 同梱するもの

- UniVRM 0.131.4（タグ `v0.131.4`）から、書き出しに使う実行時のコードとシェーダーだけを `Editor/Vendor/UniVRM/` にソースのまま入れる。UniGLTF（Runtime・UniUnlit・UniHumanoid・Utils・SpringBoneJobs）と VRM-1.0（Runtime・MToon10・FastSpringBone10・VrmLib）の 9 アセンブリ。インポーター・インスペクター・メニュー・サンプル・テストは入れない。
- DLL にはしない。ソースで持てば、差分を見られ、Unity の版ごとのコンパイルも Unity に任せられる。
- `Tools/vendor-univrm.py` が UniVRM のチェックアウトから毎回同じものを作る。`--check` で、リポジトリの `Editor/Vendor/UniVRM/` が作り直したものと同じかを確かめられる。改行は LF にそろえる（git の取り出し方で `--check` の結果が変わらないように）。
- ライセンス全文（MIT）を `Editor/Vendor/UniVRM/LICENSE.txt` と `ThirdPartyNotices/UniVRM.md` の両方に置く。

### プロジェクトの UniVRM とぶつからないようにする

Unity のプロジェクト全体で 1 つしか持てない名前をすべて変える。

- 名前空間: `UniGLTF`・`UniVRM10`・`UniHumanoid` などを `VRVlog.LilToonExporter.Vendor.UniGLTF` のように変える。
- アセンブリ名: `VRVlog.LilToonExporter.Vendor.<元の名前>`。`autoReferenced` は false にし、Exporter のアセンブリだけが名前で参照する。
- GUID: 元の GUID から決まる別の GUID にする（作り直しても同じ値になる）。プロジェクトの UniVRM と同じアセットの GUID が重ならない。
- シェーダー名: `Hidden/VRVlog/LilToonExporter/Vendor/` の下に移す（MToon10・MToon10 の URP 版・UniUnlit）。シェーダー名で材質を見分けていた所（ギミックの判定、材質の置き換え、互換表示）は、同梱版の名前と元の名前の両方を見る。
- `Resources` のフォルダ名・メニュー・**Add Component** の項目も変える（メニューと **Add Component** には出さない）。
- 同梱したアセンブリは `includePlatforms` を空にし、`defineConstraints` に `UNITY_EDITOR` を入れる。Editor だけに限る `includePlatforms: ["Editor"]` にすると、Unity はそのアセンブリの MonoBehaviour を `AddComponent` で付けられない（「editor script」として拒む）。Exporter は書き出し用の複製に `Humanoid`・`Vrm10Instance` を付けるので、これでは書き出せなかった。`UNITY_EDITOR` の条件でビルドには入らない。

### プロジェクトの UniVRM で付けた設定を読む

アバターにはプロジェクトの UniVRM の `Vrm10Instance`（と `VRM10Object`・`VRM10Expression`、MToon10 の材質）が付いていることがある。Exporter はそれらの型をコンパイル時に参照できない（版も入れ方も分からず、入っていないこともある）ので、`ForeignUniVrm` が型の名前で探す。

- 名前空間が `UniVRM10`・`UniGLTF`・`UniHumanoid` で、同梱版に同じ名前の型があるものを「プロジェクトの UniVRM の型」とみなす。
- 書き出し用の複製では、それらのコンポーネントを同梱版のものに付け替え、`SerializedObject` で同じパスと型の値を写す。プロジェクトの UniVRM にあって同梱版に無い項目・同梱版にあってプロジェクトの UniVRM に無い項目は、写さずに同梱版の既定値のままにする（版が違っても止まらない）。参照している `VRM10Object`・表情のアセットは、同梱版の型で一時的に作り直す。MToon10・UniUnlit の材質は、シェーダーを同梱版に替えた複製を使う。
- 元のアバター・プロジェクトのアセット・材質は変更しない。瞬き・表情・中立の形の読み取りなど、元のアバターから設定を読む所は `ForeignUniVrm.AuthoredSettings` で、作り直した同梱版の設定を読み取り専用で使う（設定のアセットが変わらない間は作り直さない）。

### なくしたもの・残したもの

- `vpmDependencies` の `com.vrmc.gltf`・`com.vrmc.vrm`、UniVRM の版の確認（`DependencyPolicy` の UniVRM の範囲と、書き出しの前の「対応しない UniVRM」）をなくした。動作環境の表示からも UniVRM の行を外した（診断のコピーには、プロジェクトの UniVRM の版を「Exporter では使わない」として残す）。
- VRMを保存したあとの **アプリでの見え方を確認**（`Editor/AppPreview/`）は、VRM を読み込むのでインポーターが要る。今回は同梱せず、これまでどおり Package Manager の UniVRM 0.131.x があるときだけコンパイルする。無いときはボタンを出さない。
- 同梱した UniVRM の依存（`com.unity.mathematics`・`com.unity.timeline`）を `package.json` の `dependencies` に足した。

## Avatar Optimizer との組み合わせ

Avatar Optimizer（AAO）1.9.20 は、`com.vrmc.vrm` が入っているときだけ（`AAO_VRM1`）UniVRM の型を知っていて、`Vrm10Instance` があるアバターでは root の Animator の controller を読まず、ばね・コンストレイントの参照を残す。同梱版の型は AAO から見えないので、そのままでは次の 2 つが起きた（develop との比較で見つかった）。

- controller の無い root の Animator を AAO が読みにいき、`ArgumentNullException` で止まる。
- AAO が root の Animator やばねの骨を使われていないものとして消す。

対処として、AAO の公開 API（`ComponentInformation`）で同梱版の `Vrm10Instance`・ばね・コライダー・コンストレイント・`Humanoid` の依存と変更を宣言した（AAO の `ComponentInfos.VRM1.cs` を写したもの、MIT）。あわせて、同梱版の `Vrm10Instance` があるときだけ、最適化の間だけ空の AnimatorController を root に貸す（AAO が `com.vrmc.vrm` の `Vrm10Instance` で controller を読まないのと同じ結果にする）。`Vrm10Instance` の無いアバターは従来どおり AAO のエラーで止まる。

## 確認

検証用のプロジェクトを 4 つの状態にして、EditMode テストを回した（Unity 2022.3）。

ENV_RESULTS

書き出し結果の比較（`Tools/run-export-regression.ps1`・`Tools/compare-exports.py`）: 実際のアバター 7 体（A1〜A7、MA・AAO・lilToon を使うもの）を、develop（UniVRM 0.131.4 を Package Manager で入れたもの）とこのブランチで 2 回ずつ書き出した。14 組すべてでファイルがバイト単位で一致し、同じ版の 1 回目と 2 回目も一致した。同梱版は 0.131.4 と同じソースから名前だけを変えたもので、VRM の中身にはシェーダー名・名前空間が入らないため。

### 旧版からの更新と旧版への戻し

同じ検証用プロジェクト（アバター 7 体）で、VCC／VPM の更新と同じくパッケージのフォルダを差し替えて、次の順に確かめた。各段で Unity を起動し直し、全アバターを書き出した。

| 段 | 状態 | 書き出し | 衝突・重複 | コンパイルエラー |
|---|---|---|---|---|
| U0 | 直近のベータ（0.12.0-beta.5）＋ `com.vrmc.gltf`・`com.vrmc.vrm` 0.131.4 | 7/7 | 0 | 0（※） |
| U1 | U0 にこのブランチを上書き（`com.vrmc` は残る） | 14/14（7 体×2 回） | 0 | 0 |
| U2 | U1 から `com.vrmc` を消す | 7/7 | 0 | 0（※） |
| D1 | U2 から直近のベータへ戻し、`com.vrmc` 0.131.4 を入れ直す | 7/7 | 0 | 0（※） |

衝突・重複は、ログの同名アセンブリ・GUID の重複・型の衝突（CS0433・CS0436）・シェーダーの重複の行を数えた。※の段は、パッケージを差し替えた直後の 1 回目のコンパイルで Unity が消えたファイルを探す CS2001 を出すが、読み込み直したあとのコンパイルでは出ず、書き出しも通っている。旧版の Exporter はプロジェクトに UniVRM の型のアセットを保存しないので、戻したときに読めなくなるアセットは無い。

## 成り立たない場合・残したこと

- UniVRM の新しい版の修正を取り込むときは、`Tools/vendor-univrm.py` で作り直し、テストと書き出しの比較をやり直す。プロジェクトの UniVRM の版を上げるだけでは、Exporter の書き出しは変わらない。
- プロジェクトの UniVRM の設定で、同梱版（0.131.4）に無い項目は書き出しに反映されない。0.131.4 より新しい版で増えた項目を使いたいときは、同梱版を上げる。
- AAO が同梱版の型を知らないことへの対処（`ExportVrm10Information.cs`）は AAO 1.9.20 の宣言を写したもの。AAO が UniVRM の扱いを変えたときは見直す。
- リリースのワークフロー（`.github/workflows/release-vpm.yml`）はまだ UniVRM の依存パッケージの ZIP を作って添付している。この変更では触っていないので、次のリリースの前に外す必要がある。
