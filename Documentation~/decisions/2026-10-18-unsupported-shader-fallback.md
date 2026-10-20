# 非対応シェーダーの材質を公式 lilToon で近似して書き出しを続ける

- 日付: 2026-10-18
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportFallbackConfirmation.cs`・`Editor/Locales/*.json`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/ToonPropertyProbe.shader`・`Tests/Editor/LilToonDerivedProbe.shader`・`Tests/Editor/ExportFallbackConfirmationTests.cs`・`README.md`・`CHANGELOG.md`

## 背景

公式 lilToon 2.3.4 のシェーダー以外を使う材質の扱いが、アバターの構成によって 3 通りに分かれていた。

- lilToon の材質を 1 つも持たないアバターは、すべての材質を色とメイン画像だけの MToon にしていた。透過・法線・発光・影色は失われていた。
- lilToon と他のシェーダーが混在するアバターは、他のシェーダーの材質を UniVRM の既定の書き出しに任せていた。lilToon 拡張には入らないので、VR Vlog では lilToon 表示にならなかった。
- 名前が `lilToon` で始まる公式外のシェーダー（lilToon を改変した派生版）は「公式lilToon材質が見つかりません。」か `NotSupportedException` で書き出しが止まっていた。

## 決定

- 書き出し用の複製で、材質の保存（「材質保存」の段）の最初に、公式 lilToon 2.3.4 のシェーダー一覧にも MToon10 にも当たらない材質を、公式 lilToon の材質に置き換える。元の材質は変更しない。置き換えた材質は元の材質と同じ名前にし、以降は通常の lilToon の経路（全情報の保存と MToon の代替）を通る。
- 置き換え先は、元の材質の描画方式から選ぶ。不透明は `lilToon`、カットアウトは `Hidden/lilToonCutout`、半透明は `Hidden/lilToonTransparent`。描画の状態（ブレンド・深度書き込み・キュー）は公式シェーダーの既定値を使う。
- lilToon の派生版（シェーダー名が `lilToon` で始まる、または `_lilToonVersion` と `_TransparentMode` を持つ）は、公式シェーダーにも同じ名前と型である値をすべて写す。`_TransparentMode` は lilToon 2.3.4 の RenderingMode（0 不透明・1 カットアウト・2 半透明・3 屈折・4 屈折ぼかし・5 ファー・6 ファーのカットアウト・7 ファーの 2 パス・8 宝石）として読み、0 は不透明、1 と 6 はカットアウト、それ以外は半透明に寄せる。3 以上のときは描画の状態を写さない。名前に `Outline` を含む派生版は輪郭線付きの公式シェーダーにする。
- 他のシェーダーは、よく使われるプロパティ名の組から、主画像（タイリングとオフセットを含む）・色・カットアウトのしきい値・法線マップと強さ・発光の画像と色・影色と影の画像・境界とぼかし・カリングを拾う。描画方式はキーワード（`_ALPHATEST_ON` など）・描画方式のプロパティ・シェーダー名・キューの順に判定する。シェーダー名に `Unlit` を含む材質は `_AsUnlit` を 1 にする。輪郭線・金属感などはここでは引き継がない。
- 影色は、影の画像が主画像と別なら `_ShadowColorTex` と `_ShadowColor` にそのまま入れる。影の画像がない、または主画像と同じなら、lilToon は影色を主色に掛けるので、元の影色を主色で割った比を `_ShadowColor` にする。
- RenderTexture などの動的な画像はファイルに保存できないので、どの画像の欄でも空として扱い、写さない（Texture2D と Cubemap だけを写す）。
- 置き換えの対象は、全情報の保存と同じく、頂点のあるメッシュを持つ SkinnedMeshRenderer と MeshRenderer（MeshFilter 付き）だけにする。パーティクルやトレイルの材質は VRM に出ないので置き換えず、確認にも出さない。
- シェーダーが見つからない材質（`Hidden/InternalErrorShader`）は、材質に保存されている値（`m_SavedProperties` とキーワード）から同じ規則で拾う。保存されている値に `_lilToonVersion` と `_TransparentMode` の両方がある材質は lilToon の派生版として扱い、`_TransparentMode` から描画方式を選ぶ。キューが「シェーダーの設定に従う」（-1）のまつげや髪の材質が、描画方式を読まれずに不透明で書き出されないようにするため。シェーダー名は残らないので、輪郭線付きの公式シェーダーにはしない。
- VRM の表情で色や画像の位置を動かす材質（表情の `MaterialColorBindings`・`MaterialUVBindings` に名前がある材質）は置き換えない。表情の動きの判定は置き換え前の材質の値で済んでおり、置き換えると基準値が変わるため。派生版は置き換えないと書き出せないので、この場合も置き換える。置き換えなかった材質は UniVRM の既定の方法で書き出す。lilToon の材質がもともとないアバターでも、ほかの材質を置き換えて lilToon の材質ができた場合は、色と主画像の MToon にはしない（表情の基準値を保つため）。
- 置き換えた材質は保存前の確認（自動設定できなかった項目）に「非対応のシェーダーを公式lilToonで近似しました」として対象のオブジェクトと材質名を出し、警告にも材質名を出す。結果の区分は「近似」にする。近似できなかった材質は元のまま残して警告を出す。
- 置き換えは段階ガード（`ExportStageGuard`）の `LilToonSnapshot` の段階の中で行う。公式の lilToon の材質が 1 つもなく、置き換えに失敗した派生版が残ったときは、段階ガードの規則に従い、止める設定では従来どおり「公式lilToon材質が見つかりません。」で止め、続ける設定ではその材質を色と主画像の MToon にして「近似」として記録する。
- 旧経路（`exporterVersion` を渡さない書き出し）は変えない。

## 確認したこと

- EditMode テストで、Standard の不透明・カットアウト・半透明、発光のキーワードが切れているとき、他の toon シェーダーと同じプロパティ名を持つテスト用シェーダー、lilToon 派生版を模したテスト用シェーダー、シェーダーが見つからない材質（派生版の値を持ち `_TransparentMode` が 1・2 でキューが -1 のもの、`_lilToonVersion` のないものを含む）、共有材質の 1 回だけの置き換え、表情で動く材質の除外、確認ダイアログの文言、混在アバターの書き出しで lilToon 拡張に両方の材質が入ること、派生版の RenderingMode 3・5・6・8 の寄せ先、主画像が RenderTexture の材質で全情報の保存と書き出しが止まらないこと、パーティクルの材質を置き換えないことを確かめた。
- 販売中のアバターと非対応シェーダーを使うアバターで、置き換え前後の見た目を比べた。比較画像はリポジトリには入れていない。

## 検討した代替案と不採用理由

- 非対応シェーダーを MToon の既定値にそろえる（lilToon のないアバターの従来の扱い）: 透過・法線・発光・影色が失われ、VR Vlog の lilToon 表示にも乗らない。
- 混在アバターの他のシェーダーを UniVRM の既定の書き出しのままにする: lilToon 拡張に入らないため、同じアバターの中で表示の仕組みが分かれる。
- シェーダーごとに個別の変換を書く: 対応するシェーダーの数だけ保守が増え、改変版に追従できない。プロパティ名の組で判定する方式にした。
