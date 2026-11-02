# 表情の変形の統合検査を 1 か所にし、無表情の検査が曲線ごとに階層を全走査するのをやめる

## 背景

- FaceEmo の共通 DEFAULT FACE（FaceEmoExpressions.ComposeCommonValues）と表情の焼き込み（VrChatExpressionBaker.Prepare）が、メニュー項目 1 つの値と曲線を「作者のパスと形」でまとめ、次に NDMF 処理後の「最終 Renderer と形」でまとめ直して食い違いを調べる同じ手順を別々に持っていた。違うのは、パスから Renderer を引く方法、食い違いの文言、Renderer が引けない channel を比べるかどうか（FaceEmo は比べない・焼き込みは引けない時点で例外）、同じ組の追加検査（焼き込みは基準形の一致）だけだった。
- 無表情の見た目の検査（NeutralShapePlan.HarmlessActivation・SelectedExpressionAppearance.IsUnboundTransform・IsUnchanged）は、曲線 1 本ごとに `GetComponentsInChildren<Transform>(true)` でアバター全体をたどりパスを比べていた。同じ対応表は NeutralShapePlan のコンストラクターと ExpressionTransformCurves が別々に作っていた。
- NeutralShapePlan.Create は allowUnchangedAppearance のとき同じ引数で UnchangedAppearanceClips を 2 回作っていた。
- VRCAvatarDescriptor を型名の文字列で探す同じ 2 行が 9 ファイルにあった。
- 中立の判定そのもの（所見 A2a-05 の A 案）は、事実確認で 7 つの条件の結果が変わると分かった。

## 決定

- 統合検査は `ExpressionChannelMerge.Groups` 1 つにし、呼び出し側は「パスから Renderer を引く関数」「食い違いの例外を作る関数」「引けない channel を比べないか」「同じ組の追加検査」を渡す。検査の順（値→曲線→曲線の初期値→最終 Renderer ごとの組）と例外の文言は変えない。組は 1 つずつ返し、呼び出し側の組ごとの処理は前と同じ順に走る。
- パスから Transform を引く対応表は `SelectedExpressionAppearance.TransformsByPath` でアバターごとに 1 回作り、検査に渡す。全走査と対応表はどちらも ordinal の比較で、同じパスが複数あるときの並びも `GroupBy` が元の並びを保つので同じになる。対応表を作った後に階層が変わる経路は無い（NeutralShapePlan は同じメソッドの中で既に対応表を引いており、サンプラーは複製の上で評価する）。対応表を渡せない 2 つの入口（FacialProjectionScope.NeedsProjection・VrChatGestureExpressions.ReadPose からの IsUnchanged）は省略時の全走査を残す。ReadClip の入口まで引数を足す変更は範囲を超える。FaceEmoExpressions.CaptureClip は型を指定した走査で、対応表に置き換えても並びが同じだと示せないので触らない。
- UnchangedAppearanceClips は invariantClips をそのまま使う。計算の位置が ValidateBindings の後になるが、この関数は自分では例外を投げず、同じ引数の Overrides と Controller は先に評価済みなので、例外の順は変わらない。
- Descriptor の探し方は既にある `VrChatExpressionMenu.Descriptor` に寄せる。探し方が違う 5 か所（型の判定だけ・有無だけ・複数を数える・ホストのハーネスがコンパイルする NdmfExportPreparation）は触らない。
- A2a-05 の A 案（中立の判定を 1 か所にする）はやらない。判定の結果が 7 つの条件で変わり、どの VRM が変わるかを 3 モデルの比較で示せないため。A2a-19(2)（切り詰めの整形の関数化）も、減る行数が 2 行で読みやすさが下がるのでやらない。

## 確かめたこと

- 統合検査の単体テスト 1 本で、引けないパスの例外が呼び出し側のまま届くこと、Renderer が null の channel は比べない指定のときだけ組に入らないこと、同じ最終 Renderer に 2 つの作者パスが来たとき先頭のパスが組を代表し追加検査が 1 回走ることを固定した。
- 統合検査を通る 11 クラス 216 件と、無表情の検査を通るクラス（InvariantMorphOwnershipTests・NeutralShapeSamplerTests・SelectedExpressionAppearanceTests ほか）が全 EditMode の中で緑。
- 全 EditMode、ホストのハーネス、書き出しの回帰比較は PR 本文に書く。
