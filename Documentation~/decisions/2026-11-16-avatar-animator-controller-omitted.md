# Gesture と Action の omitted をそのコントローラーに置き、遷移を変える省略ではそのコントローラーだけを飛ばす

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Tools/AvatarAnimatorEvaluatorTests.cs`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-2b）

## 背景

段5-2（`2026-11-16-avatar-animator-all-controllers.md`）で、評価器は回せない Gesture や Action を飛ばし、FX は今までどおり回すようにした。ただ、`omitted` は拡張の全体で 1 つしかなく、どのコントローラーを読んで出たものか分からない。そのため Gesture や Action を読んだときだけに出る遷移の省略（`parameter`・`operation`・`unreadParameter`）でも、FX を含めた全体を断っていた（段5-1 で Gesture・Action を書き出すようになってからのデグレ）。

## 決定

- Gesture と Action のコントローラーに、任意の `omitted` を足す。そのコントローラーを読む間に書き出しが入れなかったもの（ステート・ビヘイビア・モーション・クリップ）を数える。中身は全体の `omitted` と同じ形（知っている種類を決まった順に、新しい種類はその後ろ、それぞれ 1 回）で、空にはしない
- FX のものと拡張の全体のもの（ターゲット・メニューなど）は今までどおり全体の `omitted` に置く。FX のコントローラーに `omitted` があれば読まない
- 評価器は、全体の `omitted` に遷移を変える種類があれば今までどおり断る。Gesture や Action の `omitted` にあれば、そのコントローラーだけを飛ばし（`SkipReason` に理由が出る）、FX は回す
- 書き出し（`AvatarAnimatorCapture`）を、Gesture・Action を読む間の省略をそのコントローラーの `omitted` に入れるように変えるのは、同じファイルを触る段6-2 のマージを待って別の PR で行う。それまでは今までどおり全体に入るので、書き出しの結果は変わらない

## 古いアプリとの関係

- FX だけのデータは、コントローラーに `omitted` を書かないので、変更前と同じ JSON になる（テストで固定）
- `omitted` を持つのは Gesture か Action のコントローラーだけで、2 つ目以降のコントローラーになる。アプリが固定している Formats は、コントローラーを 1 つまでしか読まないので、Gesture や Action を含む VRM はこの変更の前から読まない。キーが増えても古いアプリの読み方は変わらない

## 選ばなかった案

- 全体の `omitted` の各項目に、どのコントローラーのものかを示すキーを足す。同じ種類がコントローラーの数だけ並ぶので、種類ごとに 1 回・決まった順という今の決まりを変えることになり、全体の数を読む書き出し側の処理（ユーザーへの注意書きなど）も直す必要がある
- 種類の名前を分ける（`gestureParameter` など）。全体の `omitted` は新しい種類を後ろに許すので古い読み手は読めるが、Gesture と Action のどちらかを名前に入れることになり、コントローラーが増えるたびに種類が増える。コントローラーの中に置く方が、何のための数かが形から分かる

## 確かめたこと

- `Tools/run-avatar-animator-evaluator-tests.ps1` が通る（13674 件の確認、Unity の記録 105 件の再生、比較ケースは 255795 フレームが一致し 63242 フレームを飛ばす。変更前と同じ）
- 足したテスト: FX だけのデータのコントローラーは `playable` と `layers` だけを書き、FX の遷移の省略は今までどおり断る。Gesture と Action のそれぞれで 3 種類の遷移の省略を持たせ、書いて読み直しても自分のものとして戻ること、全体を断らずそのコントローラーだけを飛ばすこと、FX の値が飛ばしたコントローラーが無いときと毎フレーム一致すること。曲線の省略では飛ばさないこと。新しい種類は読み、FX のコントローラーの `omitted`・空の `omitted`・順番違い・重複は読まないこと
- `Tools/validate.py` が通る
