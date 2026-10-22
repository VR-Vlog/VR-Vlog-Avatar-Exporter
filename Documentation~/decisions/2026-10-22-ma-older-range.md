# 表示を固定できる Modular Avatar を 1.12.2 以上に広げる

- 日付: 2026-10-22
- 対象: `Editor/MaAppearanceSnapshot.cs`・`Compatibility/dependencies.json`・`Editor/Compatibility/DependencyPolicy.Generated.cs`・`Tools/DependencyCompatibilityTests.cs`・`Tests/Editor/MaDeletionExportTests.cs`・`Tests/Editor/AppearancePreparationTests.cs`・`Tests/Editor/MaReactiveExpressionExportTests.cs`

## 背景

表示の固定（`MaAppearanceSnapshot`）は、1.18.0 以上 1.19.0 未満の正式版だけを通し（プレリリースは版を読めないものとして API を試す。2026-10-21-ma-unknown-version）、それより前の MA では書き出しの前に止まっていた（2026-10-18-modular-avatar-appearance-range）。そのときは、1.17.x 以前は削除の型が `IMeshSelector` ではなく `IVertexFilter` なので、範囲の明示で足りるとしていた。

MA の公開ソースを 1.10.0〜1.18.7 の全正式版で読み直した結果は次のとおり。

- 固定が使う型・メンバー（`ReactiveObjectAnalyzer` のコンストラクタ・`Analyze`・`ForcePropertyOverrides`・`ForceMenuItems`・`GetGameObjectStateProperty`、`AnalysisResult.InitialStates`・`Shapes`、`TargetProp`、`AnimatedProperty.actionGroups`、`ReactionRule.ControllingObject`・`ControllingConditions`、`ControlCondition` の 3 フィールド、`ROSimulator` の 2 つの上書き、`ParameterAssignerPass.AssignMenuItemParameter`、`ModularAvatarShapeChanger.Shapes`）とプロパティ名（`m_IsActive`・`blendShape.<名前>`・`m_Materials.Array.data[i]`・`deletedShape.<名前>`）は、1.10.0 から 1.18.7 まで同じ。`ReactiveObjectAnalyzer.OptimizeShapes` は 1.10.4 から。
- 版で違うのは、Shape Changer の Delete と Mesh Cutter でメッシュを削るところだけ。
  - 1.18.0 以降: 削除の値は `IMeshSelector`。MA は `RemoveVerticesFromMesh.FilterPrimitivesOnly` で削る。
  - 1.14.0〜1.17.1: 削除の値は `IVertexFilter`。MA は `RemoveVerticesFromMesh.RemoveVertices(Renderer, Mesh, IEnumerable<(TargetProp, IVertexFilter)>)` で削る。Mesh Cutter のフィルタのコンポーネントは `IVertexFilterBehavior` を持つ（1.18.0 で `IMeshSelectorBehavior` に改名）。
  - 1.13.0〜1.13.4: Mesh Cutter は無い。`deletedShape.<名前>` の値は、削除するときはしきい値の float、しないときは null。MA は `RemoveBlendShapeFromMesh.RemoveBlendshapes(Mesh, IEnumerable<(int, float)>)` で削る。
  - 1.10.2〜1.12.5: `deletedShape.<名前>` の値は 1（削除）か 0。MA は `RemoveBlendshapes(Mesh, IEnumerable<int>)` で削り、自分のビルドでは 0.5 を超える値を削除として扱う。
- Material Swap は 1.13.0、Mesh Cutter は 1.14.0 からある。それより前の版には、そのコンポーネント自体が無い。

## 決定

- `modularAvatarMinimum` を 1.12.2 にする。上限（1.19.0 未満）とプレリリースの扱いは変えない。
- 版ごとの分岐は、メッシュを削るところだけに置く。分岐は版の番号ではなく、MA にどの型・メソッドがあるかで決める。
  - 削除の型は `IMeshSelector` があればそれ、無ければ `IVertexFilter` を使う。どちらかがあれば、これまでどおり SkinnedMeshRenderer ごとに値を集め、`FilterPrimitivesOnly` があればそれで、無ければ `RemoveVertices` で削る。`RemoveVertices` には `TargetProp` を既定値にした組を渡す（MA は組のフィルタしか読まない）。
  - どちらの型も無いとき（1.13.x 以前）は、`deletedShape.<名前>` の float を集めて `RemoveBlendshapes` で削る。引数の要素が `(int, float)` なら（BlendShape の番号, 値）を、`int` なら値が 0.5 を超えるものの番号を渡す。
  - Mesh Cutter を外すときは、`IMeshSelectorBehavior` と `IVertexFilterBehavior` のどちらを持つフィルタも一緒に外す。
