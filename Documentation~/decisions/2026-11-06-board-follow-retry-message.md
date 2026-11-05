# 書き出しの失敗画面からの再試行では、表情ボードを作り直したとは書かない

- 日付: 2026-11-06
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

元のアバターが表情ボードのプレビューのコピーと違っていたとき、ボードはコピーを作り直し、Console に「表情ボードの選択を残したままコピーを作り直しました」と書く（`FollowedSourceFormat`）。書き出しの失敗画面から再試行したとき（`ExportOnTheAvatarAsItIs`）も同じ文を書いていた。再試行は今のアバターから一時のコピーを作って書き出すだけで、ボードのコピーは作り直さない。ボードを閉じたあとでも再試行できるので、この文は起きたことと違う。

PR #603 のレビューでは、このほかに次の 2 件が挙がっていた。

- アバターに追従したあとも、前のコピーで描いたサムネイルが残る
- 追従の作り直しに失敗すると、ボードが閉じる

## 決定

再試行では、今のアバターから書き出し直したことを書く別の文（`RetriedOnSourceFormat`）を使う。4 言語の訳も足した。

サムネイルは、作り直しが毎回通る `Cleanup` が `ClearThumbnails` を呼んでいて、今のコードでは残らない。テスト（`AChoiceAfterAnEditOfTheSourceRunsOnARebuiltCopy`）で、追従の前に描いたサムネイルが消えることを確かめるようにした。

作り直しに失敗したときは、ボードは「書き出しを準備」の画面に戻り、エラーを表示する。今のアバターでコピーを作れないので、表示できる表情もない。選択だけを残してあとで戻す作りは、コピーなしで選択を持つ状態が増えるので入れない。アバターを直したあとは「書き出しを準備」からやり直す。

## 確認

`ExperimentalExpressionCaptureTests` を Unity 2022.3.22f1 で回す。再試行のテストは新しい文が出ることを、追従のテストはサムネイルが消えることを確かめる。
