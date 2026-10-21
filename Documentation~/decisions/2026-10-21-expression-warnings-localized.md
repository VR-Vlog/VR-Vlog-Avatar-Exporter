# 既知の表情名と口の形の通知を英語・韓国語・中国語でも出す

- 日付: 2026-10-21
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`CHANGELOG.md`

## 背景

書き出し結果の一覧に出る次の 3 つの通知が、日本語以外の言語でも日本語のまま出ていた。

- 「{0}: 既知の表情名からVRMの割り当てを追加しました。動作を確認してください。」
  `$"{mapping.Preset}: …"` で表情名を埋め込んだ後の文で訳を探していたので、訳の表と一致しなかった。訳の表には `{mapping.Preset}: 既知の…` という、どこからも引かれない見出しが入っていて、韓国語と中国語の訳は `{地图。 Preset}` のように崩れていた。
- `UnboundVisemeWarning`（Lip Sync で指定した変形が書き出したメッシュに無い）と `UnexportedVisemeMeshWarning`（Lip Sync の Face Mesh が書き出したアバターに無い）。訳を通さずに一覧へ入れていて、訳の表にも無かった。

## 決定

- 既知の表情名の通知は `KnownNameFormat`（`{0}` 入りの元の文）で訳を引いてから `string.Format` で表情名を入れる。同じファイルの `FilledFormat` と同じ書き方。
- 口の形の 2 つは、一覧に入れるところで `ExporterLocalization.T` を通す。定数そのものは日本語の元の文のままにして、日本語の環境とテストが定数と比べている箇所はそのまま一致する。
- 4 言語（en・ko・zh-Hans・zh-Hant）の訳を足し、崩れた `{mapping.Preset}: 既知の…` の見出しは消した。通知の一覧はそのまま表示するだけで、ほかの場所で二重に訳すことはない。
- 日本語の文は変えていない。

## 入れなかったもの

- 同じファイルの「{mapping.Preset}: mesh {meshIndex} に同名の表情が複数あるため…」も同じく埋め込み後の文で訳を探していて訳が当たらないが、今回の 3 つに絞った。
- 「瞬きのVRM設定を自動生成できませんでした。…」の英語・韓国語・中国語の訳が「瞬き」を「瞬間」と取り違えているのも、今回は直していない。

## 確認したこと

- `ExpressionNameInferenceTests.EveryShippedLocaleTranslatesTheMouthAndKnownNameWarnings` で、3 つの元の文に 4 言語とも空でない訳があり、既知の表情名の訳が `{0}` で始まり、崩れた見出しが残っていないことを確かめる。
- 口の形の通知を比べている既存のテスト（`DescriptorVisemeExportTests`・`ExpressionNameInferenceTests`・`NdmfLostMorphFollowTests`）が日本語の環境で通ることを確かめた。
