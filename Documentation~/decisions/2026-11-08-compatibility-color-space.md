# 実際の Unity で回すテストを、Gamma と Linear のプロジェクトで分けて記録する

- 日付: 2026-11-08
- 対象: `Tools/run-unity-compatibility.py`・`Tools/unity_packages.py`・`Tools/test-compatibility-tooling.py`・`Compatibility/README.md`

## 背景

Exporter は画像を GPU で焼き込む。Gamma のプロジェクトは sRGB の画像を読むときに Linear の値へ直さないので、同じ素材でも Linear のプロジェクトと違う値になり、Exporter 側で直している（#712 のアルファマスク、1.8.0 のシェーダーの置き換えなど）。

これらの修正は、手元で Gamma のプロジェクトを作って回した結果が根拠だった。`run-unity-compatibility.py` はプロジェクトの色空間を見ず、受け取り（JSON）にも残さないので、どちらで回した結果かが後から分からなかった。CI は Unity のテストを回していない。

## 決定

- `run-unity-compatibility.py` に `--expect-color-space linear|gamma` を足す。`ProjectSettings/ProjectSettings.asset` の `m_ActiveColorSpace` を読み、違う色空間なら Unity を起動する前に止める。
- 受け取りに `colorSpace` を必ず書く（オプションを付けないときも、読めた値か null）。テストの後にもう一度読み、変わっていたら失敗にする。これはプロジェクトの設定で、Unity の中でテストが読んだ値そのものではない（スクリプトが一時的に切り替えて戻す形は見つけられない）。
- `Compatibility/README.md` の手順に、Linear と Gamma の 2 つのプロジェクトで回すことを足す。`create-liltoon-version-projects.py` が作るプロジェクトは Unity が Gamma で開くので、Linear の分は複製して値を 1 にする手順を書く。

## 検討した代替案と不採用理由

- `--expect-color-space` を必須にする: 色空間を気にしない回し方（UniVRM の共存の確認など）まで書き換えが要る。受け取りに必ず残るので、後から区別はできる。
- `create-liltoon-version-projects.py` に色空間のオプションを足す: Unity が最初に開く前は `ProjectSettings.asset` が無く、手で書いた最小のファイルを Unity がどう補うかを確かめていない。複製して 1 か所を書き換える方が確実。
- CI に Gamma のレーンを足す: 今の CI は Unity のテストを回しておらず、ライセンスとランナーの用意が要る（別の判断として残っている）。

## 確かめたこと

- `Tools/test-compatibility-tooling.py` に、受け取りに色空間が残ること、違う色空間・値の無いプロジェクトでは Unity を起動しないこと、実行中に変わると失敗になることのテストを足し、全件が通った。
- Unity 2022.3.22f1 が作った lilToon 2.3.4 のプロジェクト（Gamma）と、その複製の値を 1 にしたもの（Linear）で、`gamma`・`linear` と読めることを確かめた。Gamma のほうで `--expect-color-space linear` が Unity を起動する前に止まり、出力のフォルダも作らないことを確かめた。複製した Linear のプロジェクトでは EditMode のテストが Linear の値で通った（#717 の検証）。
