# VRChat SDK が無いときは表情の記録のテストを飛ばす

- 日付: 2026-10-21
- 対象: `Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`

## 背景

`ExperimentalExpressionCaptureTests` の一部のテストと、`PhysBoneSpringExportTests.AddVrChatAvatarDescriptor` は、VRChat SDK の `VRCAvatarDescriptor` が見つからないと `Assert.That(..., Is.Not.Null)` で失敗にしていた。SDK を入れていない環境でテストを流すと、書き出しの不具合ではないのに失敗が並ぶ。ほかのテストは SDK が無いとき `Assert.Ignore("Install the real VRChat SDK.")` で飛ばしている。

## 決定

SDK の型が見つからないときの 4 か所を、ほかのテストと同じ `if (type == null) Assert.Ignore("Install the real VRChat SDK.");` にした。

- `OrdinaryRouteSaysWhenTheMenuGivesMoreFacesThanTheList`
- `ExistingMenuCandidateCanBeDiscoveredPreviewedAndCapturedWithoutChangingItsController`
- `ExplicitBlinkMetadataSurvivesModelAndRecordedFaceExport` の SDK を使う組み合わせ
- `PhysBoneSpringExportTests.AddVrChatAvatarDescriptor`

## 入れなかったもの

- SDK があるのに中の型やフィールドが見つからないときの `Assert.That`（`AnimLayerType` など）は、SDK の版が合っていないことを知らせるため失敗のままにした。
- APL や Modular Avatar が無いときの `Assert.That` は、今回の対象（VRChat SDK）と別なので変えていない。

## 確認したこと

- 変えたテストを Unity の EditMode テストで流し、SDK がある環境で結果が変わらないことを確かめた。
