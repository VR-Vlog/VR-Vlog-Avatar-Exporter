# lilToonの版ごとの検証用プロジェクトとrunnerの版の照合

- 日付: 2026-10-14
- 対象: `Tools/create-liltoon-version-projects.py`・`Tools/unity_packages.py`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`
- 関連: Epic #145、子 Issue #149（WBS 1-6・1-7 の runner 改修。1-8 は設計のみ）

## 背景

対応する lilToon の範囲を 2.0.0〜2.3.4 へ広げる（#145）。広げた後は、範囲内の各版で同じ Unity テストを回し、「版 X で書き出したもの」と「同じアバターを 2.3.4 へ上げてから書き出したもの」が `extras` の版の記録以外で一致することを確かめる。

変更前は次の状態だった。

- 版ごとの Unity プロジェクトは、範囲を決めるための実測で手作業で作っていた。作り方はリポジトリに無かった。
- focused runner（`Tools/run-unity-compatibility.py`）は Unity と UniVRM の版を照合するが、lilToon の版は見ていなかった。2.3.4 以外の lilToon が入ったプロジェクトで回しても、どの版で通ったのかが結果の JSON に残らなかった。

## 決定

### 版ごとのプロジェクト（1-6）

`Tools/create-liltoon-version-projects.py` で、lilToon の版ごとに空の Unity プロジェクトを 1 つずつ作る。既定の版は 2.0.0／2.1.0／2.1.10／2.2.1／2.3.0／2.3.4。

- Unity 2022.3・Built-in RP の空のプロジェクトに、`Compatibility/README.md` の手順 3 と同じ固定の版（Test Framework 1.4.6・Collections 2.1.4）を入れ、`testables` に Exporter を足す。Exporter は `--exporter` のチェックアウト（既定はこのリポジトリ）を `file:` で参照する。
- lilToon は GitHub の公式リリースの VPM 用 ZIP（`jp.lilxyzw.liltoon-<版>.zip`）を取得し、`Packages/jp.lilxyzw.liltoon` に埋め込む。取得元の URL と ZIP の SHA-256 をプロジェクトごとの記録（`liltoon-version-project.json`）に残す。ZIP の中の `package.json` の名前と版が指定と違えば止める。ZIP をキャッシュのフォルダに先に置いておけば取得しない。
- UniVRM（UniGLTF・VRM）と、`exporter-behavior` に要る VRChat SDK・NDMF・Modular Avatar は、呼び出す側が公式の配布物を展開したフォルダ（`--dependencies`）から各プロジェクトへ埋め込みでコピーする。UniGLTF と VRM が同じ版で、`Compatibility/dependencies.json` の対応版に入っていなければ止める。lilToon と Exporter がこのフォルダに入っていても止める。
- `--unity` を渡すと、作ったプロジェクトを batch mode で 1 回開いてパッケージを解決・インポートさせ、実際に入った版（`packages-lock.json` と各パッケージの `package.json`、`ProjectVersion.txt`）を記録に書く。指定と違う版が入ったら止める。コンパイルエラーで Unity が 0 以外で終わったプロジェクトは記録しない。
- 既にあるフォルダには作らない（作り直すときは消してから回す）。

### runner の lilToon の期待版（1-7 の runner 改修）

`--expect-liltoon <版>` を足した。

- Unity を起動する前に、プロジェクトの `packages-lock.json` から lilToon の入り方（埋め込み・`file:`・registry・git）を読み、そのパッケージの `package.json` の版を期待と比べる。違えば Unity を起動せずに止める。packages-lock が無い、パッケージのフォルダが別の名前を持つ、git のパッケージが `Library/PackageCache` に無い、のいずれも止める。
- 期待版はリリースの版（`2.3.4` の形）だけを受け付ける。プレリリースや `v2.3.4` は受け付けない。
- 結果の JSON に `lilToon`（期待版・実際の版・入り方）を書く。`--expect-liltoon` を付けない場合も、読めれば実際の版を書く（読めなければ `installed: null` と理由）。
- Unity の実行後にもう一度読み、実行中に lilToon が入れ替わっていたら失敗にする。

版を読む処理は `Tools/unity_packages.py` に置き、作成スクリプトと runner の両方から使う。

## 検討した代替案と不採用理由

- lilToon を git の URL（タグ）で manifest に書く: Unity が `Library/PackageCache` に取り込むまで版を確かめられず、取得のたびに GitHub へつなぐ。リリースの ZIP なら一度取れば SHA-256 で同じものか確かめられ、オフラインでも作り直せる。
- lilToon も `file:` で共有のフォルダを参照する: 版ごとに別のフォルダが要るのは同じで、Unity がパッケージのフォルダへ `.meta` を書くことがあるため、プロジェクトの中へ埋め込む方が他のプロジェクトへ影響しない。
- 期待版を C# のテスト（`DependencyEnvironmentTests`）で見る: Unity を起動してからでないと分からず、テストのケース数の照合も変わる。版が違うプロジェクトでは Unity を起動する前に止める方が早く、結果の JSON にも残せる。
- UniVRM や VRChat SDK もスクリプトで取得する: 配布元と認証の扱いがパッケージごとに違い、VRChat SDK などは利用条件に従って取得する必要がある。呼び出す側が公式の配布物を用意する形にした（`Compatibility/README.md` の手順 3 と同じ）。

## 影響

- 既存の runner の呼び方は変わらない。`--expect-liltoon` を付けなければ照合しない（結果の JSON に `lilToon` が増えるだけ）。
- Exporter のパッケージに入るファイル・書き出しの挙動は変わらない。`.github/workflows` は変えていない。
- Exporter を `file:` で参照するので、Unity が Exporter のチェックアウトへ `.meta` を書くことがある。runner の `runnerSource.gitDirty` はそのとき true になる。

## 1-8 の自動照合の設計（実装は次の段）

版 X で書き出したものと、同じプロジェクトを 2.3.4 へ上げて書き出したものを比べる。#146（版の範囲の判定）が入るまで、2.3.4 以外の版では書き出しが「版が違う」で止まるので、実装と全版での実行は #146 の後に行う。

1. 合成マテリアルを版 X の lilToon で作る。版 X のプロジェクトでテスト用の Editor コードが、2.3.4 のカタログにあるシェーダーで `.mat` を作り、アセットとして保存する（版 X の既定値とシリアライズの形がそのまま残るように、メモリ上ではなくファイルにする）。作るもの:
   - バックライト（`_UseBacklight` 1、`_BacklightColor` の A を 0.5 と 1）
   - ファー（`lilToonFur` 系。`_FurLayerNum` は 2.x の範囲の値）
   - Outline Lit（輪郭線付きのシェーダーで `_OutlineLitColor` などを既定から変える）
   - MatCap の UV1（`_MatCapBlendUV1` を 0 以外に）
   - リムの `_EnvRimBorder` を既定のまま（2.1.9 以前は 0.85 が保存される）
   - 影色（2.1.0 で描画が変わった `_ShadowColor` 系）と、半透明・カットアウト
   - カタログの各シェーダーの既定のマテリアル（Exporter が書き出せるもの全部）
   合成のメッシュとアバター（既存の生成フィクスチャの作り方を使う）にこれらを割り当て、プレハブとして保存する。
2. 版 X のまま書き出す（出力 A）。
3. プロジェクトの `Assets`・`Packages`・`ProjectSettings` を別のフォルダへコピーし（版ごとのプロジェクトは残す）、`Packages/jp.lilxyzw.liltoon` を 2.3.4 の ZIP に差し替えて開く。lilToon 2.3.4 の取り込み時の処理がマテリアルを移行する。Exporter は `lilStartup.MigrateMaterial` を呼ばない。同じプレハブを書き出す（出力 B）。
4. A と B の glTF の JSON から `VRVLOG_materials_liltoon` を取り出し、キーを並べ替えた JSON で比べる。画像はバイト列の SHA-256 で比べる。メッシュ・ノード・VRM の拡張も同じく比べる。差として許すのは `asset.extras` の実際の lilToon の版の記録（#147 で足すキー）だけで、それ以外の差は 1 つでも失敗にする。浮動小数も完全一致で比べる（移行処理は保存済みの値を書き換えないので、同じ値になるはず）。
5. 比較の道具は Python（`Tools/` に置く）にして、合成マテリアルの生成と書き出しは版ごとのプロジェクトで runner とは別のテストクラスとして回す（既存のプロファイルのケース数を変えない）。

確かめること: 2.0.0／2.1.0 で `_EnvRimBorder` が 0.85 のまま A と B の両方に出ること（2.3.4 へ上げても値は変わらない）。差が出たら Exporter で補正せず、原因を lilToon 側の移行の差として記録する。

## 未決

- 1-8 の比較で、A と B のテクスチャの圧縮や再エンコードが lilToon の版で変わる場合の扱い（画像を比べる前にデコードした画素で比べるか）。実測してから決める。
- 2.3.4 より新しい版（Epic の未決 1）のプロジェクトは作っていない。方針が決まったら `--versions` に足す。

## 出典

- 範囲を決めた実測（Unity 2022.3.22f1・Built-in RP・batch mode。版ごとに空のプロジェクトを作り、全シェーダーでマテリアルを作って移行処理を通し、2.3.4 と比べた）: Epic #145 の本文
- lilToon の各版の VPM 用 ZIP: [lilxyzw/lilToon のリリース](https://github.com/lilxyzw/lilToon/releases)
- runner と固定の版: `Compatibility/README.md`・`Compatibility/dependencies.json`
