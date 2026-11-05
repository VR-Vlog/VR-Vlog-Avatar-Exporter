# 公開用の読み込みを Windows でも動かす

- 日付: 2026-11-06
- 対象: `Tools/prepare-exporter-publication.py`・`Tools/sync-vpm-mirrors.py`・`Tools/test-exporter-publication.py`・`Tools/test-vpm-mirrors.py`

## 背景

公開するファイルを読む 3 か所が `os.O_NOFOLLOW | os.O_NONBLOCK` で開いていた。Windows の Python にはこの 2 つが無いので `AttributeError` になり、`Tools/run-validation.py` は Windows では `test-vpm-mirrors.py` と `test-exporter-publication.py` で止まって完走しなかった（エラー 77 件・失敗 1 件）。

## 決定

開くときのフラグを `SAFE_READ_FLAGS` にまとめ、無いフラグは `getattr(os, name, 0)` で 0 にした。Windows で読み込みがテキストモードにならないよう `O_BINARY` も足した。Windows でもシンボリックリンクと通常のファイル以外は、開く前の `lstat` の検査で弾く。Linux で開くときのフラグは変わらない。

テストのうち Windows で作れないもの（FIFO・名前に `\` や改行を含むファイル・`0o600` の権限）は Windows では飛ばし、CI（Linux）で確かめる。

## 確かめたこと

Windows（Python 3.12・PyYAML 6.0.3）で `Tools/run-validation.py` を回し、27 件すべて終了コード 0 で `passed: true` になった。
