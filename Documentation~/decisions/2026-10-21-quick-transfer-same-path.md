# アプリに転送（かんたん）を表情ボードの保存で書き出す

- 日付: 2026-10-21
- 対象: `Editor/AppTransferExport.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/Locales/*.json`・`Tools/validate.py`・`Tools/ExportRegressionRunner.cs`・`Tests/Editor/AppTransferQuickExportTests.cs`・`Tests/Editor/ExportSettingsTests.cs`・`Tests/Editor/ExportAuthorTests.cs`・`README.md`・`README.en.md`・`Documentation~/CloudTransfer.md`・`CHANGELOG.md`

## 背景

同じアバターを **アプリに転送（かんたん）** と、表情ボードで何も変えずに **アプリに転送（通常）** で書き出したところ、VRM の表情がかんたんでは 9 件、通常では 40 件だった。このアバターでは、書き出し時に表情メニューを読み直しても顔の表情を 1 件も取り出せない（FX 以外のレイヤーが表情の読む値を書き換えている）。

かんたんは `AppTransferExport.Export` から `ExportSettings` と同じ作り方で `LilToonExporterWindow.ExportAndSaveNormally` を呼び、表情ボードを使っていなかった。表情ボードの保存（`ExperimentalExpressionCaptureWindow.Save`）にだけある次の 2 つが、かんたんには無かった。

- 表情メニューから書き出せた表情が **その他の表情** の一覧より少ないときに、一覧のとおりに記録して書き出し直す処理（`ResolveMenuShort`）。このアバターは表情メニューから 1 件も書き出せないので、この処理を 1 件も書き出せなかったときだけに絞った `2026-10-21-board-keeps-menu-animation.md` の後も、通常では一覧のとおりに書き出し直す。
- 表情ボードで記録した喜怒哀楽などの枠の割り当て（`ExpressionPresetStore`）と、ボードを開いたときの自動の割り当て（`ApplyAutomaticPresets`）。

かんたんは「通常の確認を省いたもの」として出しているので、書き出す内容が通常と違うのは不具合として扱う。

## 決定

- かんたんも、画面に出さない表情ボードを準備して、通常と同じ `TransferExport` → `Save` で書き出す。書き出しの経路を 1 本にする。
- `ExperimentalExpressionCaptureWindow.PrepareHidden(avatar)` を足す。`CreateInstance` で作ったウィンドウを表示せずに「転送」の状態にし、`SelectSource` と `Prepare` を通常を開いたときと同じ順に呼ぶ。準備に失敗したとき・キャンセルしたときはここでウィンドウを消してから例外を投げ直す。
- `TransferExport` に `interactive`（既定 `true`）を足し、`Save` に渡す。かんたんは `false` を渡し、自動設定できなかった項目の確認（書き出し結果の一覧）を出さずに保存する。通常はこれまでどおり確認を出す。
- `AppTransferExport.Export` は、作者名の確認 → 利用条件の記録 → `PrepareHidden` → 書き出し、の順にする。準備のキャンセル（`OperationCanceledException`）は何もせず `null` を返し、それ以外の失敗はそのまま投げる。書き出しのあとは成否にかかわらず `DestroyImmediate` でボードを消す。`ExportDetailed` と共通の `ExportFrom` で、保存したときに転送の名前を記録して `saved` を呼ぶ。
- これまでのかんたんが持っていて表情ボードの書き出しに無かったものは、ボードの側に足して、かんたんが退行しないようにする。新しい特別扱いは足さず、`ExportSettings.Export` と同じ既存の部品を使う。
  - 表情ボードを準備するとき、まばたきの設定をアバターの書き出し設定（`ExportSettings.Blink`）から読む。手動ならその設定を、なしならまばたきなしにする。まばたきの設定画面で選んだときと同じ対応にする。自動（書き出し設定を保存していないアバターもこれになる）では、ボードのまばたきのオン・オフをそのままにする。新しく準備したボード（かんたん）では自動のまばたきになる。プレビューの作り直し（`RebuildKeepingAssignments`）では、その時点の選択を保つ。別のアバターに替えるとき（`ChangeAvatar`）は、まばたきをオンに戻してから保存した設定を当てる。前のアバターで切ったまばたきが残ると、新しく準備するかんたんと結果が変わるため。
  - 全身ポーズ（手動で足したポーズ・書き出さないポーズ・ポーズの表示名）も、アバターの書き出し設定（`ExportSettings.Poses`）から読んでボードのセッションに入れる。プレビューの作り直しでは、作り直す前のボードのポーズに置き換え、二重に入れない。
  - `ExperimentalExpressionCaptureSession.Export` の最初で `UniVrmOneClickExporter.ValidateSourceHumanoid`・`ValidateSourceHasMesh` を呼ぶ。書き出したバイト列は `ValidateOutputHasMesh` で確かめる。
  - 失敗した段階の扱いは、確認を出すかどうかで分ける。ボードの書き出し（通常）はこれまでどおり失敗した段階で止まり、対策画面を開く。確認を出さない保存（かんたん）は、これまでのかんたんと同じく **失敗した段階を省いて続ける** の設定（`ExportStageGuard.ContinueByDefault`）に従う。セッションの `Export` では設定を当てず、`Save` で確認を出さない転送（`TransferExport` の `interactive: false`）のときだけ当てる。ファイルへの保存（`SaveVrm`）は確認を出すかどうかにかかわらず止まる。セッションの `Export` で当てると、ボードの対策画面のテスト（失敗した段階で材質の対策を出す）が成り立たなくなる。かんたんでも止めると、これまで材質の段階を省いて書き出せていたアバターが書き出せなくなる（29 体の書き出しで 1 体あった）。
  - 自動設定できなかった項目の確認を出さない保存（かんたん）では、これまでのかんたんと同じく、項目があれば書き出し結果の一覧を Console に警告で出す。
