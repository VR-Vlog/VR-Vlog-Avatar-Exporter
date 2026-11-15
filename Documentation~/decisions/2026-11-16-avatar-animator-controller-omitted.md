# Gesture と Action の omitted をそのコントローラーに置き、遷移を変える省略ではそのコントローラーだけを飛ばす

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`、`Tools/AvatarAnimatorEvaluatorTests.cs`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-2b）

## 背景

段5-2（`2026-11-16-avatar-animator-all-controllers.md`）で、評価器は回せない Gesture や Action を飛ばし、FX は今までどおり回すようにした。ただ、`omitted` は拡張の全体で 1 つしかなく、どのコントローラーを読んで出たものか分からない。そのため Gesture や Action を読んだときだけに出る遷移の省略（`parameter`・`operation`・`unreadParameter`）でも、FX を含めた全体を断っていた（段5-1 で Gesture・Action を書き出すようになってからのデグレ）。

## 決定

- Gesture と Action のコントローラーに、任意の `omitted` を足す。そのコントローラーを読む間に書き出しが入れなかったもののうち、遷移を変える種類（`parameter`・`operation`・`unreadParameter`、`AvatarAnimatorData.TransitionOmittedKinds`）だけをこの順に数え、後の版で足す種類はその後ろに置く。空にはしない。ほかの種類を置いた `omitted` は読まない
- 曲線の省略など、ほかの種類は今までどおり全体の `omitted` に置く。書き出しは曲線の一部（コマを超えた分・ノードの無い分など）を読み終えた後に全クリップまとめて数えるので、コントローラーごとに分けると読み取り時の分と後段の分が割れる。評価器が見るのは遷移を変える種類だけなので、そこだけを分ける
- FX のものと拡張の全体のものも全体の `omitted` に置く。FX のコントローラーに `omitted` があれば読まない（スキーマも `if`/`then` で断る）
- 評価器は、全体の `omitted` に遷移を変える種類があれば今までどおり断る。Gesture や Action の `omitted` にあれば、そのコントローラーだけを飛ばし（`SkipReason` に理由が出る）、FX は回す
- 書き出し（`AvatarAnimatorCapture`）を、Gesture・Action を読む間の遷移の省略をそのコントローラーの `omitted` に入れるように変えるのは、同じファイルを触る段6-2 のマージを待って別の PR で行う。そのときは、全体の `omitted` から数を読むユーザーへの注意書き（`OmittedCurvesLine`。`parameter` を数える）と `Tools/compare-exports.py` の集計も合わせて見直す。それまでは今までどおり全体に入るので、書き出しの結果は変わらない

## 古いアプリとの関係

- FX だけのデータは、コントローラーに `omitted` を書かない。FX のコントローラーのキーが `playable` と `layers` だけであることをテストで固定した
- `omitted` を持つのは Gesture か Action のコントローラーだけで、2 つ目以降のコントローラーになる。アプリが固定している Formats（5367b866）はコントローラーを 1 つまでしか読まないので、Gesture や Action を含む VRM はこの変更の前から読まない。キーが増えても古いアプリの読み方は変わらない
- 段5-2 の後（e76f77be）からこの変更の前までの読み手は、コントローラーを 3 つまで読むが、コントローラーのキーは `playable` と `layers` だけを受ける。この読み手をアプリに固定すると、書き出しの PR の後で Gesture や Action に遷移の省略がある VRM を拡張ごと読まない。アプリの固定はこの範囲で止めない

## 選ばなかった案

- 全体の `omitted` の各項目に、どのコントローラーのものかを示すキーを足す。同じ種類がコントローラーの数だけ並ぶので、種類ごとに 1 回・決まった順という今の決まりを変えることになる
- 種類の名前を分ける（`gestureParameter` など）。全体の `omitted` は新しい種類を後ろに許すので古い読み手は読めるが、Gesture と Action のどちらかを名前に入れることになり、コントローラーが増えるたびに種類が増える。コントローラーの中に置く方が、何のための数かが形から分かる

## 確かめたこと

- `Tools/run-avatar-animator-evaluator-tests.ps1` が通る（13695 件の確認、Unity の記録 105 件の再生、比較ケースは 255795 フレームが一致し 63242 フレームを飛ばす。変更前と同じ）
- 足したテスト `ControllerOmitted`:
  - FX だけのデータのコントローラーは `playable` と `layers` だけを書き、FX の遷移の省略は今までどおり断る
  - Gesture と Action のそれぞれで 3 種類の遷移の省略を持たせると、書いて読み直しても自分のものとして戻り、全体を断らず、そのコントローラーだけを飛ばして自分のターゲットを書かない。FX の値は飛ばしたコントローラーが無いときと毎フレーム一致する。省略を外すとそのコントローラーを回す。同じ省略を全体に置くと（今の書き出しの形）断る
  - Gesture は省略なし・Action だけありの 3 つのコントローラーで、Action だけを飛ばす
  - 新しい種類は読み、ほかの知っている種類・FX の `omitted`（JSON からも）・空・順番違い・重複は読まない
- `Tools/validate.py` が通る
