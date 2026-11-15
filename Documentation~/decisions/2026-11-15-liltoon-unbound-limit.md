# 差し替え先として足すマテリアルは、アプリの上限に収まる分だけ足す

- 日付: 2026-11-15
- 対象: `Editor/AddedMaterialLimit.cs`、`Editor/Vrm10AppearanceExporter.cs`、`Tests/Editor/AddedMaterialLimitTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`

## 背景

4-5b（決定記録 2026-11-15-liltoon-unbound-materials）は、表情のアニメーションが差し替える lilToon のマテリアルのうち、どのメッシュも使わないものを glTF の`materials`の末尾に足した。足したマテリアルとその画像も、アプリの上限（マテリアル 64・画像 128）に数えられる。書き出しを続ける設定（lenient）では、上限を超えたファイルは`VRVLOG_materials_liltoon`を外して書くので、上限ぎりぎりのアバターでは、足したマテリアルのせいで元のメッシュの lilToon の見た目まで落ちていた。

## 決めたこと

- 足すマテリアルは曲線の順に 1 つずつ数え、マテリアルの数と画像の数がどちらも上限に収まる間だけ足す。最初に収まらなかったものと、その後ろは全部足さない。残すマテリアルの番号は末尾に連続したままになる
- 数えるのは、書き出した直後の glTF（`Vrm10Exporter`のあと、`afterExport`の前）。画像の数は MToon の書き出しで決まるので、書き出す前には正確に分からないため
- 画像は、元のメッシュのマテリアルが使うものと、どのマテリアルも使わないものを先に数え、足すマテリアルだけが使う画像を足すごとに増やす。元のメッシュのマテリアルだけで上限を超えているときは何も足さない（今までどおり lilToon の拡張が外れるのは、元のアバターの分だけになる）
- 足さなかったマテリアルは`afterExport`の前に`model.Materials`から外す。lilToon の記録（`VRVLOG_materials_liltoon_unbound`）は書かれず、それを差し替え先にする曲線は今までどおり`material`（このファイルに無いマテリアルへの差し替え）に数える。`AvatarAnimatorCapture`は変えていない
- ファイルからは、足さなかったマテリアルと、それだけが使っていた画像（`textures`・`images`・`bufferViews`）を外し、番号を詰める。外した画像のバイトは`GlbBinaryOptimizer.Compact`で捨てる
- 上限に収まるとき（足すマテリアルが無いときを含む）は、書き出すバイト列は今までと同じ

## 制約

- 足さなかったマテリアルは Console に件数を 1 行出すだけで、書き出し結果の画面の警告には出さない。曲線の数は`VRVLOG_avatar_animator`の`omitted`の`material`に入る
- 外す処理は、書き出し直後の glTF で画像を指すのがマテリアル（キーが`Texture`で終わる textureInfo）と`thumbnailImage`だけという前提で番号を詰める
