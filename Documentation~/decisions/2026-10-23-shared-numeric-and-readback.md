# 数値の有限チェックとGPUの読み戻しを共通の関数にまとめる

- 日付: 2026-10-23
- 対象: `Editor/NumericChecks.cs`・`Editor/GpuReadback.cs`（新規）、`Finite` を持っていた `Editor/` の各ファイル、`Editor/AlphaMaskBaker.cs`・`Editor/EmissionMaskBaker.cs`・`Editor/LilToonMainTextureBaker.cs`・`Editor/MatcapApproximation.cs`・`Editor/TextureColors.cs`・`Editor/UnsupportedShaderSubstitution.Toon.cs`・`Tools/`

## 背景

`Editor/` には、NaNと無限大を弾く `Finite(float)` が約20のファイルにそれぞれ private で書かれていた。`Vector3`・`Vector4` 版も4ファイルにあった。中身はどれも `!float.IsNaN(value) && !float.IsInfinity(value)` で同じだった。

GPUで描いて画素を読み戻す処理も、同じ手順が複数の場所に書かれていた。

- ベイク用の材質をsRGBの一時RenderTextureに描き、RGBA32のミップマップ無しの画像として読み戻す処理: `AlphaMaskBaker.Bake`・`EmissionMaskBaker.Create`・`LilToonMainTextureBaker.Bake`・`MatcapApproximation.Bake` の4か所。一時RenderTextureの形式（ARGB32・sRGB）、`GL.sRGBWrite` の設定と戻し方、読み戻した画像の形式・ラップ・フィルター、失敗したときの後始末まで同じで、違うのは描き方（Blitの材質とパス、シェーダーのコンパイルを待つかどうか）と画像の名前だけだった。
- リニアの一時RenderTextureに描いて `Color[]` で読む処理: `TextureColors.ReadLinear`・`UnsupportedShaderSubstitution` の `ReadRow`・`Sampled`・`MatcapApproximation.Mean` の4か所。違うのは大きさ・形式（Half か Float か）・Blitの引数だけだった。

## 決定

書き出すVRMは変わらない。計算・形式・色空間・名前・順序は元のコードのままにした。

- `NumericChecks`（`internal static`）に `Finite` の `float`・`double`・`Vector3`・`Vector4` 版を置き、使うファイルは `using static VRVlog.LilToonExporter.NumericChecks;` で読み込む。呼び出し側は `Finite(value)` のまま変えていない。partial クラスの別ファイル（`ExperimentalExpressionCaptureSession.Clips.cs`・`UnsupportedShaderSubstitution.Toon.cs`）にも同じ `using static` を足した。`PoseMenuResolver` の `PoseSampling.Finite(...)` は `Finite(...)` にした。
- `NeutralShapeSnapshot.Finite` は、ほかのファイルが `NeutralShapeSnapshot.Finite(...)` の名前で呼んでいるので、`NumericChecks.Finite` を呼ぶだけの形で残した。
- `GpuReadback.BakeRgba32` に、sRGBの一時RenderTextureへ描いてRGBA32で読み戻す手順を置いた。描き方は呼び出し側が `Action<RenderTexture>` で渡し、各呼び出しの `Graphics.Blit` はそのまま渡している。ラップ・フィルターは渡した画像から写し、画像が無いときは `Repeat`・`Bilinear`（`LilToonMainTextureBaker` の元の書き方と同じ）にした。ほかの3か所は常に画像を渡すので元と同じ値になる。
- `GpuReadback.BlitCompiled` に、シェーダーの非同期コンパイルを止めて描く処理（`LilToonMainTextureBaker`・`MatcapApproximation` にあったもの）を置いた。
- `GpuReadback.LinearPixels` に、リニアの一時RenderTextureへ描いて `Color[]` で読む手順を置いた。Gammaプロジェクトでの `.linear` への変換（`TextureColors`）と、`GL.sRGBWrite` を切る処理（`MatcapApproximation.Mean`）は呼び出し側に残した。`ReadRow`・`Sampled` の、読めなかったときに null を返す `catch` も呼び出し側に残した。
- `Tools/run-bake-tests.ps1` と `Tools/run-behavior-tests.ps1` のコンパイル対象に新しいファイルを足し、`Tools/BakeTestShim.cs` に `Vector3` の代用品を足した。`GpuReadback.LinearPixels` は `GetPixels` を使うので、ベイクのハーネス（`EXPORTER_BAKE_TESTS`）ではコンパイルしない（`DeterministicNames.cs` と同じ書き方）。

