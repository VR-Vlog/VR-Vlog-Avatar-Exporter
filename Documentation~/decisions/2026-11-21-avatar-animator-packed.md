# VRVLOG_avatar_animator は schema 2 で、同じ動きのクリップをまとめ、gzip して BIN に置く

- 日付: 2026-11-21
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Schema/VRVLOG_avatar_animator.2.schema.json`、`Editor/AvatarAnimatorPacking.cs`、`Editor/AvatarAnimatorCapture.cs`、`Tools/compare-exports.py`、`Tools/AvatarAnimatorSettledCheck.cs`、`Tools/AvatarAnimatorComparisonReplay.cs`

## 背景

`VRVLOG_avatar_animator` 1 は、FX の表をそのまま JSON チャンクに書く。試した 9 件のアバターでは拡張だけで 5.0〜7.0MB あり、VRM（12〜34MB）の 2〜4 割を占めていた。アプリへの転送が遅くなり、アプリは読み込みのたびにこの JSON を全部解析する。

中身を見ると、次の 2 つで大半が占められていた。

- 名前・長さ・ループ・Cycle Offset だけが違い、トラックがまったく同じクリップ。衣装のオン・オフを、メニューの項目ごとに同じ動きのクリップとして持つアバターが多い。まとめると拡張は約 45% 小さくなる。
- 同じ形の数字の並び（キーの時刻・値・接線）。gzip でさらに 5〜6 倍小さくなる。

## 決定

### 同じ拡張名のまま schemaVersion を 2 にする

拡張の中身は `{"schemaVersion": 2, "bufferView": n}` だけにし、文書（schema 1 と同じ表）は bufferView `n` に置く。

- 今のアプリ（1.2.8）の `AvatarAnimatorData.Read` は、`schemaVersion` が 1 でないと例外を投げ、Animator を付ける処理がそれを捕まえて、Animator なしでアバターを表示する。表が 32MiB を超えて拡張を外したときと同じ見え方で、落ちない。新しい拡張名にしても古いアプリの見え方は同じなので、中身が同じ表に 2 つの名前を作らない。
- 古いアプリでも開けるので、強制アップデートは要らない。`VRVLOG_requiredAppVersions` には、schema 2 を読むアプリの版（1.2.9、仮）を書く。アプリの版が決まったら合わせて直す。

### クリップは前のクリップのトラックを sameTracks で指せる

`Write` は、トラックが前のクリップと完全に同じクリップに、`tracks` の代わりに `"sameTracks": j` を書く。

- `j` は、自分のトラックを持つ（`sameTracks` でなく、空でない）前のクリップに限る。読む側は 1 回たどるだけで済み、循環も起きない。
- 名前・長さ・ループ・Cycle Offset はそのクリップ自身の値を書く。
- トラックの比較は、書き出す JSON の文字列が同じかで決める。浮動小数の誤差でわずかに違うトラックはまとめない。
- `Read` は schema 1 の `sameTracks` を退ける。schema 1 の読み方は変えない。

### 文書は gzip して BIN チャンクの bufferView に置く

- base64 にして JSON に入れると 4/3 倍になり、JSON チャンクの解析も重いまま残る。BIN に置けば、アプリは JSON チャンクを解析したあと、別のスレッドでそのバイトの範囲だけを読んで展開できる。
- 圧縮は gzip（`System.IO.Compression.GZipStream`）にした。Unity の Mono と IL2CPP（Android・iOS）の両方にあり、アプリにライブラリを足さずに済む。Brotli は Unity の .NET のプロファイルに無い。
- 同じ文書は同じバイトになる（`CompressionLevel.Optimal`。テストで確かめる）。ただし比較の道具（`Tools/compare-exports.py`）は展開してから比べるので、圧縮のバイトの違いには左右されない。
- `Unpack` は展開した長さを `MaximumDocumentBytes`（32MiB）までに抑え、途中で切れた流れ（gzip の末尾の長さと合わないもの）を退ける。切れた流れを例外にしない実装があるため。
- bufferView は buffer 0 のもので、`byteStride` を持たないものに限る。書き出しでは、ほかのバイトのあとに 4 バイトに揃えて置く。

### 書き出しの順番と上限

- 上限（32MiB）は今までどおり、圧縮する前の文書に掛ける。アプリが展開したあとに持つ量の上限なので。
- 表を足す段階（エフェクト・Contact など）は Animator のあとにも bufferView を足すので、Animator の bufferView は最後とは限らない。
- 表をアプリの版に合わせて外すとき（`AvatarAnimatorCapture.Remove`）は、拡張と一緒にその bufferView とバイトも外し、あとの bufferView の番号と位置を詰める。ほかに同じ bufferView を指すもの、同じバイトに掛かる bufferView があるときは、bufferView もバイトも残す。

### 比較の道具

`Tools/compare-exports.py` は、schema 2 の拡張を展開して JSON の中に戻し、`sameTracks` を指す先のトラックに置き換え、bufferView とバイトを BIN から外してから比べる。schema 1 と 2 の VRM は、拡張の `schemaVersion` だけが違って見える。`AvatarAnimatorSettledCheck`・`AvatarAnimatorComparisonReplay` も schema 2 を読む。

## 測った値

試した 9 件のアバターで、拡張は 5.0〜7.0MB から、まとめるだけで 2.8〜3.9MB、圧縮して 0.48〜0.65MB（元の 1/10〜1/12）になった。VRM 全体では 4.5〜6.3MB 小さくなった。9 件とも、展開して `sameTracks` を戻すと元の文書と同じになり、評価器の静止フレームの結果も schema 1 と同じだった。

## 直らないもの・制限

- 1.2.8 までのアプリでは、schema 2 の VRM のメニューの再生が、表が無いときと同じになる。
- アプリは展開に時間とメモリを使う。値はアプリ側の変更で測る。
