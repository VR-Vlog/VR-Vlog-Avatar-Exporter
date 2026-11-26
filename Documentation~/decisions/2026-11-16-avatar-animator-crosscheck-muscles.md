# GestureとActionを重ねてUnityと比べ、マッスルをHumanPoseHandlerで突き合わせる

- 日付: 2026-11-16
- 対象: `Editor/AvatarAnimatorCrossCheck.cs`、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tests/AnimatorComparison/`、`Tests/Editor/AvatarAnimatorCrossCheckTests.cs`、`Tools/Fixtures/AvatarAnimator/`（`gestureAction`・`actionMask`・`humanoidNodeMask`）

## 背景

書き出し後の突き合わせ（`AvatarAnimatorCrossCheck`）は、UnityでFXだけを再生していた。そのため、GestureとActionのマッスルは比べていなかった。比較テストも1つのAnimatorで1つのコントローラーを再生するので、コントローラーを重ねる順番と重みの規則（M15）や、コントローラーをまたぐマスク（M16）はUnityで確かめていなかった。

## 決めたこと

- 突き合わせは、マッスルのターゲットがあるとき、またはFXのほかに評価器が再生するコントローラーがあるときだけ`PlayableGraph`で再生する。`AnimationLayerMixerPlayable`にGesture・Action・FXの順でつなぎ、Actionの重みは0から始める。マッスルは`HumanPoseHandler.GetHumanPose`で読み戻し、許容差1e-3で比べる。FXだけのアバターは今までどおりAnimatorでFXを再生する
- GestureとActionのコントローラーは、Descriptorの`baseAnimationLayers`から取る。読み方は書き出し（`AvatarAnimatorCapture`）と同じ
- 比較テストは、レイヤーを持つコントローラーが2つ以上あるケースを同じグラフで再生する。データのビヘイビアはテストが再生する。フレームごとに、各レイヤーの現在のステートと遷移先のステートを見て、新しく入ったステートのParameter Driver（`set`）をすべてのコントローラーに書く。Playable Layer Controlは、次のフレームからミキサーの重みを動かす（M8・M10の仮定に合わせて当てる）。編集専用のアセンブリの`StateMachineBehaviour`はステートに付けられないので、この形にした
- 比較ケースを3つ足した
  - `gestureAction`: GestureのFistのDriverが`Emote`を立てる。ActionとFXはこれを読み、ActionはWaveのPlayable Layer Controlで重み1へ動く。FXのベースレイヤーはマスクを持たず、腕のマッスルを書くが効かない
  - `actionMask`: 左腕だけのマスクを持つActionのベースレイヤーと、Bodyだけのマスクを持つ重み0.5の2枚目のレイヤー
  - `humanoidNodeMask`: Humanoidのリグで、Transformのマスクがマッスル・GameObjectの有効・BlendShapeをどう扱うか
- 評価器（M16）を2点直した
  - マッスルを書くコントローラーは、Unityで測ると、自分のレイヤーが書かないマッスルもレストの値（0）で書き、下のコントローラーを上書きする（`gestureAction`: Actionの重みが上がるとGestureの指が0へ戻る）。評価器もそうする
  - コントローラーのベースレイヤーのマスクは、そのコントローラー全体のマッスルにかかるものとする。VRChatはPlayable Layerをベースレイヤーのマスクでマスクするとされ、FXの規則と同じく仮定として置く。VRChat SDKの無い素のUnityでは、上のレイヤーはこのマスクの外も書く（`actionMask`で測った）。そこで比較テストと突き合わせのUnity側は、各Playableをミキサー上でベースレイヤーの部位でマスクしてVRChatに合わせる。これはFXの規則と同じ扱いで、VRChat上での計測ではない

## 古い出力との関係

- 書き出しが評価器で確かめるのは、FXの表情（BlendShape）だけ（`FaceMismatch`）。どちらの変更もマッスルだけにかかる。FXはベースレイヤーにマスクが無ければマッスルを書かず、表情の判定は変わらない
- 既存の比較の記録（114件）は、Unityで記録し直してもバイト単位で同じだった

## 測れなかったこと

- VRChatが実際にベースレイヤーのマスクをPlayable全体にかけるか（Humanoidの部位とTransformの両方）。素のUnityでは測れない
- 秒数0のPlayable Layer Controlが同じフレームに効くか。比較テストではビヘイビアをフレームの後に当てるので、`actionMask`は0.2秒のブレンドにした
- マッスルのカーブを持たないコントローラー（マスク付きのFXなど）が、マッスルをレストで上書きするか
