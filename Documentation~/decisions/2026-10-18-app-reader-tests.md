# アプリの VRM 読み込み部を使うテストを Built-in のプロジェクトでも動かす

- 日付: 2026-10-18
- 対象: `Tests/Editor/AppReader.cs`（新規）・`Tests/Editor/LilToonFullExportTests.cs`・`Tests/Editor/LilToonFullRenderTests.cs`

## 背景

書き出した VRM をアプリの読み込み部（`com.vrvlog.avatar-runtime`）で読み戻すテストが、読み込み部を入れた Built-in のプロジェクトで 10 メソッド失敗していた（`EveryShaderFamilyLoadsThroughSchema2` 52 件・`ExtraMaterialDrawsBecomeExplicitPrimitivesWithoutEditingTheSourceMesh` 1 件・`FullExportLoadsThroughApplicationWithoutNameBasedFallback` 2 件・`MultiKeywordsMatchReference` 5 件・`TessellationCacheKeepsReusableEntriesBeyond256DistinctTuples` 1 件）。原因は次の 3 つ。

- テストがリフレクションで呼んでいた `UniVrmRuntimeLoader.ConfigureLilToon` が読み込み部から無くなった（今の読み込み部は lilToon を自分で判定する）。
- テストはアプリのシェーダー（`VR Vlog/lilToon 2.3.4/<id>`）があることを前提にしていた。Built-in のプロジェクトにそれは無く、読み込み部は `OfficialLilToonShaderSource` で公式の lilToon シェーダーを使う。ただし既定の `AvatarRuntimeHooks.ShaderSource` はアプリのシェーダーを指したままなので、そのまま読むとシェーダーが見つからず止まる。
- テッセレーションのキャッシュはアプリのネイティブプラグイン（`VRVlogTessellator`）を呼ぶ。読み込み部のパッケージにこのプラグインは入っていないので `DllNotFoundException` になる。

## 決定

- 読み込み部の呼び出しを `AppReader` にまとめる。asmdef に読み込み部への参照は足さず、リフレクションだけで呼ぶ（読み込み部が無いプロジェクトでもテストがコンパイルできるように）。
- アプリのシェーダーがあるプロジェクト（アプリ本体）では従来どおり読む。無いプロジェクトでは、読み込みとシェーダー名の解決のあいだだけ `AvatarRuntimeHooks.ShaderSource` を `OfficialLilToonShaderSource` に差し替え、`finally` で元に戻す。この値はプロセス全体で共有されるため。
- `FullExportLoadsThroughApplicationWithoutNameBasedFallback` は、読み込んだマテリアルのうち「そのプロジェクトの実行時シェーダー（アプリ本体ならアプリのシェーダー、それ以外なら読み込み部のカタログにある公式シェーダー）」を使うものを比べる。以前の「その集合のシェーダー名がすべてアプリの接頭辞で始まる」という検査は、絞り込みの条件と同じで常に真だったので、マテリアル名が `VRVLOG/` で始まることの検査に置き換えた。
- `MultiKeywordsMatchReference` はシェーダー名を `LilToonFullRuntime.ShaderName` で解決する（アプリ本体ではアプリのシェーダー、それ以外では公式のシェーダー）。

## Ignore のまま残すもの

- 読み込み部が無いプロジェクトでは、これまでどおり上の 5 メソッドを Ignore にする。
- `DefaultShaderMatchesReferencePixels`・`EnabledFeatureMatchesReferencePixels`・`FurSubdivisionAndRandomnessMatchReference` は、アプリのシェーダーが無いプロジェクトでは Ignore にする。公式のシェーダーで読むと、元のマテリアルと同じシェーダーどうしを比べることになり、何も確かめられないため。
- `MultiKeywordsMatchReference` のうち、公式のシェーダー一式で lilToon Multi のファー（`Hidden/lilToonMultiFur`）に解決されるケースは、アプリのシェーダーが無いプロジェクトでは Ignore にする。読み込み部は Multi のキーワードを外して `_Use*` の値だけで機能を切り替えるが、公式の Multi シェーダーはキーワードで機能を切り替えるので、この比較はアプリのシェーダーでしか成り立たない。
- `TessellationCacheKeepsReusableEntriesBeyond256DistinctTuples` は、ネイティブプラグインが読み込めないとき（`DllNotFoundException`）Ignore にする。

## 確認すること

- 公式のシェーダーで読む場合、ピクセル比較が通るかはそのプロジェクトの lilToon の設定（使う機能のシェーダーバリアントを残しているか）にも左右される。
- 公式の Multi のファーをキーワード無しで描く問題は、Built-in のプロジェクトで読み込み部を使うと実際の見た目にも出るおそれがある。読み込み部の側で確かめる。
