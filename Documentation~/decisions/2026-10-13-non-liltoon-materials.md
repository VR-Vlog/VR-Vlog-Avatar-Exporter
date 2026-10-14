# lilToon の材質が無いアバターと Missing Script を含むアバターで書き出しを止めない

- 日付: 2026-10-13
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/LilToonFullSnapshot.cs`・`Editor/LilToonGlbExtension.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`

## 背景

販売中の VRChat 用アバター 8 体で書き出しを試したところ、2018 年の SDK2 用アバター 1 体が書き出せなかった。

- 材質がすべて UnityChanToonShader 2 で、lilToon の材質が 0 個だった。通常の書き出しは「公式lilToon材質が見つかりません。」で止まった。
- 欠けたスクリプト（SDK2 の Avatar Descriptor）が 1 個あり、表情を記録する画面ではアバターを指定した時点で `NdmfExportPreparation.PreservesExportBehaviour` が NullReferenceException を出した。Unity は Missing Script を null の Behaviour として返す。

lilToon と他のシェーダーが混ざったアバターは、これまでも lilToon 以外を UniVRM の材質書き出し（MToon 以外は glTF 材質）で書き出せていた。止まるのは lilToon が 0 個のときだけだった。

## 決定

- 書き出す材質に lilToon が 1 つも無いアバターに限り、lilToon 以外で MToon10 でもない材質を、色（`_Color`／`_BaseColor`）とメイン画像（`_MainTex`／`_BaseMap`）だけの MToon にし、材質名を挙げた警告を出す。シェーダー名に Cutout か Clipping を含むものは Cutout にする。MToon10 の材質はそのまま。
- そのアバターは lilToon 拡張を付けない標準の MToon の VRM として保存する。拡張の契約（`LilToonFullContract`）は材質 0 件を許さないので、空の拡張は出さない。代わりに `asset.extras.vrvlogWithoutLilToonMaterials` に印を付け、保存前後の検査（`LilToonGlbExtension.Validate`）はこの印があるときだけ拡張の無い VRM を通す。
- lilToon が混ざったアバターの lilToon 以外の材質は、これまでどおり UniVRM の書き出しに任せる（見た目を変えない）。
- 名前が lilToon で始まるが公式 2.3.4 の一覧に無いシェーダーだけのアバターは、これまでどおり「公式lilToon材質が見つかりません。」で止める（黙って簡易な MToon にしない）。
- Missing Script は書き出し用コピー（`InstantiateOwnedCopy`）から取り除く。元のアバターは変更しない。`PreservesExportBehaviour` と表情記録の画面のループは null を飛ばす。

## 検討した代替案と不採用理由

- 空の lilToon 拡張を付ける: 契約で拒否され、アプリ側の読み込みも同じ契約に従う。
- 検査を「拡張が無くても通す」に緩める: lilToon のアバターで拡張が失われた不具合を見逃す。印の有無で区別する。
- 混在するアバターの lilToon 以外もすべて MToon にそろえる: 透過・Cutout の扱いが UniVRM の書き出しより粗くなり、これまで書き出せていたアバターの見た目が変わる。

## 影響

- lilToon が 0 個のアバターは、lilToon 拡張の無い VRM になる。lilToon 固有の表示は無く、色とメイン画像だけの MToon で表示される。
- Missing Script を含むアバターも、表情記録の画面と通常の書き出しの両方で書き出せる。
- 印 `vrvlogWithoutLilToonMaterials` を持つファイルは、手動の検証メニュー（`LilToonExportMenuValidation`）でも拡張なしで通る。この印は出力の `asset.extras` にしか書かれず、外部の VRM に付くことは想定していない。

## 未決

- アプリが lilToon 拡張の無い VRM をそのまま読めるかの実機確認。
- 透明（Transparent）系のシェーダーの扱い。今回は Opaque か Cutout だけにした。

## 出典

- `LilToonFullContract.Validate`（材質 0 件を「No full lilToon materials.」で拒否）
- UniVRM 0.131 `BuiltInVrm10MaterialExporter`（MToon 以外は glTF 材質として書き出す）
- 販売アバター 8 体での書き出し計測（Unity 2022.3.22f1・購入データはリポジトリに含めない）
