# エフェクトとContactの表を書けなかったときを、ほかの省略と同じ流れで伝える

- 日付: 2026-11-16
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportStageGuard.cs`・`Editor/AvatarContactsExport.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/AvatarAnimatorCaptureTests.cs`・`Tests/Editor/AvatarContactsExportTests.cs`・`Tests/Editor/ExportStageGuardTests.cs`・`CHANGELOG.md`
- 前の記録: `2026-11-16-avatar-table-robustness.md`

## 背景

`2026-11-16-avatar-table-robustness.md`の変更のレビューで、次の3つが残っていた。

1. 書けなかった表があるとFXを作り直すが、作り直しは1回だけだった。作り直したあとに別の表が書けなかったら、FXはその表を指したまま残る。
2. 対応していないContactしかないアバターでは、表を書かないので、数えた分がファイルにも書き出しの注意にも残らなかった。
3. 表を書けなかったとき、失敗はコンソールの例外（`Debug.LogException`）にしか出なかった。しかもContactの表を書けなくても「Contact Receiverが上限の…」の注意は出ていた。

## 決めたこと

### 表を書けなかったら、ほかの省略と同じ段階として扱う

`ExportStage`に`AvatarEffects`（表示名「アプリで動かすエフェクト」）と`AvatarContacts`（「アプリで動かすContact」）を足した。表を組む（`Resolve`）・書く（`Inject`）は`guard.Optional`を通す。書けなかったときは、ほかの段階と同じく次の2つに出る。

- 書き出しの注意: `書き出しの一部を省略しました: アプリで動かすContact — <例外のメッセージ>`
- 書き出しの結果一覧: 「省略した機能」の行。対処の欄に「表示された設定とConsoleを確認してください」が入り、「詳細をコピー」で原因の情報を写せる

表を書けなかったときは、その表の結果の注意（Receiverの上限など）を出さない。表が無いのに上限で外したと出ると、どちらが原因か分からなくなるため。

`Debug.LogException`だけを出していた`Written`は消した。

### FXは、指す表がすべて書けるまで作り直す

FX・エフェクトの表・Contactの表の順に書き、書けなかった表があれば、その表を指す対象を外してFXを作り直し、もう一度3つを書く。外す表は1回ごとに1つ以上増え、表は2つなので、書くのは多くて3回。

今の`Inject`は表のデータとノードの数だけで結果が決まり、FXを書いてもノードは変わらない。なので2回目に初めて失敗することは今は起きない。それでも作り直しを1回に決め打ちすると、この前提が崩れたときに、ファイルに無い表をFXが指すことになる。繰り返す形なら前提に頼らない。テストは2回目の書き込みだけ失敗させるフックで確かめた。

### Contactの表から外したものを、書き出しの注意に出す

`AvatarContactsExport.Capture`の`LeftOut`を1行から行の並びにし、次を出す。

- Receiverの上限（今までどおり）: `Contact Receiverが上限の{1}個を超えたため、{0}個をアプリで動かしません`
- Senderの上限: `Contact Senderが上限の{1}個を超えたため、{0}個をアプリで動かしません`
- 対応していない形・Receiverの種類（新しいSDKで足されたもの）をまとめて: `このバージョンのExporterが対応していないContact {0}個をアプリで動かしません。ALCOMでExporterを更新すると動く場合があります`

対応していないContactしかないときは表を書かないが、3つめの行は出る。前の記録の「ほかは結果の警告に出さない」を、Contactを丸ごと外すものについては改めた。数がまれでも、アプリでそのContactが動かない理由がどこにも残らないのは困るため。Tagの数とパラメーター名の長さで削ったものは、Contact自体は動くので、今までどおりファイルの`omitted`だけに書く。

4言語の訳を、2つの表示名と2つの文に足した。

## 古い出力との関係

書き出すファイルの形（`Formats/`）は変えていない。表を書けたときのVRMは変わらない。変わるのは、表を書けなかったときに注意と結果一覧に行が増えること、FXがその表を指さないこと、Senderの上限と対応していないContactの行が注意に出ることだけ。アプリの読み手の固定を上げる必要はない。

## 残したこと

- 書き出しを最初からやり直したとき（ビルドの処理の失敗で、その処理を省いてやり直すとき）、前の試行で書けなかった表は、やり直しでも試さずに省いたままにする。段階の省略は書き出し全体で1回と決まっているため（「FXとメニューの動き」と同じ）。ファイルの中身は食い違わない。
- PhysBoneとConstraintの表は、今も書けなくてもコンソールにしか出ない。同じ段階の扱いにそろえるのは別の作業にする。（`2026-11-17-avatar-table-stages.md`でそろえた）

## 採らなかったこと

- 作り直しを2回までに増やす: 回数に根拠が無く、表を足したら直す場所が増える。
- 表の失敗に専用の注意の文を作る: ほかの段階の省略と並び方・対処の案内がそろわなくなる。
- Tagの数とパラメーター名の長さも注意に出す: Contactは動くので、注意を増やすほどではない。

## 確かめたこと

Unity 2022.3.22f1・EditMode・batchmodeで回した。

- 変えた・足したテストは修正前に失敗する: 修正前のコードに新しいテスト（足した定数は同じ文字列に置き換え）を入れて`AvatarAnimatorCaptureTests`と`AvatarContactsExportTests`を回すと、116件のうち変えた・足した8件だけが失敗した。`ATableThatFailsOnTheSecondWriteIsNotNamed`は、FXの対象に`(contact, 0, 1, 25)`が残って失敗した（レビューの指摘どおり）。
- パッケージのEditModeテストを全件回した（VRChat SDK入りのプロジェクト）。Exporterのテストで失敗したのは、`ExportStageGuardTests`の新しい段階の4件（fixtureが段階に届いていなかった。下の「新しいテスト」で直した）と、ウィンドウを開くテスト61件だけ。61件は、修正前のコードで同じクラスを回しても同じ名前の61件が失敗した。ウィンドウを出せないセッションから回したためで、この変更とは関係しない。
- fixtureを直したあと、`ExportStageGuardTests`・`AvatarAnimatorCaptureTests`・`AvatarContactsExportTests`と上の61件のクラスを回し直した。749件のうち失敗は同じ61件だけ。
- 新しいテスト:
  - `AvatarAnimatorCaptureTests.ATableThatFailsOnTheSecondWriteIsNotNamed`: エフェクトの表は毎回、Contactの表は2回目だけ書けないとき、どちらの表も無く、FXがどちらも指さず、2つの段階の注意が1行ずつ出る。
  - `AnEffectsTableTheExportCannotWriteIsNotNamed`・`AContactsTableTheExportCannotWriteIsNotNamed`: コンソールの例外を期待する代わりに、段階の注意が1行出ることを確かめる。
  - `AvatarContactsExportTests`: Senderの上限と対応していないContactの行。対応していないContactしかないときも行が出る。
  - `ExportStageGuardTests.EveryStageCanFailAndTheFileIsStillWritten`: 2つの新しい段階も、失敗させてファイルが書け、結果一覧に1行出る。fixtureにライトかContact Receiverを足して段階に届くようにした。
