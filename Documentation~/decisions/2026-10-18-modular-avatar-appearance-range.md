# 表示の固定に対応する Modular Avatar を 1.18.0 以上 1.19.0 未満と明示する

- 日付: 2026-10-18
- 対象: `Compatibility/dependencies.json`・`Tools/generate-compatibility.py`・`Editor/Compatibility/DependencyPolicy*.cs`・`Editor/MaAppearanceSnapshot.cs`・`Editor/Locales/*.json`・`Tools/DependencyCompatibilityTests.cs`・README 3 種・`Compatibility/README.md`・`CHANGELOG.md`

## 背景

`MaAppearanceSnapshot` は、アバターに Modular Avatar（MA）の Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutter があるとき、MA のプレビュー評価をリフレクションで呼んで、書き出し用の複製に現在の表示を固定する。対応する MA の版はどこにも書いておらず、リフレクションで型やメンバーが見つからなかったときに初めて「Modular Avatar の現在の表示を固定できませんでした」で止まっていた。止まりはするが、どの版なら通るのかは利用者に分からなかった。

## 決定

- `dependencies.json` の `authoring` に `modularAvatarMinimum: "1.18.0"`・`modularAvatarBelow: "1.19.0"` を足し、生成した `DependencyPolicy.Generated.cs` の定数で判定する。生成スクリプトは、確認済みの `modularAvatarReference`（1.18.7）がこの範囲に入っていることも確かめる。
- 判定は `DependencyPolicy.SupportsModularAvatarAppearance` に置き、`X.Y.Z` の数字だけの正式版で、1.18.0 以上 1.19.0 未満のときに通す。プレリリースとビルドメタデータ付きは止める。
- 固定の対象になるコンポーネントがあるときだけ、MA のコンポーネントの型が入っているアセンブリから `PackageInfo.FindForAssembly` でパッケージを引き、`nadena.dev.modular-avatar` の版を読む。範囲外なら、リフレクションに入る前に、入っている版・対応範囲・直し方（VCC／ALCOM で 1.18.x に変更）・アバターは変更していないこと・これらのコンポーネントがなければどの版でも書き出せることを表示して止める。文言は `DependencyPolicy.ModularAvatarAppearanceError` に置き、ほかの Exporter のメッセージと同じく `Editor/Locales/*.json` で翻訳する。
- 固定の対象になるコンポーネントがないアバターは、これまでと同じく MA の版を見ない。
- NDMF の下限 `ndmfMinimum` は 1.8.3 のまま変えない。

## 確認したこと

- MA の公開ソースを 1.10.0〜1.18.7 の全正式版と 1.19.0-alpha.0・alpha.1・alpha.2 で読んだ。固定で使う `editor.IMeshSelector`・`RemoveVerticesFromMesh.FilterPrimitivesOnly(Renderer, Mesh, IEnumerable<IMeshSelector>)`・`vertex_filters.IMeshSelectorBehavior` は 1.18.0 から 1.19.0-alpha.2 まである。1.17.1 以前は代わりに `IVertexFilter` を使っている。
- `MaAppearanceSnapshot` は固定の対象があると必ず `IMeshSelector` を引くので、1.17.x 以前では固定できない。
- 1.19.0-alpha.1 と alpha.2 では `AnalysisResult` が `Shapes` と `InitialActions` に変わり、`InitialStates` と `TargetProp` がなくなった。固定はこれらを読むので動かない。上限を 1.19.0 未満にした。
- Unity の実プロジェクトで MA 1.13.4 を入れると、これまでも固定の時点でメッセージを出して止まっていた（対応範囲は出ていなかった）。
- MA の VPM リストでは、1.18.0 が NDMF 1.11.0 以上、1.18.1〜1.18.3 が 1.14.3 以上、1.18.4〜1.18.7 が 1.14.7 以上を要求する。MA 1.18.x を入れたプロジェクトでは、NDMF は VCC／ALCOM がこの条件で揃える。
- NDMF の下限 1.8.3 は、MA を使わない NDMF だけの機能のための値で、Exporter が呼ぶ NDMF の API は 1.8.0〜1.15.0-beta.2 で同じだった。1.8.0 より前は `INDMFPlatformProvider` などがないので、下限は下げられない。
- 実プロジェクトでの結果は Compatibility/Evidence/ModularAvatarRange/ に追記する。

## 検討した代替案と不採用理由

- これまでどおり、型やメンバーが見つからないときだけ止める: 止まりはするが、どの版に変えればよいかを利用者が知る手段がない。
- 1.17.x 以前にも対応する（`IVertexFilter` を使う経路を足す）: 版ごとに別の経路を持つことになり、確かめる組み合わせが増える。1.18.x へ更新すれば通るので、範囲の明示で足りる。
- 1.19.0 のプレリリースも受け付ける: alpha.1 以降は解析結果の形が変わり、固定が動かない。正式版が出たら型を確かめてから `modularAvatarBelow` を上げる。
- MA の版にかかわらずアバター全体で止める: 固定の対象がないアバターは MA の版に関係なく書き出せていたので、止める理由がない。

## 影響

- MA 1.18.x で、固定の対象になるコンポーネントを使うアバターの動きは変わらない。
- MA 1.17.x 以前と 1.19 以降・プレリリースで、固定の対象になるコンポーネントを使うアバターは、これまでの「固定できませんでした」の代わりに、対応範囲と直し方を示して止まる。止まる位置は固定の処理の入口で、原本のアバターは変わらない。
- 固定の対象になるコンポーネントがないアバターは、これまでと同じくどの MA の版でも書き出せる。
- MA 1.19.0 の正式版が出たら、固定で使う型とメンバーを確かめてから `modularAvatarBelow` を上げる。
