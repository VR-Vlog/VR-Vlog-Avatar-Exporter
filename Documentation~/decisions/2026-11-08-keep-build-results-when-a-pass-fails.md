# ビルド処理が1つ失敗しても、ほかの処理の結果を残して書き出す

- 日付: 2026-11-08
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/Locales`・`Tests/Editor/NdmfPreparationTests.cs`・`Tests/Editor/MaSceneReferencePreparationTests.cs`・`Tests/Editor/NdmfLostMorphFollowTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

2026-10-30-leave-out-only-failing-build-passes で、Optimizing の失敗では Optimizing だけを外すようにした。Generating と Transforming の失敗は、これまでどおり `ExportStage.BuildProcess` ごと外していた。

アプリで髪や衣装が体に付いて動かない、という報告があった。VRChat では正しく動き、NDMF の手動ベイクをしてから書き出すと直る、という内容だった。Modular Avatar の Merge Armature で付けた衣装と髪を使い、検証用の Unity 2022.3 でアプリに転送（かんたん）と同じ経路を通して書き出したところ、次の 2 つで同じ症状が出た。

- Transforming で例外を投げるテスト用のプラグインを足すと、衣装のボーン 43 本のうち 0 本しか Hips の下に入らなかった。足さないときは 43 本すべて入る。髪も 71 本中 0 本になった。
- PhysBone のコライダーにシーンの別のアバターのコライダーを入れると、書き出し用コピーの外部を指すシーン参照の検査が例外を投げ、同じく 0 本になった。

どちらも書き出し自体は成功し、結果一覧の「近似」に 1 行出るだけだった。NDMF は失敗したパスの例外を ErrorReport に記録して次のパスへ進むので、NDMF のプレイモードと手動ベイクでは、失敗したパスのほかは Merge Armature を含めて適用される。エクスポーターだけが、1 つでも失敗があるとすべてのビルド処理を外していた。手動ベイクで直るのは、Merge Armature の結果がアバター本体に焼き込まれ、あとで書き出しがビルド処理を外しても残るためだった。

VRChat のアップロードは失敗が 1 つでもあると止まるので、VRChat で動いているアバターは VRChat の文脈ではエラーが無い。エクスポーターの文脈でだけ失敗するプラグインがあるか、別のアバターを指す参照が残っていると推測している（投稿者のアバターでは確かめていない）。

## 決定

- `NdmfExportPreparation.Prepare` と `ProcessClone` に `tolerateFailedPasses` を足す。オンのときは、Generating と Transforming の後に NDMF の失敗があっても次のフェーズへ進み、ErrorReport の Error と InternalError を「プラグイン / パス: メッセージ」の形で結果一覧の注意に出す（`FailedPassesWarning`・8 件まで）。
- 次のときは、これまでどおり例外を投げる。書き出しは `BuildProcess` を外して書き出し直す。
  - ErrorReport を読めない、または失敗の記録が無いのに `Successful` が偽のとき。
  - Optimizing を始めた後で失敗が増えたとき。これは 2026-10-30 の決定どおり `Optimizing` の失敗として扱い、Optimizing を外して書き出し直す。
- 書き出しは `guard.ContinueOnFailure`、表情ボードは `ExportStageGuard.ContinueByDefault` で `tolerateFailedPasses` を決める。どちらも失敗した段階を外す設定と同じ値なので、通常とかんたんで同じに効く。厳密な設定ではこれまでどおり止まる。`interactive` は確認を出すかどうかだけを決める決まりのまま。
- `RequireCopySceneReferences` を `DetachExternalSceneReferences` に変える。書き出し用コピーのコンポーネントがコピーの外のシーンのオブジェクトを指していたら、その参照をコピーの上だけで外し、どのコンポーネントのどの項目が何を指していたかを結果一覧に出す（`ExternalReferenceWarning`）。ビルド処理がその参照をたどって別のアバターを書き換えることはない。共有のアセット（ScriptableObject など）が外部を指すときは、外すと元のアセットを書き換えるので、これまでどおり止める。
  - これは厳密な設定でも同じにする。アバターの外を指す参照はアバターと一緒に保存されず、VRChat に上げたアバターにも残らない。段階の失敗ではなく、外した場所を結果一覧に出せば足りるため。
- Modular Avatar が無いとき、別の NDMF の版のときも同じ経路を通る。ErrorReport の読み取りはリフレクションで、読めなければ従来の振る舞いに戻る。
- VRCFury の処理（`ApplyVrcFury`）の失敗はこの変更に含めない。VRCFury の途中の状態を書き出すと別の崩れ方をするおそれがあり、検証環境に VRCFury の衣装が無く確かめられないため。

## 確認

- `NdmfPreparationTests.ToleratedFailedPassLeavesOutOnlyThatPass`（Generating と Transforming のそれぞれ）: 失敗を記録したあとも Optimizing まで進み、失敗したパスの名前が警告に出る。
- `NdmfPreparationTests.FailedPassStillStopsAStrictExport`・`ToleratedPreparationStillStopsOnAFailureInOptimizing`・`ToleratedPreparationStillStopsWhenTheReportNamesNoFailure`: 厳密な設定、Optimizing での失敗、記録の無い失敗では止まる。
- `NdmfLostMorphFollowTests.APassFailingInTransformingIsLeftOutAloneByTheBoardAndTheLenientExport`: Transforming で失敗するテスト用のパスがあっても、ボードのコピーと書き出した VRM の両方で、Modular Avatar で頭に付けたメッシュが頭の下に残る。厳密な設定ではボードと書き出しの両方が止まる。テスト用のパスは Modular Avatar の後に走るので、このテストが示すのは失敗したパスより前の結果が残ることまで。失敗したパスより後のパスが走ることは、NDMF 1.14.8 の `BuildContext.RunPass` が例外を記録して再送出せず、次のパスへ進むことをソースで確かめた。
- `MaSceneReferencePreparationTests` の `...IsDetached...` の 4 件: 外部を指す参照はビルド処理の前にコピーで外れ、元のアバターと外部のオブジェクトは変わらず、外した項目が警告に出る。
- 検証用の Unity 2022.3（Modular Avatar 1.18.7・NDMF 1.14.8）で、上の 2 つの再現の組み合わせをアプリに転送（かんたん）と同じ経路で書き出し直した。衣装は 43 本中 43 本、髪は 71 本中 71 本のボーンが体の下に入り、失敗の無いときと同じだった。結果一覧には失敗したパスの名前と、外した参照の場所が出た。
