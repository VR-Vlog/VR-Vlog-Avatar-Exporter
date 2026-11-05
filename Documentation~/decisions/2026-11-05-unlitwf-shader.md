# UnlitWF を lilToon へ写す

- 日付: 2026-11-05
- 対象: `Editor/ShaderApprox/UnlitWFApproximation.cs`・`Tests/Editor/ShaderApprox/UnlitWFApproximationTests.cs`

## 背景

UnlitWF の UnToon 系は影・発光・リム・マットキャップ・アウトラインを独自の接頭辞（`_TS_`・`_ES_`・`_TR_`・`_HL_`・`_TL_`）で持つので、汎用の写しでは主画像と色しか入らない。

## 決定

- `IShaderApproximation` の実装を 1 つ足し、プロパティの意味どおりに lilToon の値へ写す。対象は名前が `UnlitWF/` で始まり `UnToon` を含むシェーダーで、ファー・ジェム・草・パーティクルは描き方が違うので汎用の写しのままにする。シェーダーを読み込めないマテリアルは、UnToon 系が共通で持つ `_GL_LevelMin` と `_TS_Enable` で見分け、`_FUR_Height` があれば外す。
- 描画の種類はシェーダー名とキューから分かるので、汎用の読み方に任せる。
- 影の強さは `_TS_Power` に 0.85 を掛ける。UnlitWF は環境光が強いほど影を弱める（`1 - ambient / 2`）ので、中程度の環境光 0.3 のときの値を固定で使う。
- lilToon に無いものは写さず、セクション名を書き出しの警告に出す（Back Face Texture・Gradient Map・Color Change・Detail Normal・Reflection・Lame・Rim Shadow・Back Light・Occlusion・Distance Fade・Fog・Dissolve など）。

## 比較の結果

配布物のシェーダーで 24 ケースを書き出し、元のシェーダーとピクセル比較した。差の平均（0〜255）は大半が 1.5 以下だった。差が大きいのはリムの加算が 5.0、既定のリムが 4.5、マットキャップの中間が 5.0、ライトの 1 ケースが 3.4 である。

シェーダー本体はリポジトリに入れない。テストは名前とプロパティだけを持つ `UnlitWFProbe.shader` で回す。

## アプリでの表示

書き出した VRM のうち 7 ケース（不透明・カットアウト・半透明・アウトライン・発光・リム・マットキャップ）をアプリの読み込み経路で表示し、元のシェーダーと同じ面から撮って並べた。アプリは VRM に入れた lilToon の値で描くので（読み込み結果は `full-liltoon:1`）、どのケースも写した設定が出る。半透明の裏面は元より薄く見える。

## レビューで直したもの

- オーバーレイのスクロールは、UnlitWF が `_Time.x`、lilToon が `_Time.y` で進むので 20 で割る。
- ブレンドの半加算は lilToon に無いので、近い加算にして警告に出す。
- 乗算のマットキャップで強さが 1 を超えるときは、色を強めず強さ 1 で写して警告に出す。
- 影に画像を焼くと、lilToon では影の側で色が置き換わり 2 層目と 3 層目が消えるので、`Layers In Shade` を警告に出す。
- オーバーレイの範囲外処理とスクロールの併用は、lilToon のデカールがスクロールしないので `Overlay Scroll` を警告に出す。