- ボードに直すところがあって `TransferExport` が止まるとき、かんたん（`interactive: false`）では「表情の割り当てを直すと作れます」に続けて、改行してボードの理由（`BoardBlockReason`）を返す。ボードが画面に無いので、何を直すかをここで示す。通常はボードの下の行に理由が出ているので、これまでどおり 1 行だけ返す。
- 書き出しの差し替え口だった `AppTransferExport.SaveNormally` は消す。テストは差し替えずに実際に書き出して確かめる。
- かんたんの画面の上の行を「表情は名前から自動・書き出さないオブジェクト N件」から「表情は前回の割り当て（なければ自動）・書き出さないオブジェクト N件」に変える。各言語の訳も置き換える。

## 選ばなかった案

- `Save` の中身（一覧のとおりに書き出し直す処理と自動の割り当て）を共通のクラスに切り出し、かんたんの書き出しからも呼ぶ。`Save` はボードのセッション・記録した一覧・プレビューの状態に深く結び付いていて、切り出すとボードの外に同じ状態を作り直すことになる。2 本の経路が残り、また片方だけに処理が足される。
- 同じアバターの表情ボードがすでに開いていれば、それを使って書き出す。開いているかどうか、そのボードで何を変えたかで、かんたんの書き出す内容が変わってしまう。かんたんは「何も変えない通常」と同じであるべきなので、毎回新しく準備する。
- 画面に出さないボードではプレビューの準備（`PreviewRenderUtility` とメッシュの確認）を省いて速くする。プレビューを作れないときに書き出しを止める判断（保存した書き出さないオブジェクトでメッシュが無くなるなど）もこの準備の中にあり、省くと通常と止まる条件が変わる。

## 変わること

- かんたんの書き出す内容は、表情ボードで何も変えずに通常で書き出した場合と同じになる。前に表情ボードで決めた枠の割り当てもかんたんに入る。
- 保存した書き出さないオブジェクトでプレビューを作れないなど、表情ボードに直すところがあるときは、通常と同じく「表情の割り当てを直すと作れます」で止まる。かんたんでは、その後に直すところの理由を続けて表示する。
- 表情ボード（「VRM書き出し」と通常）も、アバターに保存したまばたきと全身ポーズの設定で開く。Humanoid でないアバター・メッシュの無いアバター・メッシュが残らない書き出しは、どの段階にも入る前か保存の前に理由を出して止まる。
- 一括書き出しは `ExportSettings` の経路のまま。名前から neutral と視線を埋める処理は、表情ボードの書き出しにも入っている（`2026-10-21-board-infers-gaze-neutral.md`）ので、かんたんでもこれまでどおり埋まる。喜怒哀楽を名前から埋める処理は一括書き出しにだけ残り、かんたんではボードの自動の割り当てが入る。

