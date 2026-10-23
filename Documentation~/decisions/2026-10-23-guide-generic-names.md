# 導入ガイドのページと画像からアバターの名前を外す

- 日付: 2026-10-23
- 対象: `Website/index.html`（フッターの `aria-label` を含む）・`Website/styles.css`（`.qrs-hierarchy img`・`.qrs-avatar-credit`）・`Website/assets/guide/unity-avatar-menu.png`・`Website/assets/guide/unity-qr-transfer.png`・`Website/assets/guide/alcom-install.webp`・`Website/assets/guide/vcc-projects.webp`・`Website/assets/guide/README.md`

## 背景

QR で転送する導入ガイドのページに、公開リポジトリに書かない決まりのアバターの名前と作者名が入っていた。画像の代替テキスト、フッターの掲載アバターの表記、画像の出典メモ、2 枚の Unity の画面（ウィンドウのタイトル・Hierarchy・Inspector・転送画面の入力欄）に写っていた。導入手順の ALCOM と VCC の画面には、アバターの愛称から付けたプロジェクト名と、Windows のユーザー名を含むパスも写っていた。手順 1 の画像はページでは Hierarchy の一部だけを見せていたが、ファイルにはアバター本体が写った画面全体が入っていた。

## 決定

- 代替テキストの名前を外し、「UnityのHierarchyでアバターを選び、右クリックした画面」とした。
- 手順 1 の画像は、ページが見せていた範囲（元画像の x 0〜380・y 75〜275）だけに切り出し、シーン名とオブジェクト名を `SampleScene*`・`Avatar` に描き直した。表示の位置合わせ（幅 505% と上へのずらし）は不要になったので、画像をそのまま枠いっぱいに出す。
- 手順 2 の画像は、ウィンドウのタイトル・アバター欄・名前欄を `Avatar` に、作者名欄を `作者A` に描き直した。ほかは変えていない。
- ALCOM の画面のプロジェクト名と保存場所を `Project` に、VCC の一覧のプロジェクト名を `Project A`〜`Project G` に、パスのユーザー名を `user` に描き直した。VCC の画像の代替テキストに残っていたプロジェクト名も外し、「起動中のVCCのProjects画面。プロジェクトの一覧から使うプロジェクトを探す」とした（main への hotfix で追加）。
- アバター本体がどの画像にも写らなくなったので、フッターの掲載アバターの表記と `Website/assets/guide/README.md` の出典メモの行を消した。フッターのリンク群の `aria-label` も「ライセンス」にした。`Website/assets/README.md`（ほかの画像の出典）は対象外で変えていない。

## 確認

- `Tools/check-names.py` で、追跡しているファイルに当たる名前がないことを確かめた。
- ページを幅 1280px と 390px で表示し、手順 1・2 の画像が前と同じ位置・大きさで出ること、フッターが崩れないことを見た。

## 欠点

- リポジトリの履歴と、公開済みのサイトには元の表記と画像が残る。公開サイトは次に main から出したときに置き換わる。
