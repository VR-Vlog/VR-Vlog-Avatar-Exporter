# 「自動で軽量化する」で、lilToonでオフにしてある機能だけが使うテクスチャを書き出さない

- 日付: 2026-11-01
- 対象: `Editor/LilToonInactiveSlots.cs`・`Editor/LilToonFullSnapshot.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/Compatibility/ExportNoteText.cs`・`Editor/Locales/*.json`・`Tests/Editor/LilToonInactiveSlotsTests.cs`

## 背景

lilToonの拡張（`VRVLOG_materials_liltoon`）は、マテリアルに割り当てたテクスチャを、その機能がオフでもすべて書き出している（`LilToonFullSnapshot.ReadMaterial`）。アプリ（VR Vlog）の表情が変えるのは色とUVだけで、`_UseBumpMap` のような機能のスイッチは変えない。スイッチがオフの機能のテクスチャはシェーダーが読まないので、画面には一度も出ない。

書き出し済みの10体で数えると、拡張のテクスチャ67.8MBのうち15.4MBがオフの機能だけから参照されていた。VRMのファイル全体（162.5MB）の約9.5%に当たり、1体では最大42%だった。多かったのは法線マップ・2ndの法線マップ・異方性・マットキャップの法線・輪郭線の向きのテクスチャ（Issue #551・Epic #494）。

## 決定

- 「自動で軽量化する」がオンのときだけ、拡張のマテリアルを読むときに、シェーダーが読まないスロット（＝テクスチャの割り当て先のプロパティ）を空として書く。法線マップのスロットは、今の空の法線マップのスロットと同じ既定の法線テクスチャ（`Texture2D.normalTexture`）になる。オフのときは新しい処理を通らず、書き出すVRMは今と同じバイト列になる。
- スロットとスイッチの対応は `LilToonInactiveSlots` の表1か所にまとめる。lilToon 2.3.4のシェーダーのソースで、そのスロットをそのスイッチがオンのときにしか読まないことを確かめた行だけを載せる。表の行は次のとおり。
  - 2nd・3rdのメインテクスチャ（`Tex`・`BlendMask`・`DissolveMask`・`DissolveNoiseMask`）→ `_UseMain2ndTex`・`_UseMain3rdTex`
  - `_AlphaMask` → `_AlphaMaskMode`
  - `_BumpMap` → `_UseBumpMap`、`_Bump2ndMap`・`_Bump2ndScaleMask` → `_UseBump2ndMap`
  - 異方性の3枚 → `_UseAnisotropy`、`_BacklightColorTex` → `_UseBacklight`
  - 影のマスク3枚と影の色3枚 → `_UseShadow`、`_RimShadeMask` → `_UseRimShade`
  - `_SmoothnessTex`・`_MetallicGlossMap`・`_ReflectionColorTex`・`_ReflectionCubeTex` → `_UseReflection`
  - マットキャップ（`Tex`・`BlendMask`・`BumpMap`）→ `_UseMatCap`・`_UseMatCap2nd`、`_RimColorTex` → `_UseRim`、ラメの2枚 → `_UseGlitter`
  - 発光（`Map`・`BlendMask`・`GradTex`）→ `_UseEmission`・`_UseEmission2nd`、`_ParallaxMap` → `_UseParallax`
  - `_DissolveMask`・`_DissolveNoiseMask` → `_DissolveParams` のxが0
  - 輪郭線の3枚（`_OutlineTex`・`_OutlineWidthMask`・`_OutlineVectorTex`）→ そのシェーダーに輪郭線のパスがあり、そのLightModeがマテリアルで有効なとき
