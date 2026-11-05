# Sunao Shader を lilToon へ写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/SunaoApproximation.cs`・`Editor/UnsupportedShaderSubstitution*.cs`・`Tests/Editor/ShaderApprox/SunaoApproximationTests.cs`

## 背景

Sunao Shader は影・発光・リム・マットキャップ・デカール・アウトラインを独自の名前で持つので、汎用の写しでは主画像と色しか入らない。

## 決定

- `IShaderApproximation` の実装を 1 つ足し、プロパティの意味どおりに lilToon の値へ写す。シェーダーを読み込めないマテリアルは、全バリアントが持つ `_SunaoShaderType` で見分ける。
- 描画の種類は `_SunaoShaderType` で決める。0 と 3 は不透明、1 と 4 は半透明、2・5・6・7 はカットアウト。
- lilToon に無いものは写さず、セクション名を書き出しの警告に出す（Stencil・Vertex Color・Occlusion・Light Boost・UV Animation・Parallax Emission・Output Correction など）。

## 比較の結果

配布物のシェーダーで 19 ケースを書き出し、元のシェーダーとピクセル比較した。差の平均（0〜255）は、リムの 1 ケースを除いて 1.5 以下だった。

リムは 6.9 で、`_RimLit` が 1 を超えるときの底上げが lilToon のリムに無い。`Rim Light Lift` として警告に出す。

シェーダー本体はリポジトリに入れない。テストは名前とプロパティだけを持つ `SunaoProbe.shader` で回す。

## アプリでの表示

書き出した VRM のうち 8 ケース（不透明・カットアウト・半透明・アウトライン・発光・リム・マットキャップ・デカール）をアプリの読み込み経路で表示し、元のシェーダーと同じ面から撮って並べた。アプリは VRM に入れた lilToon の値で描くので（読み込み結果は `full-liltoon:1`）、どのケースも写した設定が出て、デカールも同じ位置と回転で出る。

差が見えるのはリムで、`_RimLit` が 1 を超えるときの面全体の底上げはアプリにも出ない。MToon だけを読む他のアプリには、MToon に無い 2 層目のデカールは出ない。
