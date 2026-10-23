# プロジェクトのUniVRMから移した設定と材質を、再コンパイルの前に解放する

- 日付: 2026-10-23
- 対象: `Editor/ForeignUniVrm.cs`（`ReleaseAll`・静的コンストラクタ）・`Tests/Editor/ForeignUniVrmTests.cs`

## 背景

#368 のレビューで残した P2。プロジェクトのUniVRMで設定したアバターは、書き出し用の複製でだけ同梱版の設定（`VRM10Object`・表情）と材質へ移す。移した先は `HideFlags.DontSave` で作り、どの複製・どの元の設定のために作ったかを静的なリスト（`adopted`・`settingsCache`）で覚えて、複製が消えたときや元の設定が編集されたときに解放している。

## 原因

- スクリプトの再コンパイル（ドメインのリロード）で静的なリストは空になるが、`DontSave` のオブジェクトはメモリに残る。`DontSave` は `DontUnloadUnusedAsset` を含むので、`Resources.UnloadUnusedAssets` でも消えず、エディタを閉じるまで残る。
- 1 回の書き出しで残るのは、設定と表情の ScriptableObject と、MToon10・UniUnlit の材質の複製だけなので、量は小さい。

## 決定

- `AssemblyReloadEvents.beforeAssemblyReload` で、覚えているものをすべて解放する（`ReleaseAll`）。登録は `ForeignUniVrm` の静的コンストラクタで行う。一度も使っていなければ解放するものもないので、`InitializeOnLoad` にはしない。
- 書き出し用の複製は `NdmfExportPreparation` が同じイベントでプレビューシーンごと閉じるので、リロードのときに生きている複製はない。どちらのハンドラーが先に呼ばれても、閉じる複製の材質や設定が先に消えるだけで問題はない。
- `AuthoredSettings` の結果は呼び出し側（瞬き・表情の一覧・中立の形の取得）がその場で読むだけで保持しないので、リロードのあとは次に読んだときに作り直す。

## 成り立たない場合

- エディタが異常終了したときはイベントが呼ばれないが、そのときはプロセスごと消える。
- 今後 `AuthoredSettings` の結果をリロードをまたいで保持する呼び出し側を足すと、解放済みのオブジェクトを指すことになる。その場合は保持せずに毎回読む。

## 検証

- EditMode: `ForeignUniVrmTests.ConversionsAreReleasedBeforeADomainReload`（`ReleaseAll` で設定と複製の両方が解放され、次の `AuthoredSettings` で作り直されること）。プロジェクトにUniVRMが無い環境では、ほかの 3 件と同じく無視される。
- CI（`validate.yml`）は Unity のテストを回さない。#368 のレビューで「CI で 3 件が無視される」としたのは、互換性の検証でプロジェクトにUniVRMを入れない構成のこと。その構成ではプロジェクトのUniVRMの型が無いので、無視されるのが正しい。UniVRMを入れた構成（VPM・Git URL・unitypackage）で通っているので、追加の対応はしない。
