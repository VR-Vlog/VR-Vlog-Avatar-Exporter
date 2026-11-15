# 評価器で FX・Gesture・Action を重ねて回し、Playable Layer Control の重みを当てる

- 日付: 2026-11-16
- 対象: `Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Tools/AvatarAnimatorEvaluatorTests.cs`、`Formats/README.md`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-2）

## 背景

段5-1 で `VRVLOG_avatar_animator` に Gesture と Action のコントローラー、muscle のターゲット、レイヤーのマスクを書くようにした。評価器は FX だけを回していたうえ、`Supports` がすべてのコントローラーを見ていた。そのため、評価器が回せない Gesture や Action（加算のレイヤーなど）を持つアバターは、FX も含めて拡張ごと断られていた。段5-1 より前の書き出しでは FX は再生されていたので、これはデグレになる。

## 決定

- デグレを先に直す。`Supports` が断るのは、遷移を変える `omitted`（`parameter`・`operation`・`unreadParameter`）があるときと、FX 自身を回せないときだけにする。回せない Gesture や Action は飛ばし（`SkipReason` で理由が分かる）、残りはそのコントローラーが無いものとして回す。飛ばしたコントローラーだけが使うブレンドツリーは理由に数えない
- VRChat のプレイアブルの順に Gesture、Action、FX を重ねる（M15）。各コントローラーのレイヤーは自分のコントローラーのレイヤーだけを見る。どのレイヤーも書かなかったターゲットは、M6 と M7 のとおり rest（Write Defaults オフなら前のフレームの値）になる。そのあと、コントローラーの出力をプレイアブルの重みで下のコントローラーの結果に重ねる。そのコントローラーが書かなかったターゲットは下の結果のままにする
- プレイアブルの重みは、Action が 0、それ以外が 1 から始める。Playable Layer Control（`playableLayerControl`）が、指定のコントローラーの重みを指定の秒数で目標値まで動かす。飛ばしたコントローラーにも重みは持たせる。重みが動いている間は落ち着いたフレームとして飛ばさない。0 秒で重みが変わったフレームも飛ばさない
- Layer Control（`layerControl`）は、`playable` で指定したコントローラーのレイヤーに当てる。指定のコントローラーが回っていなければ何もしない。各コントローラーの 0 番レイヤーは、`SetLayerWeight` でも Layer Control でも重み 1 のままにする
- レイヤーの番号は、回すコントローラーのレイヤーをデータの順（FX が先）に 1 本に並べたものにする。FX のレイヤーの番号は今までと変わらない。`LayerIndex(controller, layer)` でこの番号を引ける
- マスクは muscle にだけ当てる（M16）。レイヤーのマスクが通さない体の部位の muscle は、そのレイヤーが動かすターゲットから外す。FX の 0 番レイヤーにマスクが無いときは、VRChat と同じく FX の muscle をすべて当てない（`Editor/PoseMenuResolver.cs` の `skipMuscles` と同じ判定）。muscle の部位は `HumanTrait.MuscleName` の順に、体、頭（首・頭・目・あご）、左脚、右脚、左腕、右腕、左手の指、右手の指とする
- コントローラーが 1 つだけ回るときは、今までと同じ計算をする

## 選ばなかった案

- 回せない Gesture や Action があれば拡張ごと断る（今までどおり）。FX の表情まで再生されなくなるので採らない
- マスクの Transform（`nodes`）も当てる。VRChat で Transform のマスクがどう効くかを測っておらず、当てると FX の出力が変わりうるので、段5-7 に回す
- Gesture や Action だけが出す `omitted` を、そのコントローラーを飛ばす理由にする。`omitted` はコントローラーごとに数えていないので、分けるには形か書き出し側を変える必要がある。今回は今までどおり全体を断る

## 確かめたこと

- `Tools/run-avatar-animator-evaluator-tests.ps1` が通る（13578 件の確認、Unity の記録 105 件の再生、比較ケースの落ち着いたフレームの飛ばしが毎フレーム評価と一致）
- 足したテスト: 回せない Gesture（加算）と Action（preventRepeats・Timing つきの同期レイヤー）を飛ばして FX が FX だけのときと毎フレーム同じ、FX の加算は今までどおり断る、Gesture のターゲットが動き FX が上書きする、Action の重み 0 から Playable Layer Control で上げ下げする（飛ばしあり・なしの両方）、FX の重みを下げると下の Gesture が見える、Layer Control が Gesture のレイヤーに当たる、マスクの部位と FX の muscle の扱い
- FX だけのデータでは、変更前（e2b5e94c）の評価器と、比較ケース 35 件の入力と乱数の入力で計 431325 フレーム、値・書いたかどうか・パラメーター・各レイヤーの状態と時間と重みがビット単位で一致した

## 仮定として残すこと

- M15（重ね方）と M16（マスク）は VRChat の資料と SDK の構成からの仮定で、段5-3 で測る
- 目とあごの muscle を頭の部位に数えている
- 記述子のプレイアブルのマスク（`CustomAnimLayer.mask`）は書き出していないので当てていない
