# VRM に入れる PNG を自前の書き出し処理で作り、不透明な画像は RGB で書く

- 日付: 2026-10-21
- 対象: `Editor/PngWriter.cs`・`Editor/MobileTextureEncoder.cs`・`Tests/Editor/PngWriterTests.cs`・`Tools/run-behavior-tests.ps1`・`Tools/MaterialTestShim.cs`・`Tools/validate.py`・`CHANGELOG.md`

## 背景

書き出した VRM の画像は、すべて `MobileTextureEncoder.EncodeConverted` の `ImageConversion.EncodeToPNG` で PNG にしている（`MobileTextureSerializer.ExportBytesWithMime`・`ResizeEncoded`・`LilToonGlbExtension` から `EncodeSource` を通る経路も最後はここに来る）。`EncodeToPNG` は、RGBA32 の画像なら全画素が不透明でも RGBA で書き、行ごとのフィルターと圧縮も弱い。

develop で書き出した 24 体の VRM を調べると、ファイルの合計 532.45 MB のうち PNG が 145.43 MB だった。同じ画素を、不透明なら RGB にし、行ごとに 5 種類のフィルターから選んで deflate で圧縮する簡単な書き出し処理で書き直すと、PNG は 28.76 MB（20%）小さくなった。ファイル全体では 5.4% にあたる。読み込んだ画素は元の PNG と 1 つも違わなかった。

## 決定

- `Editor/PngWriter.cs` を足し、`EncodeConverted` の 2 か所の `ImageConversion.EncodeToPNG` を `PngWriter.Encode` に置き換えた。縮小の処理と、PNG を作れなかったときのエラー（`RequirePng`）と警告は変えていない。
- 自前で書くのは、読み取りでき、1 チャンネル 8 ビットの非圧縮の形式（`RGBA32`・`ARGB32`・`RGB24`・`BGRA32`）の画像だけ。`GetPixels32` で画素を取り出す。16 ビット・half・float・1 チャンネル（`R8`・`Alpha8`・`R16`）・圧縮形式と、読み取りできない画像は、これまでどおり `EncodeToPNG` に渡すので、出力は変わらない。
- 全画素のアルファが 255 なら color type 2（RGB・8 ビット）、それ以外は color type 6（RGBA・8 ビット）で書く。パレットやグレースケールへの変換はしない。
- 行の順は、Unity の画素配列（下の行が先）を上下に反転して PNG の順（上の行が先）にする。`EncodeToPNG` の出力を読み込んだ画素と同じ向きになる。
- フィルターは行ごとに None・Sub・Up・Average・Paeth の 5 つを試し、フィルター後のバイトを符号付きとして見た絶対値の和が最も小さいものを選ぶ（libpng と同じ選び方）。同じ和なら番号の小さいほうを使う。
- 圧縮は zlib 形式（ヘッダー `0x78 0x9C`・`DeflateStream`（`CompressionLevel.Optimal`）の deflate・ビッグエンディアンの Adler-32）。行は 1 行ずつ `DeflateStream` に流し、行のバッファーは使い回す。画素の配列と出力のほかに使うメモリは、行の 7 本分と IDAT 1 つ分（1 MiB）だけ。
- zlib の出力は 1 MiB ごとに IDAT を区切る。チャンクは IHDR・IDAT・IEND だけで、どれにもテーブルで求めた CRC-32 を付ける。develop で書き出した 24 体の VRM の PNG 322 枚を調べると、`EncodeToPNG` の出力にも IHDR・IDAT・IEND 以外のチャンク（sRGB・gAMA・iCCP など）は無かった。足すと読む側によって色の扱いが変わりうるので、足していない。

### 互換性

- アプリの UniVRM は `Texture2D.LoadImage` で画像を読む。glTF・VRM を読むほかのビューアーも標準の PNG ならどれも読める。今の書き出しでも、24 体の 322 枚のうち 212 枚はすでに RGB の PNG だった。
- 読み込んだ画素は元の PNG と同じなので、古いアプリで新しい VRM を読んでも、新しいアプリで古い VRM を読んでも、見た目は変わらない。

## 入れなかったもの

- パレット（color type 3）やグレースケール（color type 0・4）への変換。小さくなる画像はあるが、画素を数えて形式を選ぶ処理が増え、読む側の対応の差も気にする必要が出るため、RGB と RGBA だけにした。
- フィルターの選び方を、圧縮して比べる方法や複数行をまとめて決める方法にすること。時間がかかるわりに、上の 1 行ずつの選び方との差が小さい。
- `EncodeToPNG` に任せている 16 ビット・浮動小数点の画像を自前で書くこと。24 体の 322 枚に 16 ビットの PNG は無く、出力を変えたくないため。

## 確認したこと

- `Tests/Editor/PngWriterTests.cs` に次のテストを足した。
  - `DecodesToTheSamePixelsAsSourceAndEncodeToPng`: 不透明・半透明・グラデーション、1×1・3×5・17×9 などの大きさ、リニアと sRGB、`RGBA32`・`RGB24`・`ARGB32`・`BGRA32` の画像を書き、`ImageConversion.LoadImage` で読んだ画素が元の画素とも `EncodeToPNG` の出力を読んだ画素とも同じこと、IHDR の color type が不透明なら 2・それ以外は 6 であること。
  - `WritesValidChunksAndZlibStream`: IHDR の各欄、全チャンクの CRC-32、zlib のヘッダーと Adler-32、展開した長さと各行のフィルター番号。
  - `WritesTheSameChunkTypesAsEncodeToPng`: チャンクの種類が `EncodeToPNG` の出力と同じで、IHDR・IDAT・IEND だけであること。
  - `HalfFloatTextureKeepsEncodeToPngOutput`: `RGBAHalf` の画像は `EncodeToPNG` と同じバイト列になること。
  - `ConvertedOpaqueTextureIsWrittenAsRgb`: `EncodeConverted` を通した不透明な画像が RGB で書かれること。
  - `SmoothOpaqueGradientIsSmallerThanEncodeToPng`: 512×512 の不透明なグラデーションで `EncodeToPNG` より小さいこと。
  - `LogsTimeAndSizeFor2048Texture`: 2048×2048 の画像で、かかった時間と大きさをログに出す（時間の上限は確かめない）。
- Unity の外でも、`PngWriter.cs` をそのまま .NET 8 でビルドし、上の 24 体の PNG 322 枚と合成画像 6 枚を書き直して確かめた。全枚で、読み込んだ画素が元と同じで、不透明な画像は RGB になり、CRC と Adler-32 も正しかった。PNG の合計は 145.43 MB から 119.04 MB（18.1% 減）になった。.NET 8 と Unity の Mono では deflate の実装が違うので、Unity での大きさは少し変わりうる。1024×1024 の 1 枚にかかった時間は最大で 0.27 秒だった。
- Unity EditMode の結果: （実行後に追記）
