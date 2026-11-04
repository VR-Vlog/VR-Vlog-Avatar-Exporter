# リリース確認スクリプトは引数を名前で渡す

- 日付: 2026-11-05
- 対象: `Compatibility/Evidence/ReleaseZipStartup/ZipProbe.cs.txt`

## 背景

リファクタリングで `PackageVersion` と `RequireLilToon` が `LilToonExporterWindow` から `ExportEntry` に移り、`UniVrmOneClickExporter.Export` の引数の順番も変わった。確認スクリプトはリフレクションで古い場所と古い位置を引いていて、コンパイルでは気づけず、実行すると `NullReferenceException` で止まる。CIはこのスクリプトを動かさない。

## 決定

メソッドの引き先を `ExportEntry` に変え、`Export` の引数は位置でなく引数名で渡す。引数の順番が変わっても壊れない。名前が変わったときは `FindIndex` が -1 を返し、例外になって結果に `failed` と出る。

## 考えた別案

位置の番号だけを新しい順番に合わせる案は採らなかった。次に順番が変わると、型の合う引数に別の値が入って気づけないことがある。

## 確かめ方

修正後のスクリプトをパッケージの複製で実行し、`startup` と `export` がどちらも `passed`、生成した VRM が 35,692 バイトになることを確かめた。修正前は同じ手順で `NullReferenceException` になる。リリース ZIP そのものでの実行は次のリリース確認で行う。
