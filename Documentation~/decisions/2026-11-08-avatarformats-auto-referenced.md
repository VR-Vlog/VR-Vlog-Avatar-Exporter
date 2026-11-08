# `VRVlog.AvatarFormats` を自動参照にし、asmdef の外のスクリプトからも型を使えるようにする

- 日付: 2026-11-08
- 対象: `Formats/Runtime/VRVlog.AvatarFormats.asmdef`・`Formats/README.md`

## 背景

[2026-10-18の決定](2026-10-18-avatar-formats-package.md)で、`VRVlog.AvatarFormats` は `autoReferenced: false` にし、使う側の asmdef が名前で参照する形にした。

Unity では、asmdef に属さないスクリプト（`Assembly-CSharp`）は `autoReferenced: true` のアセンブリしか参照できず、参照を足す場所がない。アプリ（VR Vlog 本体）は `ObjectToggleData` を `Assembly-CSharp` のスクリプトからも使っているため、このパッケージの型を使えず、同じ内容の写しを手元に持ち続けていた。写しがあると、2026-10-18 の決定で避けたかった「片方だけ直してずれる」が `ObjectToggleData` について残る。

## 決定

- `VRVlog.AvatarFormats` を `autoReferenced: true` にする。asmdef から使う側は、これまでどおり名前で参照する（Exporter の `VRVlog.LilToonExporter.Editor` などの参照は変えない）。
- アセンブリの中身・名前空間・`noEngineReferences: true`・プラットフォームの指定は変えない。
- 2026-10-18 の決定のうち `autoReferenced: false` の 1 点だけをこの記録で置き換える。

## 検討した代替案と不採用理由

- アプリ側で `ObjectToggleData` を使う `Assembly-CSharp` のスクリプトを asmdef に移す: 対象が `AvatarManager` などアプリの中心のクラスで、それらが参照する型ごと asmdef に分ける必要があり、写しを消すための変更として大きすぎる。
- アプリ側で写しを残し、照合テストで同一性を確かめ続ける: 2026-10-18 の決定で写しをやめた理由がそのまま残る。

## 影響

- Exporter を入れた VRChat のプロジェクトでも、`Assembly-CSharp` から `VRVlog` 名前空間の型が見えるようになる。型は `System` だけを使い、Unity の API に依存しないので、ビルドの中身は変わらない。利用者のスクリプトが同じ完全名の型を持っていなければ衝突しない。
- 書き出しの結果は変わらない。
