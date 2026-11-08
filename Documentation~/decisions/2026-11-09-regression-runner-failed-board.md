# 表情ボードの準備に失敗したジョブで回帰ランナーが待ち続けないようにする

- 日付: 2026-11-09
- 対象: `Tools/ExportRegressionRunner.cs`

## 背景

`run-export-regression` のランナーは、表情ボードが何もしなくなるのを待ってから書き出す。表情ボードの準備が例外（表示するメッシュが無い、など）で失敗すると、次のジョブへ進まず `OpenTimeoutSeconds`（900 秒）まで待っていた。

待ちの条件 `Busy` は `pending` か `candidateValidationPending` を見ている。表情ボードの `Advance` は `candidateValidationPending` を `session` があるときだけ実行に回し、`Prepare` が失敗すると `session` が無いままフラグだけが残る。ランナーから見るといつまでも処理中になる。

## 決定

`Busy` を表情ボードと同じ条件にして、`session` が無ければ `candidateValidationPending` を待たない。準備に失敗したジョブはそのまま `open failed: <例外の文>` で次へ進む。

ジョブごとの時間切れは足さない。`OpenTimeoutSeconds` がすでにあり、書き出し自体は同期呼び出しなのでランナーの中からは打ち切れない。

## 検討した代替案と不採用理由

- 例外のログを拾って打ち切る: 表情ボードは `error` にも同じ内容を入れていて、待ちの条件を直せばそこで止まる。ログの文で判定すると別の警告を巻き込む。
- `OpenTimeoutSeconds` を短くする: 大きなアバターは準備に数分かかるので、正常なジョブが落ちる。

## 確かめたこと

検証用の PC で、メッシュが表示されないアバターを含むジョブ一覧を修正前と修正後のランナーで流して、`progress.txt` の `OPEN` の秒数を比べた。結果は PR の本文に書く。
