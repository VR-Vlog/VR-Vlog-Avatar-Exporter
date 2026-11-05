# シェーダーごとの近似を 1 ファイルずつ足せるようにし、NonToon を最初の 1 件にする

- 日付: 2026-11-05
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Editor/ShaderApprox/`・`Editor/UniVrmOneClickExporter.cs`・`Editor/Locales/*.json`・`Tests/Editor/ShaderApprox/`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/MarkedMainProbe.shader`・`README.md`・`CHANGELOG.md`

## 背景

NonToon のマテリアルを書き出すと、VRM にテクスチャが入らず白い表示になっていた。原因は 2 つある。

- NonToon は主画像を `_BaseTexture` に持ち、`[MainTexture]` の印を付けている。非対応シェーダーの近似（2026-10-18 の決定）は決まった名前の一覧から主画像を探しており、`_BaseTexture` も `[MainTexture]` の印も読んでいなかった。lilToon のマテリアルが 1 つもないアバターの MToon も `_MainTex`・`_BaseMap` だけを読んでいた。
- NonToon の半透明は、ビルトインのレンダーパイプラインでキューを 2460 にする。キューが 2500 以下で `_Cutoff` を持つマテリアルはカットアウトと読んでいたので、半透明がカットアウトになっていた。

また、VRChat でよく使われる他のシェーダーも見た目を近づける方針になった（mk 指示）。2026-10-18 の決定ではシェーダーごとの変換を書かない方針だったが、汎用の読み方では描画モードや輪郭線の持ち方がシェーダーごとに違い、拾えない。

## 決定

- 汎用の読み方を直す。主画像と主色は、シェーダーが `[MainTexture]`・`[MainColor]` の印を付けたプロパティを最初に読み、無ければ従来の名前の一覧を読む。一覧に `_BaseTexture` を足す（シェーダーが見つからず印が読めないマテリアルのため）。lilToon の無いアバターの MToon も同じ順に読む。
- キューが 2450 以上 2500 以下でも、ブレンドが `SrcAlpha`・`OneMinusSrcAlpha` なら半透明と読む。
- シェーダーごとの近似を `Editor/ShaderApprox/` に 1 シェーダー 1 クラスで置く。共通の `IShaderApproximation` は次の 4 つを持つ。
  - `Matches(shaderName, values)`: そのシェーダーのマテリアルか。シェーダーが見つからないマテリアルでは `shaderName` が null で、保存された値で見分ける。
  - `RenderMode(values)`: 不透明・カットアウト・半透明のどれにするか。null なら汎用の読み方に任せる。
  - `Outline(values)`: 輪郭線付きの公式 lilToon にするか。
  - `Apply(values, material, textures, dropped)`: 汎用の読み方で埋めた公式 lilToon のマテリアルに、そのシェーダー独自の名前の値を書く。再現しない効果の名前を `dropped` に入れる。
- 登録は `ShaderApproximations.All` の 1 か所で、先に当たったものを使う。近似の判定は lilToon の派生版の次、VRChat SDK のシェーダーと他のトゥーンシェーダーより前に置く。汎用の読み方（主画像・主色・法線・発光・影色・カリング・カットアウト）を先に通すので、各クラスは差分だけを書く。
- テストは `Tests/Editor/ShaderApprox/<クラス名>Tests.cs` に 1 ファイルずつ置く。配布物のシェーダー本体は入れず、プロパティ名だけを宣言した代わりのシェーダーを使い、`Create` に本来のシェーダー名を渡す。
- 置き換えたマテリアルは保存前に「対応表のあるシェーダーの設定を公式lilToonの同じ役割の項目へ写しました」として出す。
- NonToon（`NonToon`・`NonToonFur`）は次を読む。
  - `_RenderingMode`: 0 不透明・1 カットアウト・2 半透明。NonToonFur はこのプロパティを持たず、本体を不透明で描く。`_RenderingMode` などの切り替えは Unity の Integer 型のプロパティなので、テストの代わりのシェーダーも Integer で宣言する。
  - 半透明でも `_Cutoff` で切り抜くので、その値を写す。
  - `_OutlineWidth` が 0 より大きいとき、輪郭線付きにし、幅（どちらも cm）と色を写す。NonToon の輪郭線は照明を受けた色に輪郭線の色を掛けるので、輪郭線の画像に主画像を入れる。
  - `_NormalMapWithRoughness` が有効な法線マップは粗さを一緒に収めた画像で、通常の法線マップとして読めないので外し、`Normal Map With Roughness` として表示する。ファーは `Fur` として表示する。頂点カラーで向きを決める輪郭線と、輪郭線の Z オフセットも表示する。
  - NonToon は輪郭線の幅の既定値が 0.1 cm なので、幅を 0 にしていないマテリアルは輪郭線付きになる。
  - シェーダーが見つからないマテリアルは、NonToon にだけある `_NTShadowBias` で見分ける。

## 確認したこと

- EditMode テストで、`[MainTexture]` と `[MainColor]` が一般的な名前より先に読まれること、キューとブレンドからの描画モード（-1・2450・2460・3000）、MToon の既定値が印の付いた主画像と主色を読むこと、シェーダーが見つからないマテリアルの `_BaseTexture`、NonToon の描画モード 3 種・輪郭線・粗さ入りの法線マップ・通常の法線マップ・シェーダーが見つからない場合・同じ名前を持つ別のシェーダーに当たらないことを確かめた。
- 上のプロジェクトで exporter の EditMode テストを全部回した（4046 件）。失敗した 57 件はウィンドウとグラフィックスを使うテストで、develop でも同じ 57 件が落ちた。
- 実際の NonToon（と動かすのに要るパッケージ）を入れた Unity 2022.3 のプロジェクトで、lilToon のアバターのマテリアルを NonToon の 6 通り（不透明・両面、カットアウト、半透明、輪郭線の色を変更、粗さ入りの法線、NonToonFur）と Standard に置き換え、develop とこの変更で書き出して比べた。描画の設定は NonToon のインスペクターと同じ値にした。
  - develop では NonToon の 6 通りすべてで、VRM の 9 マテリアルのどれにも主画像が入らず、白い表示だった。半透明はカットアウト（MASK）になっていた。
  - この変更では 6 通りとも 9 マテリアル中 8 に主画像が入った（残る 1 つは元の lilToon のマテリアルに主画像がない）。半透明は BLEND、カットアウトは MASK、Fur は OPAQUE で、Fur には「Fur」の表示が出た。
  - Standard に置き換えたものは、develop とこの変更で書き出した VRM がバイト単位で同じだった。
  - 書き出した VRM をアプリの読み込み経路（アプリの Unity 6 Editor）でも表示し、develop は白、この変更はテクスチャ付きで表示されることを確かめた。比較画像はリポジトリには入れていない。

## 検討した代替案と不採用理由

- 汎用の名前の一覧に NonToon の名前だけを足す: 主画像は戻るが、描画モードと輪郭線の持ち方が拾えず、他のシェーダーを足すたびに汎用の分岐が増える。
- シェーダーごとの分岐を `UnsupportedShaderSubstitution.cs` に書き足す: 複数の作業が同じファイルを変えて衝突し、どこまでが特定のシェーダー向けか読みにくくなる。
