# 拡張の読み込みで、知らない種類の対象を書かない対象として受ける

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Tests/Editor/AvatarAnimatorDataTests.cs`、`Formats/README.md`

## 背景

拡張（`VRVLOG_avatar_animator`）の読み込み（`AvatarAnimatorData.Read`）は、対象（`targets`）の`property`が`active`・`morph`・`parameter`のどれでもないと、拡張ごと断っていた。この先、対象の種類はノードの`enabled`やマテリアルの値のように増えていく。そのたびに`schemaVersion`を上げたり、古い読み手が拡張ごと捨てたりすると、新しいExporterで書いたVRMを古いアプリで開いたとき、今ある種類の動きまで失われる。

## 決めたこと

`property`が知らない文字列の対象は、ほかのキーを見ずに「書かない対象」として読む。

- `Target.Known`が偽になる。`Node`は-1、`Rest`は`rest`が数値ならその値で、それ以外は0
- クリップのトラックはその対象を指してよく、評価器（`AvatarAnimatorEvaluator`）は他の対象と同じように値を出す。どこに書くかを知らないので、アプリはその値をどこにも書かない
- 読んだキーは`Target.Fields`に残し、`Write`はそれをそのまま書く。`Read`→`Write`で、新しい種類のキーが落ちない。コードで作った対象（`Fields`が無い）は`property`と`rest`だけを書く
- 同じ種類の対象どうしが重なっていないかは確かめない（キーの意味を知らないため）
- `property`が文字列でない・空・1024文字を超える・無いときは、今までどおり拡張ごと断る
- スキーマの`target`に、上の3つ以外の`property`を受ける形を足した。`schemaVersion`は1のまま

今のExporterは知らない種類の対象を書かないので、書き出しの結果は変わらない。

## 採らなかった案

- 知らない対象の`property`と`rest`だけを持ち、ほかのキーを捨てる: 往復で新しい種類のキーが落ち、読んだものと書いたものが食い違う
- `schemaVersion`を2に上げる: 古い読み手は2を拡張ごと断るので、今ある種類の動きまで失われる。キーの意味が変わるときだけ上げる

## 確かめたこと

- `AvatarAnimatorDataTests`に、知らない種類の対象を読み、`Write`→`Read`→`Write`が同じ文字列になること、`rest`が数値でないときに0になること、評価器がその対象を動かすことを足した。Unity 2022.3.22f1のEditModeで、`AvatarAnimatorDataTests`・`AvatarAnimatorCaptureTests`・`AvatarAnimatorCrossCheckTests`が通った
- `Tools/run-avatar-animator-evaluator-tests.ps1`で、評価器の単体の確認109件と69個の記録の再生が通った

## 残したこと

- アプリの`AvatarAnimatorBinding`は、知らない種類で拡張ごと外す作りのまま。拡張を読むアプリを出す前に、警告を1行出して先へ進む形に直す（Epicの別の単位）
