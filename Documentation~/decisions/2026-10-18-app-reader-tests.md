# アプリの VRM 読み込み部を使うテストを Built-in のプロジェクトでも動かす

- 日付: 2026-10-18
- 対象: `Tests/Editor/AppReader.cs`（新規）・`Tests/Editor/LilToonFullExportTests.cs`・`Tests/Editor/LilToonFullRenderTests.cs`

## 背景

書き出した VRM をアプリの読み込み部（`com.vrvlog.avatar-runtime`）で読み戻すテストが、読み込み部を入れた Built-in のプロジェクトで 5 メソッド・61 件失敗していた（`EveryShaderFamilyLoadsThroughSchema2` 52 件・`ExtraMaterialDrawsBecomeExplicitPrimitivesWithoutEditingTheSourceMesh` 1 件・`FullExportLoadsThroughApplicationWithoutNameBasedFallback` 2 件・`MultiKeywordsMatchReference` 5 件・`TessellationCacheKeepsReusableEntriesBeyond256DistinctTuples` 1 件）。原因は次の 3 つ。

- テストがリフレクションで呼んでいた `UniVrmRuntimeLoader.ConfigureLilToon` が読み込み部から無くなった（今の読み込み部は lilToon を自分で判定する）。
- テストはアプリのシェーダー（`VR Vlog/lilToon 2.3.4/<id>`）があることを前提にしていた。Built-in のプロジェクトにそれは無く、読み込み部は `OfficialLilToonShaderSource` で公式の lilToon シェーダーを使う。ただし既定の `AvatarRuntimeHooks.ShaderSource` はアプリのシェーダーを指したままなので、そのまま読むとシェーダーが見つからず止まる。
- テッセレーションのキャッシュはアプリのネイティブプラグイン（`VRVlogTessellator`）を呼ぶ。読み込み部のパッケージにこのプラグインは入っていないので `DllNotFoundException` になる。

## 決定

- 読み込み部の呼び出しを `AppReader` にまとめる。asmdef に読み込み部への参照は足さず、リフレクションだけで呼ぶ（読み込み部が無いプロジェクトでもテストがコンパイルできるように）。
- アプリ本体のプロジェクトかどうかは、アプリの側にだけある `FaceMaskVTuber.AvatarRuntimeHostInstaller` が読み込まれているかで決める（`AppReader.AppProject`）。シェーダーがあるかどうかでは決めない。アプリ本体でシェーダーが欠けたときに、テストが Ignore に倒れて見逃すため。
- アプリ本体では従来どおり読み、`AvatarRuntimeHooks.ShaderSource` には触らない。それ以外のプロジェクトでは、読み込みとシェーダー名の解決のあいだだけ `OfficialLilToonShaderSource` に差し替え、終わったら元に戻す。この値はプロセス全体で共有されるため、戻すのは自分が入れた値がまだ入っているときだけにする。
- 読み込みは、読み込み部のプレビューが同じ値を差し替えるときに使うロック（`AvatarRuntimePreview.LoadGate`）を取ってから行い、プレビューの読み込みと重ならないようにする。ロックが無い古い読み込み部ではロックを取らない。シェーダー名の解決は差し替えのあいだに `await` が無く他の処理が割り込めないので、ロックを取らない（メインスレッドでロックを同期で待つと止まるおそれがあるため）。
- `FullExportLoadsThroughApplicationWithoutNameBasedFallback` は、読み込んだマテリアルのうち「そのプロジェクトの実行時シェーダー（アプリ本体ならアプリのシェーダー、それ以外なら読み込み部のカタログにある公式シェーダー）」を使うものを比べる。以前の「その集合のシェーダー名がすべてアプリの接頭辞で始まる」という検査は、絞り込みの条件と同じで常に真だったので、マテリアル名が `VRVLOG/` で始まることの検査に置き換えた。公式のシェーダーで読むと、ピクセル比較（`Compare`）は元と同じシェーダーどうしになる。この場合の比較はシェーダーが同等かではなく、値が実行時のマテリアルへ渡っているかを確かめるものとして残す。
- `MultiKeywordsMatchReference` はシェーダー名を `LilToonFullRuntime.ShaderName` で解決する（アプリ本体ではアプリのシェーダー、それ以外では公式のシェーダー）。

## Ignore のまま残すもの

- 読み込み部が無いプロジェクトでは、これまでどおり上の 5 メソッドを Ignore にする。
- `DefaultShaderMatchesReferencePixels`・`EnabledFeatureMatchesReferencePixels`・`FurSubdivisionAndRandomnessMatchReference` は、アプリ本体以外では Ignore にする。公式のシェーダーで読むと、元のマテリアルと同じシェーダーどうしを比べることになり、何も確かめられないため。アプリ本体でアプリのシェーダーが見つからないときは失敗にする。
- `MultiKeywordsMatchReference` のうち、公式のシェーダーで `Hidden/lilToonMultiFur` に解決されるファーのケースだけを、アプリ本体以外では Ignore にする。読み込み部は Multi のキーワードを外して `_Use*` の値だけで機能を切り替えるが、公式の Multi シェーダーはキーワードで機能を切り替えるので、この比較はアプリのシェーダーでしか成り立たない。ファー以外の Multi が公式の Multi シェーダーに解決された場合は失敗にする。
- `TessellationCacheKeepsReusableEntriesBeyond256DistinctTuples` は、アプリ本体以外でネイティブプラグインが読み込めないとき（`DllNotFoundException`）Ignore にする。アプリ本体では失敗にする。

## 確認したこと

- 読み込み部を入れた Built-in のプロジェクトで実行し、失敗していた 61 件のうち 59 件が通った。残る 2 件は上の Ignore（Multi のファーとテッセレーション）。全体は 3364 件で、失敗 8 件はいずれも実行環境に由来するもので、この変更とは関係ない。
- この結果は、アプリ本体の判定・ファーだけの Ignore・ロックを入れる前の版で取った。

## 確認すること

- 公式のシェーダーで読む場合、ピクセル比較が通るかはそのプロジェクトの lilToon の設定（使う機能のシェーダーバリアントを残しているか）にも左右される。
- 公式の Multi のファーをキーワード無しで描く問題は、Built-in のプロジェクトで読み込み部を使うと実際の見た目にも出るおそれがある。読み込み部の側で確かめる。
