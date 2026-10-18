# VRM に書き込む生成名を、ランダムな GUID から入力で決まるハッシュに変える

- 日付: 2026-10-18
- 対象: `Editor/DeterministicNames.cs`・`Editor/BlinkExportSession.cs`・`Editor/ExportOptimizationBindings.cs`・`Editor/VrChatExpressionBaker.cs`・`Editor/AuthoredExpressionEndpoints.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/LilToonMainTextureBaker.cs`・`Editor/AlphaMaskBaker.cs`・`Tools/BakeTestShim.cs`・`Tools/run-bake-tests.ps1`・`Tests/Editor/DeterministicNamesTests.cs`

## 背景

Exporter は書き出しの途中で、BlendShape（モーフターゲット）・マテリアルの複製・焼き込んだテクスチャを作り、VRM に書き込む。これらの名前は `__VRVlog_Blink_` などの接頭辞に `Guid.NewGuid().ToString("N")`（小文字 32 桁）を付けていた。そのため同じアバターを同じ設定で 2 回書き出しても、VRM の JSON が毎回違った。

- lilToon の版ごとの書き出しを比べるとき（`2026-10-14-liltoon-version-parity.md`）、比較ツールで `__VRVlog_BlinkNone_<GUID>` と `__VRVlog_Empty_<GUID>` を番号に置き換えてから比べる必要があった。
- 書き出し結果の差分を見て回帰を探すとき、名前の違いが本物の違いを埋もれさせる。

接頭辞は消費側が `StartsWith` で判定している（`ExportOptimizationBindings` の生成名の判定、`NdmfExportPreparation`、`ExpressionAnimationData.TargetPrefix`）。生成名は名前だけで参照される（メニュー表情のターゲット、追跡表情の `shape`、最適化後の対応付け）。`MapGeneratedName` は書き出し用の複製全体で名前が 1 つに決まることを前提にしている。

## 決定

- 接頭辞の後ろの 32 桁を `DeterministicNames.Hash(...)` に置き換える。入力の各部分を「文字数 + `:` + 値 + 改行」でつなぎ、UTF-8 の SHA-256 を取り、先頭 16 バイトを小文字の 16 進で書く。長さは GUID と同じ 32 文字で、名前の長さと形（`[0-9a-f]{32}`）は変わらない。部分の文字数を入れるので `("a", "bc")` と `("ab", "c")` は別の値になる。
- ハッシュの入力は、用途の名前・アバターのルートからのレンダラーのパス・元の BlendShape 名（またはマテリアル名）と、その場所で 1 つに決めるのに要るキー。

| 接頭辞 | 作る場所 | 入力 |
| --- | --- | --- |
| `__VRVlog_Blink_` | `BlinkExportSession.Bake` | `Blink`・レンダラーのパス・元の BlendShape 名・スロット（`blink`／`blinkLeft`／`blinkRight`） |
| `__VRVlog_BlinkNone_` | `BlinkExportSession.Bake`（瞬きなし） | `BlinkNone`・レンダラーのパス |
| `__VRVlog_BlinkNone_`／`__VRVlog_Empty_` | `ExportOptimizationBindings.RecreateNoOp` | `BlinkNone` または `Empty`・最適化前に記録したパスと BlendShape 名（`route.Label`） |
| `<マテリアル名>__VRVlog_Expression_` | `ExportOptimizationBindings.ApplyAuthored` | `Expression`・レンダラーのパス・スロット番号・マテリアル名 |
| `__VRVlog_Menu_` | `VrChatExpressionBaker.Bake`（固定の表情） | `Menu`・出力先のレンダラーのパス・メニュー項目名・元のチャンネルごとのパス・BlendShape 名・値 |
| `__VRVlog_Anim_` + `__VRVlog_Menu_` | `VrChatExpressionBaker.Bake`（アニメーション） | `MenuAnimation`・出力先のレンダラーのパス・メニュー項目名・元のパス・BlendShape 名・初期値・端点の値 |
| `__VRVlog_Endpoint_` | `AuthoredExpressionEndpoints.SetEndpoint` | `Endpoint`・`Clip` または `Tracking`・表情ファイル名または追跡表情名・レンダラーのパス |
| `__VRVlog_Capture_` | `ExperimentalExpressionCaptureSession`（記録した表情） | `Capture`・レンダラーのパス・記録した表情の名前 |
| `__VRVlog_Capture_` | 同（喜怒哀楽の標準表情） | `CapturePreset`・レンダラーのパス・プリセット名 |
| `<マテリアル名>__VRVlog_Main_` | `LilToonMainTextureBaker.Bake` | `Main`・そのマテリアルを最初に使うレンダラーのパスとスロット番号・マテリアル名 |
| `<マテリアル名>__VRVlog_Alpha_` | `AlphaMaskBaker.Bake` | `Alpha`・同上・マテリアル名 |

