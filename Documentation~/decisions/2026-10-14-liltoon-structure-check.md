# 書き出し前に lilToon のシェーダー構成を 2.3.4 のカタログと照合する

> 2026-10-22の[lilToonのバージョンで書き出しを止めない](2026-10-22-liltoon-no-version-gate.md)で一部を置き換えた（止めるのはベイク用シェーダーの違いだけで、ほかの違いは警告にする）。

- 日付: 2026-10-14
- 対象: `Editor/LilToonStructureCheck.cs`・`Editor/LilToonStructureCheck.Unity.cs`・`Editor/LilToonExporterWindow.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/Compatibility/DependencyPolicy.cs`・`Editor/Locales`・`Tests/Editor/LilToonStructureCheckTests.cs`・`Tools/LilToonStructureCheckTests.cs`・`Tools/run-compatibility-tests.ps1`・`Tools/run-unity-compatibility.py`
- Issue: #148 の後半（Epic #145 の WBS 1-5）

## 背景

#146 で lilToon 2.0.0〜2.3.4 を書き出せるようにした。範囲の根拠は「この範囲の版は lilToon 本体の移行処理だけで 2.3.4 と同じ構成になる」という実測なので、書き出しのたびに導入済みのシェーダーがその構成になっていることを確かめたい。

書き出しの経路には、すでに構成に関わる検査が 2 つある。

- `LilToonFullSnapshot.ReadMaterial`（マテリアルを読む途中）: カタログに無いプロパティ名で止まる。値の型はシェーダーではなくカタログの宣言から書く。
- `LilToonFullContract.Validate`（書き出しの最後・アプリと同じコード）: マテリアルごとの runtime のプロパティの集合、名前付きのパスの集合と LightMode、割り当てた画像の面の数をカタログと比べる。

どちらも止まるのはベイクや UniVRM の変換のあとで、文言は開発者向けの英語か、日本語でも訳が無い。シェーダーが宣言するプロパティの型、画像プロパティの次元（2D／Cube）、editor／external のプロパティの型、ベイク用シェーダー（`Hidden/ltsother_baker`）は誰も見ていない。

## 決定

### どこで照合するか

- 書き出しの入口で版の範囲を確かめる `LilToonExporterWindow.RequireSupportedLilToon` に、照合（`LilToonStructureCheck.Require`）を足す。書き出し画面と表情記録の画面の両方がここを通る。範囲外の版は従来どおり版の文言で止まり、照合まで進まない。
- 既存の 2 つの検査は残す。入口の照合は「書き出し前に、分かりやすい理由で止める」ためのもので、NDMF などの準備で入れ替わったマテリアルや書き出した結果は、従来どおり途中と最後の検査が見る。比べる規則（名前の無いパスの扱い、LightMode の大文字小文字）はスナップショットと契約に合わせた。

### 何を照合するか

- 対象のシェーダー: スナップショットが記録する Renderer（`ExportRendererSelection.Enumerate` のうち、形状のあるメッシュを持つ `MeshRenderer`／`SkinnedMeshRenderer`。`LilToonFullSnapshot.Capture` と同じ選び方）のマテリアルが使っているカタログのシェーダー。ベイク用シェーダーは、それらのマテリアルのどれかがベイクの対象（`LilToonMainTextureBaker.NeedsBake`）のときだけ照合する。書き出しの設定はレイヤーを外す方向にしか働かないので、これは実際にベイクするマテリアルを含む。lilToon のマテリアルが無いアバターは照合しない（lilToon の構成と関係なく MToon で書き出せるので）。シェーダーはマテリアルが参照している実体を読む（同じ名前のシェーダーが 2 つあっても、使われている方を見る）。
- 材質用のシェーダー（カタログの 52 本）:
  - 宣言されたプロパティがすべてカタログにあり、型（float／color／vector／texture）が一致する。`_DummyProperty` は数えない（スナップショットと同じ）。
  - runtime の画像プロパティの次元が一致する（2D と Cube）。editor の画像（`_MainTex3D` など）は次元を持っていないので見ない。
  - runtime のプロパティの集合が、そのシェーダーのカタログの必須プロパティと一致する。足りないもの、ほかのシェーダーにだけある runtime のプロパティを差として出す。
  - 名前付きのパスの集合と LightMode が一致する。名前の無いパス（gem・ref 系の `GrabPass`）と LightMode が `Never` のパス（lilToon の偽のパス）は数えない。同じ名前が 2 回出たら最初だけを見る。LightMode は大文字小文字を区別しない。
