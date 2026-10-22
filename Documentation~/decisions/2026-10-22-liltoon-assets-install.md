# Assets の下に入れた lilToon でも書き出す

- 日付: 2026-10-22
- 対象: `Editor/Compatibility/LilToonInstallation.cs`・`Editor/Compatibility/DependencyPolicy.cs`・`Editor/Compatibility/DependencyDiagnostics.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/AppPreviewWindow.cs`・`Editor/LilToonMainTextureBaker.cs`・`Editor/Locales/*.json`・`Tools/DependencyCompatibilityTests.cs`・`Tests/Editor/LilToonInstallationTests.cs`・`Compatibility/README.md`・`CHANGELOG.md`

## 背景

書き出し前の確認は、lilToon を Package Manager に登録された `jp.lilxyzw.liltoon` から探していた。lilToon を unitypackage から読み込むと `Assets` の下に入り、Package Manager には何も登録されない。この場合はシェーダーが使えるのに「lilToonが見つかりません（VCC／ALCOMでlilToonを追加）」で止まり、環境の画面と診断のコピーでも未インストールになっていた。

## 決定

- lilToon の場所は `LilToonInstallation.Find()` の 1 か所で決める。順番は次のとおり。
  1. Package Manager に `jp.lilxyzw.liltoon` があれば、その版を使う（これまでと同じ）。
  2. 無ければ、lilToon 自身の `package.json` を GUID `397d2fa9e93fb5d44a9540d5f01437fc` で探す。lilToon も自分の版をこの GUID の `package.json` から読んでいて、unitypackage にも同じ GUID で入っている。パスが `Assets/` の下で、`name` が `jp.lilxyzw.liltoon`（または空）なら `version` を読む。`version` が無いか読めなければ版は不明にする。
  3. それも無ければ、`Shader.Find("lilToon")` のシェーダーが `Assets/` の下にあれば、版を不明として扱う。
- 版が不明なときは `DependencyPolicy.LilToonUnknownVersion`（`unknown`）を使う。`X.Y.Z` として読めない版と同じ扱いになり、書き出しは止めず、「アプリではlilToon 2.3.4として描画します（導入済みのバージョンは不明）」の警告を出す。書き出したファイルの `asset.extras.VRVLOG_lilToonInstalledVersion` には `unknown` を記録する。アプリはこの値を読まない。
- シェーダー構成の確認（`LilToonStructureCheck`）とベイク用シェーダー `Hidden/ltsother_baker` の確認は、もともとパスではなく `Shader.Find` で探しているので変えない。版が不明のときは、止める文の版の部分を「不明」にする。
- メインカラー2nd/3rdの透明度を焼き込む `Hidden/VRVlog/LayerAlphaBaker` は、lilToon のファイルを `Packages/jp.lilxyzw.liltoon/...` のパスで `#include` している。lilToon が `Assets` の下にあるとこのパスが無く、シェーダーのコンパイルに失敗して Unity のエラー用シェーダーで描かれ、透明度が 1 の画像を黙って書き出していた。`Assets` の下のときは同じソースの `#include` を見つけたフォルダへ書き換え、`ShaderUtil.CreateShaderAsset` でメモリ上に作ったシェーダーで焼き込む。ベイク用シェーダーにコンパイルエラーがあるときは、誤った画像を書き出さずに止める。焼き込み中は非同期のシェーダーコンパイルを切り、コンパイル中の仮のシェーダーで描かないようにした。
- 依存関係の診断の画面の lilToon の行と、書き出しの画面の **動作環境** の lilToon の行は「2.3.4（Assetsに導入: Assets/lilToon）」のように、版と入っている場所を出す。**アプリでの見え方** の画面は版だけを出し、不明なら「lilToon 不明」にする。診断のコピーは `jp.lilxyzw.liltoon: 2.3.4 (Assets: Assets/lilToon)` と書く。「不明」と「{0}（Assetsに導入: {1}）」は 4 つの言語のファイルにも足した。

## 検討した代替案と不採用理由

- `Assets` の下を名前で探す（`lilToon` という名前のフォルダや `package.json` を全部見る）: フォルダの名前は利用者が変えられ、ほかのパッケージの `package.json` も拾う。lilToon 自身が使っている GUID のほうが確実で、`AssetDatabase` の 1 回の呼び出しで済む。
- 版が読めないときは止める: シェーダーがあれば書き出せる。これまでの方針（2026-10-22-liltoon-no-version-gate）では版で止めないので、版が分からないことも警告にとどめる。

## 確認したこと

- `Tools/run-compatibility-tests.ps1` で、`unknown` が読めない版として分類され、止めずに「不明」の警告になること、`DisplayLilToon`・`DescribeLilToon` の表示を確かめた。
- `LilToonInstallationTests` で、`package.json` から版と場所を読むこと、版の無い・壊れた `package.json` は不明になること、ほかのパッケージの `package.json` は lilToon と見なさないことを確かめた。場所の判定は `FromAssetPaths`（パスと読み込みを渡す）に分けたので、lilToon が登録済みの CI でも、`Assets` の下の `package.json`・移したフォルダ・`package.json` が無くシェーダーだけある場合（場所は `Shader` の 1 つ上）・`Packages` の下だけの場合をそれぞれ確かめている。
- 同じテスト用プロジェクトを 2 つ複製し、lilToon 2.3.4 を `Packages` の下に置いたもの（Package Manager に登録される）と、`Assets/lilToon` に移したもの（何も登録されない）で、Unity 2022.3.22f1 の EditMode テストをすべて流した。`Assets` 側では `Find()` が `Assets/lilToon` と 2.3.4 を返し、環境の診断と書き出しの確認が同じ版を使い、表情ボードの書き出しの経路で 1 回書き出して lilToon の拡張が検証を通ることを確かめた。

- 上の 2 つの複製で、最初は `Assets` 側だけ `LayerAlphaTests` の 14 件が透明度 1.0 で失敗した（`Packages` 側は通る）。原因は `LayerAlphaBaker` の `#include` のパスで、書き換えたあとに `Assets` 側で `LayerAlphaTests` と `LilToonInstallationTests` が通ることを確かめた。

## 影響

- unitypackage で lilToon を入れたプロジェクトでも書き出せるようになる。Package Manager に lilToon があるプロジェクトの動きは変わらない。
- `Assets` の下の lilToon では、パッケージを読み込んだときに `LayerAlphaBaker.shader` 自体のコンパイルエラーがコンソールに出る。焼き込みはメモリ上のシェーダーを使うので書き出しには影響しない。
- lilToon の `package.json` を消した・GUID が変わった場合は、シェーダーがあれば版不明として書き出す。
