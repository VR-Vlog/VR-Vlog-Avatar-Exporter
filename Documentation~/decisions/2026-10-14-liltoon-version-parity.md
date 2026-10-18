# lilToonの版ごとの書き出しと、2.3.4へ上げてからの書き出しの自動照合

- 日付: 2026-10-14
- 対象: `Tests/Editor/LilToonVersionParityHarness.cs`・`Tools/compare-liltoon-version-exports.py`・`Tools/run-liltoon-version-parity.py`・`Tests/Compatibility/DependencyStartupTests.cs`・`Compatibility/Evidence/LilToonVersions/`
- 関連: Epic #145、子 Issue #149（WBS 1-7 の全版での実行と 1-8 の自動照合）

## 背景

対応する lilToon の範囲は 2.0.0〜2.3.4 で、根拠は「lilToon 本体の移行処理だけで 2.3.4 の形になる」ことだった（#145）。範囲を決めた実測では、シェーダーの構成とマテリアルの保存済みプロパティを比べたが、Exporter が実際に書き出すファイルは比べていなかった。Epic の完了の条件は「lilToon 2.0.0〜2.3.4 のプロジェクトで書き出せ、2.3.4 へ上げてから書き出したものと `extras` の版の記録以外の差が無い」で、これを版ごとのプロジェクト（#165 で作ったもの）で自動で確かめる手段が無かった。

1-7 では 6 つの版のプロジェクトで `compatibility` と `exporter-behavior` を回す。main（598783d）の Exporter に入れ替えた直後の `compatibility` は、2.0.0／2.1.0／2.1.10 の 3 版とも `DependencyStartupTests.MenuResolvesBackendWithoutInitializationRegistration` の 1 件だけが落ちた。前の段（#165）でも 2.3.4 の作成直後に 1 回だけ落ち、2 回目は通っていた。

## 決定

### 1-8: 照合の手順

1. 版 X のプロジェクトをコピーする（版ごとのプロジェクトは触らない）。コピーの `Packages/manifest.json` の Exporter の参照だけを、照合するチェックアウトへ向ける。
2. コピーを batch mode で開き、`LilToonVersionParityHarness.CreateAndExport` を実行する。版 X の lilToon で合成マテリアルを作り、`Assets/VRVlogLilToonParity/` に `.mat` として保存する（版 X の既定値とシリアライズの形を残すため）。テクスチャは 4x4 の PNG を 2 枚（色、A が 1 未満のランプ）作る。マテリアルごとに、テストの合成 Humanoid（`AttachmentConnectionTests.Fixture`）へ割り当てて書き出す（`A`）。同じセッションでもう一度書き出す（`A-again`）。
   - 合成マテリアル: バックライト（色の A 0.5 と 1、テクスチャの A が 1 未満）、ファー、Outline Lit、MatCap の UV1 ブレンド（1st・2nd）、影色（1st〜3rd・境界色）、リム、リムシェード、半透明、カットアウト、カタログの全シェーダー（52 本）の既定のマテリアル。計 63 件。
   - 書き出しの前の確認は書き出し画面と同じ `LilToonExporterWindow.RequireSupportedLilToon`（範囲の判定・構成の照合・実際の版）を通す。
3. コピーの `Packages/jp.lilxyzw.liltoon` を 2.3.4 に差し替えて開き直し、`ExportSaved` で保存済みのマテリアルを書き出す（`B`）。ハーネスはエディタが 20 秒落ち着くまで待ってから書き出すので、lilToon 2.3.4 の起動時の処理（`lilStartup` の `MigrateMaterials` など）が先に走る。Exporter は移行処理を呼ばない。
4. `Tools/compare-liltoon-version-exports.py` で `A` と `A-again`、`A` と `B` を比べる。glTF の JSON は全部の値を完全一致（浮動小数も `==`）で比べ、BIN はすべての bufferView を SHA-256 で比べる（`VRVLOG_materials_liltoon` の DEFLATE のチャンクを含む）。差として許すのは `asset.extras.VRVLOG_lilToonInstalledVersion` だけ。`A` の値が版 X、`B` の値が 2.3.4 であることも確かめる。
5. 1〜4 は `Tools/run-liltoon-version-parity.py` で 1 本で回せる。結果は `parity.json` に、版・Unity・Exporter のコミット・比較の結果を書く。

ハーネスはテストのクラスではなく `-executeMethod` の入口にした。`compatibility` と `exporter-behavior` のケース数を変えないため。

### 書き出しごとに変わる名前の扱い

対照（2.3.4 のまま開き直す）で、同じ版の 2 回の書き出しの間にも JSON が 1 か所違った。Exporter が瞬き用の空のターゲットを書き出しのたびに `__VRVlog_BlinkNone_<GUID>` という名前で作るため（`BlinkExportSession`。`ExportOptimizationBindings` の `__VRVlog_Empty_<GUID>` も同じ）。lilToon とは関係が無い。比較ツールは、この形の名前（接頭辞と小文字 32 桁の GUID）だけを出現順の番号に置き換えてから比べる。番号に置き換えるので、片方で 2 つの名前がもう片方で 1 つになっているような構造の違いは差として残る。