- 次の場合はスロットを読むものとして扱い、テクスチャを残す。
  - 表に無いスロット。`_MainTex`・`_MainGradationTex`・`_MainColorAdjustMask`・`_DitherTex`・ファーのスロット・`_TriMask` などがここに入る。
  - マテリアルにスイッチのプロパティが無いとき、lilToon 2.3.4のシェーダーでないとき。
  - lilToonMultiのシェーダー。スイッチではなくキーワードで機能を選ぶので、スイッチの値で判断できない。
  - 宝石と屈折（ぼかし）のシェーダーの `_SmoothnessTex` と反射のキューブマップ。この2つのパスは `_UseReflection` を見ずに読む。
- スイッチは0以外ならオンとみなす。発光の抑制（`LilToonEmissionPolicy`）は拡張に書くスイッチを0にするが、元のマテリアルのスイッチは1のままなので、そのテクスチャは残す。メインテクスチャへの焼き込み（`LilToonMainTextureBaker`）の複製は拡張の記録を取ったあとに作るので、判断には元のマテリアルのスイッチを使う。
- オフの機能でも同じテクスチャをオンの機能が使っていれば、今と同じく1回だけ書き出す。どのスロットからも参照されなくなったテクスチャだけが書き出されない。スイッチの値は変えない。
- 書き出し結果の一覧には1行だけ「オフの機能だけが使うテクスチャ○枚を書き出さずメモリ使用量を○MB減らしました」を出す。MBはオフのときに書き出していた大きさ（今の上限で縮めた後の大きさ・RGBA・ミップマップ込み・非圧縮）で数え、テクスチャの大きさを決める「自動で軽量化する」の1行と同じく小数第1位まで出す。書き出さなかったテクスチャはテクスチャを小さくした行の枚数に入らない。

## 確認したこと

- EditModeテスト `LilToonInactiveSlotsTests` で、表の行ごとにスイッチがオンなら残りオフなら空になること、スイッチの無いシェーダー・lilToon以外・lilToonMulti・宝石と屈折（ぼかし）では残ること、輪郭線のパスが無いか3つのLightModeがすべて無効なら輪郭線のスロットが空になること、ディゾルブのモード、共有するテクスチャが1回だけ書かれること、法線マップのスロットが既定の法線テクスチャになること、スイッチの値と元のマテリアルが変わらないこと、オフのときの拡張が変わらないこと、書き出しの一覧に行が出るのはオンのときだけなこと、文言の決まりと4言語の訳があることを確かめた。
- lilToonのマテリアルを5つ持つアバター1体を書き出した。オフのVRMは変更前と同じバイト列（24,343,136バイト）だった。オンでは、法線マップのスイッチがオフのマテリアル1つで、法線マップのスロットが既定の法線テクスチャになった。そのテクスチャは法線マップをオンにした別のマテリアルが使うので書き出しを続け、書き出さなかったテクスチャは0枚、一覧の行も出なかった。オンのファイルは24,142,332バイトで、テクスチャの大きさを決める段階だけのときと同じだった。
- `exporter-behavior` の1,612件のEditModeテストを回し、変更前のパッケージでも失敗する13件だけが失敗した。

## 分かっている制限

- 表はlilToon 2.3.4のシェーダーで確かめた。ほかのバージョンでスロットの使い方が変わっていても、拡張は2.3.4の形で書くので、アプリでの描き方は変わらない。
- スイッチを変えるアニメーションは今もアプリでは動かないので数に入れていない。アプリが将来スイッチを動かすようになったら、この表を見直す。
- 減らした量は非圧縮で数える。VRMの中ではPNGなので、ファイルの差はこれより小さい。

## 検討した代替案と不採用理由

- スロット名の前置き（`_Rim*` など）で対応を決める: `_RimShade*` が `_UseRim` に当たるような誤りが起きる。2.3.4のスロット名をそのまま並べた。
- テクスチャの色が一様なら外す: 色が一様でもオンの機能では見た目に効く。スイッチで決めた方が描画と一致する。
- スロットを空にせずテクスチャだけ外す: 空でない参照が書き出されないテクスチャを指すと、アプリで読めない。今の空のスロットと同じ書き方にした。
