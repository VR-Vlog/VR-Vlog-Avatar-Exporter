# リリースで UniVRM のパッケージを作らない

## 決めたこと

`release-vpm.yml` は書き出しツールの ZIP だけを Release に添付する。UniVRM の `com.vrmc.gltf`・`com.vrmc.vrm` の ZIP を作る手順、`univrm_version` の入力と出力、`Compatibility/dependencies.json` を読む行を消した。`build-listing.yml` の手元のリスティング検査も、書き出しツールの ZIP だけで組む。

## なぜ

UniVRM 0.131.4 は `Editor/Vendor/UniVRM` に同梱した（[2026-10-22-vendor-univrm.md](2026-10-22-vendor-univrm.md)）。`vpmDependencies` から `com.vrmc.*` を外し、`Compatibility/dependencies.json` の `uniVrm` も無くなったので、前の手順のままではリリースもリスティングの検査も `uniVrm` を読めずに止まる。

過去の Release に付いている UniVRM の ZIP はそのまま残る。リスティングは各 Release の添付から組むので、旧版を入れる人には今までどおりそれが出る。

## 再発を防ぐ検査

- `Tools/check-pipeline.py` は `release-vpm.yml` のどこにも `univrm`・`com.vrmc` が無いことを求める。
- `Tools/test-pipeline.py` は入力・出力・作る手順・添付のどれを戻しても検査が落ちることを確かめる。
- `Tools/validate.py` は `release-vpm.yml` が `Compatibility/dependencies.json` と `com.vrmc` を参照しないことを確かめる。
