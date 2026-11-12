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
- `ExportStage.AvatarAnimator`を足し、記録・読み取り・ノードへの対応付け・書き込みの4か所をすべて`ExportStageGuard.Optional`で包む。失敗したら拡張だけを外して書き出しを続ける（`Omitted`）。やり直しの書き出しはしない。`Optional`は`Run`と違い、失敗で止める設定（`ContinueOnStageFailure`がオフ）でも止めない。この段は以前のファイルに無かったデータを足すだけで、外せばこれまでと同じファイルになるため、この段のせいで今まで書き出せたアバターが書き出せなくなるのを避ける。キャンセルとほかの段の失敗はこれまでどおり止める。外したときは、止めない設定と同じく結果一覧と警告に「FXとメニューの動き」として出る
- 拡張のJSONが4MiBを超えたら拡張を書かない。この段の失敗と同じ扱いにして、結果一覧と警告に「FXとメニューの動き」の段として出し、理由に「FXとメニューの動きの情報が4MiBを超えたため、VRMに含めませんでした。」を書く
- 拡張は`AvatarFormatRequirements.Write`の直前、ほかの拡張をすべて書いた後に足す。`UnusedMorphTargets`の除去より後なので、その対象の一覧は変えない

## 設計書との違いと解釈

- 曲線のキーの読み書きは`ExpressionAnimationData`のものをそのまま使うため、`WriteCurve`と`ReadCurve`を`static`から`internal static`にした。処理は変えていない。このため曲線には`preWrap`・`postWrap`も入る。どちらも同じアセンブリ（`com.vrvlog.avatar-formats`）の中なので、パッケージとして取り込むアプリ側に変更は要らない
- キーの上限16384は文書全体でなくクリップごとに数える。上限を超える曲線と、キーの時刻や値が決まりの外にある曲線は、その1本だけを`other`に数えて外す。1本のために拡張の全体を外さない
- 遷移は、Entryと子のステートマシンから出る遷移（exitTransitions）にはUnityのデータに時間の項目が無いので、時間の項目を書かない。ステートとAny Stateからの遷移には書く。行き先の無い遷移はUnityでも通らないので書かない
- Direct以外のブレンドツリーの子には`directParameter`を書かない。Parameter Driverの操作の`source`は、名前があるときだけ書く。メニューの項目の`parameter`と`menu`は、無いときは書かない
- 名前は空文字を許す。Unityの各名前とSDKのメニュー項目は空を許すため
- 同期レイヤー（syncedLayerIndex）は、元のレイヤーのステートマシンを複製して持ち、モーションとステートのビヘイビアは同期レイヤーの上書きを使う。あわせて同期元の番号（`syncedLayer`、同期でなければ-1）とTimingの設定（`syncedLayerAffectsTiming`、同期でなければfalse）を常に書く。アプリが複製をそのまま再生しても、同期元と時間を合わせても扱えるようにするため。範囲外や自分自身を指す`syncedLayerIndex`は、同期でないレイヤーとして読む。同期レイヤーに同期したレイヤーは、Unityと同じく鎖をたどった先（ステートマシンを持つレイヤー）の複製を持ち、`syncedLayer`にはそのレイヤーの番号を書く。読む側が鎖をたどらずに済むよう、`syncedLayer`が同期レイヤーを指すデータは読むときに拒む。鎖が輪になっているレイヤーは、同期でないレイヤーとして読む
- 先頭のレイヤーの`weight`は、`defaultWeight`によらず1を書く。Unityは先頭のレイヤーを常に1で再生する（`ExpressionDependencies`も同じ扱い）
- クリップの設定のCycle Offsetを`cycleOffset`として常に書く。ステートの`cycleOffset`とは別の値
- Parameter Driverの操作の`source`は、種類が`copy`のときだけ読む。ほかの種類はSDKが`source`を使わないが、Inspectorで切り替えた前の名前が残っていることがあり、それを読むとパラメーターの表に使われない行が増えるため
- 曲線のパスが複製に無いものは`missingNode`に数える。出力にノードの無い対象（空のノードの除去やコンストレイントの処理で消えたもの）も、対応付けのときに`missingNode`に数える
- `expressions`は予約で、schema 1では空の配列だけを許す

