# 2 秒で止まらないメニューの表情は、Animator の時間の流れから最終状態を決めて書き出す

- 日付: 2026-10-19
- 対象: `Editor/ExpressionSettlement.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/ExperimentalInstalledExpressionPlayer.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/VrChatFxExpressions.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/ExpressionSettlementTests.cs` ほか既存テスト・`README.md`・`CHANGELOG.md`

## 背景

メニューの表情は、入力を入れてから 120 フレーム（2 秒）進めた時点の顔を読み、その顔がこの先も変わらないことを確かめてから書き出している。次のどれかに当たる表情は「固定表情に変換できません」として書き出しから外れていた。

- 2 秒より後に exit time の遷移がある（少し待ってから別の顔に切り替わる表情）
- 2 秒より長いクリップで、まだ BlendShape が動いている
- ループするクリップで BlendShape が動き続けている

アバターの作り方としては珍しくなく、メニューにあるのに VRM に入らない表情が出ていた。

## 決定

- 入れたもの:
  - `ExpressionSettlement.Settle` を足した。2 秒進めたあと、Animator のステートマシンを読んで次に起きる遷移（遷移中なら終わるまで、そうでなければ条件を満たす exit time の遷移のうち最も早いもの）までの時間を求め、その時間だけ PlayableGraph を進める。遷移が起きなくなるまで繰り返す。ステートを `Play()` で飛ばさないので、Parameter Driver などのステートに入ったときの処理は実機と同じ回数だけ動く。
  - 遷移が尽きたら、ループしないクリップは最後のキーまで進め、ループするクリップは最初のフレームで止める。
  - 遷移が元のステートに戻る（循環する）ときは、最初に戻ったところで止める。
  - 最終状態に exit time の遷移が残っていて、今の入力では起きないときも、警告を付けて書き出す。条件が今の値で偽になることだけでは「この先も変わらない」とは言えないので、警告なしでは通さない。
  - 時間で決めた表情には次のどれかの警告を付ける。書き出し結果の一覧では「近似」の行として表情の名前と一緒に出る。英語・韓国語・中国語（簡体・繁体）の訳も足した。
    - 表情の最終状態をAnimatorの時間遷移から確定しました
    - ループする表情のため、最終状態の最初のフレームで保存しました
    - 遷移が循環するため、最初に戻った状態で保存しました
    - 時間で変わる表情を最後のキーで保存しました
  - 警告は、メニューの項目（`VrChatExpressionSampler.Analyze`）・FX の条件から見つけた候補・Gesture のステートのどの経路で読んだ表情でも、その表情のメッセージに入れる。書き出しはこのメッセージから警告と結果の一覧の行を作る。最初の実装では、メニューの項目を読む `Analyze` だけが警告を受け取っておらず、exit time の遷移で決めた表情が書き出されても警告と「近似」の行が出なかった。
  - 表情と同時に再生されているクリップに、手の待機アニメーションのようなヒューマノイドの筋肉（またはルートモーション）のカーブがあり、それが時間で動くときも、BlendShape のカーブと同じように警告を付けて書き出す。これらは Animator のカーブだが Animator Controller のパラメーターではなく、BlendShape を動かせない。これまでは「時間で変わるBlendShape・パラメーター曲線」として表情ごと外していた。
  - 試験的な一括収録（`ExperimentalInstalledExpressionPlayer`）にも同じ処理を入れた。
  - Gesture の Blend Tree の表情で時間の決定が必要になったときは、これまでどおりアニメーションとして読む経路（`AnimatedGestureTree.Read`）に回す。
- 入れなかったもの:
  - Motion Time・速度のパラメーター・Cycle Offset で時計が決まるステートは進めない。どこまで進めれば最終状態になるかがパラメーター次第で決まらないので、これまでどおり書き出しから外す。
  - Animator Controller のパラメーターを動かすカーブ、遷移の途中で止まる表情、Animation Event を含む表情、オブジェクト参照のカーブも、これまでどおり外す。
  - `Play()` で遷移先へ直接飛ぶ案: ステートに入ったときの処理が 2 回動くことがあり（Add の Parameter Driver など）、値が実機と変わるので使わない。

## これまで書き出せた表情の値を変えないこと

`Settle` は、2 秒の時点で待っている遷移が無く、これまでの「この先も変わらない」検査が通る表情では PlayableGraph を 1 回も進めずに戻る。検査が通ったときは 3 回の検査点も従来どおり行う。そのため、これまで書き出せた表情の値は変わらない。`ExpressionSettlementTests` の 0.25 秒の遷移で入る表情のテストで、値が同じで警告が付かないことを確かめる。

## 確認したこと

- Unity の API の前提（`AnimatorStateInfo.length` がステートの速度を含むこと、`AnimatorTransitionInfo.duration` が `durationUnit` の単位で返ること）を、速度 0.5・1・2 と固定／正規化の遷移時間で、PlayableGraph を細かく進めて実測した時間と比べた（`NativeClocksMatchTheSettlementConversion`）。
- `ExpressionSettlementTests` で、10 秒のクリップ（速度 0.5・1・2）、5 秒後の exit time（遷移時間 0・0.5 秒・正規化 0.5）、ループ、循環、0.25 秒の遷移、Add の Parameter Driver、Motion Time、試験的な一括収録を確かめる。メニューの項目を `Analyze` で読んだとき exit time で決めた表情のメッセージに警告が入り、結果の一覧に「表情」の「近似」の行が出ること、ループする手の筋肉のカーブが表情と同じクリップ・別のレイヤーにあっても表情の値が書き出されて警告が付くことも確かめる。`SelectedLayerControlTests` で、exit time のあとに VRCAnimatorLayerControl でレイヤーの重みを変える表情を確かめる。
- これまで「書き出しから外れる」ことを確かめていた既存のテストのうち、時間で決まるものは、最終状態の値と警告を確かめるように直した。Motion Time・速度のパラメーター・Cycle Offset・パラメーターのカーブは引き続き外れることを確かめる。
- Unity 2022.3.22f1 の EditMode テストで、表情・既定表情・FX の候補・レイヤー制御まわりの 14 クラス（967 件）を回し、963 件が通った。落ちた 4 件は `RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces` の APL のケースで、テスト用のプロジェクトに APL が入っていないことによる（この変更の前から落ちる）。
