# 喜怒哀楽の語表を読む場所で分け、涙を感情の語から外し、五つ以外の表情は「割り当てない」と決める

- 日付: 2026-10-25
- 対象: `Editor/ExpressionNameInference.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/ExpressionShapeNames.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`Tests/Editor/ExpressionPresetTests.cs`
- 前の決定: [2026-10-25-emotion-name-vocabulary.md](2026-10-25-emotion-name-vocabulary.md)

## 背景

前の決定で語を増やしたあとのレビューで、次の 4 点を実際のコードで確かめた。すべて再現した。

1. 生の BlendShape の名前でも英語の語は読むので、`Teary`・`Tear`・`other_tear_1`・`TearFlow` が sad になる。
2. 日本語の語は名前の中の部分一致なので、足した `わらい` が `あざわらい`・`つくりわらい` にも当たり happy になる。
3. メニューの表情は、葉の項目名が何も言わないときに親のメニューを読む。`Jitome`・`Peace`・`きょとん`・`Doya_1` は感情の語を含まないので「何も言わない」扱いになり、`Egao / Jitome` は happy、`Nagomi / Peace` は relaxed になる。
4. `Bikkuri` はメニュー経由だけ・`Suprised` は生の BlendShape でも、という挙動を固定するテストが無かった。

1 を調べると、涙の名前は生の BlendShape でもメニューでも涙の効果のトグルだった。メニューの `Tear` は `Cheek`・`Sweat` と並んで置かれていて、悲しい顔ではない。

## 決定

- 語表の 2 つの列を、言語ではなく読む場所で名付け直す。`Words` はどの名前でも読み、`MenuWords` はメニューの表情とクリップの名前だけで読む。引数 `englishOnly` は `rawShape` に改める。列の中身（英語の語と日本語・ローマ字の語）はそのまま。
- 涙（`tear`・`tears`・`teary`・`namida`・`涙`・`なみだ`）は感情の語から外す。涙だけの名前は感情の語を持たないので割り当てず、`Sad Tears`・`Smile Tears`・`泣き顔_涙あり` は残りの語で読む。泣いている顔は `Cry`・`泣` で読む。涙だけの項目がメニューの `悲しい` の下にあれば親の sad を受け継ぐが、データでは涙のトグルは `Cheek`・`Sweat` と同じ効果のメニューの下にあり、喜怒哀楽の親の下には無かった。
- 五つの感情のどれでもない顔と手のサイン（`jitome`・`doya`・`doyagao`・`kyoton`・`gunu`・`peace`・`thumb`・`thumbs`・`thumbup`・`ジト`・`ドヤ`・`きょとん`・`キョトン`・`ぐぬ`・`嘲笑`・`あざ笑`・`あざわら`・`作り笑`・`つくりわら`）を除外語に入れる。`thumb` を入れるのは、`ThumbUp` が単語に分けると `thumb` と `up` になるため。除外語を含む名前は「割り当てない」とその場で決まり、親のメニューは読まない。これまで「不明」と「除外」は同じ `null` で、除外の語が表に無かったために親を読んでいた。除外の仕組み（`Vetoed` と `decided`）は前からあるので、語表に語を足すだけで直る。
- `わらい` は外す。かなの語は部分一致なので、別の語の末尾に出るかなの語は表に置かない。`warai` は単語の一致なので残す。あざわらい・つくりわらいは感情の語に当たらなくなるだけだと親のメニューを読むので、上の除外語に入れて「割り当てない」と決める。

## 選ばなかった案

- 語ごとに条件を足す（`わらい` の前に文字があれば外す、など）: 語が増えるたびに条件が増える。
- 非 ASCII の語も区切りで一致させる: `ニコニコ笑顔` の `笑顔` のように、区切りの無い日本語の名前が読めなくなる。
- 涙を `MenuWords` に移してメニューだけで読む: データではメニューの `Tear` も効果のトグルだった。
- 涙を除外語にする: 最初はこうしたが、`Sad Tears`・`Smile Tears` のように感情を言っている名前まで割り当てなくなる。

## 検証

- EditMode: 上の 4 点をテストに足した（`TearsAloneNameNothing`・`MenuOnlyWordsFillFromMenuFacesButNotFromRawShapes`・`RomajiAndTearShapesAreNotSuggested`、`あざわらい`・`つくりわらい`・`Egao / Jitome`・`Nagomi / Peace`・`笑顔 / きょとん`・`Smile / Doya_1`・`笑顔 / あざわらい`・`Smile / ThumbUp` のケース。`Smile Tears`・`Sad Tears`・`泣き顔_涙あり` は感情のまま読むケースとして残す）。前のコードではどれも失敗し、今回のコードで通る。
- 書き出し済みの VRM から喜怒哀楽を外して割り当て直した（前の決定と同じ方法）。調整に使っていない 99 体では、前の決定のあと 145 個 → 今回 137 個。減った 8 個はすべて sad に入っていた涙の BlendShape・涙のトグル（`51_TearON`・`eff_tear`・`TearFlow`・`Tear` など）で、前の決定で増えた割り当ては 1 つも減っていない。調整に使った 21 体では 35 → 34 個で、減った 1 個も涙のトグル。
