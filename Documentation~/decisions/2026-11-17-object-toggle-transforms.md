# トグルが動かす骨の形は、別の拡張 VRVLOG_object_toggle_transforms に書く

- 日付: 2026-11-17
- 対象: `Formats/Runtime/ObjectToggleTransformData.cs`、`Schema/VRVLOG_object_toggle_transforms.1.schema.json`、`Editor/ObjectToggleCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Editor/ExportEmptyNodes.cs`、`Formats/Runtime/AvatarFormatVersions.cs`

## 背景

`VRVLOG_object_toggles` は、メニューのトグルの値ごとに、オブジェクトの `activeSelf` だけを持つ。帽子のトグルが、オンのときに帽子を出すと同時に、帽子の下に来るアホ毛の骨の大きさを 0.01 にして少し下げるアバターがある。骨の形は書き出していないので、アプリで帽子をオンにすると、アホ毛が帽子を突き抜けて見える。

骨の形は、オブジェクトの表示と同じトグルの値で決まる。値ごとに FX を流して表示を読む今の仕組み（`ObjectToggleCapture`）の中で、骨の形も一緒に読める。

## 決定

### 別の拡張にして、VRVLOG_object_toggles 1 は変えない

骨の形は新しい拡張 `VRVLOG_object_toggle_transforms`（`schemaVersion` 1）に書く。`VRVLOG_object_toggles` は 1 のまま、中身も変えない。

- 今のアプリの `ObjectToggleData.Read` は、知らないキーがあると拡張ごと退け、既定で非表示の部品がすべて見えてしまう。`VRVLOG_object_toggles` にキーを足すと、骨を読めない古いアプリでは帽子や着替えの切り替えまで失う。
- アプリと Studio は、知らない `VRVLOG_` 拡張を読まずに無視する。別の拡張なら、古いアプリは骨の形だけを読まず、表示の切り替えは今までどおり動く。
- `VRVLOG_requiredAppVersions` では、新しい拡張に、それを読むアプリの版（1.2.9、仮）を書く。アプリはこの値を見て読み込みを止めないので、古いアプリでも開ける。アプリの版が決まったら合わせて直す。

### 形

```
{ "schemaVersion": 1,
  "groups": [ { "parameter": "Hat",
                "targets": [ { "node": 12, "properties": ["translation", "scale"] } ],
                "states": [ { "value": 0, "transforms": [ { "translation": [0, 0.1, 0], "scale": [1, 1, 1] } ] },
                            { "value": 1, "transforms": [ { "translation": [0, 0.08, 0], "scale": [0.01, 0.01, 0.01] } ] } ] } ] }
```

- グループは、`VRVLOG_object_toggles` の同じパラメーターのグループを広げるもので、そのグループの値と同じ値の組を持つ。`Read` は `ObjectToggleData` も受け取り、パラメーターが無いとき、値の組が違うときは拡張ごと退ける。
- ターゲットは、トグルが動かす骨のノードと、値によって違うプロパティ（`translation`・`rotation`・`scale` のうち、この順に）を持つ。値ごとの形は、そのプロパティだけを持つ。
- 値は Unity のローカルの値で、glTF の座標に直さない。表情の骨の動き（`ExpressionAnimationPlayer`）と同じ書き方で、アプリはそのまま `localPosition` などに入れられる。rest からの差ではなく値そのものにしたのは、書き出しの rest が既定の値と違っても、既定の値の状態でアバターの形に戻せるようにするため。
- 回転は単位四元数 `[x, y, z, w]` で、長さが 1 から 0.001 より離れていたら退ける。
- 上限は `VRVLOG_object_toggles` と同じく、グループ 256、グループごとのターゲット 1024、全体のターゲット 4096、値の数 2〜256。ノードは全体で 1 回まで。

### 書き出し

- 比べる骨は、FX のクリップが Transform の位置・回転・大きさを動かす骨のうち、パスが 1 つのオブジェクトに決まるものだけにする。ほかの骨は、値によって浮動小数の誤差で揺れても読まない。
- 次の骨は除く。
  - ルート（ノードが無い）。
  - ヒューマノイドの骨。アプリでは姿勢の再生が動かす。
  - ルートの大きさが 1 でないときの、ルート直下の骨。`RootScaleBake` が書き出しでその位置と大きさを書き換える。
- 値ごとのサンプルは、表示と同じく、ほかのパラメーターを既定の値にして FX を 2 秒流したあとの形にする。1 つのサンプルの骨の形は次のサンプルに残るので、サンプルの前に骨を rest に戻す。
- 違いの許容は、表情の骨の動き（`ExpressionTransformCurves`）と同じにする。位置は 0.0005 m（親の大きさで割る）、回転は 0.05 度、大きさは各成分 0.0005。
- 骨を動かすだけのレイヤーのパラメーターも流す（`ParametersThatSwitchObjects` が比べる骨のパスを受け取る）。
- 同じ骨を 2 つのパラメーターが動かすときは、表示と同じく先のグループに入れて注意を出す。
- トグルの項目は、表示か骨の形のどちらかを変えれば残す。
- 書き出しで無くなった骨は、その骨の形だけを外して注意を出す。
- 動かす骨は、空のオブジェクトを外す処理（`ExportEmptyNodes.Prune`）で残し、Aim の中継を足すときにも親を変えない（表情の骨と同じ扱い）。
- 値はサンプルしたときの親からのローカルの値なので、書き出しで親が変わった骨（Parent・Position Constraint をソースの子に移して書いたものなど）は、その骨の形だけを外して注意を出す。コンストレイントの書き出しは今までどおりにする。

## 直らないもの・制限

- 表示を何も変えないトグル（骨だけを動かすトグル）は、`VRVLOG_object_toggles` にグループが無いので書かない。
- ヒューマノイドの骨を動かすトグルは書かない。
- 2 つ以上のパラメーターの組み合わせで決まる骨の形は、表示と同じく書かない。
- 同じ骨を表情も動かすときの重なりは、アプリ側で表情を優先し、トグルの形を当てない。

## 比べた案

- A（この決定）: 別の拡張にする。古いアプリは表示の切り替えをそのまま使える。
- B: `VRVLOG_object_toggles` を 2 に上げて骨の形を足す。古いアプリは拡張ごと退け、既定で非表示の部品がすべて見える。
- C: `VRVLOG_object_toggles` 1 に省略できるキーを足す。今のアプリの読み込みは知らないキーを退けるので、B と同じことになる。
