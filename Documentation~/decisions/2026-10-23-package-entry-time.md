# パッケージのZIPの各ファイルの日時をビルドしたコミットの日時にする

- 日付: 2026-10-23
- 対象: `Tools/build-package.py`（`entry_time`・`build`）・`Tools/test-package.py`・`CHANGELOG.md`

## 背景

0.12.0-beta.8から0.12.0-beta.9へVCCで更新すると、`AppTransferPanel.cs(850,54)` と `(851,53)` で `error CS0117: 'ExporterWindowHeader' does not contain a definition for 'Version'` が出て、コンパイルが通らないという報告があった。パッケージを消して入れ直しても同じだった。

Unity 2022.3.22f1の新しいプロジェクトで、VPMのCLI（VCCと同じライブラリ）でbeta.8を入れてコンパイルし、beta.9へ更新すると同じ行・列で再現した。ディスク上の `ExporterWindowHeader.cs` はbeta.9のもの（`Version` あり）で、`Library/ScriptAssemblies` の `VRVlog.LilToonExporter.Compatibility.dll` はbeta.8のときに作ったままだった。

原因は次の3つが重なったこと。

- `build-package.py` はZIPの全ファイルの日時を2020-01-01 00:00に固定していた（同じコミットから同じバイト列のZIPを作るため）。
- VCC・ALCOM・インストーラー（中でVPMの解決を走らせる）はZIPの日時をそのまま付けて展開する。展開後のファイルはどの版でも2020-01-01になる。
- Unityのスクリプトのビルド（`Library/Bee`）は、アセンブリのファイルの一覧と各ファイルの日時が前回と同じなら作り直さない。中身やサイズは見ない。

beta.8からbeta.9で `VRVlog.LilToonExporter.Compatibility` はファイルの増減がなく、`ExporterWindowHeader.cs` の中身だけが変わった。そのためこのアセンブリだけ作り直されず、ファイルが増減して作り直された `VRVlog.LanTransfer.Editor` が古いDLLの `ExporterWindowHeader` を参照してCS0117になった。パッケージを消して入れ直しても `Library/Bee` の記録が残るので同じになる。

過去の版のタグを比べると、beta.9へ上げたときにこの形（ファイルの一覧が同じで中身が違う）になるアセンブリがあるのはbeta.6・beta.7・beta.8からだけで、beta.5以前からはどのアセンブリも作り直される。実機でもbeta.5からbeta.9への更新では起きなかった。

## 決定

- ZIPの各ファイルの日時を、固定の2020-01-01ではなく、ビルドしたコミット（`HEAD`）のコミット日時（UTC）にする。リリースの版を変えると `package.json` を変えるコミットが入るので、版ごとに日時が変わり、Unityは前回と違う日時のファイルを含むアセンブリを作り直す。
- 同じコミットからは同じ日時になるので、リリース候補のZIPと公開時に作り直したZIPのSHA-256の突き合わせ（`release-vpm.yml`）はそのまま成り立つ。
- 全ファイルを同じ日時にする点は変えない。ファイルごとの最終更新コミットの日時にはしない（`git log` をファイル数だけ回すことになり、どのアセンブリも版ごとに日時が変わる今回の決定で足りるため）。

## 確認

- `Tools/test-package.py` で、ZIPの日時がコミット日時になることと、コミットを重ねると日時が変わることを確かめる。
- beta.9のタグから新しい `build-package.py` で作ったZIPは、公開したbeta.9のZIPと全1658ファイルの名前と中身が一致し、日時だけが違った。2回作ると同じバイト列になった。
- Unity 2022.3.22f1で、beta.8を入れてコンパイルしたプロジェクトをこのZIPで更新すると、`Compatibility` のDLLが作り直されてエラーなく開けた。同じ手順で公開したbeta.9のZIPに更新するとCS0117になる。

## 検討して採らなかった案

- ビルドした時刻にする案は採らなかった。リリース候補と公開時でZIPのバイト列が変わり、SHA-256の突き合わせが成り立たなくなるため。
- エディタ拡張でパッケージの更新を検出して `Library/Bee` を消す・再コンパイルを要求する案は採らなかった。CS0117の時点ではそのアセンブリ自身がコンパイルできず、直す側のコードが動かないことがあるため。

## 欠点

- すでに公開したbeta.6〜beta.9は日時が2020-01-01のままなので、beta.6〜beta.8からbeta.9へ上げてこの症状が出たプロジェクトは、Unityを閉じて `Library/Bee` フォルダを消してから開き直す必要がある（`Library/ScriptAssemblies` だけ消しても直らない）。次の版からの更新では、この変更で作り直される。
- ZIPの日時にはタイムゾーンが無く、展開するPCのタイムゾーンの時刻として付く。前回と違う日時であれば足りるので問題にしない。
