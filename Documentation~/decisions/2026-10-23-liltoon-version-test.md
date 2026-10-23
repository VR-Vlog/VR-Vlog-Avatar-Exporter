# lilToonのバージョンのテストを「止めない」決定に合わせる

- 日付: 2026-10-23
- 対象: `Tests/Editor/MainColorLayersExportTests.cs`

## 背景

`MainColorLayersExportTests.ExportStopsForLilToonOutsideTheSupportedRange`は、導入済みのlilToonが1.10.3・2.3.5・2.3.4-betaのとき書き出しが`NotSupportedException`で止まることを確かめていた。[2026-10-22-liltoon-no-version-gate.md](2026-10-22-liltoon-no-version-gate.md)でバージョンによる停止をなくしたので、この3件は書き出しが成功して失敗していた。nullだけは`LilToonFullSnapshot.Inject`が「lilToonが見つかりません」で止めるので通っていた。

## 決定

- 1.10.3・2.3.5・2.3.4-betaは`ExportRecordsInstalledLilToonVersionAndKeepsCatalogueVersion`のケースに移す。書き出しが成功し、`sourceLilToonVersion`が2.3.4のまま、導入済みのバージョンが`asset.extras.VRVLOG_lilToonInstalledVersion`に記録されることを確かめる。
- nullのケースだけを`ExportStopsWhenLilToonIsMissing`として残す。バージョンで止まるのはlilToonが入っていないときだけ、という現在の決定に合わせた名前にした。

製品の動作は変えない。
