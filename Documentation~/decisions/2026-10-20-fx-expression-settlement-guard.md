# FX の条件から見つけた Blend Tree の表情は、時間で決めた 1 フレームで書き出さない

- 日付: 2026-10-20
- 対象: `Editor/VrChatFxExpressions.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/DirectFxExpressionDiscoveryTests.cs`・`Tests/Editor/ExpressionSettlementTests.cs`・`Tools/run-unity-compatibility.py`・`CHANGELOG.md`

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
- メニューの表情（`VrChatExpressionSampler.Analyze`）も同じ扱いにした。PR のレビューで、Radial Puppet のように Blend Tree のステートを選ぶメニューの項目が、2 秒より長く動く子のクリップの最終キーを混ぜた 1 フレームで「最後のキーで保存しました」の警告付きで書き出されると指摘を受けた。メニューの経路には候補のステートが無いので、`Evaluate` が時間の決定に入る直前に、時間の決定の対象になるレイヤーのうち、再生中のステートの motion（Synced Layer はその上書きと元のレイヤーをたどる）が Blend Tree のものを `SampleFixed` の呼び出し元へ返す。`Analyze` は、時間の決定が要ってそのようなレイヤーがあれば、同じ `SettledFxTreeOmitted` で項目を外す。
- 判定は `ReadGestureState` と同じく「時間の決定が要ったか」と「候補のステートが Blend Tree か」だけで行う。時間で動いているのが別のレイヤー（手の筋肉のループなど）でも、Blend Tree の候補は外れる。2026-10-19 の変更より前もこの候補は外れていたので、リリース済みの版から書き出せなくなる表情は無い。
- 残る制約: どの経路も、時間の決定を始めるときのステートの motion を見る。時間の遷移でたどり着いた先のステートが Blend Tree でも、このガードでは外れない。
- Synced Layer の上書きで Blend Tree になったステートは、いまは依存関係の解析が「このメニューに影響する同期Animatorレイヤーの表情変換は未対応です。」で先に外す。時間の決定には届かない。

## 確かめたこと

- `ExpressionSettlementTests.AMenuItemSelectingAMovingTreeIsNotSettledToOneFrame` を足した。Toggle で選ぶステートが 1D の Blend Tree で、選ばれる子が 10 秒かけて BlendShape を動かすとき、メニューの項目が専用の警告で外れ、値と「確定しました」などの警告が残らないことを確かめる。`Analyze` のガードを外すと `Error` が null になって落ちることも確かめた。
- `DirectFxExpressionDiscoveryTests.SyncedTreeOverrideIsNotSettledToOneFrame` を足した。Synced Layer がステートの motion を動く Blend Tree で上書きするとき、その値の候補が値も時間の決定の警告も持たずに外れることを確かめる（いまの外れる理由は同期レイヤーの未対応）。
- この 2 つを含めて `ExpressionSettlementTests` を `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` に足した。

- `DirectFxExpressionDiscoveryTests.MovingTreeKnotIsNotSettledToOneFrame` を足した。1D の Blend Tree の片方の子が 30 秒かけて BlendShape を動かすとき、その値の候補が専用の警告で外れ、値と「確定しました」などの警告が残らないことを確かめる。このガードを外した状態で回すと、候補は警告付きで書き出されてテストが落ちる（`Error` が null）ことも確かめた。
- Unity 2022.3.22f1 の EditMode テストで、表情・既定表情・FX の候補・レイヤー制御・Gesture・工程まわりの 23 クラスを回した。1342 件のうち 1336 件が通った。2 件は APL が入っていないときに飛ばすテスト。落ちた 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` の APL のケースで、テスト用のプロジェクトに APL が入っていないことによる（この変更の前から落ちる）。
