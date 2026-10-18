# 通常の書き出しで空の喜怒哀楽を名前から埋める

- 日付: 2026-10-18
- 対象: `Editor/ExpressionNameInference.cs`・`Editor/VrmExpressionBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExportRecovery.cs`・`Editor/ExportStageGuard.cs`（書き出し結果の一覧）・`Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`

## 背景

2026-10-14 の決定（`2026-10-14-emotion-presets.md`）で、表情・ポーズ指定UI（「VRM書き出し」）から喜怒哀楽の標準表情（happy・angry・sad・relaxed・surprised）を指定できるようにした。一方、表情・ポーズ指定UIを通らない通常の書き出し（**アプリに転送** と、表情を指定しない一括書き出し画面 `LilToonExporterWindow`）は、口（aa など）と瞬きだけを名前から自動で埋め、喜怒哀楽は作者が設定していなければ空のまま保存していた。

通常の書き出しでも、VRChat のメニュー表情は `VRChat / <名前>` のカスタム表情として出力に入っている。喜怒哀楽を読む VRM アプリやツールのために、名前から判断できるものは標準表情にも入れたい。

名前から表情を推定する処理は、喜怒哀楽の語表が `ExperimentalExpressionCaptureSession.Presets.cs`、口の別名が `VrmExpressionBindings.cs` に分かれていた。

## 決定

- 名前から VRM の表情を推定する処理を `ExpressionNameInference` にまとめた。
  - 喜怒哀楽: 2段の語表（`Emotions`）と除外語（`VetoWords`・`VetoParts`）、判定の `MatchEmotion(name, latinOnly)`。語表と判定の規則は 2026-10-14 の決定から変えていない。メニューの階層を読む `MatchMenuPath(path)` を足した。
  - 口: 別名の完全一致（`MouthShapes`・`FindExact`）。
  - 瞬きは従来どおり `BlinkShapeNames`。視線（lookUp など）と neutral は名前から推定しない。
- 通常の書き出し（`UniVrmOneClickExporter.Export`、既定 `inferEmotionPresets: true`）は、口と瞬きを埋めたあとに、空の喜怒哀楽を次の順に埋める。
  1. メニュー表情: カスタム表情の名前から `VRChat / ` と、続く `記録 / ` を外した残りを `MatchMenuPath` で判定する。日本語の名前も使う。最後の ` / ` より後のラベル（葉）だけで判定し、葉に除外語も感情の語も無いときだけ、近い親の階層から順に1階層ずつ判定する。除外語を含む階層・2つの感情に当たる階層に来たらそこで「該当なし」とする。`喜怒哀楽 / 笑顔` は happy、`Eye / Sad` は sad、`喜怒哀楽 / 1`・`Smile / Wink` は該当なし。表情ごとに、段の小さいもの → カスタム表情の並び順で1つ選び、morph target・マテリアルの bind と override をそのままコピーする。`isBinary` は `false` にする。
  2. メッシュの BlendShape（glTF の `extras.targetNames`）: `MatchEmotion(name, latinOnly: true)` で英語の語だけで判定する。表情ごとに、段の小さいもの → ノードと BlendShape の並び順で候補を選び、その名前を持つすべてのノードに重み1で bind する。`overrideBlink` は `block`、`overrideMouth`・`overrideLookAt` は付けない（`none`）。1つのメッシュに同じ名前が2つあると、どちらか決められないので警告を出し、その名前をほかのメッシュにも使わずに次の候補へ進む。
- 「空」は、表情が無いか、bind が1つも無い表情を指す。作者が設定した bind のある喜怒哀楽は置き換えない。
- 埋めた表情ごとに「`<表情>: 名前から自動で設定しました（<元の名前>）。動作を確認してください。`」の警告を出す。警告は書き出しの詳細（`LilToonExporterWindow` の「書き出しの詳細」と、Console の「VR Vlog 書き出し詳細」。**アプリに転送** は Console だけ）に出る。文言は `ExporterLocalization.T` で訳した書式に値を `string.Format` で入れる。
- 埋めた表情は、書き出し結果の一覧（`ExportFallbackReport.Results`、2026-10-18 の段階ガードの決定 `2026-10-18-stage-guard.md`）にも表情ごとに1件記録する。結果は **保持**（`ExportOutcome.Kept`）、分類は「名前から自動で設定」、名前は表情（`happy` など）、理由は上の警告と同じ文。出力に残した表情なので要確認の件数（`Count`）には数えない。埋める処理は表情出力の段階（`guard.Run(ExportStage.ExpressionOutput, …)`）の中で行い、失敗したら埋める前の出力に戻す。
- 表情・ポーズ指定UIの書き出し（`ExperimentalExpressionCaptureSession` からの3つの呼び出し）は `inferEmotionPresets: false` にした。メニューの「VR Vlog → VRMを書き出す」はこの画面を開くので、メニューからの書き出しの出力は変わらない。名前からの推定が入るのは **アプリに転送**（`AppTransferExport`）と `LilToonExporterWindow` の書き出し。

