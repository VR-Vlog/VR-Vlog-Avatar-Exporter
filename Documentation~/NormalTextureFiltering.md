# 通常の法線画像と iPhone の読み込み互換性

Exporter 0.11.11 (`f5aed3fc`) は通常の 8-bit 法線画像も、デコード後に
`rgbaFloat`（成分ごとに 32-bit float）へ保存していた。未設定の法線スロットで
使う Unity の既定法線画像も対象になる。元の Bilinear / Trilinear を保持する
ため、Float32 画像のフィルタリングに対応しない GPU ではアプリの検査が拒否する。

iPhone 13 Pro は A15 Bionic を使用する。Apple の Metal 表では A15 は
Apple8 に属し、RGBA32Float の通常の対応には Filter が含まれない。
Apple7 / Apple8 の一部の iPadOS / macOS GPU にある追加対応とは区別する。
RGBA16Float は Filter に対応する。
出典: [iPhone 13 Pro の仕様](https://support.apple.com/en-us/111871)、
[Metal Feature Set Tables](https://developer.apple.com/metal/Metal-Feature-Set-Tables.pdf)
（2026-05-21、2・14・15 ページ）。

通常の 8-bit / 圧縮法線画像と既定法線画像を `rgbaHalf`（成分ごとに
16-bit float）へ保存する。各 mip、linear 色空間、filter、wrap、aniso と
mipBias、元の Unity アセットと importer 設定を保持する。32-bit float、
16-bit normalized、11-bit EAC など、高精度の法線画像は既存の Float 出力を
保持する。元が Half の法線画像は Half のまま保存する。
ただし 8-bit / 圧縮法線画像のうち、8-bit にしても方向の差が小さいものは
`rgba32` で保存する（下の「誤差が小さい法線画像の rgba32 保存」）。

これは Half の丸めを伴い、ビット単位の可逆変換ではない。
合成画像の実 GPU コピーでは、Float 出力との差は成分ごとに最大
0.00048447。表面に対して浅い方向の法線を含むストレス画素では、アプリと同じ
RG からの方向再構成による差は最大 1.15639 度だった。任意の強度設定や
すべての実アバターで同じ誤差を保証するものではない。

既存の VRM には元画像の精度情報がないため、アプリで Float を一律に Half へ
変換しない。0.11.11 で書き出した該当 VRM は修正版で再書き出しする。
Half は既存の拡張形式であり、TestFlight 445 の loader が受け付ける。
高精度の Float 画像を意図的に使う場合の GPU 条件は引き続き必要になる。

## 検証の範囲

- Unity 2022.3.62f3 / Metal の実 Editor で、通常・圧縮・未設定の法線画像、
  全 mip と sampler、高精度の形式選択、元アセットの不変を検証した。
- 実 PNG を TextureImporter で法線画像として読み込み、実際の VRM を書き出した。
  通常法線と既定法線の出力が Float から Half へ変わり、元ファイル、meta、
  importer 設定と sampler は一致した。
- その合成 VRM を Unity 6000.3.25f1 / Metal の実アプリ loader で読み込み、
  Full lilToon の素材と Half 法線画像、5 段の mip と sampler、描画成功を確認。
  この Mac は Float32 の補間にも対応するので、旧 VRM の拒否の再現には使わない。
- schema / package / publication policy / listing / compatibility / AAO patch の
  ホスト検証は成功した。

テスターの私有 VRM と端末側の例外ログは未取得。書き出しの注意ログと端末条件に
合う不具合だが、そのアバターの原因確定や iPhone での受入成功を示す結果ではない。
正式リリースと TestFlight 配布はこの修正の検証に含まれない。

## 誤差が小さい法線画像の rgba32 保存

元が 8-bit の法線画像でも、`rgbaHalf` に保存した canonical 値
（`UnpackNormal` の結果を 0〜1 に戻した値）は 8-bit の値の間に来ることがある。
縮小時の面積平均、BC 圧縮の展開、AG 形式で R に A を掛ける処理がその例。
これを 8-bit に丸めると方向が少し変わるので、変わる量を測ってから形式を決める。

対象は `StorageFormat` が法線だという理由だけで `rgbaHalf` を選んだ画像、
つまり 8-bit / 圧縮法線画像と既定法線画像。Half・Float・16-bit normalized・
EAC の法線画像、法線以外の画像、PNG と MToon 側は変えない。

書き出し時に、全 face と全 mip の画素について、Half の値と
`round(clamp01(v) * 255)` で 8-bit にした値の両方から、アプリと同じ式で方向を出す。

- `xy = (rg * 2 - 1) * s`、`z = sqrt(1 - saturate(dot(xy, xy)))`
- `s = max(1, |scale|)`。scale は `_BumpMap` → `_BumpScale`、
  `_Bump2ndMap` → `_Bump2ndScale`、`_MatCapBumpMap` → `_MatCapBumpScale`、
  `_MatCap2ndBumpMap` → `_MatCap2ndBumpScale`。機能のトグルがオフでも使う。
- 同じ画像を複数のマテリアル・プロパティが参照するときは、最も大きい `s` で測る。
  上の 4 つ以外の法線プロパティ（アウトラインやファーのベクトル、異方性の接線）から
  参照される画像は、この式で読まれないので `rgbaHalf` のままにする。

2 つの方向の角度差が次の両方を満たすときだけ `rgba32` で保存し、
`normal: true` は残す。どちらかを外れたら従来どおり `rgbaHalf` で保存する。

- 99 パーセンタイル（nearest-rank）が 0.5 度以下
- 最大が 2.0 度以下

この閾値は、実際のアバターを測る前に決めた。決めたあとで、書き出し済みの
VRM 24 個に含まれる法線画像 64 枚（`rgbaHalf` で計 95.51 MB）を測った。
63 枚は 99 パーセンタイルが 0.074 度以下、最大が 1.374 度以下だった。
残る 1 枚は最大が 2.13 度で上限を超えたので `rgbaHalf` のままになる。
合格した 63 枚を `rgba32` にすると計 22.7 MB 減る。

`rgba32`（RGBA8 Unorm）は iPhone を含む対象の GPU で Filter に対応するので、
上の Float の問題は起きない。`rgba32` は schema 2.0 の既存の形式で、アプリの法線の
デコードは保存形式によらないため、アプリ側の変更は要らない。
