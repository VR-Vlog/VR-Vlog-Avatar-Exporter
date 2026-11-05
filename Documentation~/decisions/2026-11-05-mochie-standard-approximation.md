# Mochie Standard の値を公式 lilToon の同じ役割の項目へ写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/MochieStandardApproximation.cs`・`Editor/ShaderApprox/ShaderApproximations.cs`・`Editor/UnsupportedShaderSubstitution*.cs`・`Tests/Editor/ShaderApprox/MochieStandard*`・`README.md`・`README.en.md`・`CHANGELOG.md`

## 背景

Mochie Standard は Unity の Standard に近い PBR のシェーダーで、描画モードを `_BlendMode`、アルファの元を `_AlphaSource`、メタリックと粗さを個別の画像か 1 枚にまとめた画像（`_PackedMap`）のチャンネルで持つ。汎用の読み方は主画像と主色しか拾えず、半透明が不透明になり、アルファのマスク・メタリック・粗さ・色調補正が落ちていた。

## 決定

2026-11-05 のシェーダーごとの近似（`IShaderApproximation`）に 1 クラス足す。値は Mochie の inspector とシェーダー本体が使う意味で読む。

- 見分け方: シェーダー名が `Mochie/Standard` で始まる（Standard・Lite・Mobile）。シェーダーが見つからないマテリアルは、Mochie Standard にしかない `_PrimaryWorkflow` と `_PrimarySampleMode` が両方あるもの。
- 描画モード: `_BlendMode` 0 不透明・1 カットアウト・2 フェード・3 透過。2 と 3 はどちらも公式 lilToon の半透明にする。
- アルファ: `_AlphaSource` 1 はアルファのマスクの選んだチャンネルをアルファに置き換える（Mochie の式 `alpha = mask[channel] * opacity`）。公式 lilToon のアルファマスクの置き換えのモードで同じ式になる。頂点カラーのアルファ（2）は表示だけ。
- 法線: `_NORMALMAP_ON` があるときだけ法線マップを使い、強さは `_NormalStrength`。裏面で法線を反転する `_FlipBackfaceNormals` は公式 lilToon の `_FlipNormal` に写す。
- 発光: `_EMISSION_ON` があるとき、Mochie と同じく `pow(_EmissionColor, 2.2) * _EmissionStrength` を発光の色にし、マスクの選んだチャンネルを公式 lilToon の発光マスクに焼く。
- 反射: メタリックと粗さはワークフロー（メタリック・まとめた画像・スペキュラー）ごとに読む。粗さは `1 - roughness` を滑らかさとして焼く。`_SmoothnessToggle` が有効なら画像を滑らかさとしてそのまま使う。スペキュラーのワークフローはメタリック 0 にし、スペキュラー色を表示する。
- 色調補正: `_Filtering` が有効なとき、色相・彩度・明るさを公式 lilToon の `_MainTexHSVG` に写す。コントラストと発光の色調補正、照らした後の色にもう一度掛ける Post の色調補正（`_HuePost` などと `_ACES`）は式が無いので表示する。
- 陰影: Standard と同じ Lambert の陰影を、公式 lilToon の影の境界 0.75・ぼかし 0.5・黒の影で近づける。
- 視差・ディテール・雨・スクリーンスペースの反射・頂点の変形・風・サブサーフェス・AudioLink・発光の点滅・トライプラナー・UV のスクロールと回転は書き出さず、保存前に表示する。
- Mobile は `STANDARD_MOBILE` を定義して、`_AlphaSource`、発光のマスクと点滅を読まない。プロパティは Standard と同じなので、シェーダー名（SCSS の近似で `SourceValues` に足した `ShaderName`）が `Mochie/Standard Mobile` のときはこれらを写さず、表示もしない。シェーダーが見つからないマテリアルは名前が分からないので Standard として読む。

共通部分の変更は 1 つだけにした。焼き込みとアルファマスクの関数を `internal` にすること、`BakeMask` の倍率、`SourceValues` のシェーダー名は先に入った SCSS の近似と同じものを使う。キーワードを持つかを確かめるときに GrabPass を飛ばすようにした。GrabPass はプログラムを持たず、`ShaderUtil.PassHasKeyword` に渡すとコンソールに `Snippet not found` が出る。Mochie Standard は最初のパスが GrabPass なので、書き出すたびにこのエラーが出ていた。

## 確認したこと

- EditMode テスト 23 件で、描画モード 4 種、Lite と Mobile の名前、Mobile でアルファの元と発光のマスクを写さないこと、アルファのマスクのチャンネルとタイリング、不透明でアルファのマスクを読まないこと、法線と発光のキーワード、発光の式とマスク、色調補正と Post の色調補正の表示、メタリックと粗さ（スカラー・個別の画像・まとめた画像）、表示する効果、シェーダーが見つからない場合、同じ名前のプロパティを持つ別のシェーダーに当たらないことを確かめた。代わりのシェーダーには GrabPass を入れ、修正前は 22 件中 21 件がエラーで落ちることを確かめた。
- Standard・lilToon・VRChat SDK のシェーダー・他のトゥーンシェーダーのテストは変えずに通る。
- 実際の Mochie Standard を入れたプロジェクトで 8 通り（不透明・メタリックと粗さの画像・カットアウト・フェード・透過・両面・アルファのマスクと発光・色調補正）を書き出した。VRM のマテリアルは主画像を持ち、カットアウトは `MASK`（0.5）、フェードと透過は `BLEND`、両面は `doubleSided` になった。球に描いた元のシェーダーとアプリでの表示の差（0〜255 の平均）は、両面の裏側（15.8）と色調補正（9.5）を除いて 3 以下で、これまでの 12〜31 から下がった。比較画像はリポジトリには入れていない。

## 検討した代替案と不採用理由

- Standard と同じ扱いにして汎用の読み方に任せる: `_BlendMode` と `_AlphaSource` を読まないので半透明とアルファのマスクが落ちる。
- 色調補正を主画像に焼き込む: Mochie の彩度とコントラストは輝度を軸にした式で HSV と違うが、焼き込むと画像が 1 枚増える。色相・彩度・明るさは公式 lilToon の色調補正で近く、差は小さいので写すだけにした。
