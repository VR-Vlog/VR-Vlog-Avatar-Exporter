# 「自動で軽量化する」の既定をオンにする

- 日付: 2026-11-09
- 対象: `Editor/ExportSettings.cs`・`Editor/ExportSettingsStore.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`README.md`・`Documentation~/ExperimentalExpressionCapture.md`・`Tests/Editor/ExportSettingsTests.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`

## 背景

表情ボードの「自動で軽量化する」は既定がオフで、アバターごとに `ExportSettings.json` へ覚える。既定をオンにしたい。

今の保存には「未設定」と「オフ」の区別が無い。

- 既定と同じ設定のアバターはエントリを書かない。トグルをオンにしてからオフに戻すと、ほかに保存するものが無ければエントリごと消える。
- 転送した名前やポーズの選択が残るアバターは、トグルを触っていなくても `"AutoLightweight":false` のエントリがある。

## 決定

- `ExportSettings.AutoLightweight`・`AvatarEntry.AutoLightweight` の既定を `true` にし、表情ボードの既定（フィールドと `ChangeAvatar`）も `true` にする。保存形式のバージョンは上げない。上げると古いファイルが「別のバージョンのファイル」として保存を止める。
- 既定がオンになると、オフは既定と違う値になるのでエントリに必ず書かれる。これからは「エントリが無い」が未設定、`false` がオフになる。
- 読み込みは次のとおり。
  - エントリが無い: オン。
  - エントリに `AutoLightweight` のキーが無い（項目を足す前のエントリ）: オン。だれも選んでいないので既定に従う。
  - `"AutoLightweight":false` が書かれたエントリ: オフのまま。
  - `"AutoLightweight":true`: オン。

## 割り切り

- これまでのバージョンが書いた `false` は、自分でオフにしたのか触っていないのかを見分けられない。保存済みの値を変えない方を選び、オフのまま残す。書き出しの結果がアップデートだけで変わることはない。
- オンにしてからオフに戻してエントリが消えたアバターは、記録が残っていないのでオンで始まる。

## 確かめたこと

- `ExportSettingsTests` に、未設定はオン、オフの保存と読み直し、以前のエントリの 3 通りを足した。
- `QuickTransferMatchesDefaultNormalTests` に、未設定はオン・オフを保存すると両方の表情ボードがオフで始まり、書き出しが一致するテストを足した。
