# avatar_animator の上限を 16MB にし、VRM の JSON がアプリの上限を超えないか確かめる

- 日付: 2026-11-16
- 対象: `Editor/AvatarAnimatorCapture.cs`（`MaximumMegabytes`・`AppJsonBytes`・`RequireJsonWithinApp`・`Remove`）、`Editor/UniVrmOneClickExporter.cs`
- 関連: VR-Vlog#3658、`2026-11-12-avatar-animator-extension.md`（4MB の上限を決めた記録。上限の値はこの記録で置き換える）

## 背景

拡張 `VRVLOG_avatar_animator` の JSON が 4×1024×1024 バイトを超えると、拡張を丸ごと書かずに「データが上限の4MBを超えています。」と出していた。検証用のアバター A2 を書き出すと拡張が 4,919,564 バイトになり、FX の動きがすべて省かれていた。内訳はクリップ 89 本で 4,808,022 バイト、ターゲット 266（うち Humanoid のマッスル 95）で、ほとんどがクリップの曲線のキー。

4MB の理由はコードにも決定記録にも残っていない。アプリの読み手（`com.vrvlog.avatar-formats` の `AvatarAnimatorData.Read`）にあるのは件数の上限（1 クリップのキー 16384・トラック 4096 など）だけで、バイトの上限はアプリが glb の JSON チャンク全体に置く 32MiB（`LilToonGlbDocument.MaximumJsonChunkBytes`。ヘッダーのチャンク長で比べるので 4 バイト境界の詰め物を含む）だけ。

## 決めたこと

- 拡張の上限を 16×1024×1024 バイトにする。ちょうど上限は書き、超えたら今までと同じく段を省いて「データが上限の16MBを超えています。」と出す。拡張を組み立てる途中で BlendShape を落とすかどうかの判定（`OverMaximumBytes`）も同じ値を使う
- 書き出しの最後（`VRVLOG_avatar_effects`・`VRVLOG_avatar_contacts` を入れ、必要なアプリのバージョンを書いた後）に、glb の JSON チャンク長が 32×1024×1024 バイトを超えないか確かめる。超えたら avatar_animator を `extensions` と `extensionsUsed` から外し、同じ段の省略として「VRMのJSON部分がアプリで読める上限の32MBを超えています。」と出す。アプリで読めない VRM を作らないため
- 外すのは avatar_animator だけにする。上限を上げたのはこの拡張で、ほかの拡張の上限は変えていないため。外した後の VRM は、拡張を入れる処理（`Inject`）が失敗したときと同じ中身になる（テスト `TheExtensionLeavesTheRestOfTheFileAsItIs` の 8 回目と `AFileWhoseJsonIsOverTheAppLimitLeavesTheExtensionOut`）
- Formats（アプリの読み手）は変えない。アプリのバージョンも上げない

### なぜ 16MB か

- A2 の 4.9MB に 3 倍強の余裕がある。手元の A1〜A11 の中ではこれが一番大きい（下の表）
- JSON に載るほかの大きいデータにも上限がある。lilToon の拡張を入れた時点の JSON 全体で 8MiB、動くポーズ 16MiB、静止ポーズ 2MiB。これらを足すと avatar_animator 抜きでも 32MiB に届き得るので、拡張の上限だけでは読めることを保証できない。そこで拡張には分かりやすい固定の上限を置き、読めるかどうかは最後の確認で決める
- 32MiB いっぱいまで許すと、上限を超えるかどうかがアバターのほかの中身で変わる場面が増える。16MB ならほとんどのアバターは拡張の上限だけで結果が決まる

## 決めなかったこと

- クリップの表し方を小さくする（値の変わらない曲線を 1 キーにまとめる・桁を落とす・曲線をバイナリのバッファへ移す）。A2 の内訳ではキーの数がほとんどなので効果は大きいが、Formats とアプリの読み手が変わる。要るかどうかは別に決める
- avatar_animator が無くても JSON が 32MiB を超える VRM（大きい動くポーズなど）は、今までどおり何もしない。外せる段を選ぶ仕組みは別に要る
- iPhone で 16MB 近い拡張を読んだときのメモリと時間は測っていない。JSON をオブジェクトに展開するとファイルの何倍かのメモリを使う

## 確かめたこと

（EditMode と A1〜A11 の突き合わせの結果を書く）
