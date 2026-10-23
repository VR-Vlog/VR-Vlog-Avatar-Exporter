# アプリに転送で、作者名の見出しを押すと入力欄にフォーカスする

- 日付: 2026-10-23
- 対象: `Editor/AppTransferExport.cs`（`DrawTerms`）・`CHANGELOG.md`

## 背景

足りない入力を赤字で示す変更（2026-10-23-required-field-highlight）で、作者名の見出しの色を変えるために `EditorGUI.PrefixLabel` へ `GUIUtility.GetControlID(FocusType.Passive)` で作った id を渡していた。見出しはこの id に結び付くので、見出しを押しても作者名の入力欄にフォーカスが移らなかった。`EditorGUILayout.TextField(label, ...)` のような見出し付きの欄では、見出しを押すと欄にフォーカスが移る。

## 決定

- `EditorGUI.PrefixLabel(rect, label, style)`（id を渡さない形）を使う。id を渡さないと、Unity は次に作られるコントロール（作者名の入力欄）の id を見出しに結び付けるので、見出しを押すと入力欄にフォーカスが移る。渡したスタイル（赤字かどうか）はそのまま使われる。
- 見た目と、赤くするかどうかの判断は変えない。

## 確認

- Unity 2022.3 の `EditorGUI.PrefixLabel` の実装で、id が 0 のときは次の `GetControlID` で見出しを描き、そのコントロールの id でフォーカスを扱うことを確かめた。
- 既存の `CloudTransferTests`（足りない入力を赤字で示すテストを含む）が通ることを確かめた。

## 検討して採らなかった案

- 見出しの領域を押したときに自分で `GUIUtility.keyboardControl` を入力欄の id にする案は採らなかった。Unity の見出し付きの欄と同じ動きを、標準の呼び方で得られるため。
