# メッシュのアセットが無い描画対象の警告を、VRMから欠けることが伝わる文言にする

- 日付: 2026-11-16
- 対象: `Editor/ExportGimmickDetection.cs`・`Editor/ExportGimmickSession.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExportGimmickTests.cs`・`Tests/LanTransfer/TransferNotesTests.cs`・`CHANGELOG.md`

## 背景

衣装だけのプレハブのように、元のモデル（FBX）がプロジェクトに無いと、SkinnedMeshRendererの`sharedMesh`が`null`になる。このときVRMにはノードだけが入り、メッシュは入らない（顔や髪が消える）。

そのとき出ていた警告は次の1行だった。

```
自動除外せず保持: Body — メッシュまたはマテリアルが不足しているため自動判定できません。
```

`ExportGimmickDetection.Inspect`は、メッシュが無い・サブメッシュが0・マテリアルが足りないを1つの分岐で扱っていた。「自動除外せず保持」はギミックの判定で残したという記録で、転送の結果画面（`2026-10-23-transfer-done-view.md`）では「そのほか」に並び、注意の件数に数えない。見た目が欠けることが伝わらなかった。

## 決めたこと

- `Inspect`で、メッシュが`null`のときだけ別の理由にする。SkinnedMeshRendererの`sharedMesh`が無いときと、MeshRendererにMeshFilterが無いか`sharedMesh`が無いときが当たる。
- 書き出しの注意は次の形にする。

```
メッシュが見つからず省略: Body — メッシュのアセットが無いため、この部分はVRMに入りません。元のモデル（FBX）がプロジェクトにあるか確かめてください。
```

- 種類「メッシュが見つからず省略」は、転送の結果画面の決まりの「<種類>: <対象> — <理由>」の形で、記録の種類（`TransferNotes.RecordKinds`）には入れない。そのため注意として件数に数え、種類ごとに畳み、複数あれば理由を1回だけ出す。VRMから部分が欠けるので、指定どおりに行ったことの記録ではなく注意として扱う。
- 種類に「省略」を入れたので、`ExportAppearanceReport`の見た目が変わる注意（「省略」などを含む行）にも出る。`Words`は変えていない。
- 種類の名前は`ExportGimmickDetection.WarningKind`の1か所で決め、`ExportGimmickSession`はそれを使う。
- サブメッシュが0・マテリアルが足りない・シェーダーが無いときは、今の文言（「自動除外せず保持: … — メッシュまたはマテリアルが不足しているため自動判定できません。」）のままにした。範囲を広げないため。
- 4言語の訳を足した。種類と理由の2つ。
- ギミックの判定の中身（`Unit`は`Review`のまま、自動では外さない）と、書き出すVRMは変えていない。変わるのは警告の文だけ。

## 採らなかったこと

- 依頼の案「メッシュが見つからないため、この部分は VRM に入りません: Body — …」は、`: `より前が24文字を超え、`TransferNotes`が種類として読まずに1行のまま「そのほか」へ回すため採らなかった。原因と結果を短い種類（「メッシュが見つからず省略」）と理由の文に分けた。
- 「自動除外せず保持」を記録の種類から外す案は、マテリアル不足の行まで注意に数えることになり、範囲を超えるので採らなかった。

## 確認

- `ExportGimmickTests`と`ExportAppearanceReportLanguageTests`（Unity 2022.3.22f1・EditMode・batchmode）: 87件のうち60件が通り、失敗は0。27件はModular AvatarとVRChat SDKが無いプロジェクトのためのskip。
  - `SkinWithoutMeshAssetWarnsThatThePartIsLeftOut`: `sharedMesh`が`null`のSkinnedMeshRendererで新しい警告が1行だけ出て、`ExportAppearanceReport.Changes`に入る。
  - `MissingMaterialKeepsTheReviewWarning`: マテリアルが無いときは今の文言のまま。
  - `MeshRendererWithoutMeshFilterIsReviewedAsMissingMesh`: MeshFilterが無いMeshRendererは新しい理由になる（期待値を直した）。
- `TransferNotesCountAMissingMeshAsANote`（`Tools/run-lan-transfer-tests.ps1`）: 新しい行が注意として数えられ、理由が1回にまとまり、「自動除外せず保持」の行は記録に残る。214件すべて通った。
- `Tools/validate.py`（訳の欠け・置き換えの印・前後の空白）が通った。