2026-10-18 からは、これらの名前は書き出しのたびには変わらない（`2026-10-18-deterministic-generated-names.md`）。番号への置き換えは、それより前の Exporter で書き出したファイルを比べるために残している。

### 1-7: 起動直後のメニューのテスト

`MenuResolvesBackendWithoutInitializationRegistration` は、Unity がインポートかコンパイルをしている間にメニューを実行すると、仕様どおり診断画面（`DependencyDiagnosticsWindow`）を開く（`DependencyDiagnostics.StartupError` が `IsBusy` を返す）。Exporter を入れ替えた直後の 1 回目の実行では、テストが始まった時点でまだ取り込みが続いていることがあり、書き出し画面が開かずに落ちていた。テストを `[UnityTest]` にし、`DependencyDiagnostics.IsBusy` が下りるまで（最大 300 秒）待ってからメニューを実行する。失敗したときは `DependencyDiagnostics.CreateReport()` をメッセージに出す。ケース数は 1 のまま。

待つだけでは足りない場合があった。2.3.0 のプロジェクトでは、テストの開始直前に「Define symbols changed」で再コンパイルが要求され、コンパイルが終わってもテストランナーがアセンブリの再読み込みを止めている（`EditorApplication.LockReloadAssemblies`）ため、`isCompiling` が下りないまま 180 秒の既定のタイムアウトで落ちた。テストの先頭で `EditorApplication.isCompiling` なら Test Framework の `WaitForDomainReload` を yield し、再読み込みをさせてから続ける。再読み込みの後は列挙子の状態だけが戻り、ローカル変数は失われるので、この yield はテストの最初に置き、その前で作った値を後で使わない。タイムアウトは 600 秒にした。

## 検討した代替案と不採用理由

- ハーネスを NUnit のテストにして runner のプロファイルで回す: 2 回の Unity の起動の間で lilToon を差し替える必要があり、1 回の `-runTests` では書けない。`[Explicit]` のテストにしても、既存のプロファイルのフィルタと件数の照合に手を入れることになる。
- アバターをプレハブとして保存する: 骨格とメッシュは lilToon の版と関係が無く、テストの合成 Humanoid を毎回同じ手順で作れば同じものになる。版で変わり得るのはマテリアルだけなので、マテリアルとテクスチャだけをアセットにした。
- 画像をデコードした画素で比べる: 前の段の決定記録の未決だった。実測では全版で bufferView のバイト列まで一致したので、デコードは要らなかった。
- GUID の名前を Exporter 側で決まった名前にする: 書き出しの挙動を変えることになり、この Issue の範囲を超える。比較の側で狭い規則で扱う方が影響が小さい。
- 起動直後のテストを再実行で通す: 不安定さの理由が分かっていて、テストの前提（エディタが落ち着いている）を待てば直る。再実行に頼ると本当の失敗を隠す。

## 影響

- Exporter のパッケージに入るファイル・書き出しの挙動は変わらない（`Tests/` と `Tools/` はパッケージに入らない）。`.github/workflows` は変えていない。
- 検証の Python のテスト（`Tools/test-liltoon-version-parity.py`・`Tools/test-liltoon-version-parity-runner.py`）を `Tools/run-validation.py` に足した。
- 全版の結果は `Compatibility/Evidence/LilToonVersions/README.md` に書いた。
- 実測で分かったこと:
  - lilToon 2.0.0〜2.3.4 はどれも `lilConstants.currentVersionValue` が 45 で、作ったマテリアルの `_lilToonVersion` は版 X でも 2.3.4 へ上げた後も 45 だった。2.3.4 の起動時の `MigrateMaterials` は走るが、書き換える対象が無い。
  - `_EnvRimBorder`（2.0.0／2.1.0 の既定 0.85、2.1.10 以降の既定 3.0）は Exporter のカタログで `float:external` で、書き出しのファイルに入らない。Epic の本文にある「0.85 のまま書き出される」は、ファイルには出ない（受け入れの文書 #150 で直す）。

## 未決

- 2.3.4 より新しい版（Epic の未決 1）は対象にしていない。方針が決まったら、そのプロジェクトを作って同じ照合を回す。
- 合成のマテリアルは lilToon の Shader Setting を既定のまま作った。Shader Setting を変えた状態・暗号化・カスタムシェーダーは範囲を決めた実測と同じく試していない。
- 実際のアバターでの見た目の比較は 1-10（#150）で行う。

## 出典

- 照合の実測（Unity 2022.3.22f1・Built-in RP・batch mode。版ごとのプロジェクトのコピーで 63 件を書き出して比べた）: `Compatibility/Evidence/LilToonVersions/README.md`
- lilToon の各版の `lilConstants.currentVersionValue` と `_EnvRimBorder` の既定値: [lilxyzw/lilToon のリリース](https://github.com/lilxyzw/lilToon/releases) の VPM 用 ZIP
- 版ごとのプロジェクト: `Documentation~/decisions/2026-10-14-liltoon-version-projects.md`
