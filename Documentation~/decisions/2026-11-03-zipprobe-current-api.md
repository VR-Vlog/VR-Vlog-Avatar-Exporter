# リリース ZIP の確認スクリプトを現在の lilToon の確認メソッドに合わせる

- 日付: 2026-11-03
- 対象: `Compatibility/Evidence/ReleaseZipStartup/ZipProbe.cs.txt`・同フォルダの `README.md`

## 背景

`ZipProbe.cs.txt` は、削除済みの `RequireSupportedLilToon` をリフレクションで呼んでいた。書き出しの段階で必ず失敗するため、README の手順どおりに動かせなくなっていた。

## 決定

- 現在の `RequireLilToon(source, warnings)` を呼ぶ。警告の一覧は同じものを書き出しにも渡す。
- README の結果は当時のメソッドで取ったものなので書き換えず、そのことを本文に書く。

## 確かめたこと

Unity 2022.3.22f1・lilToon 2.3.4 のプロジェクトで `E145ZipProbe.Run` を実行し、`installedLilToon` が 2.3.4、`export` が passed になった。`startup` の失敗は、テストを回すためにそのプロジェクトで `testables` を有効にしていたことをプローブが正しく弾いたもの。リリース ZIP を入れたプロジェクトでは回していない。
