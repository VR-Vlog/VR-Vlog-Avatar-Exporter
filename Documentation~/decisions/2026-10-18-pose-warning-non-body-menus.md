# 体のレイヤーを動かせないメニューを「ポーズ未対応」の警告に出さない

- 日付: 2026-10-18
- 対象: `Editor/PoseMenuResolver.cs`・`Editor/PoseSampling.cs`・`Editor/PoseExportSession.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/PoseRegistrationTests.cs`・`CHANGELOG.md`

## 背景

同梱ポーズの書き出しでは、Expression Menu のすべての項目を `PoseMenuResolver.Read` で姿勢の候補として調べ、確定できなかった項目を `PoseExportSession` が「ポーズ未対応: <項目> — <理由>」の警告にしていた。`Read` はレイヤーを順に調べ、体・手足・指の Tracking Control が Animation 以外を指定するレイヤー（手のジェスチャーのレイヤーなど）に当たると、その項目と関係がなくても止まる。そのため服や表情の切り替えも全部この理由で警告になり、1 体で 30〜60 件の意味のない警告が出て、本当にポーズとして調べるべき項目の警告が埋もれていた。

## 決定

- `Read` の各レイヤーの証明に入る前に、項目が体の姿勢を選べるかを分類する（`CannotSelectBody`）。項目のパラメーター（Parameter Driver で書き込まれる先も含む）が、体を動かしうるレイヤーの入力に 1 つも届かなければ「体の姿勢を変えないメニュー」とする。
- 体を動かしうるレイヤーは、体のカーブを持つ状態か BlendTree があるレイヤー、体のカーブを持つ Controller で Write Defaults の状態があるレイヤー、同期レイヤー、何らかの Behaviour（欠落したスクリプトを含む）を持つレイヤーとする。体のカーブの判定は既存の証明と同じく Override Controller の差し替えと FX の `skipMuscles` を使う。
- 入力は遷移の条件だけでなく、Entry・サブステートマシンの遷移、状態の time／speed／mirror／cycleOffset パラメーター、BlendTree のパラメーターまで含める。
- Parameter Driver は、入力がメニューに依存するレイヤーにあれば Set／Add／Random の書き込み先もメニュー依存とし、Copy はコピー元がメニュー依存なら書き込み先もメニュー依存とする。これを変化がなくなるまで繰り返す。SDK の制御用 Behaviour 以外の型、読めない Driver、知らない操作の種類があれば分類をやめ、従来の証明に回す。分類の途中で例外が出たときも従来の証明に回す。
- 分類で除いた項目は確認画面の一覧に残し、`row.Error` を「体の姿勢を変えないメニューです。」、`PoseCandidate.NotBodyPose` を `true` にする。`PoseExportSession` はこの行を「ポーズ未対応」の警告にせず、代わりの行も出さない。確認画面は変えない。
- 項目自体にすでにエラーがある行（Puppet など）は分類せず、従来どおり警告に出す。

## 確認したこと

- 分類で除く項目は、体を動かしうるレイヤーの遷移をメニューの値で選べず、Playable Layer の重みを変える Behaviour にも届かない。従来の証明でも `selectedAffectsBody` が `false` のままになり、ポーズとして保存されることはなかった。ポーズとして保存する項目は変わらない。
- `row.Error` を警告にしているのは `PoseExportSession` の 1 か所だけだった。表情の記録（`ExperimentalExpressionCaptureSession`）は手動で追加したポーズだけを見ていて、書き出しの結果レポートはポーズのエラーを使っていない。

## 検討した代替案と不採用理由

- Tracking Control の検査を項目に関係するレイヤーだけに絞る: 本当のポーズ候補の診断と、ポーズとして保存する項目が変わる。今回の目的は意味のない警告を減らすことだけなので、証明そのものには手を入れない。
- 「体の姿勢を変えないメニュー」も件数だけ警告に出す: 利用者が直す必要のない情報で、警告の一覧を読みにくくするだけなので出さない。

## 影響

- 服・表情・小物の切り替えの項目は「ポーズ未対応」の警告に出なくなる。体のレイヤーを動かす項目は、従来どおりの理由で警告に出る。
- 本当にポーズを選ぶ項目でも、関係のないレイヤーに Animation 以外を指定する Tracking Control があると、従来どおり Tracking Control の理由で止まる。これは別の制約で、今回は変えていない。
- 分類は迷ったら従来の証明に回すので、体を動かせないのに分類できなかった項目（知らない Behaviour があるレイヤーを通るものなど）は、これまでどおり警告に残る。
