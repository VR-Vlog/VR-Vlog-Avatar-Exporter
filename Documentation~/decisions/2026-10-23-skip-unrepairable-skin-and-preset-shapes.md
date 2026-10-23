# 直せないスキンメッシュと、解決できないプリセットのシェイプで書き出しを止めない

- 日付: 2026-10-23
- 対象: `Editor/SkinnedMeshFallbackWeights.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`

## 背景

書き出しの経路を、VRChat SDK でアップロードできるアバターの構成から順に追い、段のガード（`ExportStageGuard`）の外で例外になる箇所を洗い出した。そのうち次の 2 つは、1 つのメッシュや 1 つのシェイプの不備で書き出し全体が止まっていた。

- `SkinnedMeshFallbackWeights.Preserve` は `ExportCore` の 2 か所でガードの外から呼ばれる。ウェイトのない頂点をレンダラーに追従するウェイトに置き換える処理で、次の構成では `InvalidOperationException` を投げていた。どれも VRChat ではそのままアップロードできる。
  - ウェイトのある頂点とない頂点が 1 つのメッシュに混ざっている
  - ウェイトのない頂点のメッシュに Cloth が付いている
  - ボーンとバインドポーズの数が合わない
  - Root Bone がアバターの外を指している
  - レンダラーや追従先のスケールが 0 で、変換を反転できない
- 表情ボードのプリセット（`AuthorPresets`）は、書き出し用コピーでパスが一意に解決できないレンダラー（同名の兄弟オブジェクトなど）や、メッシュに無いシェイプを指すと例外を投げ、これもガードの外だった。

## 決定

- `SkinnedMeshFallbackWeights.Preserve` に `lenient` 引数を足し、`ExportCore` は `guard.ContinueOnFailure` を渡す。lenient のときはレンダラーごとに例外を受け、そのメッシュはウェイトを直さずに書き出して警告を出す（書き出しは段を外して 2 回目の処理を走らせることがあるので、同じ警告は 1 件にまとめる）。各処理は例外を投げる前にレンダラーとメッシュを元に戻している（`finally`）ので、受けたあとのコピーは手を付ける前と同じになる。strict のときは今までどおり止まる。ほかの lenient の扱い（`ReplaceLilToonMaterials` など）と同じ書き方にした。
- `AuthorPresets` は、解決できないシェイプだけをそのプリセットから外し、プリセット名とパス・シェイプ名を警告に出す。警告は書き出しの試行ごとの一覧に入るように、`authorCopyExpressions` のコールバックに試行の警告の一覧を渡す形にした（呼び出し元は表情ボードの 1 か所だけ）。`AuthorPresets` はインスタンスの状態を使わないので `static` にしてテストから直接呼べるようにした。
- かんたん転送も同じ `SaveVrm` を通るので、通常の書き出しとの一致は変わらない。

## テスト

- `SkinnedMeshFallbackWeightTests.LenientExportKeepsAnUnrepairableMeshAndWarns`: 頂点の混在・アバターの外の Root Bone・スケール 0・Cloth の 4 ケースで、止まらずに警告が 1 つ出て、メッシュとボーンが元のままになること。strict で止まることは既存のテストがそのまま確かめている。
- `ExperimentalExpressionCaptureTests.APresetShapeTheCopyCannotResolveIsLeftOutWithAWarning`: 無いシェイプと無いレンダラーを指す行だけが外れ、残りがプリセットに入ること。