- 値（重み）は `double` を `"R"` 書式・インバリアントカルチャで文字列にする。
- 入力が同じになる場合は名前の末尾に `_2`・`_3` を付けて区別する。たとえば同じ名前の兄弟レンダラー（パスが同じになる）、同じ名前の表情ファイル、前回の書き出しで作った名前が残った VRM を読み込んだアバター。BlendShape では、書き出し用の複製にあるすべての BlendShape 名（非アクティブのレンダラーも含む）を先に使用済みにし、作った順に番号を振る。マテリアルの複製は複製にあるマテリアル名、テクスチャは同じ書き出しで作ったテクスチャ名と比べる。作る順は毎回同じなので、番号も毎回同じになる。
- 次のものは GUID のまま残す。VRM には入らず、毎回別の名前である必要がある。
  - 一時ファイルのパス（`ExportDetailsText`・`ExportRecoverySession`・`ExperimentalExpressionCaptureWindow` の `.tmp`）。`Tools/validate.py` が `ExportRecoverySession` の GUID を確かめている。
  - 転送用の一時ファイル（`Editor/LanTransfer/` 以下）。
  - `NdmfExportPreparation` の一時フォルダ名（`VRVlogExportTemp-<GUID>`）。書き出しの後に消すフォルダで、VRM の名前には使わない。
- 表情の記録で形が変わるかを確かめる一時の BlendShape（`__CaptureProbe_`）は、作ってすぐ消すので VRM には入らない。ほかと合わせてハッシュにした。

## 検討した代替案と不採用理由

- 連番だけにする（`__VRVlog_Menu_0` など）: 前回の書き出しの名前が残ったアバターや、別の処理が作った名前と重なりやすい。レンダラーや BlendShape を 1 つ足すと、後ろの名前がすべてずれて差分が大きくなる。
- 元の BlendShape 名をそのまま名前に入れる: 日本語や記号を含む名前、長い名前がそのまま VRM の名前になり、接頭辞での判定や長さの扱いが変わる。ハッシュなら形と長さが GUID と同じになる。
- ハッシュを短くする（8 桁など）: 重なったときは番号で区別できるが、名前の長さを変える理由が無い。GUID と同じ 32 桁にして、既存の正規表現や比較ツールの規則をそのまま使えるようにした。
- 番号を付けずに例外で止める: 同じ名前の兄弟レンダラーは実際のアバターにあり、これまで書き出せていた。番号で区別して書き出しを続ける。

## 影響

- 同じアバターを同じ設定で書き出すと、生成名が毎回同じになる。名前の接頭辞・長さ・使える文字は変わらない。
- 前回の書き出しで作った名前が残ったアバターでは、名前の末尾に `_2` などが付く。
- `Tools/compare-liltoon-version-exports.py` の置き換え規則（`^(__VRVlog_(?:BlinkNone|Empty)_)[0-9a-f]{32}$`）は新しい名前にも当てはまるので変えていない。置き換えなくても同じ名前になる。
- `LilToonMainTextureBaker.Bake` と `AlphaMaskBaker.Bake` に省略できる引数 `origin` を足した。指定しないときはマテリアル名だけで名前を決める。

## 未決

- Unity の EditMode テスト（`Tests/Editor/DeterministicNamesTests.cs`）は、この変更ではまだ Unity で実行していない。
