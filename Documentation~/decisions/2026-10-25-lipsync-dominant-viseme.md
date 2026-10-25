# Viseme Parameter Only のアバターで、いちばん強い口の形だけを出す

- 日付: 2026-10-25
- Issue: Exporter #503・アプリ VR-Vlog/VR-Vlog#2207
- 対象: `Formats/Runtime/LipSyncData.cs`・`Schema/VRVLOG_lip_sync.1.schema.json`・`Formats/Runtime/AvatarFormatVersions.cs`・`Editor/LipSyncCapture.cs`・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/LipSyncCaptureTests.cs`（アプリ側は同名の決定記録に書く）

## 背景

VRChat の Avatar Descriptor で Lip Sync を **Viseme Parameter Only** にしたアバターは、VRChat が `Viseme` パラメーターに「いちばん強い口の形の番号」だけを入れ、FX がその番号の口の形を 100・ほかを 0 にするクリップへ遷移で切り替える。口の形は同時に 1 つだけで、切り替えは遷移の時間（よくある値は 0.05 秒）をかけて混ざる。

VR Vlog アプリは、顔のトラッキングから出したリップシンクの母音を、それぞれの強さのまま同時に出す。同じアバターでも、VRChat より口の形が混ざって見える。

## 決定

### データ（`VRVLOG_lip_sync` スキーマ1）

glTF のルートの `extensions.VRVLOG_lip_sync` に書き、`extensionsUsed` にも名前を入れる。`extensionsRequired` には入れない（読まないアプリは今と同じ動きになる）。読み書きは `LipSyncData`（`VRVlog.AvatarFormats`）で行い、アプリも同じソースを使う。

```json
{ "schemaVersion": 1, "dominantViseme": true, "transitionSeconds": 0.05 }
```

| キー | 型 | 意味 |
|---|---|---|
| `schemaVersion` | 整数 | `1` |
| `dominantViseme` | 真偽値 | `true` のとき、リップシンクの母音をいちばん強い 1 つだけ残し、ほかを 0 にする |
| `transitionSeconds` | 数値 | いちばん強い母音が変わったときに、前の母音から次の母音へ切り替える秒数。0 以上 10 以下。0 はすぐ切り替える |

- 3 つのキーがちょうどそろっていて、型と範囲が合うときだけ読む。外れたら拡張全体を読まない（`InvalidOperationException`）。書き込みも同じ検査を通す。
- 拡張が無い VRM と、`dominantViseme` が `false` の VRM は、今と同じ動き（母音を混ぜて出す）にする。Exporter は `false` を書かない。

### Exporter が書く条件と値

- NDMF の処理を終えたコピーの Avatar Descriptor で `lipSync` が `VisemeParameterOnly` のときだけ書く。`VisemeBlendShape`・`JawFlapBone` などほかの方式と、Avatar Descriptor が無いアバターには書かない。
- `transitionSeconds` は、FX（NDMF で統合した後のもの）のすべてのレイヤー（サブステートマシンと Any State を含む）で、条件に `Viseme` パラメーターを使う遷移の長さを秒に直し、いちばん多い値を使う（同数なら短いほう）。固定の長さ（Fixed Duration）の遷移はその値、正規化の遷移は、Animator と同じく遷移元のステートのクリップの長さを掛ける。Any State からの遷移は遷移元が決まらないので、遷移先のステートのクリップの長さを掛ける（クリップの長さが 0 か、クリップでないときは 1 秒として掛ける）。1 ミリ秒の単位に丸める。
- そのような遷移が FX に 1 つも無いときは `0` を書く（VRChat でも口の形は 1 つずつしか来ないので、いちばん強い 1 つだけを出すことは変えない）。
- FX の読み取りに失敗したときは警告を出し、拡張を書かずに書き出しを続ける。
- かんたんの書き出しも同じ処理を通るので、表情ボードの既定で書き出した VRM と同じ拡張になる。

### アプリの動き

- 拡張があり `dominantViseme` が `true` のアバターでは、リップシンクの母音（`A`/`Aa`・`I`/`Ih`・`U`/`Ou`・`E`/`Ee`・`O`/`Oh`）のうち、そのフレームでいちばん強い 1 つだけを残す。同じ強さなら あ・い・う・え・お の順で先のもの。すべて 0 のときは、直前にいちばん強かった母音のままにする。
- いちばん強い母音が変わったら、前の母音の重みの割合を 1 から 0 へ、次の母音の割合を 0 から 1 へ、`transitionSeconds` をかけて一定の速さで動かす（Animator の遷移と同じ直線の混ぜ方）。各母音の出力は、トラッキングの値にこの割合を掛けたもの。
- パーフェクトシンクと Unified Expressions の口の形（`JawOpen`・`MouthFunnel` など）は母音ではないので変えない。

### 必要なアプリのバージョン

`asset.extras.VRVLOG_requiredAppVersions` の `VRVLOG_lip_sync` は `1.0.9` にする。アプリの develop の版で、仮の値。この機能を入れて出す版が決まったら直す。拡張を読まない古いアプリでも、口が今と同じ動きになるだけで、表示は壊れない。

## 入れなかったもの・制限

- VRChat の `Voice`（声の大きさ）は使わない。強さはトラッキングの値のままにし、いちばん強い母音を 100 に固定しない（口を閉じているときに口が開いたままにならないように）。
- FX の遷移のうち、割り込み（Interruption Source）・Exit Time・遷移のオフセットは再現しない。切り替えの長さだけを使う。
- FX が `Viseme` の値ごとに違う長さの遷移を持つときも、1 つの値（いちばん多い値）にまとめる。
- 数えるのはレイヤーごとで、同じステートマシンを使うレイヤーはそれぞれ数える。同期レイヤー（Synced Layer）のモーションの差し替えは、正規化の遷移の秒数に反映しない。
- 口の形のクリップの中身（どのシェイプキーを何にするか）は、これまでどおり表情ボードと Lip Sync の書き出しで決まる。この拡張は混ぜ方だけを決める。

## 確認

- `LipSyncCaptureTests` で、データの往復と、契約から外れたデータを読まないことを確かめる。
- 同じテストで、VRChat SDK のある環境で、`VisemeParameterOnly` のアバター（Fixed Duration 0.05 秒の遷移・正規化の遷移・`Viseme` の遷移が無い FX）から書く値と、`VisemeBlendShape` のアバターに書かないことを確かめる。書き出した VRM に拡張と `extensionsUsed` と必要なアプリのバージョンが入ることも確かめる。
- 実際のアバターで、Viseme Parameter Only のアバターに拡張が入り、ほかの方式のアバターには入らないことを確かめる。