## 検討した代替案と不採用理由

- 書き出し前の元のアバターから読む: 最適化で消えた物や名前の変わった物を指してしまい、出力のノードと対応しない。最適化後の複製から読む
- 失敗したら書き出しをやり直す（`guard.Step`）: 拡張が無くても今のVRMとして使える。やり直すと時間がかかるうえ、ほかの段の結果を変えるおそれがある。その場で外す
- BlendShapeとマテリアルの曲線も書く: 対象の表し方（メッシュとモーフ、マテリアルとプロパティ）をアプリ側と決めていない。今回は件数だけを書き、次のschemaで足す

## 既知の制限

- 有限でない数値（遷移の時間・条件のしきい値・ステートの速度など）や、表の上限を超える件数、1024文字を超える名前が1つでもあると、拡張の全体が外れる。読む側と同じ検査（`AvatarAnimatorData.Read`）を書く前にも通し、決まりの外のデータを書かないため。外れたときは結果一覧と警告に「FXとメニューの動き」として出て、書き出しは続く。曲線だけは1本ずつ外す（上記）
- FXのパラメーターを動かす曲線（アバターのルートのAnimatorに付く曲線）は書かず、`omitted`の`parameter`に件数だけを書く。この曲線の値を条件にした遷移は、書いたデータだけを再生しても起きない。92体では合わせて193本あった。曲線の行き先にパラメーターを足す形式の追加が要るので、`blendShape`の次の変更で書く。アプリ側の再生はまだ無く、それまでに出る挙動は無い（Codexレビューの指摘を今回は見送った理由）
- レイヤーのAvatar Maskは書かない。マスクの対象（ボーンとTransformのパス）をアプリ側のノードにどう対応させるかを決めていないため。マスクで隠れる部分も、アプリ側ではマスクの無いレイヤーとして動く
- 同じ体を2回書き出したとき、拡張の内容が一致するかはビルドツールが付ける名前に左右される。レイヤー・ステート・クリップの名前はビルドの処理が付けたものをそのまま書き、その名前が書き出しのたびに変わることがあるため

## 確かめたこと

- 読む時点から書き込みまでの間に、記述子とFXを書き換える処理が無いことをコードを読んで確かめた。FXに書き込むのは、書き出しが自分で作る調査用のコントローラーだけだった
- `AvatarAnimatorCaptureTests`で、FaultInjectionでこの段の4回の呼び出しのそれぞれを失敗させ、失敗で止める設定と止めない設定の両方で、書き出しが成功し、通常の書き出しから拡張・`extensionsUsed`の1項目・必要バージョンの1項目を外すとJSONとBINチャンクが一致し、結果一覧と警告にこの段が出ることを確かめる
- EditModeテスト（`AvatarAnimatorDataTests`、`AvatarAnimatorCaptureTests`、`ObjectToggleCaptureTests`、`ExpressionGestureTests`、`ExportStageGuardTests`、`ExportAppearanceReportLanguageTests`）は131件が通る
- 9体を2回ずつ書き出し、変更の前（`feature/full-reproduction`）と`Tools/compare-exports.py --allow-added-extension VRVLOG_avatar_animator`で比べた。`18 pairs, 0 mismatched`で、足した拡張のほかはJSONもBINチャンクも一致した。同じ9体の1回目と2回目（`--runs`）も`9 pairs, 0 mismatched`で、この9体では拡張の内容も2回で一致した
- 92体（書き出せた89体と、変更の前後とも同じ理由で書き出せない3体）を1回ずつ書き出し、同じように比べた。`92 pairs, 0 mismatched`だった。89体のうち77体に拡張が付き、大きさは最小13,939バイト、中央値41,863バイト、最大956,521バイトだった。拡張の無い12体には「FXとメニューの動き」の警告が無く、上の「拡張の全体が外れる」に当たった体は0だった。12体に書く対象が無いことは、1体ずつは確かめていない
- 92体で書かなかった曲線は、`blendShape`が229,510本、Transformが11,796本、筋肉の値が10,611本、マテリアルが1,886本だった。`blendShape`は次の変更で書く
- アプリでの読み込みは確かめていない