- 1.18.x の動きは変えない。どちらの型も無いのに Mesh Cutter がある、`RemoveBlendshapes` が無いなど、必要なものが見つからないときは、これまでと同じく例外にして「固定できませんでした」の文で止める。
- 止めるときの文の「VCC／ALCOMでModular Avatarを1.18.xに変更してください」は変えない。1.18.x は今も対応範囲の中にある。
- NDMF の下限（`ndmfMinimum` 1.8.3）はこの決定では変えない。

## 理由

- 削る方法を MA が自分のビルドで使うのと同じにすれば、固定した結果は、その版の MA でビルドしたときと同じ形のメッシュになる。版の番号で分けると、確かめていない版を番号だけで通すことになるが、型の有無で分ければ、API が変わった版では型が見つからずに止まる。
- 下限は、Unity 2022.3 の実プロジェクトで固定・削除・メニューの表情の取り込みがすべて通る最も古い版にした。
  - 1.10.4〜1.11.7 は固定そのものは動くが、MA 自身のビルドが作る FX から、Shape Changer をメニューで切り替える表情を取り込めない（`MenuReactiveShapeSurvivesCanonicalBuildAndVrmReload` の 6 件。1.10.4・1.11.2・1.11.7 で同じ）。Int と Float のメニューでは、その表情が BlendShape 以外に GameObject の表示（`m_IsActive`）も切り替えるものとして作られ、Bool のメニューでは、MA が合成した FX の遷移の条件の型が合わず Unity がエラーを出す。どちらも MA が作る FX の中身なので、取り込みの側で合わせると 1.12 以降の版での取り込みまで変わる。
  - 1.12.0 と 1.12.1 は、このエクスポーターの NDMF の下限 1.8.3 と組み合わせると MA 自身がコンパイルできない（`IVirtualizeMotion` が見つからない）。VPM の依存範囲（NDMF 1.7.0 以上・1.7.1 以上）では 1.8.3 も選べるが、組み合わせて動かない。
  - 1.12.2 は NDMF 1.8.3 でコンパイルでき、固定・削除・メニューの表情の取り込みがすべて通る。1.12.3・1.12.4・1.12.5 も同じ。
  - 1.10.0〜1.10.1 は削除を別の形（`AnalysisResult.DeletedShapes` と `IsDelete` のルール）で返し、1.10.2〜1.10.3 は `OptimizeShapes` が無いので、どのみち別の経路が要る。どれも 1.12.2 以上へ更新すれば通る。
- 1.13.x 以前で Delete を固定せずに MA のビルドに任せる案もあった。ただ、固定したコンポーネントは外すので、任せるには Delete を含む Shape Changer だけを残す別の扱いが要る。`RemoveBlendshapes` を呼ぶ分岐の方が短く、ほかの版と同じく固定の時点で削れる。

## 検証

- `Tools/run-compatibility-tests.ps1`: 1.12.2〜1.12.5・1.13.4・1.14.0・1.17.1 などが通り、1.12.1・1.12.0・1.11.7・1.11.2・1.10.4・1.9.16 は版を名指しして止まることを確かめた。止めるときの文に新しい範囲（1.12.2 以上 1.19.0 未満）が入ることも確かめた。
- `Tests/Editor/MaDeletionExportTests` は、Mesh Cutter の無い版（1.13.x 以前）では Mesh Cutter のケースだけを飛ばし、Shape Changer の Delete のケースは流す。1.14.0 より前の Delete のプレビューは `ShapeChangerPreview` にあるので、プレビューの設定はそちらを切り替える。
- テストでメニューを作るところは、1.13.0 より前の `ModularAvatarMenuItem.Control`（VRChat SDK のコントロール）にも値を入れる。
- Unity 2022.3 の実プロジェクトでの結果は `Compatibility/Evidence/ModularAvatarRange` に書く。
