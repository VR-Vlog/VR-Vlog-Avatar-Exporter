# DependencyStartupTests のメニューの確認に、アプリでの見え方の確認の項目を入れる

- 日付: 2026-10-20
- 対象: `Tests/Compatibility/DependencyStartupTests.cs`

## 背景

統合ブランチで `DependencyStartupTests.MenuResolvesBackendWithoutInitializationRegistration` が落ちていた。このテストは `VR Vlog/` の下のメニューが「VRMを書き出す」の 1 つだけであることを確かめていた（7f141ad で書き出しの入口を 1 つにまとめたときのもの）。

ccf5f56（`2026-10-18-app-preview.md`）で `VR Vlog/アプリでの見え方を確認` を足したため、項目とその有効判定の 2 つが増えて落ちる。develop にはこの画面が無いので、develop では通る。テストが古くなっていたもので、メニューの実装の回帰ではない。

## 決定

テストが守りたいのは「書き出しの入口が 1 つ」なので、次の 2 つを確かめる形にする。

- `VR Vlog/VRMを書き出す` がちょうど 1 つある
- `VR Vlog/` の下の項目は「VRMを書き出す」と「アプリでの見え方を確認」のどちらか

「アプリでの見え方を確認」は本体のエディタのアセンブリにあり、UniVRM 0.131 以外では本体ごとコンパイルされない。対応外の UniVRM で回す互換性のテストでは無いので、あってもなくても通るようにした。

## 確認したこと

- Unity 2022.3.22f1 の EditMode で、このテストがメニューの確認を通る（環境変数 `VRVLOG_TEST_UNIVRM` が無いので、その先は従来どおり Ignore になる）。
