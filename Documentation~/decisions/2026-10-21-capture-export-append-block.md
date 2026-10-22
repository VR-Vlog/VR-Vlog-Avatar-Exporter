# 記録した表情を BlendShape として足す処理を `Export` のローカル関数にまとめる

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureSession.cs`（`Export`）

## 背景

`ExperimentalExpressionCaptureSession.Export` は、記録した表情を書き出し用の Mesh に BlendShape として足す。この処理（Renderer を探す、名前を決める、追加する頂点データの量を数えて 128 MiB を超えたら止める、`AvatarBaseShape.AppendExpression` で足す、足した BlendShape の重みを 0 にする、`MorphTargetBinding` を追加する）が、次の 3 か所に 8 行ずつ同じ形で書かれていた。

1. 記録した表情（`expressions`）の、基準の表情（`Baseline`）と値が違うメッシュごと
2. 1 で値が違うメッシュが 1 つもなかったときの、先頭のメッシュ（`Channels[0]`）に差分 0 の BlendShape を足す分
3. 標準の表情（`presetFaces`）の、基準の表情と値が違うメッシュごと

3 か所で違うのは、メッシュの番号、名前のハッシュに入れる種類（`"Capture"` か `"CapturePreset"`）と名前（`pose.Name` か `face.Preset.ToString()`）、目標の表情（`pose`・`Baseline`・`face.Pose`）、追加先の一覧だけだった。

## 決定

`usedNames` を作った直後に、ローカル関数 `AppendCapture(List<MorphTargetBinding> into, int channel, string kind, string key, Pose to)` を置き、3 か所をその呼び出しに替える。中身は置き換えた 8 行そのままで、上の違う値だけを引数にした。

## 書き出す内容が変わらない理由

- 関数の中の処理の順番（Renderer を探す → 名前を決める → `addedBytes` を足して上限を確かめる → `AppendExpression` → 重みを 0 にする → 一覧に追加）は置き換え前と同じ。
- 目標の表情は `Pose` のまま渡し、その `Rows[channel].Weights` は置き換え前と同じく `AppendExpression` の引数の位置で読む。基準の表情の `Baseline.Rows[channel].Weights` も同じ位置で読む。2 の分は `to` に `Baseline` を渡すので、置き換え前と同じく `Baseline.Rows[0].Weights` を 2 回読む。
- 呼び出しの引数として先に評価するようになったのは、名前（`pose.Name`・`face.Preset.ToString()`）と `Baseline` プロパティの読み取りだけ。どれも副作用がなく、`pose` と `face` はそれより前の行（`VrmMenuExpressions.UniqueName(pose.Name, ...)`・`CreatePresetExpression(face.Preset)`）で読んでいるので、ここで新しく例外が出ることはない。`Baseline` が `null` の場合も、例外が出るのは置き換え前と同じ `Baseline.Rows` を読む位置。
- `addedBytes` と `usedNames` はローカル関数から参照で使うので、足した量と使った名前は呼び出しをまたいで置き換え前と同じく積み上がる。ローカル関数はその場で呼ぶだけで、デリゲートにして後で呼ぶことはない。
- 上限を超えたときに出す例外の型と文言（`ExporterLocalization.T` の文字列）も変えていない。

## 確認したこと

- 確認: 検証用の PC での書き出し突き合わせと EditMode テスト（PR 本文に記載）
- テスト: 2 の分を `Export` から直接確かめるテストは足していない。試しに書いたところ、差分 0 の形状は書き出しの最後で「使われていない BlendShape」として VRM から外され、表情のバインド先は別の形状になった。この形状は出力から見えないので、2 の分は書き出し突き合わせで変わらないことを確かめた
