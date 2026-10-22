# 発光の置き換え（#342）のレビューで残した4件の扱い

- 日付: 2026-10-23
- 対象: `Editor/LilToonEmissionPolicy.cs`・`Tests/Editor/EmissionGradientTests.cs`（新規）・`Tests/Editor/UnsupportedShaderSubstitutionTests.cs`・`Tests/Editor/EmissionKeywordProbe.shader`（新規）・`CHANGELOG.md`
- 関連: [2026-10-22-emission-substitution-enable.md](2026-10-22-emission-substitution-enable.md)

## 背景

#342の敵対的セルフレビューで、優先度の低い指摘が4件残った。該当する実例が無いので、そのときは直さなかった。

## 決定

| 指摘 | 扱い |
| --- | --- |
| (a) Gammaのプロジェクトでは、sRGBをオフにしたグラデーション画像も`TextureColors.ReadLinear`がリニアに変換する | 直さない。Gammaのプロジェクトではシェーダーが画像の値をsRGBの設定にかかわらずそのまま使うので、どちらに合わせるのが正しいかが決まらない。VRChatのプロジェクトはLinearが前提で、Gammaのプロジェクトの実例も無い |
| (b) 幅が256より大きいグラデーション画像は、256の幅に1回のバイリニアで縮小してから平均していた | 直す。縮小の比が2を超えると、バイリニアは間の画素を読まずに飛ばす。`LilToonEmissionPolicy.GradientFactor`は画像の幅のまま1行を読む（1画素につき1サンプル）。1行だけなので幅が大きくても読む量は小さい。`EmissionGradientTests.AWideGradationImageAveragesEveryTexel`（幅1024・4画素ごとに白で、平均0.25）で確かめる |
| (c) `_EMISSION`と`_ES_ENABLE`の両方を宣言するシェーダーは、どちらか一方がオンなら発光をオンにする | 直さない。両方を宣言して別々に切り替えるシェーダーの実例が無く、どちらを優先すべきかを決める根拠が無い。スイッチのプロパティ（`_ES_Enable`など）があればそちらが先に決める |
| (d) キーワードだけで発光を決める経路（スイッチのプロパティが無く、キーワードを宣言するシェーダー）のテストが無い | テストを足す。`UnsupportedShaderSubstitutionTests.AnEmissionKeywordWithoutASwitchDecidesTheEmission`で、`_ES_ENABLE`だけを持つ`Hidden/VRVlogTests/EmissionKeywordProbe`が、画像と白の発光色があってもキーワードがオフなら光らず、オンなら光ることを確かめる |

## 欠点

- (b)で幅の大きい画像では読み出す行が長くなる。画像の最大の幅（16384）でも1行なので、書き出しの時間への影響は小さい。
