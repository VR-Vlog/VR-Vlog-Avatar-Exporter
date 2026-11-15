# 書き出しで、Gesture と Action の遷移の省略をそのコントローラーの omitted に入れる

- 日付: 2026-11-16
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-2c）

## 背景

段5-2b（`2026-11-16-avatar-animator-controller-omitted.md`）で、Gesture と Action のコントローラーに `omitted` を置ける形にし、評価器はそこに遷移の省略があればそのコントローラーだけを飛ばすようにした。書き出しはまだ全体の `omitted` に入れていたので、Gesture や Action だけで出る遷移の省略（Gesture のクリップが動かす int のパラメーター、型の違うパラメーターを読むステートの設定など）でも、書き出した VRM では FX を含めた全体を断っていた。

## 決定

- `Capture` が Gesture と Action を読む間は、`omitted` の数をそのコントローラー専用の辞書に入れ、読み終えたら FX の辞書に戻す
- `Resolve` で、各コントローラーの辞書のうち `AvatarAnimatorData.TransitionOmittedKinds`（`parameter`・`operation`・`unreadParameter`）をそのコントローラーの `omitted` に `OmittedKinds` の順で入れる。ほかの種類（曲線の省略など）は今までどおり全体の `omitted` に足す。種類ごとの合計は変更前と同じ
- 書き出しの結果に出す「入れなかったアニメーションの曲線」の行（`OmittedCurvesLine`）は、コントローラーの `omitted` の `parameter` も足して数える。行の中身は変更前と同じになる
- `Tools/compare-exports.py` は変えない。拡張の `omitted` で扱うのは `--allow-kept-morphs` が外す全体の `blendShape` だけで、これはコントローラーに移らない

## 影響

- FX だけのアバター、Gesture と Action に遷移の省略が無いアバターの VRM は変わらない
- Gesture か Action に遷移の省略があるアバターでは、その数が全体の `omitted` からそのコントローラーの `omitted` に移る。アプリの評価器はそのコントローラーだけを飛ばし、FX を回す
- そのアバターでは評価器の `Supports` が false から true に変わるので、書き出しの中で評価器を使う所も動くようになる。表情（拡張の `expressions`）は、これまで評価器で比べられずに全部外していたものを、比べて一致したものだけ書く。書き出し後の突き合わせ（`AvatarAnimatorCrossCheck`）は、これまで「回せない」で止めていたものを実際に比べ、差があれば警告に出す。どちらも段5-2 で Gesture・Action を飛ばせるようにしたときの意図どおりの動きで、FX だけのアバターでは変わらない
- 段5-2 の後から段5-2b の前までの読み手（コントローラーのキーを `playable` と `layers` だけ受ける）は、この VRM の拡張を読まない。その範囲をアプリに固定しないことは段5-2b の決定記録に書いた

## 限界

- Parameter Driver の操作（`operation`）は、書いたコントローラーの `omitted` に入る。VRChat のドライバーは他のコントローラーのパラメーターも変えるので、Gesture が飛ばされたとき、Gesture のドライバーが変えるはずだった Action の値は評価器では変わらない。飛ばしたコントローラーのドライバーが働かないのは段5-2 の決まり（M15）と同じ扱いで、この変更で新しく生じたものではない

## 確かめたこと

- EditMode の `AvatarAnimatorCaptureTests` が 85 件とも通る。足した `AGestureOrActionKeepsWhatItLeavesOutOfItsTransitions` と直した `GestureAndActionControllersAreWrittenWithMasks` は、変更前の書き出しでは失敗する（遷移の省略が全体に入る）
  - Gesture と Action の `unreadParameter` がそれぞれのコントローラーに入り、全体には残らない。評価器の `Supports` が通り、2 つだけを飛ばす
  - Gesture と Action のクリップの曲線の省略（`muscle`）は全体に入る
  - 同じ省略が FX にあると全体に入り、`Supports` が断る
  - Gesture の `parameter` の曲線は Gesture に入り、結果の行には今までどおり数える
- 回帰のアバター 11 体（Gesture か Action を持つもの 6 体）を変更前と変更後で書き出し、`Tools/compare-exports.py` で 11 組とも一致した。この 11 体には Gesture と Action の遷移の省略が無く、どれも出力が変わらない。省略が移る場合はテストでだけ確かめている
