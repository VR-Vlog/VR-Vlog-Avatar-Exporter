# VRVLOG_expression_animations に schemaVersion 2 を足し、読めるアプリの版を asset.extras に書く

- 日付: 2026-10-19
- 対象: `Formats/Runtime/ExpressionAnimationData.cs`・`Formats/Runtime/AvatarFormatVersions.cs`・`Schema/VRVLOG_expression_animations.1.schema.json`・`Schema/VRVLOG_expression_animations.2.schema.json`・`Tests/Editor/ExpressionAnimationDocumentTests.cs`・`Formats/README.md`

## 背景

schemaVersion 1 の `VRVLOG_expression_animations` は、アニメーションの途中の形を書き出し時に作った専用の変形（`__VRVlog_Anim_` で始まる名前）として VRM に足し、その名前で参照している。表情のアニメーションが増えるほど変形の数とファイルの大きさが増えるうえ、アプリは元の変形を直接動かせない。また、カスタム表情がメニュー・ジェスチャー・FaceEmo のどれから来たかを書く場所が無い。

あわせて、アプリが読めない版のデータを含むファイルを古いアプリで開いたときに、何が足りないかをアプリ側で判断できるようにしたい。今は拡張ごとの版をアプリが読んでみて失敗するまで分からない。

この変更は形式のコードと schema だけで、書き出しが schemaVersion 2 と `VRVLOG_requiredAppVersions` を書くのは次の変更から。それまでは書き出すファイルは変わらない。

## 決定

- schemaVersion 2 のルートは `schemaVersion`（2）・`expressions`・`animations` にする。
  - `expressions` は `{ expression, source }` の配列で、`source` は `menu`・`gesture`・`faceEmo` のどれか。表情名は重複させない。アニメーションの無い表情も載せる。
  - `animations` の形は v1 と同じで、point の `target` だけを `{ node, index }`（glTF のノードの番号とその mesh の morph target の番号）か null にする。null の point は変形を動かさない。v1 にあった「先頭の point は null で、t=0 の値と一致する」決まりは v2 には無い。
  - `animations` の各 `expression` は `expressions` に載っている名前にする。1 つのアニメーションの中で同じ (node, index) を 2 回参照しない。
  - `expressions` と `animations` の両方が空のファイルは不正にする（書く意味が無い）。
- v2 の意味（schema の description にも書いた）: VRM の表情は time = duration の形を持つ。アプリは毎フレーム、VRM の表情の更新の後に各チャンネルのカーブを評価し、その値を挟む 2 つの point の間を補間した重みを、その point の変形へ書く（ほかから書かれた値は上書きする）。表情の選択が終わったら、書いた変形を 0 に戻す。
- `ExpressionAnimationData.Write`・`Read` は v1 のまま残す。v2 は `WriteDocument`・`ReadDocument`（`ExpressionAnimationDocument`）で扱い、`ReadDocument` は v1 も読む。`WriteDocument` は v2 だけを書き、返す前に自分の出力を `ReadDocument` に通す。
- `AvatarFormatVersions` に asset.extras のキー `VRVLOG_requiredAppVersions` と、拡張名と schemaVersion（`VRVLOG_materials_liltoon` は schemaMajor）から最低のアプリ版を返す表を置く。表に無い組み合わせは例外にする（推測の版を書かない）。版の比較 `Compare` は `major.minor.patch` を数値で比べ、それ以外の形（前ゼロ・4 要素・空など）は `FormatException` にする。
- 表の値は「その版の番号を持つアプリのビルドならどれでも読める」最低の版にする。

| 拡張 | 版 | アプリの版 |
|---|---|---|
| `VRVLOG_materials_liltoon` | schemaMajor 2 | 1.0.0 |
| `VRVLOG_expression_animations` | 1 | 1.0.0 |
| `VRVLOG_expression_animations` | 2 | 1.0.5 |
| `VRVLOG_humanoid_poses` | 1 | 1.0.0 |
| `VRVLOG_humanoid_animations` | 1 | 1.0.4 |

