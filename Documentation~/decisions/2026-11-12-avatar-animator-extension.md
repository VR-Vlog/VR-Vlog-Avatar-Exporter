# アバターのFX・メニュー・パラメーターを拡張`VRVLOG_avatar_animator`で書き出す

- 日付: 2026-11-12
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Editor/ExportStageGuard.cs`（`ExportStage.AvatarAnimator`）、`Formats/Runtime/AvatarFormatVersions.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`

## 背景

アプリでVRChatのアバターのFXとExpression Menuを動かすには、FXのレイヤー・ステート・遷移・条件と、メニュー・パラメーターをそのまま渡す必要がある。今の拡張（表示の切替、表情のジェスチャーなど）は書き出すときに結果を決めた表で、アバターが組んだ動きを再現できない。

一方で、今のVRMを読むアプリや他のVRMアプリに影響を出したくない。今の出力は1バイトも変えず、拡張と、その`extensionsUsed`の1項目、`asset.extras.VRVLOG_requiredAppVersions`の1項目だけを足す。

## 決定

- 新しい拡張`VRVLOG_avatar_animator`（schemaVersion 1）を足す。`extensionsUsed`には入れ、`extensionsRequired`には入れない。必要なアプリのバージョンは仮に`1.2.8`とする
- 読むのは最適化を終えた複製のアバターの記述子（descriptor）と、そのFX・Expression Menu・Expression Parametersで、`ValidatePreparedHumanoid`の直後に読む。複製・アセット・Animatorには書き込まない
- 曲線は`GameObject`の`m_IsActive`だけを残し、ほかは種類ごとの件数を`omitted`に書く（`blendShape`・`material`・`transform`・`enabled`・`parameter`・`muscle`・`other`・`missingNode`・`behaviour`の順、0件の種類は書かない）。ビヘイビアはParameter Driver・Tracking Control・Animator Layer Control・Playable Layer Controlだけを書き、ほかは`behaviour`に数える
- 対象の`rest`は、書き出しの処理が複製の`activeSelf`を変える前の値にする。表示の切替が既定でオフのメッシュをオンにするため、変えた後の値では既定の状態が分からない。変える前の値は`afterTransforming`の最初に記録する
- `ExportStage.AvatarAnimator`を足し、記録・読み取り・ノードへの対応付け・書き込みの4か所をすべて`guard.Run`で包む。失敗したら拡張だけを外して書き出しを続ける（`Omitted`）。やり直しの書き出しはしない
- 拡張のJSONが4MiBを超えたら拡張を書かず、警告「FXとメニューの動きの情報が4MiBを超えたため、VRMに含めませんでした。」を出す
- 拡張は`AvatarFormatRequirements.Write`の直前、ほかの拡張をすべて書いた後に足す。`UnusedMorphTargets`の除去より後なので、その対象の一覧は変えない

## 設計書との違いと解釈

- 曲線のキーの読み書きは`ExpressionAnimationData`のものをそのまま使うため、`WriteCurve`と`ReadCurve`を`static`から`internal static`にした。処理は変えていない。このため曲線には`preWrap`・`postWrap`も入る。どちらも同じアセンブリ（`com.vrvlog.avatar-formats`）の中なので、パッケージとして取り込むアプリ側に変更は要らない
- キーの上限16384は文書全体でなくクリップごとに数える。上限を超える曲線と、キーの時刻や値が決まりの外にある曲線は、その1本だけを`other`に数えて外す。1本のために拡張の全体を外さない
- 遷移は、Entryと子のステートマシンから出る遷移（exitTransitions）にはUnityのデータに時間の項目が無いので、時間の項目を書かない。ステートとAny Stateからの遷移には書く。行き先の無い遷移はUnityでも通らないので書かない
- Direct以外のブレンドツリーの子には`directParameter`を書かない。Parameter Driverの操作の`source`は、名前があるときだけ書く。メニューの項目の`parameter`と`menu`は、無いときは書かない
- 名前は空文字を許す。Unityの各名前とSDKのメニュー項目は空を許すため
- 同期レイヤー（syncedLayerIndex）は、元のレイヤーのステートマシンを使い、モーションとステートのビヘイビアは同期レイヤーの上書きを使う
- 曲線のパスが複製に無いものは`missingNode`に数える。出力にノードの無い対象（空のノードの除去やコンストレイントの処理で消えたもの）も、対応付けのときに`missingNode`に数える
- `expressions`は予約で、schema 1では空の配列だけを許す

## 検討した代替案と不採用理由

- 書き出し前の元のアバターから読む: 最適化で消えた物や名前の変わった物を指してしまい、出力のノードと対応しない。最適化後の複製から読む
- 失敗したら書き出しをやり直す（`guard.Step`）: 拡張が無くても今のVRMとして使える。やり直すと時間がかかるうえ、ほかの段の結果を変えるおそれがある。その場で外す
- BlendShapeとマテリアルの曲線も書く: 対象の表し方（メッシュとモーフ、マテリアルとプロパティ）をアプリ側と決めていない。今回は件数だけを書き、次のschemaで足す

## 確かめたこと

- 読む時点から書き込みまでの間に、記述子とFXを書き換える処理が無いことをコードを読んで確かめた。FXに書き込むのは、書き出しが自分で作る調査用のコントローラーだけだった
- `AvatarAnimatorCaptureTests`で、FaultInjectionでこの段を失敗させた書き出しと通常の書き出しを比べ、拡張・`extensionsUsed`の1項目・必要バージョンの1項目を外すとJSONとBINチャンクが一致することを確かめる
- 実際のアバターでの書き出しとアプリでの読み込みは確かめていない
