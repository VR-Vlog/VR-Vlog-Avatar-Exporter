# 標準の表情の枠でも、元のメッシュに無いBlendShapeへの参照を省く

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Tests/Editor/ExpressionPresetTests.cs`・`CHANGELOG.md`（#275 の項目に追記）・`Documentation~/decisions/2026-10-21-board-absent-blendshape-refs.md`

## 背景

`2026-10-21-board-absent-blendshape-refs.md` で、表情ボードの判定と記録は元のメッシュに無いBlendShapeへの参照を省くようにした。これでその参照を含む表情もボードの喜怒哀楽などの枠に置けるようになったが、枠の表情をVRMの標準表情に書く `PresetMorphs` は参照を省かずに全部渡していた。VRMの標準表情は元のメッシュに束ねる（`AuthorPresets`）ので、無いBlendShapeで「元のメッシュにBlendShapeがありません」の例外になり、保存ごと失敗した。

同じ20体のアバターで表情ボードの件数とVRMの件数を比べ直したとき、前の回は保存できていた2体がこれで保存できなくなっていた。

## 決定

- `PresetMorphs` は、書き出しの前の処理より前のメッシュ（`NdmfExportPreparation.HadShape`）にも表示中の書き出し用メッシュにも無いBlendShapeへの参照を省いて返す。ボードの判定（`BuildClipPoseUnchecked`）が省くものと同じ条件にした。
- 処理で消えたBlendShape（元のメッシュにはあった）は省かない。これはその前の `BuildClipPoseUnchecked` の検査で対応外になり、ここまで来ない。
- 書き出しの前の処理で足されたBlendShape（表示中のメッシュにだけある）も省かない。ボードの枠ではその形が動くので、黙って省くと保存したVRMの標準表情とプレビューが食い違う。これまでどおり `AuthorPresets` が理由付きで止める。
- 参照が全部無いBlendShapeの表情は、ボードの判定と同じく「有効な顔のBlendShapeがない」として枠に置けない。

## 採らなかったもの

- `AuthorPresets` で無い参照を読み飛ばすこと。束ねる先で黙って捨てると、処理で消えた参照のような本当の不整合まで見えなくなる。省く判断はボードの判定と同じ場所の規則に揃えた。

## 確認したこと

- EditMode: `PresetFromAClipWithShapesTheAvatarNeverHadBindsOnlyTheRest`（無い参照を含む表情を標準の枠に置いて保存でき、VRMには有る参照だけが入る。無い参照だけの表情は置けない）。処理で消えた参照は #275 の `ShapesLostByPreparationStayUnsupportedOnTheBoard` が確かめる。
- マージ後に同じ20体で保存できるかと件数を確かめる。
