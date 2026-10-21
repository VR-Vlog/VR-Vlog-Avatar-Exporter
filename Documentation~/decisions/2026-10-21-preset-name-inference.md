# 空の標準の表情を名前から埋める対象を、口の形・neutral・視線に広げる

- 日付: 2026-10-21
- 対象: `Editor/ExpressionNameInference.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`README.md`・`README.en.md`・`CHANGELOG.md`
- Issue: #185

## 背景

通常の書き出し（**アプリに転送（かんたん）**・一括書き出し）は、空の喜怒哀楽の 5 つを表情メニューの表情と英語の顔全体の BlendShape 名から埋めている（`2026-10-18-emotion-preset-inference.md`）。残りの標準の表情は次のとおりだった。

- 口の形（aa・ih・ou・ee・oh）: Avatar Descriptor の Lip Sync、なければ決まった名前（`vrc.v_aa`・`Fcl_MTH_A` など）から埋めていたが、通知が訳の仕組みを通らず、書き出し結果の一覧にも出ていなかった。
- まばたき: `BlinkShapeNames` の決まった名前から埋めている。
- neutral と視線（lookUp・lookDown・lookLeft・lookRight）: 名前から埋めておらず、作者が設定したときだけ入っていた。

## 決定

名前の語彙と照合の規則は `ExpressionNameInference` に置き、埋めるのは `VrmExpressionBindings.AddMissing` の 1 か所にした。まばたきの名前は `BlinkShapeNames` に残す（瞬きの書き出し処理が同じ表を使うため）。

- **neutral**: 語は `neutral`・`真顔`・`無表情`・`ニュートラル`。感情と同じく、目・口・眉・左右・解除などの語を含む名前や、感情の語も含む名前は使わない。`closed`・`half`・`open`・`sleep` を含む名前（目を閉じた顔など）も使わない。表情メニューの表情は、いちばん下の階層の名前で決め、上の階層に感情・部位・左右の語があれば使わない。メニューの表情を先に、次に英語の BlendShape 名（`Fcl_ALL_Neutral` など）を使う。どちらから埋めるときもまばたき・口・視線を止めない（neutral は顔を足さないため。メニューの表情の `block` は写さない）。neutral の名前のメニューの表情は、動かす BlendShape から感情を推定する対象からも外す（少し微笑んだ待機顔が happy に入らないように）。
- **視線**: 両目が動く名前の組がそろったときだけ埋める。組は `lookUp`・`look_up`・`eyeLookUp`・`eye_look_up` のような 1 つの名前か、Perfect Sync の左右の組（lookUp = `eyeLookUpLeft` + `eyeLookUpRight`、lookLeft = `eyeLookOutLeft` + `eyeLookInRight`、lookRight = `eyeLookInLeft` + `eyeLookOutRight`）。左右はアバター自身の左右で、VRM の lookLeft とそろう。大文字・小文字は区別しない。片目だけの名前や、組の片方しか無いメッシュがあるときは使わない。左目と右目が別のメッシュで組の名前が分かれているアバターも埋めない（片目だけ動かすおそれがあるより、空のままにする）。通知にはメッシュにある名前をそのまま出す。
- **口の形**: 埋め方は変えない。Lip Sync 以外の名前から埋めたときの通知を、ほかの自動設定と同じ「`<表情>: 名前から自動で設定しました（<名前>）。書き出したVRMで表情の動きを確認してください。`」にし、書き出し結果の「名前から自動で設定」に 1 行ずつ出す。Lip Sync から入れたものは作者の設定なので出さない。表情ボードからの書き出しでも、口の枠を空にしたときは以前から名前で埋めていたので、同じように一覧に出る。
- neutral と視線は、喜怒哀楽と同じく `inferEmotions` のときだけ埋める（表情ボードの書き出しは埋めない）。どれも空の表情（無いか、何も動かさない）だけを埋め、表情メニューや作者が設定した表情は変えない。
- 同じメッシュに同じ名前が 2 つあるときは、その名前を使わず通知する（喜怒哀楽と同じ）。
- 引数 `filledEmotions` を `filledPresets` に、`RecordInferredEmotions` を `RecordInferredPresets` に改名した。

## 文言

新しい文言は足していない。行の文は既存の `FilledFormat` を、同名の通知は既存の `DuplicateNameFormat` を使い、4 言語の訳はそのまま使える。Codex に相談した結果は次のとおり。

- 採用: 口・視線の行も「表情の動き」で統一する（分けても操作は明確にならない）。
- 採用: 視線の名前の組は ` + ` でつなぐ。
- 不採用: 視線を埋めたときに「アプリの視線追従には反映されない場合があります」という共通の注記を足す。結果の一覧にグループ単位の注記の仕組みが無く、足すと 4 言語の訳と表示の追加になるため、README と変更履歴に書いた。

## 入れなかったもの

- 書き出す VRM の lookAt は、これまでどおり目のボーンで動かす（`VrmLookAtExport` の `bone`）。仕様どおりのアプリは視線の表情を lookAt に使わないので、今回埋めた視線の表情は、アプリが表情として直接動かすときだけ使われる。目のボーンが無いアバターで lookAt を表情の型で書き出すかは未決。
- 日本語の視線の名前（`目_上` など）や、左右の意味が確かめられていない Unified Expressions の名前は使わない。
- 表情ボードの自動の候補に neutral と視線を足すこと。
- 古い口の形の通知の訳（`ExporterLocale_*.json` の `{mapping.Preset}: 既知の表情名から…`）は使われなくなったが、訳のファイルは手で並べ替えずに残した。

## 確認したこと

- `ExpressionNameInferenceTests` に、neutral・視線（Perfect Sync の組・1 つの名前・片目だけ・組が別のメッシュに分かれたもの・同名）・口の形の記録・紛らわしい名前（`eye_smile_L`・`Fcl_MTH_Neutral`・`eye_look_left_R` など）のテストを足した。
- 手元のアバターで変更前と変更後の VRM を比べ、もとから何かを動かしていた表情が変わらず、空だった表情だけが埋まることを確かめた（結果は PR）。
