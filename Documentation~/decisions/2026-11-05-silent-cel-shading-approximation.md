# Silent's Cel Shading の値を公式 lilToon の同じ役割の項目へ写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/SilentCelShadingApproximation.cs`・`Editor/ShaderApprox/ShaderApproximations.cs`・`Editor/UnsupportedShaderSubstitution*.cs`・`Tests/Editor/ShaderApprox/SilentCelShading*`・`README.md`・`README.en.md`・`CHANGELOG.md`

## 背景

Silent's Cel Shading（SCSS）は Crosstone と Lightramp の 2 つの陰影の型を持ち、それぞれに輪郭線つきと毛の層つきの名前がある。陰影は Crosstone が影色の段（`_1st_ShadeColor`・`_2nd_ShadeColor` と各段の Step・Feather）、Lightramp がランプ画像（`_Ramp`）で決まる。汎用の読み方は主画像と主色しか拾えず、影・輪郭線・リム・マットキャップが落ち、Lightramp では影が出なかった。

## 決定

2026-11-05 のシェーダーごとの近似（`IShaderApproximation`）に 1 クラス足す。値は SCSS の inspector とシェーダー本体が使う意味で読む。

- 見分け方: シェーダー名が `Silent's Cel Shading/` で始まる。シェーダーが見つからないマテリアルは、SCSS にしかない `_LightingCalculationType` と `_IndirectShadingType` が両方あるもの。
- 6 つの名前はすべて同じプロパティを宣言するので、輪郭線を描くか、Crosstone か Lightramp かは名前でしか分からない。そのため `SourceValues` に元のシェーダー名（見つからないときは null）を持たせた。共通部分の変更はこれと、`Toon.cs` の `Ratio`・`FitShadowEdge`、`Toon.cs`・`Alpha.cs` の焼き込みとアルファマスクの関数と、画像を作る `NewImage` を `internal` にしたことだけ。シェーダーが見つからないときは、Lightramp にしかない `_LightRampType` が無ければ Crosstone として読む。
- 描画モード: `_Mode` 0 不透明・1 カットアウト・2 フェード・3 透過・4 加算・5 カスタム（`_CustomMode`）。加算は半透明にして表示する。
- 透過: `_AlbedoAlphaMode` 0 は主画像のアルファ、1 は主画像のアルファを滑らかさに使うので、アルファを色のアルファだけにする。2 は切り抜きマスクの R に `_Tweak_Transparency` を足して 0〜1 に収めた値を画像に焼き、公式 lilToon のアルファマスクで色のアルファを掛ける。元は色のアルファを掛ける前に 0〜1 に収めるので、マスクと足す値を分けて渡すと透過の濃さが変わる。
- 色: `_Color` は `_ColorMask` の G で効き方が変わるので、色が白でないときは表示する。HSV のモードは公式 lilToon の色調補正に写し、マスクを焼く。
- Crosstone: 1 段目と 2 段目の影色・境界（`step - feather / 2`）・ぼかし（feather）を公式 lilToon の 1 段目と 2 段目の影に写す。影の画像を主画像に掛けるか（`_CrosstoneToneSeparation`）は `_ShadowMainStrength` で表す。公式 lilToon はこの値を 2 段で共有するので、2 段の掛け方が違うときは表示する。主画像に掛けない影色で影の画像が無いときは、白い画像を公式 lilToon の影の画像に入れる。公式 lilToon は影の画像が無いと主画像に影色を掛けるので、そのままでは主画像の柄が影に出る。
- Lightramp: 影の位置を SCSS の式（half-Lambert に `_ShadowLift` と `_Shadow` を掛けたもの）で 129 点出してランプ画像を読み、明るさが半分になる位置を境界、10% と 90% の幅をぼかしにして公式 lilToon の 1 段の影にあてはめる。ランプの明るさが変わらないときは、白なら影なし、それ以外は全面を影にしてランプの値で暗くする。
- 輪郭線: 名前に `(Outline)` があり `_OutlineMode` が 0 でないときだけ。幅は `_outline_width`、色は `_outline_color`、Tinted（1）は主画像を掛ける。
- 裏面: SCSS は裏面で法線を表に向けて照らすので、公式 lilToon の `_FlipNormal` を 1 にする。両面のマテリアルの裏面が暗くなっていたのを、比較画像で見つけて足した。
- リム・マットキャップ・発光・スペキュラーはそれぞれの強さと色とマスクを公式 lilToon の同じ役割の項目へ写す。3 枚目と 4 枚目のマットキャップ、ディテール、ハッチング、AudioLink、サブサーフェス、接触影、毛の層などは書き出さず、保存前に表示する。

## 確認したこと

- EditMode テスト 29 件で、描画モード 6 通り（裏面の法線も）、加算、輪郭線の名前、Tinted の輪郭線、1 段目と 2 段目の影、影の画像と掛け方、主画像に掛けない影色の白い画像、ランプ画像のあてはめ、白いランプ、灰色一色のランプ、滑らかなランプ、切り抜きマスクの焼き込み、滑らかさのモード、色相、リム、マットキャップ、発光のキーワード、2 つ目の発光、スペキュラーとメタリック、表示する効果、シェーダーが見つからない場合、同じ名前のプロパティを持つ別のシェーダーに当たらないことを確かめた。代わりのシェーダーは SCSS のプロパティ名だけを持つテスト用のもので、SCSS 本体はリポジトリに入れていない。
- Standard・lilToon・VRChat SDK のシェーダー・他のトゥーンシェーダー・NonToon のテストは変えずに通る。
- 実際の SCSS を入れたプロジェクトで 10 通り（Crosstone の不透明・2 段の影、Lightramp の不透明、カットアウト・フェード・透過、輪郭線 2 種、両面、リムと発光）を書き出した。VRM のマテリアルは主画像を持ち、カットアウトは `MASK`（0.5）、フェードと透過は `BLEND`、両面は `doubleSided`、発光は発光の画像と色を持つ。球に描いた元のシェーダーとアプリでの表示の差（0〜255 の平均）は、Lightramp で 9.7 から 5.2、両面で 27.1 から 8.1 に下がった。カットアウトは SCSS がアルファを MSAA の被覆率として描くため縁がぼけ、差が 10 ほど残る。比較画像はリポジトリには入れていない。

## 検討した代替案と不採用理由

- 名前をクラスの状態として持つ: 近似のクラスは 1 つのインスタンスを全マテリアルで使うので、名前は値と一緒に渡す必要がある。`Matches` だけで名前を読むと、輪郭線と陰影の型を `Apply` で知る方法が無い。
- ランプ画像を影の画像として焼く: 公式 lilToon の影の画像は位置ではなく色を変えるもので、光の向きによる段の位置は表せない。境界とぼかしへのあてはめのほうが、段が 2 つのランプでは元に近い。
