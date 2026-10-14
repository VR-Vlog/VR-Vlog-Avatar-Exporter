# lilToon 2.0.0〜2.3.4の各版で、配布する形のZIPから起動と書き出しを確かめた記録

- 日付: 2026-10-14
- 対象: `Compatibility/Evidence/ReleaseZipStartup/`（README・結果の JSON・確認に使った Editor スクリプト）
- 関連: Epic #145、子 Issue #150（WBS 1-9 と 1-10 の残り）、PR #165（版ごとのプロジェクトを作るスクリプト）、#172（見た目の比較）

## 背景

対応範囲を lilToon 2.0.0〜2.3.4 に広げた（#146〜#149）。これまでの Unity での確認は、Exporter のチェックアウトを `file:` と `testables` で参照したプロジェクトで行っていた。利用者は VCC／ALCOM から ZIP を埋め込みパッケージとして入れるので、`Compatibility/README.md` は起動に関わる変更のたびに ZIP を埋め込んで、テスト用のアセンブリなしで `ExporterStartupProbe` を回すよう求めている。対応範囲を広げたあとの main で、各版についてこれを行っていなかった。

## 決定

- main `3f3fa45` から `Tools/build-package.py` で ZIP を作り、`Tools/create-liltoon-version-projects.py` で作った 6 版のプロジェクトへ埋め込みパッケージとして入れた。スクリプトが書く `file:` の参照と `testables` は消した。
- 版の番号は `package.json` の 0.11.15 のまま変えなかった。この版番号は `Tools/validate.py` とウィンドウの既定値に固定で書かれており、変えるのは次のリリースの準備になる。公開済みの 0.11.15 とは別物なので、記録ではコミットと ZIP の SHA-256 で区別した。
- 起動の確認（`ExporterStartupProbe.Run`）に続けて、同じ Unity の実行の中で合成のアバターを書き出した。書き出しは、書き出しウィンドウと同じ内部メソッド（`PackageVersion`・`RequireSupportedLilToon`・`UniVrmOneClickExporter.Export`）をリフレクションで呼んだ。版の範囲と構成の照合（#148）も、ウィンドウから書き出すときと同じ経路で通る。
- 確認用の Editor スクリプトは `ZipProbe.cs.txt` として置いた。`.cs` のままだと、このチェックアウトを `file:` で参照する検証用プロジェクトで読み込まれるおそれがある（#172 と同じ理由）。

結果: 6 版とも、メニューが開き、診断が Ready になり、書き出しが通った。`asset.extras.VRVLOG_lilToonInstalledVersion` は各版、`sourceLilToonVersion` は 2.3.4 だった。

## 検討した代替案と不採用理由

- `Tools/build-release-candidate.py` で候補を作る: ホスト検証の受領書（`Tools/run-validation.py`）が要る。受領書のテストは Linux 前提（`os.O_NOFOLLOW` など）で、Windows では作れなかった。ZIP の中身は `build-package.py` が作るもので同じなので、候補用の追加の検査（ZIP 内のリンク・20 MiB の上限）は同じ関数で確かめた。同じコミットの `Validate` ワークフローは成功している。
- GitHub の `Build release candidate` ワークフローで作る: 公開はしないが、作業環境から手動でワークフローを起動できなかった。
- 書き出しをテスト用アセンブリ（`MainColorLayersExportTests`）で確かめる: `testables` とテスト用のアセンブリを入れると、利用者と同じ構成での確認にならない。
- 書き出しウィンドウを操作して書き出す: batch mode ではダイアログとファイル選択が使えない。ウィンドウの書き出しと同じメソッドを呼ぶ方法にした。

## 影響

- Exporter のパッケージに入るファイルと挙動は変わらない（`Compatibility/` と `Documentation~/` はパッケージに入らない）。
- #150 の完了の条件「全版で release ZIP からメニューが開き、書き出せる」の根拠になる。リリースのときは、そのときの版の ZIP で同じ確認をやり直す。

## 未決

- Android 実機（VR Vlog）での読み込み: 端末につながらず、この作業では行っていない。lilToon 2.0.0 のプロジェクトから書き出した VRM は手元に残してある。
- 既存の利用者プロジェクトのインポートと再読み込みの履歴は再現していない（`Compatibility/README.md` の記述どおりの限界）。

## 出典

- `Compatibility/README.md`（起動に関わる変更の確認手順）、`Compatibility/StartupVerification.md`
- lilToon 公式リリース（VPM 用 ZIP）2.0.0・2.1.0・2.1.10・2.2.1・2.3.0・2.3.4
- 実行: Unity 2022.3.22f1・batch mode・2026-10-14