## 確認したこと

- Unity 2022.3.22f1（VRChat SDK・UniVRM）で関係する EditMode テスト 10 クラスを回した。外部のアプリを入れていないと動かない 4 件を除いて通った。
- 29 体をかんたんと通常で書き出した。27 体で表情の名前・数が一致し、残る 2 体はどちらの書き出しでも同じ理由で止まる。そのうち 1 体は Humanoid でないアバターで、これまでの表情ボードは Humanoid のボーンが 0 本の VRM を書き出していた。VRM として正しくないので、理由を出して止まる今の形にした。メッシュの数・頂点数・材質は修正の前後で変わらない。
- `NonInteractiveTransferFollowsTheStageChoice`: 材質の段階が失敗するアバターで、確認を出さない `TransferExport` が **失敗した段階を省いて続ける** の設定がオンなら保存し、オフなら保存せずに対策画面を開く。
- 足したテスト（`Tests/Editor/AppTransferQuickExportTests.cs`）:
  - `QuickTransferWritesTheExpressionsOfAnUntouchedBoard`: 名前からは選ばれない形で喜びの枠を記録しておき、かんたんの書き出しと、同じアバターで準備した表情ボードの `TransferExport` の書き出しで、VRM の標準の表情とカスタムの表情の名前が一致し、喜びの枠に記録した形が入る。
  - `QuickTransferLeavesNoBoardBehind`: かんたんの書き出しのあと、準備に失敗したあとのどちらでも、表情ボードのウィンドウが残らず、`ForTransfer` が `null` になる。
  - `TheHiddenBoardUsesTheSavedBlink`: 書き出し設定のまばたき（自動・手動・なし）が、準備したボードの書き出しのまばたきの設定になる。
  - `TheBoardExportKeepsTheStageChoiceItIsGiven`: ボードのセッションの `Export` が、渡した `ExportRecoveryReport` の `ContinueOnStageFailure`（止まる）を書き換えない。
  - `TheHiddenBoardUsesTheSavedPosesOnce`: 書き出し設定の書き出さないポーズとポーズの表示名が準備したボードに入り、プレビューを作り直しても二重にならない。
  - `ABlockedHiddenBoardSaysWhatToFix`: 直すところがあるボードの `TransferExport` が、`interactive: false` では理由を続けて返し、`interactive: true` では 1 行だけ返す。どちらも保存しない。
  - `ChangingTheAvatarTurnsTheBlinkBackOn`: `ChangeAvatar` でまばたきがオンに戻る。
- 失敗した段階で対策画面を出すボードのテスト（`ExperimentalExpressionCaptureTests.PrimaryWindowOffersMaterialRecoveryAndSavesRetryWithoutChangingSource`）は、変えずにそのまま通った。
- 書き換えたテスト（`Tests/Editor/ExportSettingsTests.cs`）:
  - `AppTransferExportsWithTheAvatarsRememberedSettings`: `PrepareHidden` で準備したボードのセッションが、記録した書き出さないオブジェクトとギミックの選択を持ち、かんたんで書き出した VRM に記録した利用条件が入る。
  - `AppTransferRemembersTheNameOnlyAfterASave`: メッシュの無いアバター（メッシュの入っていない SkinnedMeshRenderer だけを持つ）では、ボードの準備は通り、書き出しが失敗して理由を返す（対策画面が開く）。名前は記録せず、`saved` も呼ばず、ファイルもできない。書き出せたアバターでは 1 回だけ `saved` を呼んで名前を記録する。
- `Tests/Editor/ExportAuthorTests.cs` の `QuickAppTransferSavesWithoutAskingAboutFallbacks` は差し替え口を使っていたので消した。上の 2 つのテストは `interactive: false` で書き出して保存まで進むことを確かめるが、どちらのアバターも自動設定できなかった項目が出るとは限らないので、項目があるときに確認を出さずに保存することまでは確かめていない。
