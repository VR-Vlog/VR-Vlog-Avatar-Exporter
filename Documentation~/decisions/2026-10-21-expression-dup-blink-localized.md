# 同名の表情・VRChatからの登録件数の通知を訳し、瞬きの訳の取り違えを直す

- 日付: 2026-10-21
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExpressionNameInferenceTests.cs`・`CHANGELOG.md`

## 背景

`2026-10-21-expression-warnings-localized.md` で入れなかった 2 つが残っていた。

- 「{0}: mesh {1} に同名の表情が複数あるため自動設定を省略しました。」は `$"{mapping.Preset}: mesh {meshIndex} …"` で埋め込んだ後の文で訳を探していたので、訳の表と一致せず日本語のまま出ていた。同じ作りの「VRChatから{0}個の表情を登録しました。…」も同じく当たっていなかった。
- 英語・韓国語・中国語の訳で、「瞬き」を「瞬間」（instant・즉시・即时）と取り違えている文が 13 個あった。書き出し結果の一覧と、瞬きの設定が壊れたときに止まる理由の文に出る。

## 決定

- 2 つの通知は `{0}` 入りの元の文を定数（`DuplicateExpressionFormat`・`BakedFormat`）にして、訳を引いてから `string.Format` で値を入れる。0 件のときの通知（`NoneBakedWarning`）と瞬きを作れなかった通知（`BlinkNotGeneratedWarning`）も定数にして、一覧に入れるところで訳を通す。
- 訳の表の見出しは `{mapping.Preset}: mesh {meshIndex} …`・`VRChatから{result.Count}個…` を `{0}`・`{1}` の形に置き換えた。
- 「瞬き」は en: blink、ko: 눈 깜빡임、zh-Hans・zh-Hant: 眨眼 に揃えて、取り違えていた 13 個を訳し直した。
- 日本語の文は変えていない。

## 確認したこと

- `ExpressionNameInferenceTests.EveryShippedLocaleTranslatesTheDuplicateAndBlinkWarningsAsBlink` で、4 つの元の文に 4 言語とも訳があり値を入れられること、古い見出しが残っていないこと、「瞬き」を含む元の文の訳に instant・moment・즉시・순간・即时・瞬间・即時・瞬間が入っていないことを確かめる。
- `ExpressionNameInferenceTests` と、0 件の通知を比べる `ExperimentalExpressionCaptureTests.OrdinaryRouteRecordsTheListWhenTheMenuGivesFewerFaces` が 89 件すべて通った。
