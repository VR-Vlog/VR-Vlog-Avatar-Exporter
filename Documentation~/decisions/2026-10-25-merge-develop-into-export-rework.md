# developを統合ブランチ feature/export-rework-1018 へ取り込む

- 日付: 2026-10-25
- 対象: 統合ブランチ `feature/export-rework-1018`（develop の 7a5f2d48 まで）、`CHANGELOG.md`

## 背景

Delete Meshのアウトライン幅マスクを縮小しない修正（#498）は統合ブランチへ、口のシェイプキーの初期値を引く修正（#502）はdevelopへ入った。どちらのブランチにも両方の修正が揃っていなかったので、developで書き出したアバターでは口の形は直っていたが、エディタでは消えるアウトラインがアプリで顎の内側の黒い線として残った。

## 決定

- developを統合ブランチへマージする（`--no-ff`）。統合ブランチはdevelopへ入れない（入れる時期は別に決める）。
- 両方で変わったファイルは `CHANGELOG.md` の「未リリース」の先頭だけだった。両方の項目を残し、統合ブランチの項目（#498）を先、developの項目（#502・#499〜#501）を後に並べた。
- コードの衝突は無かった。#498 は `Editor/LilToonFullSnapshot.cs` の画像の大きさ、#502 は `Editor/AvatarBaseShape.cs` などの口のシェイプキーを変えていて、同じファイルを触っていない。

## 確認

- EditModeテストで #498 の `FullTextureResizeTests` と #502 の口の組のテスト（`BaseShapeTests`・`DescriptorVisemeExportTests`）がマージ後に通ること。
- マージ後の版で書き出したアバターをアプリで表示し、顎の内側の線が出ないこと、無表情と「あ」の口が元のアバターと同じであること。
