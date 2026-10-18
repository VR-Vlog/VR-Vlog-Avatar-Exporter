# NDMF のビルド処理で変形や Renderer が消えても、元の名前で追って書き出しを続ける

- 日付: 2026-10-18
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/PreparedExpressionBindings.cs`・`Editor/BlinkExportSession.cs`・`Editor/UnifiedExpressionPreparation.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/Locales/*.json`・`Tests/NdmfPlugin/LostMorphTestPlugin.cs`・`Tests/Editor/NdmfLostMorphFollowTests.cs`

## 背景

書き出しは、NDMF の Generating・Transforming で動く処理（Modular Avatar や、そのあとに動く別のプラグイン）を書き出し用コピーに通してから、表情・瞬き・表情トラッキングの対象を処理後のコピーへ付け替える。付け替えの途中で、必要な BlendShape や Renderer が見つからないと例外を投げていた。

`ContinueOnStageFailure` の設定（書き出し画面とアプリへの転送の既定）でも、この例外は `afterTransforming` の中で起きるので NDMF のビルドの失敗として扱われ、`BuildProcess` の段階がまるごと省略されていた。Modular Avatar の Merge Armature や Bone Proxy が適用されないファイルになる（`2026-10-18-stage-guard.md` の残した課題の 2 つ目）。

## 想定した場面と扱い

- (a) 生成した変形（`__VRVlog_Menu_` など）や使っていない変形だけが削られ、元の変形は残る。
  - 残った変形はそのまま使う。
  - 削られた生成の変形は、その名前で「表情」の省略として記録し、警告を出す。
  - 削られた変形を使っていた表情は、その変形の結び付きだけを外す。ほかの変形が残る表情は「近似」、何も残らない表情は「省略」として記録し、表情の名前を警告に出す。
- (b) GameObject の名前が変わる、または SkinnedMeshRenderer が別のものに差し替わる。
  - 元の Renderer の変形の名前で、処理後に同じ名前の変形を新しく持った Renderer を探し、それが 1 つだけなら付け替える。表情は 1 件ずつ「近似」として記録し、警告を出す。瞬きも同じく付け替えて「近似」として記録する。
- (c) 顔のメッシュが別の Renderer に統合される。
  - (b) と同じく名前で追う。統合先は、処理前にその名前の変形を持っていなかった Renderer に限る。候補が 2 つ以上あれば追わない。
- (d) 変形が本当に消えた。
  - 表情はその結び付きを外し、(a) と同じく記録する。
  - 瞬きは追えなければ「瞬きなし」にし、警告と「瞬き」の省略を記録する。
  - 表情トラッキングは、元の変形との照合を省略して「近似」として記録する。出力の経路は処理後のコピーから作り直すので、トラッキング自体は残る。

## 決定

- 追うのは名前だけ。`NdmfExportPreparation` が処理の直前に各 Renderer の変形の名前を控え、`FollowShapes` で「元の Renderer（または登録された差し替え先）がその名前を持っていればそれ」「なければ、名前をすべて新しく持った表示中の Renderer が 1 つだけならそれ」を返す。同じ名前の変形をもともと持っていた Renderer には付け替えない。
- 続ける設定（`ContinueOnStageFailure`）のときだけ追う。止める設定は従来どおりの例外を投げる。名前で追うのは、処理の前後で変形が同じものだと確かめる従来の照合より弱い。止める設定を使う呼び出し元と既存のテストは、その照合で止まることを前提にしている。
- 追えない項目は、その表情・瞬き・照合だけを省き、`BuildProcess` は省略しない。Modular Avatar の処理は残る。
- 警告は表情の名前と変形の名前を出す。記録は `ExportFallbackReport.Results` に、付け替えたものを「近似」、外したものを「省略」として残す。
- 最適化ツールの経路（`ExportOptimizationBindings`）は変えない。

## 確認したこと

- `Tests/NdmfPlugin/LostMorphTestPlugin.cs` は、Modular Avatar のあとの Transforming で動くテスト用のプラグイン。目印の子オブジェクトがあるアバターにだけ、(a) 変形を削る、(b) 名前を変えて Renderer を差し替える、(c) 顔のメッシュを別の Renderer に統合する、の 3 つを行う。NDMF が入っているときだけコンパイルされる。
- `Tests/Editor/NdmfLostMorphFollowTests.cs` で 3 つの場面を続ける設定で書き出し、次を確かめた。
  - 書き出しが終わり、`BuildProcess` の省略が記録されない。Bone Proxy で頭の下に移したオブジェクトが、出力でも頭の下にある。
  - 出力の VRM で、表情と瞬きが正しい Renderer の正しい向きに動く変形を指す。
  - 外した項目と付け替えた項目が、警告と結果の一覧に残る。
- 止める設定で統合の場面を書き出すと、従来どおり例外で止まることも確かめた。
- テスト用のプロジェクトで EditMode テストを全件回し、変更前と比べて新しい失敗がないことを確かめた。

## 検討した代替案と不採用理由

- 頂点の位置や変形の量で、処理前後の変形を突き合わせる: 統合や頂点の並べ替えのあとでは一致の判定があいまいになる。名前で追い、追えないときは省く方が結果を説明しやすい。
- 追えないときに `BuildProcess` を省略して書き出し直す（従来の挙動）: 衣装や追従が外れたファイルになる。表情の一部が欠ける方が影響が小さい。
- 止める設定でも追う: 既存の呼び出し元が頼っている照合を弱めることになる。

## 残した課題

- メニューや FX から作る表情と FaceEmo の表情は追わない。処理後に対象が見つからない項目は、従来どおり 1 件ずつ外れ、続ける設定ではその表情を「省略」として記録するだけ。
- 変形の名前を変えるプラグインは追えない。その変形を使う表情は外れる。
- 表情トラッキングの照合を省略したときは、変形の動きが元と同じかを確かめていない。
- 統合先の候補が 2 つ以上あるときは、確実に選べないので省く。

## 影響

- 続ける設定では、NDMF の処理で変形や Renderer が動いても、Modular Avatar の処理を残したまま書き出す。付け替えた表情と省いた表情は警告と `ExportFallbackReport.Results` に残る。
- 止める設定の挙動は変わらない。
