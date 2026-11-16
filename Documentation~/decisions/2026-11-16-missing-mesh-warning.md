# メッシュのアセットが無い描画対象を、VRMから欠けることが伝わる注意にする

- 日付: 2026-11-16
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportGimmickDetection.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExportGimmickTests.cs`・`Tests/Editor/RendererSelectionTests.cs`・`Tests/LanTransfer/TransferNotesTests.cs`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`・`CHANGELOG.md`

## 背景

衣装だけのプレハブのように、元のモデル（FBX）がプロジェクトに無いと、SkinnedMeshRendererの`sharedMesh`が`null`になる。このときVRMにはノードだけが入り、メッシュは入らない（顔や髪が消える）。

そのとき書き出しの注意には次の2行が出ていた。

```
自動除外せず保持: Body — メッシュまたはマテリアルが不足しているため自動判定できません。
メッシュが空のRendererを書き出しから外しました: Body
```

1行目は`ExportGimmickDetection.Inspect`が、メッシュが無い・サブメッシュが0・マテリアルが足りないを1つの分岐で扱って出していた（自動除外がオンのときだけ）。「自動除外せず保持」はギミックの判定で残したという記録で、転送の結果画面（`2026-10-23-transfer-done-view.md`）では「そのほか」に並び、注意の件数に数えない。2行目は`DisableEmptyMeshRenderers`（`2026-10-18-skip-null-mesh-renderers.md`）が、UniVRMのコンソールの警告を抑えるために出す1行で、`<種類>: <対象> — <理由>`の形ではないので、これも件数に数えない。どちらからも、VRMにその部分が入らないことと、どこを確かめればよいかが伝わらなかった。

## 決めたこと

- メッシュのアセットが無い描画対象の注意は、`DisableEmptyMeshRenderers`で出す。SkinnedMeshRendererの`sharedMesh`が無いときと、MeshRendererにMeshFilterが無いか`sharedMesh`が無いときが当たる。部分ごとに次の1行にする。

```
メッシュが見つからず省略: Body — メッシュのアセットが無いため、この部分はVRMに入りません。元のモデル（FBX）がプロジェクトにあるか確かめてください。
```

- `DisableEmptyMeshRenderers`は、除外・NDMF・最適化ツール・マテリアルの置き換えがすべて終わった書き出し用の複製で動く。ビルドの処理（メッシュの結合など）がメッシュを入れた描画対象には出さず、最後までメッシュが無く本当にVRMから欠けるものだけに出る。自動除外のオン・オフにも左右されない。
- メッシュはあるが頂点や面が0のものは、これまでどおり「メッシュが空のRendererを書き出しから外しました: 」の1行にまとめる。見た目の部分が欠けたとは限らないため。
- `Inspect`は、メッシュが`null`の描画対象を判定の対象にしない（`null`を返す）。判定に使う形が無く、ビルドの処理でメッシュが入ることもあるため。これでギミックの注意（「自動除外せず保持」）と、復元の案内（`ExportRecovery`）の対応していないシェーダーとしての案内にも出なくなる。
- サブメッシュが0・マテリアルが足りない・シェーダーが無いときは、今の文言（「自動除外せず保持: … — メッシュまたはマテリアルが不足しているため自動判定できません。」）のままにした。範囲を広げないため。
- 種類「メッシュが見つからず省略」は記録の種類（`TransferNotes.RecordKinds`）に入れない。そのため転送の結果画面では注意として件数に数え、種類ごとに畳み、複数あれば理由を1回だけ出す。VRMから部分が欠けるので、指定どおりに行ったことの記録ではなく注意として扱う。
- 種類に「省略」を入れたので、`ExportAppearanceReport`の見た目が変わる注意（「省略」などを含む行）にも出る。`Words`は変えていない。
- 文はギミックの注意と同じく日本語のまま警告に入れ、転送の結果画面が種類を訳す。4言語の訳を種類と理由の2つ足した。
- 書き出すVRMは変えていない。変わるのは注意の文だけ。

## 採らなかったこと

- `Inspect`の分岐を分けてギミックの注意の種類を変える（最初の案）: ギミックの判定はビルドの前の複製で動くので、ビルドの処理であとからメッシュが入る描画対象にも「VRMに入りません」と出てしまう。自動除外がオフのときは出ず、`DisableEmptyMeshRenderers`の行と2行になる問題も残る。
- 依頼の案「メッシュが見つからないため、この部分は VRM に入りません: Body — …」: `: `より前が24文字を超え、`TransferNotes`が種類として読まずに1行のまま「そのほか」へ回すため採らなかった。原因と結果を短い種類（「メッシュが見つからず省略」）と理由の文に分けた。
- 「自動除外せず保持」を記録の種類から外す: マテリアル不足の行まで注意に数えることになり、範囲を超える。

## 確認

- Unity 2022.3.22f1・EditMode・batchmode・lilToon 2.3.4を入れたプロジェクトで、パッケージのEditModeテストを全件回した。4563件のうち3218件が通り、失敗は0。1345件はVRChat SDKとModular Avatarが無いプロジェクトのためのskip。
- 新しいテスト:
  - `RendererSelectionTests.AMissingMeshIsANotePerPartAndAnEmptyMeshKeepsTheOneLine`: メッシュが`null`の2つは部分ごとに新しい注意、頂点の無いメッシュは今の1行。
  - `RendererSelectionTests.EmptyMeshRenderersAreLeftOutOfTheExportCopyOnly`: 期待値を直した。メッシュの無いSkinnedMeshRendererとMeshRendererが、それぞれ新しい注意になる。
  - `ExportGimmickTests.ExportWarnsWhenAPartHasNoMeshAsset`: 書き出しの入口から、自動除外のオン・オフのどちらでも、その部分の行が新しい注意の1行だけになる。
  - `ExportGimmickTests.SkinWithoutMeshAssetIsANoteThatThePartIsLeftOut`・`MeshRendererWithoutMeshFilterIsLeftToTheMissingMeshNote`: ギミックの判定はメッシュの無い描画対象を見ない。
  - `ExportGimmickTests.MissingMaterialKeepsTheReviewWarning`: マテリアルが無いときは今の文言のまま。
- `TransferNotesTests.TransferNotesCountAMissingMeshAsANote`（`Tools/run-lan-transfer-tests.ps1`）: 新しい行が注意として数えられ、理由が1回にまとまり、「自動除外せず保持」の行は記録に残る。
- `Tools/validate.py`と`Tools/test-compatibility-tooling.py`が通った。`RendererSelectionTests`は互換の表（`compatibility_counts`）に載っているので、ケース数を4から5に、`test-compatibility-tooling.py`の合計を36から37に直した。
