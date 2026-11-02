# アプリに転送の画面のフォントを、シーンを開いても壊れないようにする

- 日付: 2026-11-02
- 対象: `Editor/LanTransfer/AppTransferWindow.cs`・`Tests/LanTransfer/AppTransferFontLifetimeTests.cs`

## 背景

**アプリに転送** の画面は、Unity 2022.3 で日本語の一部が太く見える問題を避けるため、OS のフォントから自前のフォント（`FontAsset.CreateFontAsset`）を作って文字を描いている。DontSave を付けていたのはフォント本体だけで、フォントが中に持つマテリアルと文字の画像（アトラスの Texture2D）には付けていなかった。

エディタでシーンを開くと、Unity はどのシーンも使っていないアセットを片付ける。このとき C# の変数から参照しているだけの HideFlags.None のオブジェクトは消える。フォント本体は残るがマテリアルと文字の画像は消え、画面を描くたびに `MissingReferenceException: The object of type 'Material' has been destroyed`（`UIRStylePainter.DrawTextInfo` → `Material.get_mainTexture`）が出ていた。書き出しの処理とは関係なく、画面を開いたままシーンを開くと出る。フォントは同じ名前で使い回すので、画面を開き直しても止まらず、止めるにはエディタの再起動が要った。

Unity 2022.3.22f1 で確かめた結果:

- シーンを開くと、マテリアルと文字の画像がすべて消える（画面を開いた状態で 19〜21 件のエラー）。
- マテリアルと 1 枚目の画像に DontSave を付けても、文字が増えたときにフォントが足す 2 枚目以降の画像は消える。2022.3 は足した画像に 1 枚目の hideFlags を写さない。新しい Unity は `FontAssetFactory` で本体・マテリアル・1 枚目に DontSave を付け、足した画像に 1 枚目の hideFlags を写す。
- `Resources.UnloadUnusedAssets` と `EditorUtility.UnloadUnusedAssetsImmediate` では消えない。消えるのはシーンを開く経路だけ。

## 決定

- フォントを作ったとき（と使い回すとき）に、本体・マテリアル・その時点の文字の画像すべてに DontSave を付ける。
- フォントが文字の画像を足すと呼ばれるエディタ内部のコールバック（`FontAsset.OnFontAssetTextureChanged`。エディタがアセットとして保存されたフォントに画像を足すのに使っているもの）にリフレクションで足し、この画面のフォント（名前で判定）が足した画像にも DontSave を付ける。新しい Unity がフォント自身で行っていることを 2022.3 で補うもので、コールバックが無い版では何もしない。
- 使い回すフォントが、古い版で作られたなどの理由でマテリアルか画像を失っていたら、自分で作ったものなので破棄して作り直す。`TextFont()` も、本体か代替フォントが壊れていれば作り直す。

## 検討した代替案と不採用理由

- 作ったときにだけ DontSave を付ける: 描いている間に足される 2 枚目以降の画像が残らない（上の確認結果）。
- シーンを開く前（`EditorSceneManager.sceneOpening`）や Play モードに入る前に付け直す: 片付けの経路ごとにフックが要り、漏れた経路では同じことが起きる。画像ができた時点で付ければ経路を問わない。
- 文字の画像を 1 枚に固定する（`isMultiAtlasTexturesEnabled = false`）: 画像がいっぱいになると文字が出なくなる。
- 壊れたのを見つけたら作り直すだけにする: 片付けから作り直しまでの間にエラーが出続け、開いている画面のスタイルも差し替え直す必要がある。今回は保険として残した。
- 自前のフォントをやめる: 太字が混ざる問題（`c6cadbbe`）が戻る。

## 影響

- 画面を開いたままシーンを開いてもエラーが出ない（Unity 2022.3.22f1 の検証環境で 0 件、修正前は 19〜21 件）。
- 書き出す VRM は変わらない（修正前後で同じアバターを書き出し、`Tools/compare-exports.py` で差 0。違うのは書き出しごとに変わる生成テクスチャ名の ID だけ）。
- DontSave を付けたマテリアルと画像はエディタを閉じるまで残る。フォントは名前で使い回すので、ドメインの再読み込みのたびに増えることはない。

## 未決

- Codex への設計相談は枠の上限（85%）を超えていて行えなかった（`codex_consult.py` の exit 5）。

## 出典

- Unity 2022.3 `Modules/TextCoreTextEngine/Managed/TextAssets/FontAsset.cs` の `SetupNewAtlasTexture`（hideFlags を写さず `OnFontAssetTextureChanged` を呼ぶ）
- Unity の新しい版の `FontAssetFactory.cs`・`FontAssetAtlasPopulation.cs`（DontSave を付け、足した画像に 1 枚目の hideFlags を写す）
- `Modules/TextCoreTextEngineEditor/Managed/TextEditorResourceManager.cs`（同じコールバックに `+=` で登録している）
