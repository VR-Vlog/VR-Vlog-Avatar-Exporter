# 一時フォルダの後片付けを確かめるテストで、前から残っていたフォルダが消えても失敗しないようにする

- 日付: 2026-10-23
- 対象: `Tests/Editor/AvatarOptimizerBoxIntegrationTests.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`

## 背景

書き出しの一時フォルダ（`Assets/VRVlogExportTemp-*`）が残らないことを確かめるテストは、書き出しの前にフォルダの一覧を取り、後の一覧が `Is.EquivalentTo` で同じになることを確かめていた。

書き出しは始めに `NdmfExportPreparation.DeleteStaleTemporaryAssetDirectories` を呼び、ほかの書き出しが使っていない古い一時フォルダを消す。前のテストが失敗して一時フォルダが残っていると、このテストの書き出しがそれを消すので、後の一覧が前より短くなってテストが失敗する。失敗の文は「一時フォルダが残った」と読めるので、原因が分かりにくい。

## 決定

- 4 か所の確認を `Is.SubsetOf` に変える。書き出しが新しく作ったフォルダが残っていれば今までどおり失敗し、前から残っていたフォルダが消えたときは失敗しない。
- 製品のコードは変えない。

## 検討して採らなかった案

- 一覧を取る前に `DeleteStaleTemporaryAssetDirectories` を呼んで古いフォルダを先に消す案。テストが掃除の処理に頼ることになり、掃除が壊れたときに後片付けのテストまで巻き込まれるので採らなかった。
