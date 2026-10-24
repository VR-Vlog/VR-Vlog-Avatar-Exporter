# 喜怒哀楽を名前から割り当てる語を増やし、英語の列と日本語の列に分ける

- 日付: 2026-10-25
- 対象: `Editor/ExpressionNameInference.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/ExpressionShapeNames.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`Tests/Editor/ExpressionPresetTests.cs`

## 背景

VRM 1.0 の喜怒哀楽（happy・angry・sad・relaxed・surprised）は、空のときにメニューの表情の名前と BlendShape の名前から割り当てている。配布アバターを書き出すと、`Nagomi_1`・`Bikkuri` のようなローマ字の名前や `Suprised_1` のようなつづりの揺れのメニューの表情が読めず、喜怒哀楽が空のまま残るものがあった。

ローマ字の語をそのまま表に足すと、これまで「英語の語だけ」としていた生の BlendShape の名前にも効く。生の BlendShape の日本語・ローマ字の名前は MMD の部位モーフ（目・眉・口だけを動かす）が多いので、そこには効かせたくない。

## 決定

- `Emotions` の各行を、英語の語の列と日本語の語の列（かな・漢字・ローマ字）に分ける。生の BlendShape の名前を読むとき（`englishOnly: true`）は英語の列だけを使い、メニューの表情とクリップの名前では両方を使う。これまでの `latinOnly`（ASCII かどうか）は `englishOnly` に置き換える。ASCII の語は単語の一致、それ以外は部分一致という読み方は変えない。
- 足した語（第 1 段＝それだけで一つの感情を指す語、第 2 段＝弱い語）
  - happy: 第 1 段 grinning・ニッコリ・egao・nikoniko・nikkori・ureshi・ureshii、第 2 段 delighted・pleased・わらい・warai
  - angry: 第 1 段 ikari・okori・punpun・iraira、第 2 段 irritated・irate
  - sad: 第 1 段 sorrowful・kanashii・kanashimi・shonbori・shobon、第 2 段 teary・sob・sobbing・gloomy・namida
  - relaxed: 第 1 段 ナゴミ・nagomi・hokkori・mattari、第 2 段 relieved・comfy・cozy・yasuragi
  - surprised: 第 1 段 suprised・surprize・surprized・amazed・bikkuri・odoroki、第 2 段 stunned・omg
- 中立（neutral）の語も同じく英語の列と日本語の列に分ける。

## 足さなかった語

- 五つの感情のどれか決められないもの: ドヤ顔、きょとん、ぐぬぬ、照れ・Shy・Blush、困る・Confused、怯え、絶望、ぴえん、がーん、Pout、Frown、Upset、Excited、Smirk・にやり、Panic、キラキラ目、ハート目
- 目だけのもの: ジト目、`eye_nagomi` のような目の名前（これまでどおり目・眉・口の語で除く）
- 短く、別の語の一部に出やすいもの: niko、naki、wara
- 手のジェスチャー: Peace、Thumbup
- 片目のウインクは happy にしない（これまでどおり）

## 検証

- EditMode: `ExpressionNameInferenceTests` に、割り当てる名前（`Suprised_1`・`Nagomi_1`・`Bikkuri`・`Egao`・`ikari`・`Shobon`・`Namida`・`Relieved`・`Amazed`・`Face_Suprised`・`Sorrowful`）と割り当てない名前（`Eyes_Nagomi`・`eyebrow_surprised`・`Peace`・`Thumbup`・`Doyagao`・`Jitome`・`Kyoton`・`Wink_1`・`Smirk` など）、生の BlendShape の `bikkuri`・`nagomi` を候補にしないことを足した。`ExpressionPresetTests` で、表のすべての語が自分の行の感情と段になり、日本語の列の語は `englishOnly` で読まれないことを確かめる。
- 書き出し済みの VRM から喜怒哀楽を外し、`VrmExpressionBindings.AddMissing` で割り当て直して前後を比べた。語の調整に使った 21 体では 26 → 35 個、調整に使っていない 99 体では 139 → 145 個が埋まり、どちらでも前に入っていた割り当ては変わらず、違う感情への割り当ては無かった。
