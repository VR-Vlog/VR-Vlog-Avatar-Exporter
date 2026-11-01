# VRCFury を動かす前に NDMF の生成物を保存する

- 日付: 2026-11-01
- 対象: `Editor/NdmfExportPreparation.cs`・`Tests/Editor/NdmfPreparationTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

VRCFury の Armature Link で衣装を付けたアバターを書き出すと、基準の顔の口の形が、同じ衣装を MA の Merge Armature で付けたときと違っていた（口の 750 頂点、最大 3.9 mm）。表情 15 個からは、口を基準から戻すシェイプキーも抜けていた（2026-11-01-vrcfury-export.md の「確かめたこと」）。

原因は VRCFury に渡す前のコントローラーの状態にあった。

- 衣装の MA のレイヤーがあると、NDMF は Transforming の終わりにアバターのコントローラーを作り直して書き戻す。コントローラー自体はアセットとして保存されるが、ステートに付いた State Machine Behaviour（Parameter Driver など）は保存されないまま残る。
- VRCFury はコントローラーを組み直すときに、State Machine Behaviour を保存済みのアセットから読む。保存されていないものは読めず、組み直したコントローラーでは 0 個になる。
- 検証したアバターでは、Action レイヤーの 26 個（AFK のステップを進める Parameter Driver 5 個を含む）が消えた。そのパラメーターを書くものが無くなったため、表情の解析はそのパラメーターを自由に変わる入力として扱い、AFK のクリップにも届くと判定した。その結果、顔のシェイプキー 302 個が保持側に回り、基準の顔が変わった。

アップロードでは、NDMF の VRChat SDK 用のフックが Transforming のあとに `BuildContext.Serialize()` を呼んで生成物を保存してから、VRCFury（-10000）が動く。書き出しはこの保存を呼ばずに VRCFury を動かしていた。

## 決定

- 書き出しでも、Transforming のあと VRCFury を呼ぶ直前に `BuildContext.Serialize()` を呼ぶ。アップロードと同じ順番になる。
- 呼ぶのは VRCFury を実際に動かすとき（VRCFury のコンポーネントと VRC Avatar Descriptor があるとき）だけにする。VRCFury の無いアバターの書き出しは変わらない。
- `Serialize` は NDMF 1.6.0 から公開メソッドとしてあり、対応範囲（1.6.0 以降の 1.x）のどの版にもある。`Bridge` は他のメソッドと同じくリフレクションで探し、見つからないときは版が合わないとして止める。
- NDMF のビルドが要らないアバター（VRCFury だけのアバター）では、保存するものが無いので呼ばない。

## 選ばなかったもの

- VRCFury の前で消える State Machine Behaviour を Exporter が自分でアセットに保存する: NDMF の保存と同じことを別に書くことになり、保存の対象や片付けが NDMF とずれる。
- いつも（VRCFury が無くても）呼ぶ: アップロードと同じだが、VRCFury の無いアバターではこれまでの書き出しで違いが出ておらず、アセットの書き込みが増えるだけになる。

## 確かめたこと

- 検証用の Unity プロジェクトで、VRCFury の Armature Link で衣装を付けたアバターを書き出し直した。基準の顔は MA の Merge Armature で付けたときと全頂点が一致し（変更前は 750 頂点が最大 3.9 mm 違った）、表情のシェイプキーの違いは 15 個から 0 個になった。VRCFury が組み直したコントローラーの State Machine Behaviour は、変更前の 0 個から元と同じ数に戻った。
- 書き出した VRM をアプリと同じ読み込み経路で読み込んで比べた。全身の形の誤差は MA で付けたときと同じ値になった。
- EditMode テスト: VRCFury の前に保存されること（順番のテスト）と、VRCFury が無いときは呼ばれないことを足した。

## 影響

VRCFury で衣装やギミックを付け、MA などで NDMF のビルドもかかるアバターの書き出しが、アップロードと同じ State Machine Behaviour のまま VRCFury を通るようになる。VRCFury だけ、または NDMF だけのアバターの書き出しは変わらない。
