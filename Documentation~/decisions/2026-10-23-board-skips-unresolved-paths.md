# パスで特定できないメッシュや、基本の形に保存できないBlendShapeで表情ボードと書き出しを止めない

- 日付: 2026-10-23
- 対象: `Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExperimentalExpressionCaptureSession.Clips.cs`・`Editor/AvatarBaseShape.cs`・`Editor/AuthoredExpressionEndpoints.cs`・`Editor/UniVrmOneClickExporter.cs`

## 背景

VRChat SDK でアップロードできるアバターの構成から、表情ボードを開く処理と書き出しの経路を追い、1 つのメッシュや 1 つの BlendShape の不備で全体が止まる箇所を洗い出した。そのうち次の 5 つは実際のアバターで起こりうる。

- 表情ボードは記録するメッシュを階層のパスで引き直す（`VrChatExpressionSampler.FindRenderer`）。メッシュより上のオブジェクト名に「/」があると、パスではそのメッシュを指せず `InvalidOperationException` になり、ボードが開けなかった。VRChat はオブジェクト名の「/」を受け付ける。
- 記録するメッシュの BlendShape の値に NaN や無限大があると、基準の顔を記録する途中で止まっていた。FX の基準表情の読み取りは `InvalidOperationException` だけを受けていて、ほかの例外ではボードが開けなかった。
- Modular Avatar の Merge Animator のパス設定（`pathMode`・`relativePathRoot`）はリフレクションで読んでいる。フィールドが無い版の Modular Avatar では `NullReferenceException` になっていた。
- `AvatarBaseShape.Preserve`（調整済みの BlendShape の値を基本の形としてメッシュに保存する処理）は、重みが 0 以下のフレームを持つ BlendShape のように評価できないフレーム構成があると例外を投げていた。この処理は書き出しの段のガード（`ExportStageGuard.Watch`）の中にあり、段を外したあとの 2 回目でも同じ例外になるため、書き出しが止まっていた。
- VRM の表情（`AuthoredExpressionEndpoints`）は、表情のバインドが指すパスにメッシュが 2 つ以上あると `InvalidOperationException` を投げていた。表情ボードのプリセットを作ったあと、NDMF や Modular Avatar の処理で同じ名前のメッシュが同じ階層に増えると起こる。これも `Watch` の中で 2 回とも失敗し、書き出しが止まっていた。

## 決定

- 表情ボードは、パスで特定できないメッシュと、BlendShape の値が NaN や無限大のメッシュを表情の記録対象から外し、パスを警告に出す。同じ階層に同じ名前のメッシュがあるときの既存の扱い（`skippedPaths` と警告）に合わせた。外したメッシュも書き出しにはそのまま入る。
- FX の基準表情を読む処理は、キャンセル以外のすべての例外を受けて警告に出し、基準の顔はそのままにする。
- Merge Animator のフィールドが無いときは、`pathMode` が無ければ相対パスの基点を不明のまま、`relativePathRoot` が無ければコンポーネントのオブジェクトを基点にする。
- `AvatarBaseShape.Rebase` は評価できない BlendShape だけを元のフレームのまま書き写し、その値を 0 にせず残す。ほかの BlendShape は今までどおり基本の形に保存する。保存しなかった BlendShape の名前を警告に出す。メッシュそのものを作り直せないとき（`Rebase` が例外を投げたとき）は、そのメッシュを元のまま書き出して警告を出す。
- `AuthoredExpressionEndpoints` は、複数のメッシュが共有するパスを指すバインドを、メッシュが無いバインドと同じ扱い（どのメッシュにも効かない状態）にして、パスを警告に出す。警告は書き出しの試行ごとの一覧に入るように、`Capture` に警告の一覧を渡す。
- どれも今まで止まっていた構成だけで動きが変わる。今書き出せているアバターの出力は変わらない。かんたん転送も同じ処理を通るので、通常の書き出しとの一致は変わらない。

## テスト

- `ExperimentalExpressionCaptureTests.AMeshItsPathCannotNameIsLeftOutAndWarns`: 名前に「/」を含むオブジェクトの下のメッシュがあっても表情ボードが開き、そのメッシュだけが記録対象から外れて警告が出ること。
- `ExperimentalExpressionCaptureTests.AMeshWithAnInvalidWeightIsLeftOutAndWarns`: BlendShape の値が NaN のメッシュが記録対象から外れ、ボードが開くこと。Unity が NaN を設定どおりに保たない版では結果を決めずに終える（`Assume`）。
- `BaseShapeTests.PreserveKeepsAShapeWithUnsupportedFramesAndWarns` と `BaseShapeFixture.UnsupportedFrameChecks`: 重みが負のフレームを持つ BlendShape がそのまま残って値も保たれ、ほかの BlendShape は基本の形に保存され、警告が出ること。
- `NeutralShapeEndpointTests.AuthoredBindingOnASharedRendererPathIsLeftInertAndWarns`: 同じパスのメッシュが 2 つあっても VRM の表情の取り込みが止まらず、そのバインドがどのメッシュにも効かない状態になり、警告が出ること。
- Merge Animator のフィールドが無い場合はテストを足していない。テストを回す環境には実際の Modular Avatar が入っていて、フィールドが無い版を作れない。

## 欠点

- 記録対象から外したメッシュの表情は表情ボードでは記録されず、そのメッシュは書き出しで基本の顔のまま動かない。警告でパスを伝えるので、名前を直せば記録できる。
- 保存できなかった BlendShape は基本の形に入らず、元の値のまま VRM の BlendShape として残る。VRM を読むアプリによっては、その値を使わずに 0 から表示することがある。
- 同じパスを指す VRM 表情のバインドは効かなくなる。どちらのメッシュに効かせるかを推測しないためで、名前を分ければ元どおり効く。