- `VRVLOG_expression_animations` 2 の 1.0.5 は、アプリの対応 PR が今の develop の版（1.0.5）の期間に入る前提の値。**アプリの PR のマージ時に版が進んでいたら、この表を直す。**
- `Formats/package.json` の version は上げない。`Formats/README.md` に「version は 1.0.0 のままで、形式の版は固定したコミットで表す」と決まっているので、それに従った。CHANGELOG には書かない（書き出しの動作が変わる次の変更で書く）。

## 確認したこと

- アプリの開発ブランチ（2026-10-19 時点）の履歴で、アプリの版が変わった日は次のとおり: 1.0.0 が 2026-09-22、1.0.1 が 09-30、1.0.2 が 10-02、1.0.3 が 10-03、1.0.4 が 10-10、1.0.5 が 10-16。それより前は 0.1.0 と 0.1.1。
- 1.0.0 にした時点で、アプリの `ExpressionAnimationData.cs` は exporter の v1 と、公開範囲（internal と public）とコメント 1 行を除いて同じ。`HumanoidPoseData.cs` は exporter の `Editor/HumanoidPoseData.cs` と同じ。`LilToonFullContract.cs` は公開範囲と、後で足した色空間の項目を除いて同じで、schemaMajor 2・schemaMinor 0・lilToon 2.3.4 と同じカタログのコミットを受け付ける。exporter が今書くのは schemaMajor 2 だけなので、1.x の表は作らない。
- 読み込みのコードが入ったのは、expression animations v1 が 09-06、humanoid poses が 09-17 で、どちらも 1.0.0 より前。0.1.1 のビルドには読めるものと読めないものが混ざるので、1.0.0 を最低にした。
- humanoid animations の読み込みは 10-07 に入った。それより前には `VRVLOG_humanoid_animations` の参照が無く、読み込みのコードは exporter の `Editor/HumanoidAnimationData.cs` と同じ。10-07 は 1.0.3 の期間の途中なので、1.0.3 のビルドのうち 10-03〜10-05 に作られたものは読めない。1.0.4 のビルドはすべて読めるので、最低を 1.0.4 にした。
- 色空間: アプリが asset.extras の `VRVLOG_lilToonColorSpace` を読むようになったのは 10-09（1.0.3 の期間）で、exporter が書き始めたのも 10-09。それより前のアプリはこのキーを無視して linear とみなすので、gamma のプロジェクトから書き出したファイルは追加の色の合成がずれるが、読み込み自体はできる。`VRVLOG_materials_liltoon` の版は変わっていないので、表は schemaMajor 2 = 1.0.0 のままにした。
- 1.0.5 の番号で作られたビルドのうち、すでにあるものは v2 の読み込みが入る前の状態から作られている。v2 を読めない 1.0.5 のビルドがあるので、表の 1.0.5 は上の前提（アプリの対応が 1.0.5 の公開までに入る）が崩れたら直す必要がある。
- `Tests/Editor/ExpressionAnimationDocumentTests.cs` で、v2 の書き出し→JSON→読み込みの往復（書き直した JSON が同じになることを含む）、アニメーションの無い表情だけのファイル、v1 を両方の読み込みで読めること、v1 の読み込みが v2 を拒むこと、版・重複・知らない source・`expressions` に無い表情・同じ変形の重複・負や小数の番号・名前の target・空のファイルを拒むこと、`WriteDocument` が v1 の内容（名前の target）を拒むこと、版の比較と表の値を確かめる。Formats の netstandard2.1・C# 9 でのビルドも警告なしで通ることを確かめた。
- 2 つの schema が draft 2020-12 として正しいことと、`Write`・`WriteDocument` の出力がそれぞれの schema に合い、もう一方の schema には合わないことを jsonschema で確かめた。v2 の schema が、知らない source・両方空・負の node・小数の index・文字列の target・余分なプロパティ・`expressions` の欠け・空の表情名・point 1 つ・要素 5 つの key・schemaVersion 3 を拒むことも確かめた。
