# 1.0.0以降の安定版でQR転送を既定で有効にする

- 日付: 2026-10-23
- 対象: `Editor/LanTransfer/CloudTransferAvailability.cs`（`IsSupportedVersion`・`IsStableFromOne`）・`Tools/build-package.py`（`transfer_adopted`）・`Tests/LanTransfer/CloudTransferTests.cs`・`Tools/test-package.py`・`README.md`・`README.en.md`・`Documentation~/README.md`・`Documentation~/CloudTransfer.md`

## 背景

QR転送を使える版は、エディタ側の `CloudTransferAvailability.IsSupportedVersion` と、ZIPを作る `Tools/build-package.py` の両方で、安定版を `0.11.15` と `1.0.0` の名指しで決めていた。1.0.0では書き出しの入口がVR Vlogメニューの転送だけになったため（2026-10-23-remove-export-menu.md）、次の安定版（1.0.1など）を名指しの追加なしで出すと、転送のコードもガイドもZIPに入らず、メニューに項目が1つも出ない版になる。1.0.0は2026-10-23に公開した。

## 決定

- 安定版は、メジャーが1以上（`1.0.0` 以降）ならQR転送を含める。`0.11.15` は従来どおり含め、それ以外の0.x安定版は書き出し専用のまま変えない。プレリリースの扱いも変えない。
- エディタ側とビルド側で同じ規則にする。どちらも `X.Y.Z` の形だけを安定版として扱い、`1.0.1+build.1` のようにビルドメタデータが付いた版は含めない（これまでの `0.11.15+beta.2` と同じ扱い）。
- `package.json` の `vrvlogCloudTransfer: false` で外せることは変えない。

## 確認

- `CloudTransferTests.CloudTransferIsOnForStableFromOneZero` で、`1.0.0`・`1.0.1`・`1.1.0`・`2.0.0`・`10.0.0` が真、`0.11.16`・`0.12.0`・`1.0.1+build.1`・`1.0`・`null` が偽になることを確かめる。
- `Tools/test-package.py` の `test_stable_from_one_zero_keeps_transfer_and_honors_opt_out` で、`1.0.1`・`1.1.0`・`2.0.0` のZIPに転送のDLLとガイドが入り、`vrvlogCloudTransfer: false` と0.x安定版では入らないことを確かめる。

## 検討して採らなかった案

- リリースのたびに版を名指しで足す案は採らなかった。足し忘れると書き出しの入口が無い版が出てしまい、それを止める検査も無いため。
- 0.x安定版も含めて「0.11.15以降」とする案は採らなかった。公開済みの0.x安定版は0.11.15で終わっており、0.12.0以降の0.x安定版を出す予定が無い。範囲を広げても得るものが無い。

## 欠点

- 1.0.0以降の安定版で転送を外したいときは、`vrvlogCloudTransfer: false` を書く必要がある。ただし1.0.0以降は転送が書き出しの入口なので、外すとメニューに項目が無くなる。
