# lilToonのカタログ生成スクリプトと構成ハッシュ

- 日付: 2026-10-14
- 対象: `Tools/liltoon_catalogue.py`・`Tools/generate-liltoon-catalogue.py`・`Tools/check-liltoon-structure.py`・`Tools/check-upstream-dependencies.py`・`Compatibility/liltoon-structure.json`
- Issue: #148（Epic #145 の WBS 1-4）

## 背景

`Editor/LilToon234Catalogue.cs`（シェーダー 52 本・プロパティ 614 件・シェーダーごとの必須プロパティ／パス／LightMode）は「lilToon 2.3.4 の公式のシェーダー宣言から生成した」とあるが、生成スクリプトがリポジトリに無かった。Epic #145 で対応範囲を lilToon 2.0.0〜2.3.4 に広げるので、範囲内の各版のシェーダー構成が 2.3.4 と同じであることを、Unity を使わずに CI で確かめられるようにしたい。

## 決定

### カタログの生成

- lilToon のタグの `Assets/lilToon/Shader/*.shader`（Built-in RP 用の 65 ファイル）を読み、カタログを生成する。2.3.4 のタグ（commit `252fd8cf`）から生成した結果は、今の `LilToon234Catalogue.cs` とバイト単位で一致した。カタログは書き換えていない。
- 読み方は次のとおり。どれも今のカタログから逆算し、2.3.4 で一致を確かめた。
  - ファイル名の順（ordinal）に読む。`Shaders` は `ltspass_` で始まらない 52 本で、値はファイル名から拡張子を除いたもの。
  - `Properties` は全 65 ファイルの宣言を初出順に並べたもの（`ltspass_*`・`Hidden/ltsother_baker` も含む。`_DummyProperty` は除く）。型は `Float`・`Int`・`Range` を `float`、`Color` を `color`、`Vector` を `vector`、`2D`・`3D`・`Cube` を `texture` にする。
  - 同じ名前が複数回宣言されたら最初の宣言を使う（Unity と同じ）。シェーダーによって型が違う名前があれば生成を止める。
  - `UsePass "Hidden/ltspass_xxx/NAME"` は参照先のパスの名前と LightMode に置き換える。名前の無いパス（`LightMode = Never` の偽のパス）と `GrabPass` はカタログに入れない。
- 読めないものは黙って飛ばさず生成を止める: Properties の中に宣言として読めない部分が残ったとき、マテリアル用のシェーダーに LightMode が `Never` 以外の名前の無いパスがあったとき、SubShader が 2 つ以上あったとき。新しい版でこれらが出たら、パーサーを直すか構成の差として扱うかを人が決める。
  - `RequiredProperties` は各シェーダーが宣言する runtime のプロパティを宣言順に並べたもの。`Passes` はパス名を ordinal で並べたもの、`PassLightModes` は宣言順。`TextureFaces` は runtime のテクスチャで、2D を 1、Cube を 6 にする。
- プロパティの役割（runtime／editor／external）は lilToon のソースからは決まらない Exporter 側の方針なので、`Tools/liltoon_catalogue.py` の `EDITOR_PROPERTIES`・`EXTERNAL_PROPERTIES` に表として持つ。表に無い名前は runtime になる。新しい版で追加されたプロパティは runtime として必須プロパティに入り、構成ハッシュが変わる。
- ソースは、手元のフォルダ（`--source`）、手元のクローンのタグ（`--git`）、GitHub のタグのアーカイブ（`--fetch`）のどれからでも読める。アーカイブは一時フォルダに展開し、リポジトリには置かない。commit はアーカイブの pax ヘッダーから読む。

### 構成ハッシュ

構成ハッシュは、カタログのモデルを次の形に正規化した JSON（キーを並べ替え・区切りの空白なし・ASCII）の SHA-256。

```
{"schema": 1,
 "properties": [[名前, 型, 役割, runtime のテクスチャの次元 (2D/Cube) か ""], ...]  名前順,
 "shaders": [[シェーダー名, ファイル名, [runtime のプロパティ名 ...] 名前順, [[パス名, LightMode], ...] 名前順], ...]  シェーダー名順}
```

- 含めるもの: マテリアルに使うシェーダー（`ltspass_` 以外）の名前とファイル名、全ファイルで宣言されたプロパティの名前・型・役割、runtime のテクスチャの次元、シェーダーごとの runtime のプロパティ、名前付きのパスと LightMode。
- 含めないもの: 版・commit、宣言の順、既定値（`_EnvRimBorder` の 0.85／3.0 など）、表示名、属性（`[lilToggle]` など）、名前の無いパス・`GrabPass`、`ltspass_*` のパス用シェーダー自体のプロパティの所属、シェーダーキーワード（`#pragma shader_feature` など）とレンダーステート。
- 同じ値をソースからも `LilToon234Catalogue.cs` からも計算できる形にした。カタログが持っていない情報（editor／external のプロパティがどのシェーダーにあるか、editor のテクスチャの次元）は含めていない。

### 照合

