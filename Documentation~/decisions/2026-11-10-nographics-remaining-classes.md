# -nographics で残っていたテストの失敗を、GPU が無いときの結果を確かめてから抜ける形に直す

- 日付: 2026-11-10
- 対象: `Tests/Editor/Support/NoGraphicsDevice.cs`・`Tests/LanTransfer/NoGraphicsDevice.cs`・`Tests/Editor` と `Tests/LanTransfer` のテスト 48 ファイル

## 背景

#713 で lilToon 以外のシェーダーの近似のテストを直したあとも、EditMode の全件を `-nographics`（`SystemInfo.graphicsDeviceType` が `Null`）で回すと 4607 件のうち 409 件が落ちていた。CI は GPU ありで回すので緑だが、CLAUDE.md の「`-nographics` で回しても失敗にしない」に合っていなかった。

落ちた理由はエラーのログが 337 件、確かめた値の違いが 72 件で、原因は次の 3 つだった。

- エラーのログ: ウィンドウを開くと `No graphic device is available to initialize the view.`、書き出しで画像を読み戻すと `Unsupported GraphicsFormat for ReadPixels` が出て、Test Framework がテストを失敗にする。書き出しもウィンドウもエラーのあと続き、テストの確認はそのまま通る。
- 読み戻した画素: `LilToonMainTextureBaker`・マットキャップの近似・`LilToonFullTexture`・輪郭線のマスク・UniGLTF の画像の変換には GPU が無いときの代わりの処理が無く、決まらない値（0xCD で埋まった値）を読む。
- GPU が無いときの別の結果: lilToon は GPU が無いと別のサブシェーダーが使われることがあり、描画パスがカタログや保存時と変わる。保存した見た目のプレビューは描画パスが無いとして断る。ウィンドウはビューを持たず `OnGUI` が動かないので、レイアウトやドラッグ＆ドロップの結果が無い。

## 決定

- テストだけを直し、製品のコードは変えない。テストメソッドの追加・削除・名前の変更はしない。
- 判定と抜け方をテスト用の `NoGraphicsDevice` にまとめる。GPU ありではどれも何もしない。
  - `AllowErrorLogs()`: そのテストのエラーのログで失敗にしない（`LogAssert.ignoreFailingMessages`）。Test Framework 1.4.6 はテストの本体・SetUp・TearDown ごとにログの見張りを作り直すので、本体の先頭で呼ぶ。ログが SetUp・TearDown で出る 2 クラスはそこでも呼ぶ。
  - `IgnorePixelChecks()`・`IgnoreWindowChecks()`・`IgnoreLilToonPassChecks()`: `Assert.Ignore` する。GPU に依らない確認（書き出しの構造・設定の値・画像の大きさと mip・元のアバターや画像が変わらないこと）を前に移してから呼ぶ。ループの中の画素の確認は GPU が無いときだけ飛ばし、ループのあとで Ignore する。
- GPU が無いときの結果が製品のコードで決まっているところは、Ignore にせずその結果を確かめる。
  - 発光のグラデーションは掛けず白のまま（`MaterialFallbackTests`）。
  - 置き換えたシェーダーのアルファは焼かない。UTS の透過は全部描く、UnlitWF のカットアウトはマスクを外す、Sunao は色付きのマスクをそのまま使う（`ShaderAlphaFamilyTests`）。
  - HDR のキューブはコピー用のシェーダーが使えないと止まる（`HdrCubeLightweightTests`）。止まらなかったときは大きさ・mip・面の数を確かめてから Ignore する。
- lilToon の描画パスが変わるかどうかは、実行の順番で変わる（今回の全件では、`ExportRecoveryTests` で lilToon をプレビューに描いたあとから変わった）。プレビューを使うテストは、GPU が無いときに断られたら取り込んだモデル・リソースが変わっていないことを確かめて Ignore し、断られなければ GPU に依らない確認まで進む。

## 検討した代替案と不採用理由

- テストの集まり全体で `ignoreFailingMessages` を立てる（`ITestAction` や属性）: 本体のログの見張りはその外で作られるので効かない。
- GPU が無いときは最初から Ignore する: 書き出しの構造や元のアバターが変わらないことなど、GPU に依らない確認まで抜ける。
- 製品のコードに GPU の有無の判定を足す: この変更はテストだけにした。下の「残した懸念」に書く。

## 確かめたこと

Unity 2022.3.22f1・lilToon 2.3.4 と Exporter だけの小さいプロジェクト（Linear）を、検証用の Windows 機で回した。

- `-nographics` の全件: 変える前は失敗 409。変えたあとは失敗 0。
- GPU あり（batchmode）の全件: 失敗 0。Ignore の理由が `Without a graphics device` で始まるものは 0 件で、GPU ありで Ignore に変わったテストは無い。
- `Tools/test-compatibility-tooling.py`・`Tools/validate.py` が通る。

## 残した懸念

- `LilToonFullTexture.Read`・`LilToonMainTextureBaker`・マットキャップの近似・UniGLTF の画像の変換は GPU の有無を見ずに読み戻し、GPU が無いと決まらない画素を警告なしに書き出す。`GpuReadback.Available` を使う発光とアルファマスク（[2026-11-08の決定記録](2026-11-08-nographics-bake-fallback.md)）と揃っていない。
- GPU が無いと、UnlitWF のカットアウトのマスクは報告なしに外れ、Sunao の色付きマスクはそのまま（R だけ）で使われる。
- `LayerAlphaTests` の「lilToon で描いた結果と比べる」10 件は、GPU が無いと両方とも決まらない値になる。これまで 9 件は比べる意味の無いまま通り、1 件は描いたときのログで落ちていた。今回、10 件とも描く前に Ignore するようにした。
