# 取り付け先が見つからない Bone Proxy / Merge Armature は警告にして書き出す

- 日付: 2026-10-24
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/Locales/*.json`・`Tests/Editor/NdmfPreparationTests.cs`・`Tests/Editor/MaSceneReferencePreparationTests.cs`

## 背景

Modular Avatar の Bone Proxy や Merge Armature の取り付け先が見つからないオブジェクトを 1 つでも含むと、書き出し全体が「Modular Avatar の追従先を取得できません」で止まっていた。小物の取り付け先が別のアバターのボーン名を前提にしているときなどに起きる。

Modular Avatar 1.18 は同じ状態を NonFatal のエラーとして記録し、そのオブジェクトを取り付けずに元の位置のまま残してビルドを続ける。書き出しだけが止まるので、VRChat 向けには使えるアバターが VRM にできなかった。

## 決定

- NDMF の Resolving と Modular Avatar の参照の解決をすべて通したあとの複製で、まだ取り付け先が無いものは、オブジェクトごとに 1 件の警告にして書き出しを続ける。オブジェクトは Modular Avatar が残した位置のまま書き出す。
- 警告は「（パス）: 取り付け先のボーンが見つからないため体の動きについてきません」と「Modular Avatar の Bone Proxy（または Merge Armature）で取り付け先を設定して書き出し直す」の 2 行で、影響と直し方を分けて書く。
- 次のものは今までどおり書き出しを止める。
  - 元のアバターに対する事前の検査（`ValidateSource` を警告の受け皿なしで呼ぶもの）
  - 取り付け先が選んだアバターの外にあるもの
  - 解決前に読めない古いパスが残っているもの

## 制約

- 警告の受け皿を渡さない呼び出し（ポーズ確認の画面）では、警告を表示せずにそのまま続ける。そこで止めても直し方が分からないうえ、書き出しのときに同じ警告が出る。
- 取り付けられなかったオブジェクトは体の動きについてこない。直すのは使う人で、書き出しは推測で取り付け先を選ばない。

## 検証

- EditMode: `NdmfPreparationTests.TargetStillMissingAfterResolutionWarnsAndExportsTheObjectWhereMaLeavesIt`（Bone Proxy と Merge Armature の 2 件）。取り付け先の無いものを複製に置いて Prepare を通し、例外にならないこと、オブジェクトが元の親の下に残ること、Transforming まで進むこと、警告が 1 件出ることを確かめる。元のアバターへの事前の検査は今までどおり例外になることも同じテストで見る。
- `MaSceneReferencePreparationTests.MissingDirectTargetAndMissingPathWarnWithoutEditingTheSource` を、例外ではなく警告と、マージされずに残ることを確かめる形に改めた。
- Modular Avatar 1.18.7・NDMF 1.14.8 を入れたプロジェクトで `NdmfPreparationTests` と `MaSceneReferencePreparationTests` を回した。
