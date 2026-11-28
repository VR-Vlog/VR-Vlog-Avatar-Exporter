# 目を動かす BlendShape を VRVLOG_eye_shapes に書く

- 日付: 2026-11-28
- 対象: `Formats/Runtime/EyeShapeData.cs`・`Formats/Runtime/AvatarFormatVersions.cs`・`Formats/README.md`・`Schema/VRVLOG_eye_shapes.1.schema.json`・`Editor/BlinkExportSession.cs`（`EyeShapes`・`InjectEyeShapes`）・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/FormatsContractBoundaryTests.cs`・`Tests/Editor/BlinkExportTests.cs`・`Tools/run-unity-compatibility.py`
- 関連: アプリ側の対応は VR-Vlog の別 PR。`2026-11-01-vowel-default-mouth.md`（口の形の決め方）

## 背景

Usasaki（MOCHIYAMA）は FX の顔のレイヤーで自分でまばたきする。`EyeOpen`（「まばたき」0、3.17 秒）が終わると条件なしで `EyeClose`（「まばたき」100、目のトラッキングを Animation にする）へ移り、両手が Neutral ならすぐ `EyeOpen` に戻る。約 3.2 秒ごとに FX がまばたきする。

アプリは 2.0 からこの FX を再生する。アプリの一覧で目を閉じた表情（`sad` など、「まばたき」に近い形を約 99% 入れたもの）を選んでいると、FX の `EyeClose` が「まばたき」100 を上から足し、閉じすぎて目の線が消える。アプリの「まばたきを有効にする」と手のトラッキングを切っても起きる。表情の `overrideBlink`（`block`・`blend`）は UniVRM を通るアプリのまばたきだけを止め、FX が書く BlendShape には効かない。

アプリが FX のまばたきを見分けるには、FX が動かす BlendShape が目を動かすかを知る必要がある。アプリは VRM の `blink`・`blinkLeft`・`blinkRight` の bind からまばたきの BlendShape を集めているが、書き出しはまばたきを `__VRVlog_Blink_` の専用の morph target に作り直して bind する。しかも Usasaki のまばたきは名前から左右のウインク（「ウインク」「ウィンク右」）で作られていて、FX が動かす「まばたき」はまばたきの元ですらない（左右の和との差は最大の動きの約 2%）。元の 2 つは書き出しの最後に使われない BlendShape として外される。

## 決定

- 新しい拡張 `VRVLOG_eye_shapes`（schema 1）に、目を動かす BlendShape を node と mesh の `extras.targetNames` での名前で書く。
- 目を動かす BlendShape は、名前ではなく形で決める。まばたきが閉じる mesh ごとに、まばたきの元の形が動かす頂点を「目の頂点」とし（`AvatarBaseShape.MouthVertices` と同じく、元の形のいちばん大きい動きの 20% 以上で 1、それより小さければ比例して 0 に近づく）、最後のフレームの動きの 50% 以上（`EyeMovementShare`）が目の頂点にある形を選ぶ。口の形を Lip Sync の形から決める方法（`2026-11-01-vowel-default-mouth.md`）と同じしきい値にした。閉じる形のほか、じと目などの目の形も入る。書き出しが作る `__VRVlog_` の形と読めない形は外す。
- 閉じる形だけでなく目の形をまとめて書く。アプリでは、目を閉じた表情が選ばれている間は FX が目に書く値を止める使い方をするので、選んだ表情が目を持つ間は FX の目の形も重ならない方がよい。VRChat で Tracking Control が目を Animation にしたステートが目を受け持つのと同じ考え方。
- 番号ではなく名前で指す。使われない BlendShape を外す段（`UnusedMorphs`）は番号を詰め直すが、この拡張は番号を持たないので、その段の確認済みの拡張の一覧に足す必要がない。書くのはその段より後（`LipSync` の後）にし、書く時点の最終ファイルにある名前だけを書く。外された名前や、最適化の統合で名前が変わったものは書かない。
- 瞬きなしで書き出すアバターと、書く形が 1 つも残らないファイルには拡張を書かない。書けなかったときはこの拡張だけを外して書き出しを続ける（`guard.Optional`）。
- 古いアプリは知らない拡張として無視するので、強制アップデートは要らない。`VRVLOG_requiredAppVersions` には読むアプリの版（1.3.1、仮）を書く。アプリの版が決まったら合わせて直す。

## 採らなかったもの

- まばたきの元の形だけを書く。Usasaki では FX が動かす「まばたき」が元に入らず、元の形も最終ファイルから外れるので、何も書かれなかった。
- `VRVLOG_avatar_animator` の morph target に印を付ける。読み手が厳密で、知らないキーを持つ target を拒むので、古いアプリが FX ごと読めなくなる。
- アプリで頂点を比べて見分ける。アプリは glTF の頂点から同じことができるが、元のメッシュの形を知っている書き出し側で 1 回決める方が、アプリごとに判定がぶれない。

## 確かめたこと

- Usasaki を、2.0.0 にこの変更を当てて書き出した。まばたきは「まばたき」（両目）と「ウィンク２」「ウィンク右２」（左右）から作られ、Body の 30 の形が目の形に選ばれた（閉じる形、ウインク、なごみ、じと目、下まぶた上げ、びっくり、たれ目、まつ毛丸、ハイライト消しなど）。使われない形を外した後の最終ファイルには、そのうち 7 つ（笑い、まばたき、なごみ、じと目、びっくり、たれ目、ハイライト消し）が残り、`VRVLOG_eye_shapes` に書かれた。FX が `EyeClose` で動かす「まばたき」が入っている。

## テスト

- `FormatsContractBoundaryTests.EyeShapeBoundaries` を足した。読める範囲（名前の長さ、別の node の同じ名前、4096 件）と拒む範囲（重複、空、4097 件、負や小数の node、空・長すぎる名前、余分なキー、別の schema）を確かめる。
- `BlinkExportTests.EyeShapesFollowTheVerticesTheBlinkMoves` を足した。まばたきと同じ頂点を動かす形・その一部を動かす形が選ばれ、口・顔全体（目の頂点が 40%）・動かない形・`__VRVlog_` の形が選ばれないこと、まばたきの元がメッシュに無ければ何も選ばないことを確かめる。
- `BlinkExportTests.ActualUniVrmExportBindsGeneratedTargetsToTheirFinalNodes` に、まばたきの形が動かないフィクスチャでは拡張を書かないことを足した。
