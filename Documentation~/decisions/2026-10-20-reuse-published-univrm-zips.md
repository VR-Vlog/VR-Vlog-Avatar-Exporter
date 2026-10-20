# 公開済みのUniVRM依存ZIPを新しいリリースに添付しない

- 日付: 2026-10-20
- 対象: `.github/workflows/release-vpm.yml`（Build UniVRM dependency packages・Publish release）

## 背景

v0.12.0-beta.1のBuild VPM Listingが「Require current mirrors immediately before canonical deployment」で止まった。`Tools/sync-vpm-mirrors.py --verify-before-deployment`の`preserve_history`が、公開済みの`com.vrmc.gltf` 0.131.0と`com.vrmc.vrm` 0.131.0の`zipSHA256`とURLが変わると判定した。Exporter本体の既存64版は変わっていなかった。

`release-vpm.yml`はリリースのたびにUniVRMのtarballから2つの依存ZIPを作り直して添付している。中身のファイルは同じでも、ZIP内の並び順と、コピーした`UPSTREAM-LICENSE.txt`の日時が毎回違うので、SHA256も毎回変わる（v0.11.6以降の全リリースで別の値）。listingの生成に使うpackage-list-actionは、同じ版が複数のリリースにあると新しいリリースのものを載せる。そのため0.12.0-beta.1のlistingは、依存2つをv0.11.15のZIP（`a3c7bd32…`／`19402867…`）からv0.12.0-beta.1のZIP（`5fab8789…`／`f4261b2d…`）へ差し替えようとした。

公開済みの版の検査は2026-10-14（#143）に入った。v0.11.15はそれより前の公開なので、今回が検査後で初めてのリリースだった。

## 決定

依存パッケージの版が正規のlistingに既にあるときは、リリースで依存ZIPを作らず、添付もしない。listingには、その版を最初に載せたリリース（今はv0.11.15）のZIPが載り続ける。

- 正規のlistingを読めないときはリリースを止める（作り直したZIPを黙って添付しない）
- 片方の依存だけが公開済みのときもリリースを止める
- UniVRMの版を上げたときは、これまでどおり新しい依存ZIPを作って添付する

## 採らなかった案

- ZIPを毎回同じバイト列で作る（並び順と日時を固定する）: 公開済みのZIPと同じバイト列にはできない。listingのURLもリリースごとに変わるので、検査はやはり止まる
- listingを作ったあとに、依存の行を公開済みの行で上書きする: package-list-actionの出力と公開するlistingがずれる。リリースページのZIPとlistingのZIPも食い違ったままになる
- 公開済みの版の検査を緩める: 利用者のVCCが、同じ名前と版でチェックサムの違うZIPを受け取ることになる
