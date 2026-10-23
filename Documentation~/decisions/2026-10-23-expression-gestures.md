# ハンドジェスチャーの組ごとの顔を VRVLOG_expression_gestures として書き出す

- 日付: 2026-10-23
- 対象: `Formats/Runtime/ExpressionGestureData.cs`・`Schema/VRVLOG_expression_gestures.1.schema.json`・`Formats/Runtime/AvatarFormatVersions.cs`・`Editor/ExpressionGestureCapture.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExportStageGuard.cs`・`Editor/Locales/*.json`・`Tests/Editor/ExpressionGestureTests.cs`・`Documentation~/ExpressionGestures.md`・`Formats/README.md`・`CHANGELOG.md`

## 背景

VR Vlog のアプリは、書き出した VRM のカスタム表情を表情として選べるが、VRChat で左右のハンドジェスチャーに割り当てていた顔をジェスチャーから出す手段が無かった。どのジェスチャーでどの顔が出るかは FX の層の条件・優先度・Write Defaults・パラメーター駆動の組み合わせで決まり、アプリ側で FX を持たずに再現するには、組ごとの結果を Exporter が表にして渡す必要がある。

形式は 3 案（64 マスの表・優先順位つきの規則・表情ごとのマスの一覧）から、アプリの引き方が `cells[left * 8 + right]` の 1 回で済む 64 マスの表にした。仕様は `Documentation~/ExpressionGestures.md`。

## 決定

- 任意の root extension `VRVLOG_expression_gestures`（schemaVersion 1）に `cells` を 64 要素で書く。マスは `null` か 0〜8 個の参照で、参照は VRM に書いたカスタム表情の名前と、トリガーに従う手（`drive`）を持つ。extensionsUsed にだけ宣言する。キーの制限・名前の長さ・同じマスでの重複の禁止・未知のキーで全体を拒むことは `VRVLOG_object_toggles` と揃えた。
- 表は NDMF などの前処理を済ませたコピーで、焼き込みの前に作る。FX を左右の組ごとに新しいグラフで 2 秒（1/60 秒刻みで 120 フレーム）再生し、100 フレーム目と最後で再生中のクリップが同じ組だけを決まったものとする。
- 再生には `ExpressionEvaluationSession` を使わず、`ExpressionGestureCapture.Sampler` で再生する。`ExpressionEvaluationSession` は 1 つの表情の値を求めるためのもので、扱えない命令が 1 つあるとセッション全体が失敗する。表では Random の駆動がある組だけを `null` にしたいので、組ごとに結果を分けられる形にした。パラメーター駆動と FX の層の重みの制御は同じ `VrChatParameterDriver`・`SdkLayerWeightControl` で実行するので、駆動の解釈は表情の書き出しと変わらない。
- 再生中のクリップは、表情ボードで書き出す表情に対応させる。対応は表情ボードのクリップそのもの、前処理が作った複製（`IsolatedCopyOf`）、NDMF が記録した元のアセットのどれかで取る。表情ボードからの書き出しでは、一覧の表情の元のクリップを `listFaceSources` で渡す。名前は VRM に書いた名前（まとめた表情はまとめ先の名前）にし、`VRMC_vrm.expressions.custom` に無い名前は落とす。
- 既定でオフのトグルがジェスチャーの顔を止めているアバターでは、既定値の表（すべてのマスが `[]`）を書く、という指示だった。すべてのマスが `[]` の表は「どの組でもジェスチャーの表情なし」で、拡張が無いときとアプリの動作が同じになる。このため、1 マスも参照が無い表は書かない規則にまとめ、読み込みでも拒む。
- 表を作れなかったときは拡張を書かずに書き出しを続け、Unity のコンソールに警告を出す。表を VRM に足す工程の失敗は、書き出しの注意に工程「ハンドジェスチャーの表情」として出す。ユーザー向けの設定・UI は足さない。
- 必要なアプリの版は仮に 1.0.8 とした。アプリでこの拡張を読む版がリリースされたら、その版に直す。
- 録画した VRMA にジェスチャーの値を持たせることは、この変更に含めない。

## 確認

- `ExpressionGestureTests.TableRoundTripsWithCellOrderDriveAndUndeterminedCells` で、マスの順・`drive`・`null` のマスが書いて読んで変わらないことを確かめる。
- `ExpressionGestureTests.TableOutsideTheSchemaIsRejected` で、マスの数・キー・名前・重複・`drive` の値・参照が 1 つも無い表を拒むことを確かめる。
- `ExpressionGestureTests.CaptureReadsEveryGesturePairInLayerOrderWithTriggers` で、トリガーの 1D ブレンドツリーが `drive` になること、層の順、Random の駆動と表情ボードに無いクリップのマスが `null` になることを確かめる。
- `ExpressionGestureTests.InjectedExtensionIsTheCapturedTableUnderTheWrittenNames` で、まとめた表情がまとめ先の名前で書かれ、VRM に無い名前の参照が落ちることを確かめる。
- `ExpressionGestureTests.AvatarWithoutHandGesturesWritesNothing` で、ジェスチャーの表情が無いアバターに拡張を書かないことを確かめる。
- 実アバター 40 体を変更の前後で書き出し、表以外の出力（表情・VRM）が変わらないことを確かめる。

## 欠点

- 書き出しのたびに FX を 64 回（拳の手がある組はもう 1 回）再生するので、書き出しが長くなる。
- メニューのトグルや表情セットの切り替えで変わる表は書かない。既定値の表だけになる。
- Write Defaults がオフで前の顔が残る構成や、トリガーを Motion Time に使う構成は近似になる。
