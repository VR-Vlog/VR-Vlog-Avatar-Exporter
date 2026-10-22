# 元のアバターの指紋から Expressions Menu の Parameters を外す

- 日付: 2026-10-22
- 対象: `Editor/ExportSourceFingerprint.cs`・`Tests/Editor/ExpressionMenuSourceFingerprintTests.cs`・`CHANGELOG.md`

## 背景

書き出しを準備したあと Hierarchy でアバターを選ぶだけで、保存が「元のアバターが変更されました」で止まることがあった。VRChat SDK の Avatar Descriptor のインスペクタは、開くたびに Descriptor の Expression Parameters を各 Expressions Menu（サブメニューを含む）の `Parameters` へメモリ上で代入する。この代入はアセットを dirty にせず、ディスクにも書かれない。`ExportSourceFingerprint` はメニューの直列化フィールドをすべてハッシュに入れていたので、この代入を元のアバターの変更とみなしていた。止まる前と後に保存した VRM はバイト単位で同じだった。

## 決定

`VRCExpressionsMenu` の `Parameters` を指紋に入れない。Exporter が表情のパラメータとして読むのは Descriptor の `expressionParameters` で、メニューの `Parameters` はどこからも読んでいない（VRM に届かないエディタ用の値）。Descriptor の `expressionParameters` と、メニューの項目（名前・種類・値・パラメータ・サブメニュー）は今までどおり指紋に入る。型は名前で判定し、SDK が無いプロジェクトでは何も変わらない。保存の前に書き出しを作り直す経路はあるが、インスペクタを開くたびに作り直すのは無駄なので、判定から外すほうを選んだ。

## 確認したこと

- `ExpressionMenuSourceFingerprintTests`: メニューの `Parameters` だけを変えても指紋が変わらないことと、メニューの項目の名前・値・追加、Descriptor の `expressionParameters` を変えると指紋が変わることを確かめる。VRChat SDK が無い環境ではスキップする。
