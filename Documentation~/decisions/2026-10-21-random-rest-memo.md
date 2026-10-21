# 表情の読み込み中は、選んだ表情に関係しない Random の待機レイヤーの判定を使い回す

- 日付: 2026-10-21
- 対象: `Editor/ExpressionInspectCache.cs`・`Editor/HeldAutomaticExpressionLayers.cs`・`Tests/Editor/HeldAutomaticExpressionTests.cs`・`CHANGELOG.md`

## 背景

表情の読み込み（`VrChatExpressionSampler.Analyze`）では、表情メニューの項目ごとに `Evaluate` を呼ぶ。`Evaluate` は最初に `SelectedRandomRestProbe.Create` を呼び、その中の `HeldAutomaticExpressionLayers.FindUnownedRandomRest` が、Random を使う待機レイヤーを外してよいかを確かめる。

手元で区間を測ると、`SelectedRandomRestProbe.Create` は重いアバターで 1 回あたり約 87 ミリ秒、Analyze 34 秒のうち 9.5 秒を占めていた。軽いアバターでは 1 回あたり約 16 ミリ秒、Analyze 9.5 秒のうち約 1.6 秒だった。

時間の大半は `FindUnownedRandomRest` の中の次の 2 つの呼び出しにかかる。

- `NeutralShapePlan.CreateProjection(avatar, 空, 空)`
- `NeutralRandomRest.Preserve(runtime, source, proof, null, context)`

この 2 つは、選んだ表情（`selectedMorphs`・`selectedInputs`）を受け取らない。コードを読んで次のことを確かめた。

- `CreateProjection` に渡す BlendShape とクリップは空なので、作られる計画の `CommittedMorphs` は空になる。アバターの階層は読むが、値の検証も階層の保護も空の集合に対してしか行わない。オブジェクトの作成・削除やメッセージの追加は無い。
- `Preserve` が読むのは、FX のコントローラー、`source` の `OtherControllers`・`ExternalParameters`・`Defaults`・`ExpressionParameters`・`ParameterPersistence`・`ExpressionParameterTypes`（`FixedNeutralValues` と `AdditionalParameterConstants.Prove` を通して）、`context.Values` だけだった。`warnings` は null で、計画の `CommittedMorphs` が空なので `PreserveTemporalRest` も警告の追加も通らない。`context.UsedParameters` も書き換えない。
- `source` のこれらの値を書き換えるのは `VrChatExpressionMenu.Read` と `NeutralInputProof.Read` だけで、どちらも Analyze の項目のループより前に終わる。
- `FixedExpressionContext` は項目ごとに作り直すが、`Values` は FX のパラメーターと `source.Defaults` から同じ手順で作るので、同じ Analyze の中では同じ中身になる。

## 決定

- `ExpressionInspectCache` に、この 2 つの呼び出しの結果（`Preserve` が返すレイヤー番号の集合）を覚える場所を足す。#212 と同じ Analyze のスコープの中だけで有効にする。
- キーは、アバターのインスタンス ID、FX のコントローラーのインスタンス ID、`source` の `RuntimeHelpers.GetHashCode`、`context.Values` の中身（名前の ordinal 順に並べ、値は float のビット列）にする。ハッシュコードは一意ではないので、覚えた `source` と参照が同じときだけ使う。
- FX のコントローラーが保存済みのアセットでないとき（Inspect のキャッシュと同じ条件。Override Controller は元のコントローラーも保存済みのときだけ）は使い回さない。`source.OtherControllers` に保存済みでないコントローラーがあるときも使い回さない。`Preserve` はほかのプレイアブルのコントローラーも読むためで、これは Inspect のキャッシュがプローブを入れない方針に合わせた。
- 覚えるときも返すときも複製する。2 つの呼び出しのどちらかが例外を投げたときは覚えない。
- テスト用に、当たった回数と外れた回数（`RandomRestHits`・`RandomRestMisses`）を数える。

## 変えないこと

- `FindUnownedRandomRest` の中で、2 つの呼び出しより前にある処理（Inspect、`HasOnlyUnchangedUnitFxCommands` など）と、その後のレイヤーごとの確認（選んだ BlendShape・入力との重なり、ゲートの確認、`GateRoutesReturnToPreparedRest`）は、順番も含めて毎回これまでどおり行う。選んだ表情で結果が変わる部分は使い回さない。
- 使い回した集合は、元と同じ順番でレイヤー番号を並べるので、結果の集合の順番も変わらない。
- スコープの外（Analyze 以外からの呼び出しとテスト）では、これまでどおり毎回計算する。

## 確認したこと

- `HeldAutomaticExpressionTests` に次のテストを足した。
  - 1 つのスコープの中で、選んだ入力が違う呼び出しの結果が、スコープの外で計算した結果と一致し、3 回の呼び出しで 2 つの呼び出しの計算が 1 回だけになること。返した集合を書き換えても次の呼び出しに影響しないこと。
  - 覚えた集合と返した集合が、呼び出し元の書き換えの影響を受けないこと。別の `source` には使い回さないこと。
  - `InStation` の固定値だけが違う 2 つの呼び出しを別々に計算すること。
  - スコープの外と、保存済みでない FX・ほかのプレイアブルのコントローラーでは使い回さないこと。
- 複製せずに返す版と、キーに `context.Values` を含めない版に一時的に書き換えると、それぞれ対応するテストが失敗することを確かめた。
- EditMode のテスト（`ExpressionInspectCacheTests`・`RandomNeutralRestTests`・`HeldAutomaticExpressionTests`・`ParameterDriverExpressionTests`・`VrChatMenuExpressionTests`・`NeutralShapeSamplerTests`・`ExperimentalExpressionCaptureTests`）は 655 件中 637 件が通り、2 件が飛ばされ、16 件が失敗した。失敗した 16 件は `ExperimentalExpressionCaptureTests` で、変更前と同じく外部パッケージが無いことによるもの。
- 実際のアバターで書き出した VRM の一致と時間の比較は、計測してから追記する。