## 検討した代替案と不採用理由

- BlendShape も日本語の名前で判定する: 日本語の BlendShape 名は MMD の標準モーフ（`怒り` は眉、`びっくり`・`なごみ` は目、`にっこり` は口だけ）であることが多く、顔の一部だけの表情が喜怒哀楽として既定で入ってしまう。2026-10-14 の候補の探し方と同じく英語の語だけにした。メニュー表情は作者が顔全体の表情として作ったものなので日本語も使う。
- BlendShape をメニュー表情より先に使う: メニュー表情は複数の BlendShape・マテリアルを組み合わせた作者の意図した顔で、BlendShape 1つより再現度が高い。名前の手がかりも作者が付けたメニュー名のほうが確か。
- 表情・ポーズ指定UIの書き出しでも埋める: 画面で「なし」にした表情が書き出し時に勝手に埋まり、画面の表示と出力が食い違う。画面の自動候補が同じ語表で既に提示しているので、そちらに任せた。
- 視線・neutral も名前から推定する: 視線は左右・上下の向きと量が名前だけでは決まらず、neutral は「無表情」を表す決まった名前が無い。誤って入ると常に顔が動くので今回は扱わない。
- 同名の BlendShape が1つのメッシュに複数あるとき先頭を使う: どちらが顔全体の変形か判断できない。口の別名で同名が複数ある場合と同じく、使わずに警告する。
- 同名が複数あるメッシュだけを外し、ほかのメッシュの同じ名前で埋める: 顔のメッシュが外れて体や髪のメッシュだけが動き、顔の一部しか動かない表情になる。その名前ごと使わない。
- メニュー表情をサブメニューを含む全体の名前で判定する: `喜怒哀楽` のように複数の感情を含むサブメニューや、`Eye` のような除外語のサブメニューの中にある表情が、葉のラベルだけなら判定できるのにすべて「該当なし」になる。逆に、親の名前が葉の判定を上書きすることも避けたい。
- BlendShape から埋めた表情の override を付けない（`none`）: 顔全体の BlendShape（`Fcl_ALL_*` など）は目の形も含むので、自動瞬きが重なると瞼が二重に動く。VRoid 系の VRM の喜怒哀楽の既定と同じく `overrideBlink: block` にした。口と視線は、顔全体の表情でもリップシンク・視線を止める理由が VRM 1.0 の仕様に無いので `none` のまま。メニュー表情は元の override をそのままコピーする。
- 埋めた表情の一覧を `ExportRecoveryReport` に専用の欄として持つ: 段階ガードで書き出し結果の一覧（`ExportFallbackReport.Results`）ができたので、別の欄を作らずそこへ **保持** として入れた。結果の一覧を表示する画面はまだ無いので、利用者に見えるのは引き続き警告。
- 埋めた表情を **近似** として記録する: 近似は元の見た目を別の形で再現したもので、要確認の件数に入る。名前からの推定は作者の表情・BlendShape をそのまま使っており、出力に残すので **保持** にした。

## 影響

- 名前から判断できるメニュー表情か英語名の BlendShape があるアバターでは、**アプリに転送** と `LilToonExporterWindow` の出力に喜怒哀楽の標準表情が増える。カスタム表情はそのまま残るので、アプリの表情一覧に `happy` と `VRChat / <名前>` が両方並ぶことがある（2026-10-14 の決定と同じ）。
- 判断できる名前が無いアバター・作者が喜怒哀楽を設定済みのアバターの出力は従来と同じ（`ExpressionNameInferenceTests` で、推定を無効にした場合と既定の場合、作者設定が残る場合を確認）。
- 推定した表情は警告として書き出し結果に出るので、保存後に確認できる。書き出し結果の一覧には **保持** の「名前から自動で設定」として表情ごとに残る（`ExpressionNameInferenceTests` で確認）。

## 未決

- 視線（lookUp・lookDown・lookLeft・lookRight）と neutral の名前からの推定。
- 自動で埋めた喜怒哀楽を、**アプリに転送** から個別に外す操作は無い。不要なら表情・ポーズ指定UIから書き出す。

## 出典

- VRM 1.0 の Expression 仕様（`preset` の happy/angry/sad/relaxed/surprised、`morphTargetBinds`、`isBinary`、override の `none`/`block`/`blend`）
- 2026-10-14 の決定 `2026-10-14-emotion-presets.md`（語表と判定の規則）
