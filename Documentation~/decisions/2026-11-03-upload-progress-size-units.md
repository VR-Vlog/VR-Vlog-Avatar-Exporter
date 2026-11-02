# アップロードの進み具合をバイト数でなくKB・MB・GBで出す

- 日付: 2026-11-03
- 対象: `Editor/LanTransfer/TransferSizeText.cs`・`Editor/LanTransfer/AppTransferPanel.cs`・`Editor/LanTransfer/CloudVrmTransferWindow.cs`・`Editor/Locales/ExporterLocale_*.json`

## 背景

「アプリに転送」と「スマホに送る」のアップロード中のバーに、`25,165,824 / 26,195,432 バイト（暗号化済み）` のように生のバイト数が出ていた。桁が多くて読みにくく、進むたびに数字の幅も変わる。

## 決定

- 2つのバーとも `TransferSizeText.Progress` で `24.0 / 25.0 MB（暗号化済み）` の形にする。
- 単位は全体の大きさで1つ決め、送った量も同じ単位で出す。送り始めの `0.0 / 25.0 MB` から終わりまで単位が変わらず、数字の幅もほとんど変わらない。
- 単位は1024ごとにB・KB・MB・GBにする。同じ「スマホに送る」の画面で、送るファイルの大きさを `EditorUtility.FormatBytes`（1024ごと・KB/MB/GB）で出しているので、それと数字をそろえる。MiBのような表記はUnityの画面で見ないので使わない。
- Bは整数、KB以上は小数1桁にする。丸めると1024になるところで次の単位にする（`1023.9 KB` の次は `1.0 MB`）。GBより上は作らない（転送の上限は256MB）。
- 数字はOSの地域設定によらず小数点を `.` にする（カンマの地域でも `24,0` にならない）。
- 翻訳の原文は `{0}（暗号化済み）` に変え、en・ko・zh-Hans・zh-Hantも同じ形にする。単位の表記は全言語で同じ。

## 確かめたこと

- `Tests/LanTransfer/TransferSizeTextTests.cs`（`Tools/run-lan-transfer-tests.ps1` で回る）で、単位の選び方・切り替わり・地域設定・負の値を確かめる。
- Unity 2022.3.22f1で2つのウィンドウを日本語・英語・韓国語・中国語（簡体・繁体）で開き、表示を目で確かめた。
