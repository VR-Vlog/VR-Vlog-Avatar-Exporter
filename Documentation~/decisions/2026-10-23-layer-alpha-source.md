# メインカラー2nd/3rdの透明度を焼くシェーダーを.shaderアセットにしない

- 日付: 2026-10-23
- 対象: `Editor/LayerAlphaBakerSource.cs`（新規）・`Editor/LayerAlphaBaker.shader`（削除）・`Editor/LilToonMainTextureBaker.cs`・`Tests/Editor/LilToonInstallationTests.cs`・`ThirdPartyNotices/lilToon.md`・`CHANGELOG.md`
- 関連: [2026-10-22-liltoon-assets-install.md](2026-10-22-liltoon-assets-install.md)

## 背景

`Hidden/VRVlog/LayerAlphaBaker`はlilToonのファイルを`Packages/jp.lilxyzw.liltoon/...`のパスで`#include`している。lilToonが`Assets`の下にあるプロジェクトでは、焼き込みに使うシェーダーは2026-10-22からメモリ上で作り直していたが、`LayerAlphaBaker.shader`のアセット自体はUnityが読み込むたびにコンパイルし、「Couldn't open include file」をコンソールに出していた（2026-10-22の記録の「欠点」）。lilToonが入っていないプロジェクトでも同じエラーが出る。書き出しへの影響は無いが、利用者にはExporterの不具合に見える。

## 決定

- シェーダーのソースを`.shader`アセットではなく、C#の文字列`LayerAlphaBakerSource.Text`として持つ。Unityはこれをシェーダーとして読み込まないので、どの構成でもコンソールにエラーが出ない。
- `LilToonMainTextureBaker.LayerAlphaShader`は、lilToonがパッケージのときはソースのまま、`Assets`の下のときは`#include`をそのフォルダに書き換えて、`ShaderUtil.CreateShaderAsset`でメモリ上に作る。lilToonのフォルダが変わらない間は作ったシェーダーを使い回す。シェーダーの中身は変えていない。
- `LilToonInstallationTests.NoShaderAssetIncludesLilToonByItsPackagePath`で、Exporterのパッケージの`.shader`がlilToonのパッケージのパスを含まないことを確かめる。`LayerAlphaBakerCompilesWhereLilToonIs`はパッケージ構成でもメモリ上のシェーダーを通るようになった。

## 検討した代替案と不採用理由

- 相対パスの`#include`にする: Exporterと`Assets`の下のlilToonの位置関係はプロジェクトごとに違い、両方の構成で解決できる相対パスが無い。
- テキストファイル（`.txt`のTextAsset）で同梱する: パッケージを作る`Tools/build-package.py`の対象の拡張子に足し、実行時にアセットを探す処理が要る。C#の文字列ならどちらも要らない。

## 欠点

- シェーダーのソースがC#の逐語的文字列の中にあるので、`"`は`""`と書く。シェーダーとしての構文の色分けは効かない。