元のコードと順序が変わったところは次のとおり。どれも描く内容と読み戻す画素には関わらない。

- 一時RenderTextureの解放と `GL.sRGBWrite`・`RenderTexture.active` の戻しが、`owned.Add` や材質への設定の前（読み戻しの直後）になった。
- 読み戻す画像の名前を、Blitの前に作るようにした。名前の重複を調べる一覧は、その間に変わらない。
- `MatcapApproximation.Mean` と `TextureColors.ReadLinear` で、読み戻し用の `Texture2D` を作るのがBlitの後になった。
- `ReadRow`・`Sampled` で、`RenderTexture.GetTemporary` が例外を投げたときも null を返すようになった（元は `try` の外で呼んでいた）。グラフィックスデバイスが無いときは元どおり先に null を返す。

## そのままにしたこと

- `AvatarBaseShape`・`NeutralCurveConditions`・`VrChatExpressionSampler` の `Finite`: 同じ時期の別の変更がこれらのファイルを触っているため、今回は触らない。次にまとめられる候補。
- `HumanoidAnimationData` の `Finite(double)`: `Tools/run-pose-contract-tests.ps1` がこのファイルだけをUnity無しでコンパイルしており、ほかのファイルを読み込めない。
- `Formats/Runtime/ExpressionAnimationData.cs` の `Finite`: 別のアセンブリ（`VRVlog.AvatarFormats`）で、Editorの型を参照できない。
- `LilToonExtensionValidator.IsFinite`: `Tools/validate.py` がこのファイルに `float.IsNaN(value)` の文字列があることを確かめている。`Tools/validate.py` も別の変更と重なるため、今回は触らない。
- `NeutralShapeSnapshot.Finite(...)` の名前で呼んでいるファイル（`AdditionalParameterConstants`・`AuthoredExpressionEndpoints`・`FacialProjectionScope`・`HeldAutomaticExpressionLayers`・`NeutralAdditionalDataPreflight`・`NeutralShapePlan` など）: 呼び方を変えても中身は同じなので、差分を増やさないよう残した。
- GPUの読み戻しのうち手順が違うもの: `LilToonFullTexture.ReadPixels`（形式とsRGBが引数で変わり、バイト列で読んで検査する）、`ExportVrmLilToonPreview.EncodeNormal`（ミップごとに読む）、`OutlineMaskTexture`（ミップマップ有り・リニアのRenderTextureで、読み戻す前に所有の一覧に入れる）、`MobileTextureEncoder`（`ReadPixels` を使わない）。無理にまとめると引数が増えて元より読みにくくなるので、それぞれのままにした。
- `CHANGELOG.md`: 同じ時期の別の変更が触っているため、この変更の項目は足していない。利用者から見える変化も無い。

## 確認したこと

- Unity 無しのハーネス: `Tools/run-bake-tests.ps1`（355件）・`Tools/run-behavior-tests.ps1`（389件・照明854件・テクスチャー縮小31件・スキンのルート7件）・`Tools/run-pose-contract-tests.ps1`（91件）・`Tools/run-ndmf-preparation-tests.ps1`（53ケース）・`Tools/run-compatibility-tests.ps1` が通った。
- Unity で生成したプロジェクトの参照（UnityEngine・VRChat SDK・NDMF など）を使い、`Editor/` のアセンブリを変更前と変更後でUnity無しにコンパイルして、エラーと警告の一覧が行番号のずれ以外同じことを確かめた（同梱のUniVRMはこの方法ではコンパイルしていない）。
- Unity の EditMode でのコンパイルとテスト、40体のアバターで書き出したVRMのバイト列の比較は別に行う。
