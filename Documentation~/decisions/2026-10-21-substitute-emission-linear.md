# 非対応シェーダーの発光の強さはリニアの値に掛ける

- 日付: 2026-10-21
- 対象: `Editor/UnsupportedShaderSubstitution.cs`・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/EmissionStrengthProbe.shader`・`CHANGELOG.md`

## 背景

非対応シェーダーの材質を公式 lilToon に置き換えるとき（2026-10-18-unsupported-shader-fallback）、発光の色に強さ（`_EmissionStrength`・`_EmissionIntensity`）を掛けて `_EmissionColor` に入れていた。材質に保存されている色は sRGB で、掛け算もその sRGB の値にしていた。

元のシェーダーは、リニアに直した色に強さを掛けて描く。書き出し側も `_EmissionColor` を `.linear` で読んで MToon の発光の値にしている（`UniVrmOneClickExporter`）。sRGB の値に掛けると、リニアでは強さの約 2.2 乗を掛けたことになり、強さが 2 なら正しい明るさの約 2.3 倍（2 の 1.2 乗）で書き出されていた。

## 決定

- 強さは `(color.linear * strength).gamma` で、リニアの値に掛けてから sRGB に戻して `_EmissionColor` に入れる。アルファは掛けずに元の値を残す。
- 強さの設定が無い材質の色は変えない。

## 確認したこと

- テスト用のシェーダー `Hidden/VRVlogTests/EmissionStrengthProbe`（`_EmissionColor` と `_EmissionStrength` を持つ）で、強さ 2 のとき書き出し用の `_EmissionColor` のリニアの値が元の色のリニアの値の 2 倍になり、アルファが変わらないことを `EmissionStrengthScalesTheLinearColor` で確かめた。
- 直す前のコードでは、このテストは sRGB 0.5 の赤がリニア 1.0（期待値は約 0.428）になって失敗した。

## 影響

- 発光の強さを 1 より大きくしている非対応シェーダーの材質は、元のシェーダーで見える明るさに近くなる（これまでより暗くなる）。1 より小さい材質は、これまでより明るくなる。
- lilToon の材質と、強さの設定が無い材質の書き出しは変わらない。
