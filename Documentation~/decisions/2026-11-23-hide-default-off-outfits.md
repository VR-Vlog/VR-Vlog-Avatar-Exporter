# 既定でオフの衣装は、標準のマテリアル（MToon）だけを透明にする

- 日付: 2026-11-23
- 対象: `Editor/UniVrmOneClickExporter.cs`（`OffByDefaultSlots`・`ReplaceLilToonMaterials`・`CreateMToonFallback`・`HideFallback`）・`Editor/ObjectToggleCapture.cs`（`OffByDefault`）・`Editor/LilToonFullSnapshot.cs`（`Carries`）・`Tests/Editor/MaterialFallbackTests.cs`・`Tests/Editor/ObjectToggleCaptureTests.cs`

## 背景

Expression Menu のトグルで既定でオフの衣装（衣装 B）は、`VRVLOG_object_toggles` で出し入れできるように、書き出しのコピーでオンにして書き出す（2026-11-17-object-toggle-transforms.md の前提）。隠すのはアプリが拡張の既定の状態を読んで行う。

標準のマテリアル（MToon）だけを読む一般の VRM ビューアは、この拡張を読まない。衣装 B が既定の衣装（衣装 A）に重なって見える。

## 決定

衣装 B のスロットの標準のマテリアルだけを、見えないもの（alpha 0・Transparent・ZWrite Off・アウトラインなし）にする。`VRVLOG_materials_liltoon` の lilToon のマテリアルは今のまま書く。

- 書き方は、MToon で描けない lilToon（FakeShadow・`ColorMask 0`）を隠す `HiddenInFallback`（2026-10-22-colormask-zero-hidden.md）と同じにする。スロットと lilToon の記録は残し、MToon の側だけが何も描かない。
- 衣装 B は、トグルの既定の値で非表示になる対象（`ObjectToggleCapture.OffByDefault`）と、その子の表示物とする。
- 書き出しは Unity のマテリアル 1 つを glTF のマテリアル 1 つにする。衣装 A と衣装 B が同じマテリアルを使うときは、衣装 B のスロットに見えないマテリアルを別に作る。衣装 A のマテリアルは今と同じで、衣装 B だけが見えなくなる。lilToon の記録は、元のマテリアルの記録を両方の番号に付ける（`LilToonFullSnapshot.Bind` がスロットの元のマテリアルから引く）。
- 別に作ったマテリアルの名前は、元の名前に `__VRVlog_HiddenByDefault` を付けて、ほかと重ならないようにする。表情の色の変化（`MaterialColorBindings`）はマテリアルの名前で引くので、衣装 A の名前を取らないようにするため。
- 隠すのは、アプリが lilToon で描くスロット（`LilToonFullSnapshot.Carries`）だけにする。lilToon の版の違いで MToon に近似したマテリアル、空のスロットがある表示物、lilToon の無いアバターは、アプリも標準のマテリアルで描くので、隠すとオンにしても見えなくなる。これらは今のまま見えるように書く。

### アプリでの見え方

- VR Vlog 1.2.1 以降: lilToon で描き、Expression Menu の既定でオフの衣装を隠す。出し入れは今と同じ。
- VR Vlog 1.2.0 以前: lilToon で描くが、既定でオフの衣装を隠さないので、今と同じく重なる（変わらない）。
- 一般の VRM ビューア: 衣装 B が見えなくなる。

### 既知の代償

アプリが lilToon でなく標準のマテリアルで描く次の 4 つの場合は、衣装 B をオンにしても透明のままになる。2026-11-23 に図で伝え、この代償で進めると決めた。

1. 読み込み前の確認で lilToon のデータを読めなかったとき
2. プレビューで標準の MToon を強制したとき
3. VRM 0.x の経路で読んだとき（URP では通らない）
4. マテリアルの差し替えに失敗したとき

### 上限と失敗のとき

- アプリのマテリアルの上限（`LilToonMobileProfile.MaximumMaterials`、64）を、別に作るマテリアルで超えると、拡張ごと落ちてアプリも標準のマテリアルで描く。超えるときは、共有しているマテリアルは別に作らず見えるままにする（衣装 B だけが使うマテリアルは数が増えないので隠す）。Console に 1 行出す。
- それでも lilToon の拡張を書けなかったとき（上限を超えた・書き込みに失敗した）は、`LilToonSnapshot` の段を外して書き出し直し、隠さずに書く。拡張の無いファイルで隠すと、アプリでオンにしても何も見えないため。
- 見えないマテリアルには画像を付けない。共有するマテリアルの発光のマスクなどを焼いた画像が、もう 1 枚ずつ増えてテクスチャの上限に近づかないようにするため。
- 読めずに MToon の既定値にしたマテリアル（`MaterialApproximatedWarning`）も、衣装 B のスロットでは隠す。アプリはそのスロットを lilToon で描く。

### 確かめていないこと

- 一般の VRM ビューアで、衣装 B の影が床に落ちるか。`HiddenInFallback` と同じ書き方で、影を落とす設定（Renderer の `shadowCastingMode`）は変えていない。
- 表情でマテリアルの色を変える設定は元の名前で引くので、衣装 B の側の見えないマテリアルには付かない。見えないので見た目は変わらない。
- 同じ lilToon の記録が 2 つの番号に付いたファイルを、アプリが番号で引くこと（名前でまとめないこと）。アプリの実機では確かめていない。

## 採らなかった案

- 衣装 B のメッシュを書かない: アプリで出し入れできなくなる。glTF のノードには非表示の欄が無い。
- 衣装 B のボーンの大きさを 0 にする: ほかのメッシュと共有するボーンでは形が崩れる。
- VRM の表情（Expression）で消す: 表情はメッシュ単位で消せない。
