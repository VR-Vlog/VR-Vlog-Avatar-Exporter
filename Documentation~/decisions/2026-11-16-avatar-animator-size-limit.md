# avatar_animator の上限を 16MB にし、VRM 全体の JSON がアプリの上限を超えないか確かめる

- 日付: 2026-11-16
- 対象: `Editor/AvatarAnimatorCapture.cs`（`MaximumMegabytes`・`AppJsonBytes`・`RequireJsonWithinApp`・`Remove`）、`Editor/UniVrmOneClickExporter.cs`
- 関連: VR-Vlog#3658、`2026-11-12-avatar-animator-extension.md`（4MB の上限を決めた記録。上限の値はこの記録で置き換える）

## 背景

拡張 `VRVLOG_avatar_animator` の JSON が 4×1024×1024 バイトを超えると、拡張を丸ごと書かずに「データが上限の4MBを超えています。」と出していた。検証用のアバター A2 を書き出すと拡張が 4,919,564 バイトになり、FX の動きがすべて省かれていた。内訳はクリップ 89 本で 4,808,022 バイト、ターゲット 266（うち Humanoid のマッスル 95）で、ほとんどがクリップの曲線のキー。

4MB の理由はコードにも決定記録にも残っていない。アプリの読み手（`com.vrvlog.avatar-formats` の `AvatarAnimatorData.Read`）にあるのは件数の上限（1 クリップのキー 16384・トラック 4096 など）だけで、バイトの上限はアプリが glb の JSON チャンク全体に置く 32MiB（`LilToonGlbDocument.MaximumJsonChunkBytes`、4 バイト境界の詰め物を含む）だけ。

## 決めたこと

- 拡張の上限を 16×1024×1024 バイトにする。ちょうど上限は書き、超えたら今までと同じく段を省いて「データが上限の16MBを超えています。」と出す。拡張を組み立てる途中で BlendShape を落とすかどうかの判定（`OverMaximumBytes`）も同じ値を使う
- 書き出しの最後（`VRVLOG_avatar_effects`・`VRVLOG_avatar_contacts` を入れ、必要なアプリのバージョンを書いた後）に、glb の JSON チャンクが 32×1024×1024 バイトを超えないか確かめる。超えたら avatar_animator を `extensions` と `extensionsUsed` から外し、同じ段の省略として「VRM全体のデータがアプリで読める上限の32MBを超えています。」と出す。アプリで読めない VRM を作らないため
- 外すのは avatar_animator だけにする。FX の大きさで膨らむのはこの拡張で、ほかの拡張は件数で頭打ちになる。外した後の VRM は、この段が失敗したときと同じ中身になる（テスト `TheExtensionLeavesTheRestOfTheFileAsItIs` の 8 回目）
- Formats（アプリの読み手）は変えない。アプリのバージョンも上げない

### なぜ 16MB か

- アプリの JSON 全体の上限 32MiB の半分にした。lilToon の拡張（JSON の上限 8MiB）・VRM 本体・ほかの拡張が残りに収まる
- A2 の 4.9MB の 3 倍強の余裕がある。手元の A1〜A11 の中ではこれが一番大きい
- 32MiB に近づけると、上の確認で拡張を外す場面が増え、どこまで書けるかがアバターのほかの中身で変わる。拡張だけで決まる上限を先に置いておけば、外れる理由が分かりやすい

## 決めなかったこと

- クリップの表し方を小さくする（値の変わらない曲線を 1 キーにまとめる・桁を落とす・曲線をバイナリのバッファへ移す）。A2 の内訳ではキーの数がほとんどなので効果は大きいが、Formats とアプリの読み手が変わる。要るかどうかは別に決める
- iPhone で 16MB 近い拡張を読んだときのメモリと時間は測っていない。JSON をオブジェクトに展開するとファイルの何倍かのメモリを使う

## 確かめたこと

- EditMode テスト: 4MB を超え 16MB 以下の拡張を書く・16MB を超えると省く（`AnExtensionOver4MBIsWrittenAndOneOver16MBIsNot`）、JSON チャンクがちょうど上限なら書き、1 バイト超えると拡張を外して理由を出し、外した後は拡張の無い VRM と同じ（`AFileWhoseJsonIsOverTheAppLimitLeavesTheExtensionOut`、上限の値を下げて確かめる）
- A1〜A11 の書き出しを前後で突き合わせ、A2 に拡張が入り、ほかは変わらないこと
