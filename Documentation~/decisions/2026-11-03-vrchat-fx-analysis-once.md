# FX の解析を 1 回の書き出しで 1 つ作り、項目ごとのループの外から渡す

## 背景

- VRChat の表情を読む Analyze は、項目（メニューの表情・ジェスチャー・一覧の顔）ごとに同じ FX コントローラーの走査・パラメーターの型の表・止まっている層の判定・ランダムな休止の証明を繰り返していた。繰り返しを避けるために静的な ExpressionInspectCache.Current を Analyze の間だけ立て、各所が「今キャッシュがあれば使う」と暗黙に参照していた。
- 走査の結果 Layer は呼び出し側が絞り込みで書き換えるので、キャッシュは Layer を毎回複製して返していた。
- 表情ボードの推定の Session が VrChatExpressionMenu.Source に載っていて、項目ごとのデータに書き出し全体の状態が混ざっていた。
- HeldAutomaticExpressionLayers は条件の標本値（しきい値・隣・中間）を作る写しを 2 か所に持ち、受け付ける条件の判定も AnimatorConditions の型つき評価と別に書いていた。

## 決定

- 1 回の書き出しで変わらない解析（走査・パラメーターの型・バインディング・動く曲線・止まっている層・定数・自動のチャンネル・ランダムな休止）を `FxAnalysis` にまとめ、Analyze が 1 つ作って引数 `analysis` で渡す。静的な current とキャッシュは消す。引数を受け取らない呼び出し（テストと境界の外）は今までどおり直接読む。
- 走査の結果は `FxAnalysis.Inspect` が配列を複製して返し、要素の Layer は共有する。Layer を書き換える側（ExpressionDependencies の 2 か所）は `Layer.Copy()` を取ってから書き換える。`SelectedLayerDriverWrites` は集合を複製して返す。
- 走査のメモの鍵は（コントローラー・除外パスの判定）で、同じ判定の委譲を渡したときだけ当たる。除外パスが変わる走査は保持しない（`Signature` が null を返す）。永続でないオブジェクトはメモしない。
- 表情ボードの Session は Source から外し、`FxAnalysis.FacialProjection` が持つ。`FacialProjectionScope.For(avatar, analysis)` で取る。
- 条件の標本値は `ConditionSamples` の 1 か所で作り、受け付けるかどうかは `AnimatorConditions.Evaluate(condition, 0, type)` が null を返すかで決める（Bool の If/IfNot だけ・Int の整数のしきい値だけ・Float の Greater/Less だけ、という旧判定と同じ）。一定の曲線の値は `CurveChecks.Constant(curve, out value)` が返す値を使う。
- A2a-09 で消した「解析が 1 回で済む」テストは `HeldProbeRunsTheSelectionAnalysesOnce` として FxAnalysis の回数で数え直した。

## やらなかったこと

- A2a-18（表情 1 つ分の要求を 1 つの型にまとめる）: 前提が違った。ExpressionSettlement は固定する入力を受け取らず、まとめる 3 つの引数が揃って渡る呼び出しが無い。null で分岐する箇所が 19 あり、型にすると分岐が増える。
- A2a-22 の残り: solo／mute の意味（`!mute` と Enabled）は Unity の挙動どおりで統一できない。再帰の走査はパスを要るので共通型に載せない。IsFalse の Float.Equals は意図した比較。HeldAutomaticExpressionLayers の空の曲線の例外は残す。

## 確かめたこと

- 全 EditMode（lilToon 2.3.4・VRChat SDK 3.10.1・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.8.16）で基準に対して新しい赤 0。初回の時間切れ 1 件（他のビルドと重なった）はクラス単位の再実行で緑。
- ホストのハーネス 6 本・validate.py・test-package.py・test-compatibility-tooling.py が緑。
- 書き出しの回帰比較と所要時間は PR 本文に書く。
