# 制約のオン・オフの対象を、読み込みで知っている種類にする

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Tests/Editor/AvatarAnimatorDataTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`、`Formats/README.md`

## 背景

書き出しは、表情のアニメーションが制約（Unity・VRCのConstraint）の`enabled`を動かす曲線を、`component`が`constraint`で`index`が`VRVLOG_avatar_constraints`の`constraints`の番号の対象として書いている。読み込み（`AvatarAnimatorData.Read`）はこの`component`を知らず、知らない種類の対象（`Target.Known`が偽）として読んでいた。これではアプリが、どの制約を切り替えるのかを対象から知れない。

## 決めたこと

- `Components`に`constraint`を足した。`enabled`で`component`が`constraint`の対象は、`node`・`property`・`component`・`index`・`rest`をちょうど持つ。`index`は0以上の整数で、`Target.Index`に入る
- 同じノードに制約が2つあれば対象も2つになるので、重なりは`(node, component, index)`で確かめる。`renderer`は今までどおり`(node, component)`
- `Write`は、`Fields`がある対象はそのキーを書く。書き出し（`AvatarAnimatorCapture`）は今の対象を`Fields`で作っており、書き出しのコードを変えずに同じ文字列になる
- スキーマに、制約の`enabled`の対象の形を足した。知らない`component`の形からは`renderer`と`constraint`を除いた。`schemaVersion`は1のまま

`index`が`constraints`の範囲にあるか、その制約の`node`と対象の`node`が同じかは、`VRVLOG_avatar_constraints`を一緒に読む側（アプリ）が確かめる。2つの拡張は別々に読むため。

## 採らなかった案

- 書き出しで`Target.Index`を埋めて`Fields`を使わない: 書き出しのコードは別の単位で並行して直しているので、この変更では触らない。`Fields`から書いても出てくる文字列は同じ
- `schemaVersion`を2に上げる: キーの意味は変わらず、古い読み手は今までどおり知らない種類として読めるので上げない

## 確かめたこと

- Unity 2022.3.22f1のEditModeで、`AvatarAnimatorDataTests`・`AvatarAnimatorCaptureTests`の100件が通った。`AvatarAnimatorDataTests`に、往復・同じノードの2つの制約・重なりの拒否・`index`が無い・負・小数、`node`が無い、`rest`が0.5、余計なキーの拒否を足した
- `Tools/run-avatar-animator-evaluator-tests.ps1`（147件と105個の記録の再生）、`Tools/validate.py`、`Tools/test-compatibility-tooling.py`が通った
