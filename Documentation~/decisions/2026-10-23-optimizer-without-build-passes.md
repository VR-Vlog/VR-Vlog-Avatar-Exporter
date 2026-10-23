# Avatar Optimizerを使うアバターでも、ビルド処理なしのやり直しで書き出せるようにする

- 日付: 2026-10-23
- 対象: `Editor/ExportOptimizationBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExportStageGuardTests.cs`・`Tests/Editor/ExportOptimizationBindingsTests.cs`

## 背景

失敗しても書き出しを続ける設定（`ExportRecoveryReport.ContinueOnStageFailure`）では、Modular Avatar・NDMFのビルド処理（`ExportStage.BuildProcess`）で何かが失敗すると、ビルド処理を外して新しいコピーから書き出し直す。ところがAvatar Optimizer（AAO）のコンポーネントを1つでも持つアバターでは、このやり直しが必ず「最適化後の表情の移動情報を取得できませんでした。」で止まっていた。

- 書き出し用のコピーはAAOのコンポーネントを残したまま作られる。`ExportOptimizationBindings.Capture` はそれを見て、AAOが表情の移動情報を渡してくる（`ExportOptimizationMarker.MappingApplied` が立つ）ものと決めていた。
- ビルド処理なしのやり直し（`NdmfExportPreparation.Prepare` の `skipProcessing`）ではAAOのパスが走らないので、`MappingApplied` は立たない。
- `ValidateAndApply` の呼び出しはガードの外にあったので、この例外で書き出し全体が止まった。

このため、AAOを使うアバターでは、ビルド処理に回るはずの失敗（ビルド中の例外、NDMFのビルドエラー、ヒューマノイドが崩れたときの `ValidatePreparedHumanoid`）がすべて書き出しの失敗になっていた。

同じ `ValidateAndApply` は、AAOが表情のシェイプキーやマテリアルスロットを消したとき（「最適化で書き出し用の表情が失われました」「最適化でVRM表情のマテリアルが失われました」）と、2つのメッシュの同名シェイプキーをまとめた結果、VRMの表情の1つのシェイプキーに違う適用量が付いたとき（「最適化で元のVRM表情の適用量が衝突しました」）にも、ガードの外で止まっていた。

## 決定

続ける設定では、どの場合も中途半端でもファイルを書き出し、外したものは結果一覧と警告に出す。止める設定（既定）の動きは変えていない。

- `Capture` に `optimizes` を足し、ビルド処理を外したやり直しでは false を渡す。false のときは、コピーにAAOのコンポーネントがあっても移動情報を待たない。AAOの版の確認（1.7以上・2.0未満でないと出す「Avatar Optimizer の表情互換処理を読み込めません」）も、移動情報を待つときだけ行う。
  - 版が合わないプロジェクトでは、まず `OptimizerEvidence` を外したやり直し、次にビルド処理を外したやり直しになり、最後はAAOを通さずに書き出す。版の文言は、外した段階の理由として結果一覧に出る。AAOのコンポーネントを最初から見ないようにする案もあったが、やり直しの仕組みに任せるほうが変更が短いのでこちらにした。
- `ValidateAndApply` の呼び出しを `guard.Watch(ExportStage.BuildProcess, ...)` で包んだ。表情やマテリアルが失われたときは、ビルド処理を外して書き出し直す（ヒューマノイドが崩れたときの `ValidatePreparedHumanoid` と同じ扱い）。
  - 失われた表情だけを外して続ける案も考えた。ただ、表情の経路はメニューの表情・まばたき・Lip Sync・トラッキング用の出力が別々に参照していて（`MapMorph`・`MenuBinding`・`OutputBinding`・まばたきとLip Syncの `Remap` など）、1つを外すにはその全部に「外れた経路を飛ばす」分岐を足すことになる。ビルド処理なしで書き出せば、衣装の切り替えなどビルド処理の結果は失われるが、表情は元のまま全部残る。分岐を増やさずに済むので、ここではビルド処理ごと外すほうを選んだ。
- 適用量の衝突は、続ける設定では止めずに、先に出てきた経路の適用量を使う。UniVRMは同じ対象を指す重複したバインディングで先頭を使うので、それと同じ扱いになる。もともと `RemapAuthoredMorphs` は出力先ごとに最初の経路の宣言だけを残しているので、書き出す中身はそのままで、確認で止めないようにしただけになる。衝突した表情は結果一覧に「近似」の「表情」として出し、警告にも出す（`UniVrmOneClickExporter.OptimizedWeightConflictWarning`）。止める設定では元どおり例外で止まる。

## そのままにしたこと

- `PhysBoneSpringExportTests` の `InstalledAvatarOptimizerWithoutAvatarDescriptorStopsInsteadOfSavingAVrmWithoutHumanoid`: 止める設定で、ビルド処理は走ったがAAOのパスが走らなかった場合を確かめている。このときは `optimizes` が true のままなので、同じ文言で止まる。テストは変えていない。
- ビルド処理を外したやり直しの中で表情が失われる場合: ビルド処理が無ければAAOも走らないので、起きない前提のまま例外で止まる。

## 確認したこと

- 追加したテスト
  - `ExportStageGuardTests.LeavingOutTheBuildPassesWritesAnOptimizedAvatar`: AAOのTrace and Optimizeを置いたアバターで、ビルド処理を1回失敗させる。ビルド処理だけを外して書き出せることを確かめる。AAOが入っていないプロジェクトでは無視される。
  - `ExportStageGuardTests.AnExpressionLostByTheBuildPassesLeavesThemOut`: コピーにVRMの表情を1つ足し、ビルド処理の後でその経路を失われた状態にする。ビルド処理を外して書き出し、外した理由に「最適化で書き出し用の表情が失われました」が出ることを確かめる。
  - `ExportOptimizationBindingsTests.CoalescedAuthoredConflictingWeightsKeepTheFirstWhenListed`: 2つのメッシュの同名シェイプキーを1つにまとめ、違う適用量が付いた表情で、例外を出さずに先の適用量を残し、衝突した表情と経路を返すことを確かめる。
- `Tools/test-compatibility-tooling.py`（62件）、Unity 無しのハーネス（`Tools/run-ndmf-preparation-tests.ps1`・`Tools/run-compatibility-tests.ps1`・`Tools/run-behavior-tests.ps1`・`Tools/run-bake-tests.ps1`・`Tools/run-pose-contract-tests.ps1`・`Tools/run-lan-transfer-tests.ps1`）、`Tools/check-names.py` が通った。`Tools/validate.py` は、今回触ったファイルを読む検査（書き出しの呼び出し順と、各言語の訳の重複・欠け）だけを手元で確かめ、全体は CI で回す。
- Unity の EditMode でのコンパイルとテストは別に行う。
