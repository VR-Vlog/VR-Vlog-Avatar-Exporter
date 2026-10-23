# 同じ分岐を別の入力で何度も確かめる TestCase を減らす

- 日付: 2026-10-23
- 対象: `Tests/Editor` の表情・ニュートラル系のテスト 13 ファイル、`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`、`Tools/test-compatibility-tooling.py` で件数を固定している 5 クラスの値

## 背景

mk が 10/23 に「テストに関しても必要最低限にする方針にしたい」と依頼した。表情とニュートラルの書き出しのテストは、同じ分岐を値だけ変えた `[TestCase]` で何度も通しているものが多く、1 ファイルで 100 件を超えるものがいくつもあった。

2 つの行を「同じ分岐」とみなすのは、期待する結果が同じで、入力の違いが同じコードを通る値の違いだけのときに限った。Animator のグラフの形を守るファイル（DirectFx・HeldAutomatic・MergedFxDefaults・NeutralShapeSampler）では、期待する結果ごとに 1 行、入力の形が他と違う行、不具合の修正と一緒に足された行（`git log -S` で確かめた）を残した。メソッドは 1 本も消していない。

## 決定

- `UnifiedExpressionExportTests`: `fullLilToon` の true/false を両方回していたメソッドの大半を片方だけにした（シナリオごとに true と false を交互に残した）。材質の証拠（`MaterialEndpointEvidence`）は色だけ全種類、ほかは 1 種類ずつ。言語ごとの失敗文の翻訳はそれぞれ別の表なので 4 言語とも残した。`NegativeBracket` の生成元は `fullLilToon` を true だけにして 36 → 18 件。
  - 主経路で代わりに守れるか: MToon 経路と lilToon 経路の違いは材質の書き出しにだけ出る。モーフだけのテストはどちらでも同じ処理を通り、材質のテストは両方の値を残した。
- `NeutralShapeSamplerTests`: 重なるモーフのフレーム、知らないプロパティ、VRChat の入力、EyeHeight、Constraint、Contact の行を種類ごとに 1〜3 行にした。
  - 主経路で代わりに守れるか: 残した行が同じ判定を通る。保存済みの露出パラメーター 17 行は入力の形がすべて違うので残した。Dynamics の行（修正 08e9c89 で足した）は有効 0/1 が同じ経路を通るので 8 → 4 行にした。
- `ParameterDriverExpressionTests`: Set/Add/Copy と EyeHeight（Meters・Percent）、Locomotion、TemporaryPoseSpace の行を半分ほどにした。
  - 主経路で代わりに守れるか: 各操作・各入力が最低 1 行残る。`MalformedBodyPlayableControl`（修正 a68af38）はそのまま。
- `ExpressionNameInferenceTests`: 名前の形（メニュー名・全顔のブレンドシェイプ名・日本語・パス）ごとに代表を残した。
  - 主経路で代わりに守れるか: 名前の判定は形ごとに分岐するので、形ごとに 1 行以上あれば足りる。修正 8ed463b で足した行のうち `Neutral_Closed`・`neutral_half` は残し、同じ判定を通る `Neutral_Sleep`・`Fcl_EYE_Neutral` は消した（部位の接頭辞の形は `Fcl_MTH_Neutral` で残る）。
- `DirectFxExpressionDiscoveryTests`: BlendTree の制御パラメーターの宣言を 14 → 6 行（種類ごとに「無い」と「型違い」を 1 行ずつ）。
  - 主経路で代わりに守れるか: 宣言を確かめる処理は BlendTree の種類によらず同じ。入れ子の既定の遷移の比較演算子 8 行は境界がすべて違うので残した。
- `MergedFxDefaultsTests`: 定数の中継を 4 行、長さ 0 の動きの拒否を 8 → 4 行（2 因子の組をすべて含む 4 行）。
  - 主経路で代わりに守れるか: 残した 4 行で各因子の真偽の組が揃う。Additive と WriteDefaults の組み合わせは残した。
- `HeldAutomaticExpressionTests`: 読み取り専用の休止ゲートを 14 → 7 行、未保存の既知入力を 6 → 3 行。
  - 主経路で代わりに守れるか: 理由ごとに 1 行残る。`Auto_Blink` の別名の行（ea2d046）は残した。
- `AdditionalParameterConstantTests`・`NeutralLayerControlTests`・`ExpressionPresetTests`: 理由ごと・型ごとに 1 行を残し、同じ判定を値だけ変えて通す行を外した（無限大の重み・長さ、似た感情名など）。
  - 主経路で代わりに守れるか: NaN と負の値の行が同じ有限値の判定を通る。
- `AdditionalPlayableCallbackTests`・`AdditionalPlayableMorphTests`・`RandomNeutralRestTests`: レイヤー制御の不正値は `NeutralLayerControlTests` と重なる行を外し、追加のモーフは Playable の種類ごとに 1 行（動く・止まるを交互）にした。
  - 主経路で代わりに守れるか: 不正値ごとの判定は `NeutralLayerControlTests` が持つ。Bool の Add と Trigger のコピー元は入力の形が違うので残した。

## 消さなかったもの

- グラフの形を守るファイルで期待する結果ごとの行、入力の形が違う行、不具合の修正と一緒に足された行。
- `UnsupportedShaderSubstitutionTests`・`TemporalNeutralShapeTests`・`MergedFixedNeutralSamplingTests` は、行ごとに読み取る値や形が違うので今回は触らなかった。
- 規約・同意・ライセンス、データの消失・ファイルの安全、QR とトークンの中身を確かめるテスト。

## 件数

| ファイル | メソッド | ケース |
| --- | --- | --- |
| UnifiedExpressionExportTests | 41 → 41 | 238 → 144 |
| NeutralShapeSamplerTests | 68 → 68 | 158 → 142 |
| ParameterDriverExpressionTests | 67 → 67 | 126 → 106 |
| ExpressionNameInferenceTests | 52 → 52 | 123 → 94 |
| DirectFxExpressionDiscoveryTests | 73 → 73 | 120 → 112 |
| MergedFxDefaultsTests | 51 → 51 | 117 → 111 |
| HeldAutomaticExpressionTests | 32 → 32 | 109 → 99 |
| AdditionalParameterConstantTests | 18 → 18 | 78 → 69 |
| NeutralLayerControlTests | 25 → 25 | 76 → 71 |
| ExpressionPresetTests | 47 → 47 | 78 → 73 |
| AdditionalPlayableCallbackTests | 14 → 14 | 53 → 41 |
| AdditionalPlayableMorphTests | 8 → 8 | 18 → 15 |
| RandomNeutralRestTests | 11 → 11 | 35 → 34 |
| 合計 | 507 → 507 | 1329 → 1111 |