- ベイク用シェーダー: 配布パッケージ 2.0.0〜2.3.4 のどれにもある 37 件（メインカラー・2nd・3rd・アルファマスク）が宣言され、宣言された名前がすべてカタログにあって型が合い、パスが 1 つ以上あること。カタログの全宣言とは比べない。カタログはタグのソースから作っていて、2.3.x のソースにある 4 件（`_Main2ndTexDecalAnimation`・`_Main2ndTexDecalSubParam`・`_Main3rdTexDecalAnimation`・`_Main3rdTexDecalSubParam`）が 2.3.4 の配布パッケージには無いので、全宣言と比べると 2.3.4 自身で止まる。Exporter は最初のパスで Blit するだけなので、パスの名前は比べない。
- 値（既定値）は比べない。

### lilToon の Shader Setting

lilToon 2.3.4 のソース（`lilToonSetting.ApplyShaderSetting`・`lilShaderContainer.UnpackContainer`）を読んで確かめた。

- Shader Setting を変えて「適用」すると、`BaseShaderResources/*.lilinternal` から `Shader/*.shader` が作り直される。プロパティの宣言は `.lilinternal` と `.lilblock` の文字列のまま書き出され、機能のオン・オフ（`LIL_FEATURE_*`）は HLSL の `#define` になるだけで宣言は変わらない。最適化（`doOptimize`）は表示名と `[lil…]`・`[NoScaleOffset]` などの属性を消すだけで、名前・型・次元は変わらない。2.0.0 の同じ処理も同じだった。
- パスは 2 つの設定で変わる。どちらも 2.3.4 でも起きる。
  - 「ForwardAddパスを使用」（`LIL_OPTIMIZE_USE_FORWARDADD`）をオフにすると、`FORWARD_ADD`・`FORWARD_ADD_OUTLINE`・`FORWARD_ADD_FUR` のパスが消える。
  - 「Fix for Deffered」（`LIL_OPTIMIZE_DEFFERED`）をオンにすると、アウトライン付きのシェーダーの `SHADOW_CASTER_OUTLINE` が `SHADOW_CASTER` に置き換わる。
- この 2 つは、今の書き出しでも `LilToonFullContract.Validate` のパスの照合で書き出しの最後に落ちている。契約はアプリと同じコードで、アプリを変えずに受け入れることはできない（Epic #145 の方針）。そこで照合は止める基準を変えず、差の中身から原因を見分けて、対処の文言を設定に合わせる（「ForwardAddパスを使用」をオンにする／「Fix for Deffered」をオフにする）。それ以外の差は「VCC／ALCOM で lilToon を入れ直すか 2.3.4 に切り替える」と案内する。

### 文言

- 1 行目に導入済みの版と、構成が 2.3.4 と一致しないこと。続けて差を最初の 5 件まで「シェーダー名: 何が違うか（2.3.4 では何か）」で出し、残りは件数だけ出す。最後に原因に合う次の操作を 1〜3 行。
- 日英韓中（簡・繁）の訳を `Editor/Locales` に足した。lilToon の設定の名前は lilToon 自身の訳（「ForwardAddパスを使用」・Use ForwardAdd など）に合わせた。「Fix for Deffered」は lilToon 側で訳が無く綴りもこのままなので、そのまま書く。

### 2.3.4 より新しい版との関係

`DependencyPolicy.AcceptsNewerLilToon` のコメントで、この照合を新しい版を受け入れるときの安全弁として結びつけた。新しい版を通すように切り替えても、使っているシェーダーの名前・型・次元・パスが 2.3.4 と違えば書き出し前に止まる。構成が同じで描画の中身だけが変わった版は検出できない（Epic #145 の未決 1 の判断材料）。

### 所要時間とキャッシュ

