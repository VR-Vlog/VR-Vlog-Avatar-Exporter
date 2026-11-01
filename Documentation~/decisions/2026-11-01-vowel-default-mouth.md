# 母音のときに、FX の既定の状態が入れている口の形を打ち消す

- 日付: 2026-11-01
- 対象: `Editor/AvatarBaseShape.cs`（`Preserve`・`MouthRests`・`MouthVertices`・`Rebase`・`ShapesWithoutOtherRests`）・`Editor/NeutralShapeSnapshot.cs`（`DefaultFace`）・`Editor/VrChatExpressionBaker.cs`・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/BaseShapeTests.cs`・`Tests/Editor/DescriptorVisemeExportTests.cs`・`Tests/Editor/Fixtures/BaseShapeFixture.cs`

## 背景

FX の既定の状態で、viseme ではない口の形（への字の口や笑った口など）を 0 でない重みで入れているアバターでは、書き出した VRM の母音（aa・ih・ou・ee・oh）で、既定の口の上に母音の口が重なっていた。アプリのリップシンクで喋ると、口が 2 つ重なったような形になる。

## 原因

- 書き出しは、FX の既定の状態が入れている形を、名前や場所で絞らずにすべてメッシュの元の形に焼き込む（`AvatarBaseShape.Preserve`）。表情なしの顔はこれで既定の状態と同じになる。
- 母音は VRChat の descriptor の viseme の形を重み 1 で 1 つ bind するだけで、viseme の形から引くのはほかの viseme の既定の重みだけだった（`ExclusiveShapes` と `Rebase`）。viseme ではない既定の形は母音の下に残る。
- これは「VRChat では FX の既定の口が viseme の下に残る」という前提の設計で、`BaseShapeTests` もその前提をテストで固定していた。実際のアバターでは、既定の口のまま喋る作りにはなっておらず、書き出し前のアバターで母音を当てた形とも食い違っていた。

## 決定

- viseme の形（母音の形）を書き出すとき、ほかの viseme の既定の重みに加えて、既定の状態の「口の形」の既定の重みも引く。母音を 1 にすると、既定の口が消えて viseme の口だけになる。目・眉など口以外の既定の形は母音のときも残す。
- どれを「口の形」とするかは名前で決めない。次の 2 つで機械的に決める。
  - 口の頂点: 頂点ごとに 0〜1 の「口らしさ」を持たせる（`MouthVertices`）。viseme の形の最後のフレームがその頂点を動かす量を、その形のいちばん大きい動きで割り、viseme の形のうちいちばん大きい値をとる。20%（`MouthFullShare`）以上で 1、それより小さければ比例して 0 に近づく。
  - 口の形: FX の既定の状態が入れている形（`NeutralShapeSnapshot.DefaultFace`）のうち、既定の重みで動かす量（頂点ごとの移動量の合計）の 50% 以上（`MouthMovementShare`）が口の頂点にあるもの（移動量に口らしさを掛けて足す）。viseme の形そのものと、評価できない形は除く。
- 引くのは口の形の既定の重みのうち、口の頂点の分だけにする。頂点ごとに口らしさを掛けて引くので、口の外へ向かって打ち消しが少しずつ弱まり、口の境目に段差ができない。口と一緒に目を細める笑顔のように、口の形が口以外も少し動かすとき、その口以外の分は母音のときも残る。
- しきい値は手持ちのアバター 3 体の書き出しで決めた。viseme の形が動かす頂点は、いちばん大きい動きの 1% で切っても 20% で切っても数がほとんど変わらず（1 体は 991 と 971）、目・眉の形は口の頂点に動きがまったく無い（0%）。口の形は 65〜100% が口の頂点にある。最初は 1% より大きいかどうかで口の頂点を決めていたが、viseme の形が目のまわりをわずかに動かすアバターで目の形まで口の形に選ばれうること、口の境目で打ち消しが急に切れることから、比例して弱める形にした。
- まばたきに使う形（`BlinkShapeBinding` に入った形）は `DefaultFace` から外し、口の形に選ばない。まばたきのたびに既定の口が消えないようにするため。
- 打ち消しは母音の重みに比例する。母音を w で当てると、既定の口の w 分が消える（0.8 なら 2 割残る）。UniVRM の `MorphTargetBindingMerger` は「bind の重み × 表情の重み」を足し合わせ、上限で止めない。そのため、打ち消しが 1 を超えないのは「同時に効く母音の重みの合計が 1 以下」のときに限る。
  - アプリのリップシンクは、この条件を満たす（2026-11-02 にアプリの develop のコードで確かめた）。`VRVLOG_lip_sync` の `dominantViseme` が無いアバターでは、5 つの母音を同時に送るが、重みはトラッキングの口の動きの大きさ（0〜1 で止めた値）を 5 つに分けたもので、合計はその大きさを超えない（アプリの `LipSyncVowels.Estimate`）。VRM に無い母音をほかの母音へ寄せても合計は増えない。`dominantViseme` のアバターでは、母音を 1 つだけ、重み 0 か 0.8〜1 で送る。
  - アプリが合計 1 を超える母音を送るように変わったら、この打ち消しは既定の口を反対向きに出す。そのときは書き出し側で止めず（下の理由）、アプリで合計を 1 に止める。
  - メニューの表情と母音が重なるときは、UniVRM の `DefaultExpressionValidator` が `overrideMouth` に従って母音を 0 にする（block）か (1 − 表情の重み) を掛ける（blend）ので、打ち消しは二重にならない。
  - 母音ごとに 1 つの共通の打ち消しの形を作って重みを上限で止める案は採らなかった。止めるかどうかが読み込む側の設定（Unity の `legacyClampBlendShapeWeights` など）で変わるため。
- 既定の口の打ち消しを持つ viseme の形は「ほかの既定の重みを引いた形」になるので、メニューの表情でその形を使うときは、これまでほかの viseme の既定の重みを引いた形と同じく、bind せずに表情の形として焼く（`ShapesWithoutOtherRests`）。
- 既定の状態はレンダラーに直接入っている重みではなく、FX の既定の状態だけで決める。FX がどこでも動かさない形（レンダラーに直接入れた体形・顔の調整）は、これまでどおり母音の下にも残る。FX のどれかのレイヤーが動かす形は、既定の状態の値で決める。Write Defaults が on で既定の状態がその形を書かないときは、VRChat と同じくレンダラーの値が既定の状態の値になり、それが口の形なら母音で打ち消す。既定の状態が書いた値とこのレンダラーの値は VRChat の表情なしの顔では見分けがつかず、どちらも表情が動かす口の既定の形なので、同じに扱う。

## テスト

- `BaseShapeTests.PreserveRebasesTwoRestingVisemesAtOnce` は、既定の状態が Smile を入れているとき、ou を 1 にすると Smile が消えることを確かめるようにした（以前は「Smile は viseme ではないので ou の下に残る」を確かめていた）。
- `BaseShapeTests.PreserveKeepsARestTheFxDefaultStateDoesNotSet` を足した。FX の既定の状態が入れていない形（レンダラーに直接入れた重み）は、以前と同じく viseme の下に残る。
- `BaseShapeTests.PreserveTakesOnlyTheDefaultMouthOffAVowel` を足した。口・目・頬の既定の重みを持つメッシュで、表情なしは既定の顔のまま、aa を 1 にすると口の既定の形だけが消え、0.8 では 0.8 分が消え、aa と ou を 0.5 ずつ当てても口は 1 回分だけ消えること、口の形が少しだけ動かす目の頂点は母音のときも既定のまま残ること、口の形として口と頬だけが選ばれることを確かめる。
- `BaseShapeTests.PreserveFadesTheMouthCancelOutAtTheEdgeOfTheMouth` を足した。viseme の形がいちばん大きい動きの 20% 以上動かす頂点では既定の口がすべて消え、10% の頂点では半分だけ消える。
- `BaseShapeTests.ShapesWithoutOtherRestsHoldsEveryVisemeWithADefaultMouth` を足した。既定の口の形があるとき、viseme の形はすべてメニューの表情で bind せずに焼く側に入る。
- `BaseShapeTests.DefaultFaceLeavesOutTheBlinkShapes` を足した。口と目を同じだけ動かすまばたきの形も、まばたきに使っていれば口の形に選ばれない。
- `DescriptorVisemeExportTests.ActualExportTakesTheDefaultMouthOffAVowel` を足した。テストの中で作るアバター（FX の既定の状態が口の形を 60、目の形を 40 で入れる）を実際に書き出して UniVRM で読み込み、aa が 0・1・0.8 のときに描かれる頂点が、それぞれ「既定の口 60%＋目 40%」「目 40%＋aa」「既定の口 12%＋目 40%＋aa 0.8」と一致することを確かめる。
