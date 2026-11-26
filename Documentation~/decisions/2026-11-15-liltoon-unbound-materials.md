# 差し替え先のマテリアルを`materials`の末尾に足し、lilToonの記録を`VRVLOG_materials_liltoon_unbound`に書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/LilToonFullSnapshot.cs`、`Editor/UniVrmOneClickExporter.cs`、`Editor/Vrm10AppearanceExporter.cs`、`Formats/Runtime/LilToonFullContract.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Schema/VRVLOG_materials_liltoon_unbound.1.schema.json`、`Tools/compare-exports.py`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`、`Tools/test-compare-exports.py`

## 背景

4-5（判断記録 2026-11-15-avatar-animator-material-slots）は、表情のアニメーションがマテリアルのスロットを差し替える曲線を`materialSlot`に書いた。ただし差し替え先が書き出しのどのメッシュも使わないマテリアルのときは、今のアプリの`VRVLOG_materials_liltoon`の読み手（`LilToonFullContract.Validate`）がどのプリミティブにも結ばれない記録を拒むため、足さずに`material`に数えていた。Epicはその置き場所として案A（古い読み手が見ない別の拡張に記録を置く）を選んだ。この単位（4-5b）はそれを入れる。

設計書は記録の置き場所を`VRVLOG_liltoon_full`（実際の名前は`VRVLOG_materials_liltoon`）と書いているが、Epicの決定（案A）で意図して上書きした。

## 決めたこと

- 差し替え先にするマテリアルのうち、書き出すレンダラー（`ExportRendererSelection.ExportedMeshRenderers`、lilToonの記録と同じ範囲）のどのスロットも曲線を読んだ時点で持っておらず、公式のlilToonのものを、曲線の順に1つずつ`materials`の末尾に足す。同じマテリアルを差し替える曲線がいくつあっても1つだけ足す。加算のクリップの曲線と、lilToon以外のマテリアルを含む曲線のマテリアルは足さない
- 足したマテリアルは、ほかのlilToonのマテリアルと同じく標準のMToonに置き換えて書く（どのプリミティブも使わない）。置き換えに失敗したら色とメイン画像だけのMToonにして警告を出す
- `materialSlot`のキーはその足したマテリアルの番号を指す。既に書いているマテリアルへの差し替えは今までどおりで、何も足さない
- lilToonの記録は新しい拡張`VRVLOG_materials_liltoon_unbound`（`{schemaVersion: 1, materials: [...]}`）に書く。記録の形は`VRVLOG_materials_liltoon`の`materials`の要素と同じで、`materialIndex`は足したマテリアルの番号、`textures`は`VRVLOG_materials_liltoon`の`textures`の番号。`VRVLOG_materials_liltoon`には書かないので、今のアプリはこの拡張を読まずに今までどおり`VRVLOG_materials_liltoon`を読む（`AnUnboundSwapIsAddedAtTheEndWithItsRecordApart`で`Validate`が通ることを確かめた）
- 名前は`VRVLOG_materials_liltoon`の記録の続きだと分かるよう、その後ろに`_unbound`（プリミティブに結ばれない）を付けた
- `requiredAppVersions`に`VRVLOG_materials_liltoon_unbound`を足した。値はアプリが読み手を入れる版が決まるまで、`VRVLOG_avatar_animator`と同じ1.2.8にしてある（4-8で直す）
- 比較ツールの`--allow-added-materials-from`は、外した足したマテリアルの記録を`VRVLOG_materials_liltoon_unbound`から外し、空になったらこの拡張ごと外す。4-5はこの記録を`VRVLOG_liltoon_full`（実在しない名前）から外そうとしていたので、名前の誤りもここで直した

## 制約

- 足したマテリアルの標準のMToonは、`LilToonMainTextureBaker.Prepare`で焼く前の元のマテリアルから作るので、書いているマテリアルのMToonより単純なことがある
- 書き出しの後で`VRVLOG_avatar_animator`の段が失敗して拡張を書かなかったときも、足したマテリアルは`materials`に残る（どこからも指されない）
- lilToonの記録を持たない書き出し（公式のlilToonのマテリアルが1つも無いとき）は、`VRVLOG_materials_liltoon_unbound`を書かず、標準のMToonだけを足す
- アプリの読み手と検証（`VRVLOG_materials_liltoon_unbound`のスキーマの照合）はまだ無い。4-8で入れる