Unity 2022.3.22f1（batchmode・GPU あり）で、カタログの 52 本とベイク用シェーダーの計 53 本を 2 回続けて読んで照合した（`LilToonStructureCheckTests.InstalledOfficialShadersMatchTheCatalogue` のログ・各プロジェクト 2 回の実行の幅）。

| プロジェクト | `Shader.Find` 53 本 | 1 回目の読み取り＋照合 | 2 回目 |
|---|---|---|---|
| lilToon 2.3.4 | 57〜65 ms | 14,168〜16,909 ms | 133〜135 ms |
| lilToon 2.0.0 | 57〜58 ms | 13,748〜16,819 ms | 120〜123 ms |

1 回目の時間は、そのセッションで初めてシェーダーを読み込む時間がほとんどで（1 本あたり約 260〜320 ms）、2 回目は 1 本あたり約 2.5 ms だった。書き出すアバターのシェーダーはマテリアルから参照されてすでに読み込まれていることが多く、まだでも書き出しの途中のスナップショットが同じ API でシェーダーを読むので、同じ読み込みが起きる。照合で増えるのは 1 本あたり数 ms なので、キャッシュは持たない。全 53 本を毎回見ないのは時間のためではなく、下の「検討した代替案」の理由による。

## 検討した代替案と不採用理由

- 導入済みの全シェーダー（52 本＋ベイク用）を毎回照合する: lilToon を使わないアバターまで、lilToon の Shader Setting の都合で止まるようになる。使っているシェーダーだけで、書き出しに載るものは全部見られる。
- 照合をスナップショットか契約（`Validate`）の中に足す: 契約はアプリと同じコードで、アプリを変えない方針に反する。スナップショットはベイクのあとに動くので、止まるのが遅い。
- ベイク用シェーダーをカタログの全宣言と比べる: 上のとおり 2.3.4 自身で止まる。
- Shader Setting でパスが消えた状態を受け入れる（記録するパスの集合をカタログに合わせて補う）: アプリの契約はパスの集合の一致を求めるので、存在しないパスを書くことになる。補正を持たない方針にも反する。
- 照合の結果をキャッシュする: 2 回目以降の照合は 1 本あたり約 2.5 ms で、無効にする条件（シェーダーの再インポート）を管理する手間に見合わない（上の「所要時間とキャッシュ」）。

## 影響

- lilToon のマテリアルを使うアバターは、書き出し前に照合を通る。2.0.0〜2.3.4 の配布パッケージで Shader Setting が既定なら止まらない（Unity の 2.3.4 と 2.0.0 のプロジェクトで確かめた）。
- 「ForwardAddパスを使用」がオフ、または「Fix for Deffered」がオンのプロジェクトは、今までは書き出しの最後に英語の文言で落ちていた。これからは書き出し前に、直す設定の名前つきで止まる。
- lilToon を使わないアバターの書き出しは変わらない。

## 未決

- 2.3.4 より新しい版の扱い（Epic #145 の未決 1）。照合は範囲内の版で通ることを確かめたが、新しい版で通るかは試していない。照合は editor／external のプロパティの型も比べる（後段のスナップショットはこれらを読まない）ので、新しい版を通すように切り替えたとき、書き出しに載らないプロパティの型の変更だけで止まることがある。
- 「ForwardAddパスを使用」オフ・「Fix for Deffered」オンのプロジェクトを書き出せるようにするかどうか。アプリの契約（パスの集合の一致）を変える必要がある。

## 出典

- lilToon 2.3.4・2.0.0 の配布パッケージ（MIT License）: `Editor/lilToonSetting.cs`（`ApplyShaderSetting`・`LIL_OPTIMIZE_USE_FORWARDADD`・`LIL_OPTIMIZE_DEFFERED`）、`Editor/lilShaderContainerImporter.cs`（`GetProperties`・`RewriteForwardAdd`・`LIL_OPTIMIZE_DEFFERED` の置き換え）、`Shader/ltspass_baker.shader`、`Editor/Localization/*.po`（設定の表示名）
- #163 の設計判断（カタログの生成とベイク用シェーダーのずれ）
- Epic #145 の範囲決めの実測（2026-10-14）
