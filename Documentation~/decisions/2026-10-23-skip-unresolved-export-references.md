# VRMの中で解決できない参照があっても書き出しを止めない

- 日付: 2026-10-23
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportOptimizationBindings.cs`・`Editor/VrmMeshAttributePreparation.cs`

## 背景

VRChat SDK でアップロードできるアバターの構成から書き出しの経路を順に追い、段のガード（`ExportStageGuard`）の外で例外になる箇所を探した。次の 3 つは、書き出し全体が止まっていた。

- スキンメッシュのボーンや Root Bone がアバターの外（シーンの別のオブジェクト）を指している。`Instantiate` で作る書き出し用のコピーはアバターの外への参照をそのまま持ち、UniVRM 0.131 の `ModelExporter.CreateSkin` はボーンを書き出すノードの辞書から引くので `KeyNotFoundException` になる。`Vrm10AppearanceExporter.Export` はガードの外にある。
- Mesh Renderer の追加頂点ストリーム（`additionalVertexStreams`）の頂点数が元のメッシュと違う。`VrmMeshAttributePreparation.Prepare` が例外を投げていた。
- フェイストラッキング（Unified Expressions・追跡プロファイル）の表情が、アバターのルートにあるメッシュを使っている。UniVRM はルートをノードに含めないので出力先のノードが無く、`ExportOptimizationBindings.BindNodes` が例外を投げていた。`afterExport` の中のこの呼び出しはガードの外にある。

ほかに調べた箇所（コンストレイントの参照先、揺れ物のジョイント、VRVLOG_ 拡張の版、`asset.extras` の形、モーフの法線と頂点カラーの補正、瞬きの出力先）は、前の段で外されるか、ガードの中で段ごと外れて書き出しが続くので変えなかった。法線のないメッシュで面が float の範囲を超えるほど大きいと法線を作れず止まるが、Unity 2022.3 はそうしたメッシュを範囲が異常なメッシュとして扱い、複製の頂点数も合わなくなる。VRChat 向けのアバターで起きる構成ではないので変えなかった。

## 決定

- アバターの外を指すボーンと Root Bone は、書き出し用コピーの上でだけ `null` にしてから UniVRM に渡す（`UniVrmOneClickExporter.DropOutsideBones`）。UniVRM は見つからないボーンをノード 0 として書き、見つからない Root は省くので、そのメッシュは書き出されるが、外のボーンに乗っていた頂点は正しく動かない。外したメッシュのパスを警告に出す。外のボーンが無いアバターでは何も変えないので、今書き出せているアバターの出力は変わらない。
- 頂点数の合わない追加頂点ストリームは使わず、元のメッシュだけを書き出して警告を出す。
- `BindNodes` は出力先のノードが無いメッシュを飛ばす。そのメッシュを使う表情の出力はガードの中の `ApplyTracking`・`ApplyUnified` で「追跡表情の対象が最終VRMにありません」として失敗し、ほかの段と同じくフェイストラッキングの段だけが外れて結果の一覧に出る。段の失敗で止める設定のときは今までどおり止まる。
- 警告の文言は定数にして `ExporterLocalization.T` を通し、4 言語の翻訳を足した。UniVRM の vendored コードは変えていない。かんたん転送も同じ `ExportCore` を通るので、通常の書き出しとの一致は変わらない。

## テスト

- `AttachmentConnectionTests.OneClickExportClearsCopiedReferencesToBonesOutsideTheAvatar`: ボーンと Root Bone がシーンの別オブジェクトを指すアバターを通常の入口から書き出し、止まらずに警告が 1 件出て、元のアバターの参照は変わらないこと。
- `MissingMeshAttributesTests.MismatchedAdditionalVertexStreamsAreLeftOutWithAWarning`: 頂点数の合わない追加頂点ストリームを付けても書き出せ、警告が出て、出力が元のメッシュの値になること。
- `ExportOptimizationBindingsTests.TrackedRendererWithoutAnOutputNodeFailsInTheTrackingOutputStage`: 出力先のノードが無くても `BindNodes` は止まらず、出力の段で決まった文言の例外になること。

## 欠点

- アバターの外のボーンに乗っていた頂点はノード 0 に乗る形で書き出されるので、そのメッシュは VRM の中で正しく動かない。警告でメッシュの場所を伝えるだけで、直すのは利用者になる。
- 合わない追加頂点ストリームで上書きしていた法線や UV は反映されない。
- ルートのメッシュを使うフェイストラッキングの表情は、1 つでもあるとフェイストラッキングの段全体が外れる。表情ごとに外す形にはしていない。
