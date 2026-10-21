# 表情にも使われずアプリからも動かせない BlendShape を VRM に入れない

- 日付: 2026-10-21
- 対象: `Editor/UnusedMorphTargets.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportStageGuard.cs`・`Editor/Locales/*.json`・`Tests/Editor/UnusedMorphTargetsTests.cs`・`Tests/Editor/Fixtures/UnusedMorphTargetsFixture.cs`・`Tools/run-behavior-tests.ps1`・`CHANGELOG.md`

## 背景

書き出した VRM には、元のアバターのメッシュにある BlendShape（glTF の morph target）がすべて入っている。衣装の補正用や、VRChat のメニューでだけ使う変形など、VRM の表情にもアプリのフェイストラッキングにも使われないものが多く、そのぶんファイルが大きくなっていた。24 体の書き出し結果で数えると、使われない BlendShape を外せばファイルは約 11% 小さくなる。

アプリが BlendShape を動かす経路は次の 4 つだけで、これ以外の BlendShape はアプリからは動かせない。

- `VRMC_vrm` の表情の `morphTargetBinds`
- 表情のアニメーション（`VRVLOG_expression_animations`）。schema 2 は `{node, index}`、schema 1 は BlendShape の名前で指す
- 名前による直接の駆動。アプリの固定の対応（`A`・`Joy`・`Blink_L` など）、瞬きを戻すときの名前、MediaPipe の `_neutral`、Perfect Sync の名前、Unified Expressions の名前
- メッシュの初期の重み（0 でない値はそのまま見た目に効く）

## 決定

- 書き出しの最後のほう、フェイストラッキングの追加（`TrackingOutput`）のあと、lilToon の材質情報（`LilToonFullSnapshot`）の前に、新しい段 `UnusedMorphs` を置く。表情・瞬き・フェイストラッキングがすべて bind を書き終えたあとなので、bind を数え漏らさない。
- 残す BlendShape は次のどれかに当てはまるもの。
  - `VRMC_vrm` の表情の `morphTargetBinds`、または表情のアニメーションの `{node, index}` が指している
  - schema 1 の表情のアニメーションが名前で指している
  - アプリが名前で動かす（`UnusedMorphTargets.AppDrives`。`TrackingExpressionNames.IsPerfectSync` と `UnifiedExpressionRegistry.TryCanonicalize` を使い、`VRChat /` の下の名前はメニューの表情なので外す）
  - メッシュまたはノードの初期の重みが 0 でない
  - 名前が無い（何に使われているか分からないので残す）
- 外すときは、各 primitive の `targets`・メッシュとノードの `weights`・`extras.targetNames` を同じ並びで詰め、`morphTargetBinds` と表情のアニメーションの `index` を詰めたあとの番号に付け替える。BlendShape が 1 つも残らないメッシュは `targets`・`weights`・`targetNames` のキーごと消す。
- 外した BlendShape だけが使っていた accessor と bufferView を消して番号を詰め、バイナリを 4 バイト境界を保ったまま詰め直す。残る BlendShape と共有している accessor は残す。書き出し前から使われていなかった accessor や bufferView には触らない。
- 次のときはファイルをそのまま返し、理由を Unity の Console に警告（`Debug.LogWarning`）で 1 行出す。書き出しの警告や結果一覧には出さない。ファイルはこれまでどおりなので、使う人が対応することは無い。
  - 中身を確かめていない拡張がある。確かめたのは `VRMC_vrm`・`VRMC_springBone`・`VRMC_springBone_extended_collider`・`VRMC_springBone_limit`・`VRMC_node_constraint`・`VRMC_materials_mtoon`・`KHR_materials_unlit`・`KHR_texture_transform`・`KHR_materials_emissive_strength`・`VRVLOG_expression_animations` で、ほかの拡張は accessor や BlendShape の番号を持っているかもしれないので、何もしない。
  - バッファが GLB のバイナリ 1 つでない、bufferView が範囲の外を指しているなど、詰め直してよいか確かめられない。
  - 表情のアニメーションの拡張が読めない。
- メッシュ単位で次のときはそのメッシュを外さない。primitive ごとの BlendShape の数が違う、重みや名前の数が合わない、bind が範囲の外を指している、glTF のアニメーションが重みを動かしている。
- 外した BlendShape の数と名前（名前は先頭の 20 個まで）は Unity の Console に `Debug.Log` で 1 行出すだけで、書き出しの警告や結果一覧（`ExportOutcome.Omitted`）には出さない。外すのは書き出しの通常の結果で、代わりの手段に落ちた（フォールバックした）わけではないため。結果一覧に出すと、ほとんどの書き出しで「省略あり」と警告が付き、何かを失ったように見えてしまう。
- 既定でオンにして、画面の切り替えは置かない。`UnusedMorphTargets.Enabled` を `false` にすれば、これまでどおりすべての BlendShape を書き出す。
- この段が失敗したときは、段を飛ばしてすべての BlendShape を入れたまま書き出す（`ExportOutcome.Approximated`）。

## 互換性

- 古いアプリで新しい VRM を開く: アプリが動かせない BlendShape だけを外し、表情の bind は付け替えるので、表情・瞬き・フェイストラッキングの動きは変わらない。
- 新しいアプリで古い VRM を開く: 古い VRM はすべての BlendShape を持っているので、これまでどおり動く。
- ほかの VRM アプリで開く: VRM の表情として書いたものはすべて残るので、表情の切り替えは変わらない。外した BlendShape を名前で直接動かすアプリでは、その BlendShape が無くなる。ただし Perfect Sync と Unified Expressions の名前は残すので、フェイストラッキング用の名前で動かす使い方は変わらない。

既定をオンにしたのは、外す BlendShape はこのツールの書き出し先であるアプリでは動かせず、ファイルが約 1 割小さくなるからで、困る場面が見つかっても定数 1 つで元に戻せるため。

## 入れなかったもの

- 外した BlendShape を書き出し結果の一覧や警告に出すこと。理由は上の決定のとおり。
- 外すかどうかを画面で切り替えること。アプリで使えないものだけを外すので、選ぶ場面が無い。
- 書き出し前から使われていなかった accessor や bufferView を消すこと。この段で使われなくなったものだけを対象にして、ほかの段の結果に手を入れない。
- 中身を確かめていない拡張があるときに、その拡張だけを避けて外すこと。どこに番号を持っているか分からない拡張は、ファイルごと触らないほうが安全なため。
- 設定ファイルを手で書き換えて、任意の BlendShape の名前をアプリの表情に対応させる使い方。アプリの既定の対応で使う名前は、残す名前（`A`・`Joy`・`Blink_L` など）にすべて入っていて、アプリには対応を書き換える画面が無い。

## 確認したこと

- ホストのテスト（`Tools/run-behavior-tests.ps1`）で `UnusedMorphTargetsFixture` の 11 件（247 個の確認）が通過した。
- Unity EditMode の結果: 前回の実行で、この段のテスト 70 件がすべて通過した（70/70）。疎な accessor（glTF の sparse accessor）のテストはこのあと追加し、追加後の結果は PR に書く。
