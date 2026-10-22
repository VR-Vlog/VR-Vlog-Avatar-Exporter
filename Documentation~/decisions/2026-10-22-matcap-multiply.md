# 色を掛ける MatCap を、MToon では色を暗くして、置き換えでは lilToon の乗算で残す

- 日付: 2026-10-22
- 対象: `Editor/UniVrmOneClickExporter.cs`（`FallbackColor`・`MatcapMultiplyFactor`）・`Editor/UnsupportedShaderSubstitution.cs`（`ApplyMatcap`）・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`

## 背景

100 体の無料アバターで見え方を比べたところ、4 体で MatCap の色が抜けていた。2 体は MatCap の画像だけで色を出すシェーダー（VRChat/Mobile/MatCap Lit）を使った体や小物が白くなり、2 体は lilToon の MatCap を乗算で使った黒い防具が薄い灰色になっていた。

## 原因

- VRChat/Mobile/MatCap Lit は、主画像 × 頂点カラー × `_MatCap` の色 × ライティングを出す。lilToon 以外のシェーダーの置き換え（`UnsupportedShaderSubstitution.FromGeneric`）は主画像と色しか引き継がないので、MatCap の色が消えて白くなる。
- lilToon 2.3.4 の MatCap の乗算（`_MatCapBlendMode` = 3）は `lerp(色, 色 × MatCap, _MatCapBlend × MatCap色.a × マスク)` で色を暗くする。標準 MToon の互換表示は MatCap を加算でしか表せないので、乗算の MatCap を外していた（`MatcapAdditive`）。外したぶん、暗くなるはずの色が明るいまま残る。

## 決定

Unity とシェーダーの定義から読めることだけで決める。アバターや材質の名前では分けない。

- MToon の互換表示: lilToon の `_UseMatCap` がオン、`_MatCapBlendMode` が 3、`_MatCapBlendMask` がないとき、MatCap が掛ける色の平均を基本色 `_Color` と影色 `_ShadeColor` に掛ける（`MatcapMultiplyFactor`）。
  - 平均は MatCap の画像を 64×64 で読み（GPU で読むので読み取り不可の画像でも使える）、MatCap が実際に使う内接円の中の画素だけで取る。各画素で `1 + _MatCapBlend × clamp01(a × MatCap色.a) × (画素 × MatCap色 - 1)` をリニアで計算して平均する。画像がなければ白として扱う。
  - 掛け算はリニアで行い、アルファは変えない。
  - マスクがある MatCap は、場所ごとに効き方が違い、平均を全面に掛けると違う見え方になるので、これまでどおり外す。
  - lilToon 拡張に書く値は変えない。
- 置き換え: MatCap で色を掛けると定義が決まっているシェーダー（表 `MultiplyMatcaps`、今は VRChat/Mobile/MatCap Lit の `_MatCap`）は、置き換え先の lilToon で MatCap を乗算（`_UseMatCap` 1・`_MatCapBlendMode` 3・`_MatCapBlend` 1・`_MatCapColor` 白）にして、その画像を `_MatCapTex` に入れる。シェーダー名が完全に一致し、画像が 2D のときだけ使う。MToon の互換表示は上の規則で同じ乗算を色に掛ける。
- 置き換えの警告に「MatCap」を加え、4 言語の訳も直した。

## 成り立たない場合

- 視点で変わる MatCap の濃淡は再現しない。平均の色の一定の暗さになる。
- `MultiplyMatcaps` にないシェーダーの MatCap は、これまでどおり引き継がない。
- 加算・スクリーンなど乗算以外の MatCap の扱いは変えていない。

## 検証

- EditMode: `MaterialFallbackTests.MultiplyMatcapDarkensTheBaseAndShadeColorsByItsAverage`（`_MatCapBlend` 1 と 0.5。四隅が黒の 16×16 の画像で内接円の外を見ないこと、マスクがあるときと加算のときは白のままであることも確かめる）、`UnsupportedShaderSubstitutionTests.AMatcapThatMultipliesTheColorUsesTheLilToonMultiplyMode`。
- 100 体の見え方の比較（元のアバター／修正前／修正後、正面・45°・背面）。
