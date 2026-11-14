# 書き出しのあとに、UnityのAnimatorとアプリの評価器の結果を突き合わせる

- 日付: 2026-11-14
- 対象: `Editor/AvatarAnimatorCrossCheck.cs`、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、ロケール、`Tools/ExportRegressionRunner.cs`、`Tests/Editor/AvatarAnimatorCrossCheckTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`
- Issue: #774

## 背景

アプリは`VRVLOG_avatar_animator`を評価器（`Formats/Runtime/AvatarAnimatorEvaluator.cs`）で再生する。評価器は`Tests/AnimatorComparison`の小さく組んだコントローラーでUnityの`Animator`と全フレームで一致させてあるが、実際のアバターのFXは、書き出しが捨てた曲線、ビルドの処理が作ったレイヤー、VRChatのビヘイビア、評価器が測っていない組み合わせを含む。アバターごとに、書いた拡張を評価器で再生した結果がUnityと同じになるかは確かめていなかった。

評価器は#784で`Formats/Runtime`に置いてあり、アプリはこのパッケージをそのまま取り込む。この変更では評価器に手を入れず、書き出しの側に突き合わせを足す。

## 決定

- 書き出しの最後に、ファイルを作り終えてから（`AvatarFormatRequirements.Write`の直前）、拡張を書いたときだけ突き合わせる（`AvatarAnimatorCrossCheck.Check`）。Unityの側は、書き出しに使った複製のルートのAnimatorに、拡張を読んだFXのコントローラー（Override Controllerはそのまま）を付けて再生する。複製はこの後に消すだけで、ファイルの中身は変わらない
- 入力は次の3種類で、どれも静止の状態（各ターゲットの`rest`に戻し、`Animator.Rebind`し、全パラメーターを拡張の`default`にする）から始める
  - 入力なし（最初の状態のまま進める）
  - メニューの各項目。トグルとボタンはそのパラメーターを値にする（最初からその値のトグルは0にする）。ラジアルは最初のパラメーターを1（最初が0.5以上なら0）、2軸・4軸は各パラメーターを1にする。サブメニューはパラメーターがあれば値にして、中の項目もたどる。同じ値の組になる項目は1回だけ数える
  - 左右の手の形の1〜7（`GestureLeft`・`GestureRight`がintであるとき）。`GestureLeftWeight`・`GestureRightWeight`があれば1にする
- 両方を60fpsで30フレーム進めてから入力を入れ、さらに150フレーム（合わせて3秒）進める。毎フレーム、`active`のターゲットは表示（`activeSelf`）が同じか、`morph`のターゲットはUnityのBlendShapeの重みの差が0.05以内かを比べ、項目ごとに最初に合わなかったフレームとターゲットを記録する
- 合わない項目は結果一覧に1行で出す（「アプリの再生がUnityと合わないメニューの項目・手の形（N項目中M項目）: …」）。名前は8個まで並べ、残りは件数にする。全部が合ったとき、評価器が再生できないデータ（`Supports`がfalse）のとき、突き合わせが失敗したときは結果一覧に出さない。利用者が直せることが無いため。数と理由はConsoleに`VR Vlog animator check: `で始まる行で出す（項目の数・比べた数・合わない数・飛ばした数・時間切れの数・ターゲットの数・比べられなかったターゲットの数・秒、合わない項目ごとのフレーム・ターゲット・両方の値）
- 回帰の確認の`Tools/ExportRegressionRunner.cs`は、この行を各ジョブのJSONの`animator_check`に残す。合わない項目の数をアバターごとに数えるため
- VRChatのビヘイビアは素のUnityでは動かないので、突き合わせの間だけSDKのフック（`VRC_AvatarParameterDriver.OnApplySettings`と、`VRC_AnimatorLayerControl`・`VRC_PlayableLayerControl`の`Initialize`から渡る`ApplySettings`）に入る。型は名前で探し、SDKが無ければ何もしない
  - Parameter Driverは、拡張に書くのと同じ読み方（`AvatarAnimatorCapture.ReadRunningBehaviour`）で操作を読み、評価器と同じ式でパラメーターを変える。Randomは両方に同じ乱数の列を渡す。ビヘイビアの動く順番が違えば値が変わり、差として出る
  - FXのAnimator Layer Controlは、次のフレームからレイヤーの重みを動かす（評価器のM10と同じ）
  - FXのPlayable Layer Controlが動いた項目は比べない（飛ばした数に入れる）。素のUnityにはFX全体の重みが無いため
- 1回の書き出しで突き合わせにかける時間は60秒までにする。超えた後の項目は比べず、時間切れの数に入れる（最初の入力なしの項目は必ず比べる）
- `active`のターゲットは複製のそのオブジェクト、`morph`のターゲットは読んだ元のレンダラーとBlendShape（逆向きのモーフターゲットでも元の方）でUnityの値を読む。書き出しの処理でメッシュからそのBlendShapeが無くなったターゲットは比べず、数だけを出す
- 突き合わせは`AvatarAnimatorCrossCheck.Enabled`で止められる（既定はオン）。止めても書き出すファイルは同じ

## 検討した代替案と不採用理由

- 拡張を読む時点（`ValidatePreparedHumanoid`の直後）の複製で再生する: Animatorを動かすと複製の表示とBlendShapeが変わり、その後の書き出しの結果が変わる。ファイルを作り終えた後なら影響が無い
- 書き出した後に元のアバターを別に複製して再生する: ビルドの処理と最適化をもう一度かけないと、拡張のターゲットと同じ物にならない。時間もかかる
- 合わない項目が無いときも結果一覧に「一致した」と出す: 利用者がすることが無く、行が増えるだけ。数はConsoleと回帰の確認のJSONで見られる
- Unityの側でビヘイビアを走らせず、そのまま比べる: Parameter Driverで動くアバターでは、評価器だけがパラメーターを変えて全部が合わなくなる。テストで、フックを外すと全項目が合わなくなることを確かめている

## 既知の制限

- Parameter DriverとLayer Controlの働き（M8・M10）は評価器と同じ仮定でUnityの側を動かしているので、その仮定自体がVRChatと同じかはこの突き合わせでは分からない。分かるのは、ビヘイビアが動く時刻（Unityがどのフレームで`OnStateEnter`を呼ぶか）と、その後の遷移と混ぜ方の違い
- 入力は1つずつ静止の状態から入れる。2つ以上の入力の組み合わせ、入れた入力を戻す動き、3秒より後の動きは比べない
- Contact・PhysBone・OSCなどで動くパラメーター、`IsLocal`・`Viseme`などのVRChatが入れるパラメーターは、拡張の`default`のまま動かさない
- マテリアル・Transformの曲線は拡張に書いていないので比べない

## 確かめたこと

- （回帰の確認の結果を書く）
