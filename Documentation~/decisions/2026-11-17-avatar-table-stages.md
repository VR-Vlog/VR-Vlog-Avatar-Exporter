# PhysBoneとConstraintの表も、書けなかったら段階の省略として伝える

- 日付: 2026-11-17
- 対象: `Editor/UniVrmOneClickExporter.cs`・`Editor/ExportStageGuard.cs`・`Editor/AvatarConstraintsExport.cs`・`Editor/AvatarPhysBonesExport.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExportStageGuardTests.cs`・`Tests/Editor/AvatarAnimatorCaptureTests.cs`・`CHANGELOG.md`
- 前の記録: `2026-11-16-avatar-table-failure.md`・`2026-11-16-missing-mesh-warning.md`

## 背景

`2026-11-16-avatar-table-failure.md`で、エフェクトとContactの表を書けなかったときは段階の省略として書き出しの注意と結果一覧に出すようにした。そのとき次が残った。

1. PhysBoneとConstraintの表は、書けなくてもコンソールの例外（`Debug.LogException`）にしか出ない。
2. 表の注意（`LeftOut`）が出る・出ないを、書き出し全体を通して確かめるテストが無い。
3. 表の段階をstrictモード（段階が失敗したら書き出しを止める設定）で失敗させるテストが無い。

エフェクトとContactの表も、読み取り（`Capture`）で失敗したときはコンソールにしか出ていなかった。

## 決めたこと

### PhysBoneとConstraintの表に段階を足す

`ExportStage`に`AvatarConstraints`（表示名「アプリで動かすConstraint」・骨格）と`AvatarPhysBones`（「アプリで動かすPhysBoneのパラメーター」・揺れ物）を足した。表を組む（`Resolve`と`Checked`）・書く（`Inject`）は`guard.Optional`を通す。書けなかったときは、エフェクトとContactの表と同じく次の2つに出る。

- 書き出しの注意: `書き出しの一部を省略しました: 骨格: アプリで動かすConstraint — <例外のメッセージ>`
- 書き出しの結果一覧: 「省略した機能」の行

書けなかったときにFXを作り直す流れは今までと同じにした。Constraintの表ならFXの`enabled`の対象（`component`が`constraint`）を外し、PhysBoneの表ならパラメーターの`builtin`を`animator`に戻して、FXを1回作り直す。この2つの表はFXより前に書き、そのあとのFXを書く繰り返しが作り直したFXを書くので、作り直しは1回で足りる。

`OptionalConstraints`（例外をコンソールに出して続ける）は使うところが無くなったので消した。Constraintの表は「今の姿勢のまま残す」と結果一覧に出ているConstraintに設定を足すだけなので、書けなくても一覧に出さない、という考えだった。ただ、アプリでConstraintが動かない理由がコンソールにしか残らず、エフェクトとContactの表と扱いが違うので、ほかの表にそろえた。

### 4つの表の読み取りも段階を通す

エフェクト・Contact・PhysBoneの表を読み取る`Capture`も`guard.Optional`を通す。読み取りで失敗したら、その段階を省略して、表の無いファイルを書く。Constraintの表は`ConstraintExport.Convert`の中で読むので、今までどおり「コンストレイントの変換」の段階が受け持つ。

段階の省略は書き出し全体で1回なので、読み取りで省略した表は、そのあとの組み立てと書き込みも試さない。

### 書き込みに失敗を差し込むフックを足す

テストが書き込みだけを失敗させられるように、`AvatarConstraintsExport.Inject`と`AvatarPhysBonesExport.Inject`でも`ExportTestHooks.TableFaultInjection`を拡張の名前で呼ぶ。エフェクトとContactの表と同じ形。

## 新しいテスト

- `ExportStageGuardTests.AStrictExportLeavesOutATableItCannotWrite`: strictモードで、4つの表それぞれについて、書き込みだけが失敗しても書き出しが止まらず、その拡張が無いファイルを書き、結果一覧に段階の行が1つ出る。先に失敗させずに書き出し、fixtureの表が書かれることも確かめる。
- `ExportStageGuardTests.AStrictExportLeavesOutAStageThatOnlyAddsData`: `AvatarAnimator`だけだったのを、表の4つの段階にも広げた（5件）。
- `ExportStageGuardTests.EveryStageCanFailAndTheFileIsStillWritten`: 段階の一覧から作るので、新しい2つの段階も入る。
- `AvatarAnimatorCaptureTests.ATableTellsWhatItLeftOutOnlyWhenWritten`: 書き出し全体で、ライトが上限を1つ超えるとエフェクトの注意が、Receiverが上限を1つ超えるとContactの注意が1行出て、段階の注意は出ない。表の書き込みが失敗すると、表の注意は出ず、段階の注意が1行出る。
- `AvatarAnimatorCaptureTests.AConstraintsTableTheExportCannotWriteIsNotNamed`・`APhysBonesTableTheExportCannotWriteIsNotNamed`: 表を書けなかったとき、拡張が無く、FXがConstraintを指さず（PhysBoneのパラメーターは`animator`に戻り）、段階の注意が1行出る。

## 採らなかったこと

- PhysBoneとConstraintの表にも、エフェクトとContactの表と同じ繰り返しの作り直しを使う: 表をFXより後に書くことになり、拡張の並び順が変わってファイルが変わる。今の順なら1回の作り直しで足りる。
- 表の失敗に専用の注意の文を作る: `2026-11-16-avatar-table-failure.md`と同じ理由（ほかの段階の省略と並び方・対処の案内がそろわなくなる）。

## 残したこと

- 書き出しを最初からやり直したとき、前の試行で省略した表を試さないのは`2026-11-16-avatar-table-failure.md`のとおり。
- 失敗を差し込むフック（`ExportTestHooks.FaultInjection`）は、表の段階では最初に`guard.Optional`を通るところ（読み取りか組み立て）で発火する。書き込みだけを失敗させるテストは`TableFaultInjection`を使う。

### #839の残り（直さない）

`2026-11-16-missing-mesh-warning.md`の変更のレビューで残った2つは、今回は直さない。

- 書き出しが途中（NDMFや最適化ツールの段階）で失敗すると「メッシュが見つからず省略」の注意が出ない: 注意は、ビルドの処理がメッシュを入れたかどうかが分かった後の複製でしか正しく出せない。早い段階で出すと、ビルドの処理でメッシュが入る部分にも「VRMに入りません」と出る（その記録の「採らなかったこと」の最初の案）。書き出しが失敗したときは失敗の理由が先に出て、直したあとの書き出しで注意が出るので、今のままにする。
- 理由の文が、日本語以外の言語でも日本語のまま書き出しの注意に入る: ギミックの注意（「自動除外せず保持」など）と同じく、書き出しの注意の文は日本語で作り、転送の結果画面が種類と理由を訳す形になっている。この1行だけを訳して入れると、ほかのギミックの注意と扱いが分かれる。書き出しの注意を言語ごとに作る形へ変えるなら、ギミックの注意とまとめて直す。

## 確かめたこと

- `Tools/test-compatibility-tooling.py`が通る（75件）。
- EditModeテスト（Unity 2022.3）はこの記録を書いた時点で結果が出ていない。結果はPRの本文に書く。