- `Compatibility/liltoon-structure.json` に、lilToon 2.0.0〜2.3.4 の全タグ（19 個）と比較用の 1.10.3 の commit・構成ハッシュ・2.3.4 と一致するかを置く。
- `Tools/check-liltoon-structure.py`（`Tools/run-validation.py` から呼ぶ・ネットワーク不要）が、表の 2.3.4 のハッシュと `LilToon234Catalogue.cs` から計算したハッシュの一致、対応範囲の全タグが 2.3.4 と一致すること、表の形を確かめる。
- 範囲内のタグが表から抜けていないことは、オフラインの検査では範囲の両端だけを確かめる（タグの一覧はネットワークが無いと分からない）。2.0.0〜2.3.4 の 19 タグが揃っていることは `Tools/test-liltoon-catalogue.py` で確かめ、表を作り直すときは `structure --fetch` が GitHub のタグ一覧から範囲内の全タグを取る。
- 対応範囲は `Compatibility/dependencies.json` の `lilToon.supported`（`minimum`・`maximum`。#146 で追加）から読む。Exporter の版の判定（`DependencyPolicy.LilToonMinimum`／`LilToonMaximum`）と同じ値を使う。
- `Tools/check-upstream-dependencies.py`（手動実行）は、lilToon の新しいリリースがあるときだけそのタグを取得して構成ハッシュを計算し、2.3.4 と一致するかと差の一覧を出力に足す。パッケージや対応範囲は変えない。

## 計算した結果

| タグ | 構成ハッシュ（先頭 16 桁） | 2.3.4 と |
|---|---|---|
| 1.10.3 | `1542a184f8cc4938` | 差あり |
| 2.0.0・2.1.0〜2.1.10（11 個）・2.2.0・2.2.1・2.3.0〜2.3.4（5 個） | `7561ec4344159603` | 一致 |

- 1.10.3 の差は、2.3.4 に無いプロパティ 35 件（`_BitKey0`〜`_BitKey31`・`_IgnoreEncryption`・`_Keys`・`_FurMeshType`）と、1.10.3 に無い 3 件（`_EnvRimBorder`・`_EnvRimBlur`・`_Ramp`）。Epic #145 の Unity での実測と同じ。
- Epic #145 の実測で使った各版のパッケージ（2.0.0／2.1.0／2.1.10／2.2.0／2.2.1／2.3.0／2.3.3／2.3.4／1.10.3）から計算しても同じハッシュになった。

## 検討した代替案と不採用理由

- Unity で読んだシェーダー（`ShaderUtil`）から構成を取る: CI に Unity が要り、ネットワーク無しの検査にできない。ソースの宣言と実行時に読める構成のずれ（下の「未決」）は書き出し前の照合（WBS 1-5）で扱う。
- ハッシュにソースの全情報（editor／external のプロパティのシェーダーごとの所属、`ltspass_*`・baker の構成）を含める: カタログから同じ値を計算できなくなり、「カタログのハッシュ＝表の 2.3.4 のハッシュ」を確かめられない。実際に全情報で比べると、2.0.0〜2.2.1 は `Hidden/ltsother_baker`（エディタでのテクスチャのベイク用）に 4 件（`_Main2ndTexDecalAnimation`・`_Main2ndTexDecalSubParam`・`_Main3rdTexDecalAnimation`・`_Main3rdTexDecalSubParam`）が無いだけで、ほかは 2.3.4 と同じだった。書き出しはこのシェーダーを使わない。
- 既定値もハッシュに含める: Epic #145 の方針で既定値の差（`_EnvRimBorder`）は構成の差に数えない。
- 役割の表をカタログから読む: 生成スクリプトがカタログに依存して循環する。表はスクリプト側に置き、カタログとの一致はバイト一致で確かめる。

## 影響

- `LilToon234Catalogue.cs` は変えていない。書き出しの挙動・アプリは変わらない。
- `Tools/run-validation.py` の検査が 2 つ増える（`check-liltoon-structure.py`・`test-liltoon-catalogue.py`）。どちらも標準ライブラリだけで、ネットワークを使わない。`.github/workflows` は変えていない。
- 新しい lilToon に合わせてカタログを作り直すときは `python3 Tools/generate-liltoon-catalogue.py catalogue --fetch <tag> --output <file>` で生成する。カタログを変えたら表も `structure --fetch --write` で作り直す。

## 未決

- ソースの宣言と Unity が実行時に読める構成のずれ（2026-10-14 の実測）: 2.3.4 の配布パッケージでは `Hidden/ltsother_baker` の上の 4 件が宣言から消えている（タグのソースにはある）。gem・ref 系の 5 本は `GrabPass` の分だけ Unity で名前の無いパスが 1 つ多い。どちらもカタログ・構成ハッシュの対象外で、書き出し前の照合（WBS 1-5）で照合の対象を決めるときに扱う。
- 2.3.4 より新しい版の扱い（Epic #145 の未決 1）は決まっていない。`check-upstream-dependencies.py` の出力は判断の材料になるだけで、範囲は変えない。
- Shader Setting を変えた lilToon、URP／HDRP 用のシェーダー（`.lilinternal` から作られるもの）は対象外。

## 出典

- lilToon（MIT License）: GitHub `lilxyzw/lilToon` のタグ 1.10.3・2.0.0〜2.3.4 の `Assets/lilToon/Shader/*.shader`
- Unity ShaderLab のリファレンス（Properties・UsePass・GrabPass・Pass Tags の LightMode）
- Epic #145 の範囲決めの実測（2026-10-14）
