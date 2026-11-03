# 書き出しの出力側で、JSON の取り出し・非アクティブな複製・拡張の配置を 1 か所にまとめる

- 日付: 2026-11-03
- 対象: `Formats/Runtime/JsonAccess.cs`・`Editor/GlbDocument.cs`・`Editor/NdmfExportPreparation.cs`・`Editor/ExportRecovery.cs`・`Editor/ExportDiagnostics.cs`・`Editor/ExportOptimizationBindings.cs`・`Editor/ExportSkinRoots.cs`・`Editor/AvatarFormatRequirements.cs`・`Editor/UnusedMorphTargets.cs`・`Editor/ExpressionGestureCapture.cs`・`Editor/ObjectToggleCapture.cs`・`Editor/VrmMenuExpressions.cs`・`Tools/run-bake-tests.ps1`・`Tools/BakeTestShim.cs`

## 背景

大規模リファクタリング（[2026-11-02-big-refactor.md](2026-11-02-big-refactor.md)）の書き出しパイプラインの 2 回目。1 回目（[2026-11-02-export-pipeline-shared-helpers.md](2026-11-02-export-pipeline-shared-helpers.md) 相当の PR #608）は出力に影響しない所見だけを扱った。今回は出力側の写しをまとめるが、VRM の中身・警告の文言と順番・表情の集合は変えない。

## 決定

- 読み込んだ JSON から値を取り出す式（持ち主が無い・キーが無い・型が違うときに null を返す緩い読み方と、呼び出し側が作った例外を投げる厳しい読み方）を `Formats/Runtime/JsonAccess.cs` に置き、出力側の 5 ファイル・19 か所をそこへ寄せた。形式ごとの例外の型と文は呼び出し側が渡すので変わらない。`GlbBinaryOptimizer` の数値の読み取りは checked な変換で null のときの挙動が違うので寄せていない。
- 非アクティブな親の下に複製し、スクリプトの扱いを済ませてから根だけを見せて親から外す枠を `NdmfExportPreparation.InstantiateInactive` 1 つにし、書き出し用の複製と表示用の複製がそれを使う。スクリプトの扱いは呼び出し側が渡す。表示用の複製の `ExportGimmickSession` は `using var` から外側の `finally` での破棄になり、失敗時は複製の破棄のあとになるが、`Dispose` は自分が作ったアセットを消すだけで複製に触らない。
- `GlbDocument` に、キーで JSON をたどる `Find` と、拡張をルートに置いて `extensionsUsed` に重複なしで足す `PlaceExtension` を足し、表情のジェスチャー・オブジェクトの表示切り替え・表情アニメーションの 3 か所をそこへ寄せた。表情アニメーションは、ルートに `extensions` が無いときに前は例外で今は作る・`extensionsUsed` に名前だけ先にあるときに前は重複して足し今は足さない、という違いがあるが、どちらも UniVRM の出力では起きない。
- 焼き込みのハーネス（`Tools/run-bake-tests.ps1`）は本体の `LilToonMobileProfile.cs` をそのままコンパイルし、代役にあった定数の写しを消した。`.github` の 3 本のワークフローが `Tools/run-validation.py` 経由でこのハーネスを呼ぶので、ハーネス自体と本体の `#if` は残す（計画の B 案）。
- 採らなかった所見:
  - `ExportRecoveryReport.Stage` を `ExportStage` にする案（A1-11）。`Stage` に入るのは復旧の節目の名前 12 種で、うち 5 つは省けない段。`ExportStage` は省ける段だけを載せる表なので型が合わない。
  - 段の間で `GlbDocument` を渡す案（A1-06）。段の関数はどれも byte[] を受けて返し、入力をそのまま返す早期 return が 15 か所以上ある。UniVRM の出力は読み直すとバイト列が戻らず、数値の型も変わるので、1 つの DOM を書き換え続ける形にすると VRM のバイト列が変わる。
  - 理由を `ExportFallbackReport.Fallbacks` に 1 回だけ記録して警告をそこから作る案（A1-16）。警告は多くの処理が足す 1 本のリストで出る順番に意味があり、16 か所のうち 8 か所は警告の文が記録した理由と違う。成功の知らせの分離は transfer_ui のマージ後に、2 つのリストへ書く共通の関数（B 案）で行う。

## 確かめたこと

- 全 EditMode（Unity 2022.3.22f1・lilToon 2.3.4・VRChat SDK 3.10.1・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.8.16）で新しい赤 0・消えたケース 0。足したテスト 6 件（`JsonAccessTests` 4・`GlbDocumentTests` 2）は緑。
- `Tools/validate.py`・`Tools/test-compatibility-tooling.py` と、ホストのハーネス 5 本（compatibility・behavior・pose-contract・bake・ndmf-preparation）・`run-lan-transfer-tests.ps1` が緑。
