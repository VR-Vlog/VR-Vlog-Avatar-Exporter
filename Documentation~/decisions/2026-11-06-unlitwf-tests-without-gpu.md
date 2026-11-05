# UnlitWF の近似のテストを -nographics でも通す

- 日付: 2026-11-06
- 対象: `Tests/Editor/ShaderApprox/UnlitWFApproximationTests.cs`・`CLAUDE.md`

## 背景

UnlitWF の近似は、影・マットキャップ・輪郭線の画像を GPU で読み戻して焼く。`-nographics` で Unity を起動すると読み戻せないので、焼かずに色だけを写すか、写せない項目を警告に出す。UTS・Arktoon・XSToon のテストはこのときの結果を確かめて抜ける分岐を持つが、UnlitWF のテストには無く、`-nographics` で 7 件失敗していた。

## 決定

7 件に、`SystemInfo.graphicsDeviceType` が `Null` のときの結果を確かめて抜ける分岐を足した。

- 影の画像: 焼いた画像が入らない。焼いた影が無いので `Layers In Shade` も出ない。
- 中間の色を引くマットキャップ: 焼けないので写さず `Matcap` を警告に出す。
- 輪郭線の画像: 入らず `Outline Color Image` を警告に出す。

CI は GPU ありで回すので CI の結果は変わらない。あわせて、`-nographics` の分岐を入れることと、EditMode の全件で Android の `TargetScanWorker` の例外のログに当たったテストの読み方を `CLAUDE.md` に書いた。後者は 2026-11-05 の全件の実行で、`PhysBoneSpringExportTests` の 1 件がこの例外で失敗し、単独で回し直すと通ったことによる（同じ日の別の検証でも 1 件同じことがあった）。

## 確かめたこと

Linear の最小のプロジェクトで `UnlitWFApproximationTests` の 39 件を回した。修正前は `-nographics` で 7 件失敗、修正後は `-nographics` と GPU ありの両方で 0 件失敗。
