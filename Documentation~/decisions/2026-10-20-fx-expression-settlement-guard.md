# FX の条件から見つけた Blend Tree の表情は、時間で決めた 1 フレームで書き出さない

- 日付: 2026-10-20
- 対象: `Editor/VrChatFxExpressions.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/DirectFxExpressionDiscoveryTests.cs`・`Tools/run-unity-compatibility.py`・`CHANGELOG.md`

## 背景

`2026-10-19-expression-settlement.md` で、選んでから 2 秒で止まらない表情を Animator の時間の流れから最終状態を決めて書き出すようにした。`VrChatFxExpressions` には、この処理を通る経路が 2 つある。

- `Add`: FX の条件・Blend Tree の値から見つけた候補を読む。`SampleFixed` に表情のメッセージ（`entry.Messages`）をそのまま渡していた。
- `ReadGestureState`: Gesture の手のステートを読む。時間の決定が要った候補のステートが Blend Tree なら例外を投げ、アニメーションとして読む経路（`AnimatedGestureTree.Read`）に回していた。

レビューで次の 2 点の指摘を受けた。

- `Add` に Blend Tree の候補が来るなら、`ReadGestureState` と同じ扱いが要る。来ないなら、その理由をコードに残してほしい。
- `ReadGestureState` が投げる例外の文言に、成功を表す警告（`settlement[0]`、「…確定しました」など）を使っていた。

`Add` には Blend Tree の候補が来る。`Discover` は、ステートの motion が Blend Tree なら、その入力の値ごと（`Points`）に候補を作る。既存の `OneDimensionalTreePublishesAuthoredSelectionsThroughNativeBlending` がその例で、名前に値を付ける処理（`FX / ステート名 (値)`）もこのためにある。子のクリップに時間で動く BlendShape があると、`ValidateFixedPose` が「最後のキーで保存しました」などの警告を付けて、動いている 1 つの子の最終キーを混ぜた 1 フレームを返していた。2026-10-19 の変更より前は、この候補は「時間で変わるBlendShape・パラメーター曲線は固定表情に変換できません」で外れていた。

## 決定

- `Add` でも、時間の決定が要った候補のステート（Override・Synced Layer を反映した motion）が Blend Tree なら、1 フレームを採らない。FX の経路には Blend Tree をアニメーションとして読む処理が無いので、候補は外す（`entry.Error`）。警告は「時間で変わるFXのBlendTreeは、時間で決めた1フレームの固定表情に変換できません。」（`VrChatFxExpressions.SettledFxTreeOmitted`）。書き出しの警告に出るので、英語・韓国語・中国語（簡体・繁体）の訳を足した。
- `Add` は `SampleFixed` に専用のリストを渡し、この判定を通ったあとで警告を `entry.Messages` に移す。外した候補に「確定しました」などの警告は残らない。
- `ReadGestureState` が投げる例外の文言を「BlendTreeの表情は時間で決めた1フレームではなくアニメーションとして読みます。」（`VrChatFxExpressions.SettledTreeIsAnimated`）にした。この例外は同じ関数の中で受け、Blend Tree なので必ず `AnimatedGestureTree.Read` に回る（読み方の候補として作っただけのものは捨てる）。文言が使う人に見えることは無いので、訳は足していない。
- 判定は `ReadGestureState` と同じく「時間の決定が要ったか」と「候補のステートが Blend Tree か」だけで行う。時間で動いているのが別のレイヤー（手の筋肉のループなど）でも、Blend Tree の候補は外れる。2026-10-19 の変更より前もこの候補は外れていたので、リリース済みの版から書き出せなくなる表情は無い。

## 確かめたこと

- `DirectFxExpressionDiscoveryTests.MovingTreeKnotIsNotSettledToOneFrame` を足した。1D の Blend Tree の片方の子が 30 秒かけて BlendShape を動かすとき、その値の候補が専用の警告で外れ、値と「確定しました」などの警告が残らないことを確かめる。このガードを外した状態で回すと、候補は警告付きで書き出されてテストが落ちる（`Error` が null）ことも確かめた。
- Unity 2022.3.22f1 の EditMode テストで、表情・既定表情・FX の候補・レイヤー制御・Gesture・工程まわりの 23 クラスを回した。1340 件のうち 1334 件が通った。2 件は APL が入っていないときに飛ばすテスト。落ちた 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` の APL のケースで、テスト用のプロジェクトに APL が入っていないことによる（この変更の前から落ちる）。
