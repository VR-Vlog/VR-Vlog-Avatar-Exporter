# 書き出しの設定をアバターごとに 1 つの型へまとめ、窓・転送・一括書き出しで共有する

- 日付: 2026-10-18
- 対象: `Editor/ExportSettings.cs`・`Editor/ExportSettingsStore.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/AppTransferExport.cs`・`Editor/Locales/*.json`・`Tools/validate.py`・`Tests/Editor/ExportSettingsTests.cs`

## 背景

- 「VRM書き出し」の除外オブジェクト・ギミックの自動除外と残す例外・瞬き・ポーズ・ライセンスは、窓のフィールドにだけあった。アバターを切り替えると消え、Unity を開き直しても残らなかった。
- **アプリに転送** は `UniVrmOneClickExporter.Export` を既定の引数で呼んでいた。そのため、窓で外したオブジェクトやギミックの選択が転送では効かず、同じアバターでも窓と転送で中身の違う VRM ができていた。
- 窓は `disableAudioLink` を渡しておらず、転送と表情の記録の窓は `true` を渡していた。変更履歴では「書き出し用の複製では AudioLink を止める」としている。
- 全アバターの一括テストのような呼び出し側は、窓と同じ設定で書き出す入口を持っていなかった。

## 決定

- 設定を `ExportSettings` にまとめる。持つのは除外オブジェクト・ギミックの自動除外・残すギミック・瞬き・ポーズ・ライセンス。
- 書き出しの呼び出しは `ExportSettings.Export` の 1 か所にする。窓・転送・一括のどれから呼んでも、lilToon の版の確認、段階の失敗時の扱い（`ExportStageGuard.ContinueByDefault`）、`disableAudioLink: true` が同じになる。窓でも AudioLink を止めるようになるが、これは変更履歴に書いた仕様どおりの動作。
- `Creator` は呼んだ時点の設定のコピーを持つ。失敗の窓から再試行したときも、その試行の設定のまま書き出す。
- 保存先は `UserSettings/VRVlogLilToonExporter/ExportSettings.json`。キーは `ExpressionPresetStore.KeyFor`（アバターの GlobalObjectId）。中のオブジェクト・レンダラー・アニメーションクリップも GlobalObjectId で持ち、名前は保存しない。
  - 読むときは、アバター自身かその子孫に解決できたものだけを戻す。解決できないもの・アバターの外へ移ったものは外して件数を返し、窓は「保存していた書き出し設定のうち N 件の対象が見つからないため外しました。」をコンソールに出す。
  - 既定のままの設定はファイルに書かない（既定に戻すと項目を消す）。最大 256 アバター。
  - 壊れたファイル・4 MB を超えるファイルは無視して既定で続ける。`Version` が違うファイルは上書きしない。
  - 保存していないシーンのアバターは、スクリプトの再読み込みまでメモリにだけ持つ。
- 窓は `OnInspectorUpdate` で設定の変化を見て保存する。ドラッグ中（`GUIUtility.hotControl != 0`）は保存しない。ほかの窓が保存したら（`ExportSettingsStore.Revision` が変わったら）読み直す。読み直すときは `settings` を作り直さず中身だけを入れ替える（ポーズ確認の窓が `settings.Poses` を参照で編集しているため）。内容が同じなら何もしない。Play Mode 中と切り替え中は保存しない。アバターの切り替え・書き出し・窓を閉じるときにも保存する。
- ファイルを書けないとき、別の版のファイルがあるときは、保存をあきらめてコンソールに 1 回だけ警告を出す。書き出しは止めない。
- 転送は、選んだアバターの保存済み設定で書き出す。転送の画面で変えたライセンスはその設定へ書き戻す。
- 一括の入口は `ExportSettings.SaveVrm(avatar, destination, author)`。保存済み設定を読み、ダイアログなしで `ExportAndSaveNormally` を通す。

## 採らなかった案

- アバター（Prefab やシーン）にコンポーネントを足して設定を持たせる案。元のアバターを変更しない方針に反するので採らない。
- EditorPrefs に保存する案。プロジェクトごとに分かれず、GlobalObjectId はプロジェクトの中でしか意味を持たないので、表情のプリセットと同じ `UserSettings` に置く。

## 範囲外

- 表情の記録の窓（`ExperimentalExpressionCaptureWindow`）が持つライセンスはこの変更では扱わない。転送でその窓の `LicenseFor` が返す値を優先する動作も変えない。

## 検証

- `Tests/Editor/ExportSettingsTests.cs`: 保存と読み直し、別のアバターは既定、アバターの外へ移したもの・消したものは外れる、既定は書かない、壊れたファイルと別の版のファイル、保存していないシーン、コピーの独立、転送が保存済みの除外とギミックを渡すこと。
- 既存の `ExportAuthorTests`・`ExpressionPresetTests`・`ExperimentalExpressionCaptureTests` を同じ環境で実行する。
