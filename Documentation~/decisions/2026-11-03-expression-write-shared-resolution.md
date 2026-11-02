# 瞬きの解決結果を 1 つにまとめ、取り直しの Renderer の追い方と拡張の置き方を既存の補助に寄せる

## 背景

- 瞬きの書き出し（BlinkExportSession）は解決結果の欄 11 個（Description・Disabled・MissingAutomatic・UnresolvedAutomatic・UnresolvedReason・PreserveAuthored・authored・allowMissingAutomaticBlink・Inferences・inferredEndpoints・deferredAutomatic）を、失ったとき（DisableLost）・取り直し（ResolvePreparedNeutral）・書き出し用の複製（ForClone）の 3 か所で 1 つずつ消す／写していた。3 か所で扱う欄がそろっておらず、DisableLost は UnresolvedAutomatic と UnresolvedReason を戻さず、ResolvePreparedNeutral は candidates と ConfiguredByDescriptor を、ForClone は ConfiguredByDescriptor を写していなかった（どれも出力には届かない経路だった）。
- 瞬きと UE の取り直しが、Renderer を 1 回ずつ引いてメモ化し、全部そろってから Clear→Add で公開する同じ型を別々に書いていた。ForClone は複製の中の Renderer を GetSiblingIndex の経路で自前に探していた。
- 口の動き（LipSyncCapture）と姿勢（PoseExportSession）の注入が、拡張の置き方（extensions と extensionsUsed）を手書きしていた。同じ補助 `GlbDocument.PlaceExtension` が既にあり、他の 3 か所は使っていた。
- 最適化後の UE 書き出し（ExportOptimizationBindings）は、生の ARKit 名の推定を止めるために全メッシュの targetNames を書き換えて Add に渡し、読み直して戻していた。

## 決定

- 解決結果を入れ子の `Resolution` 1 つにまとめ、DisableLost は空の Resolution の代入、ResolvePreparedNeutral は参照の差し替え、ForClone は `Resolution.Map(Target)` にする。写し漏れていた欄も写すようになるが、読む側が無いので出力は変わらない。
- Renderer の追い方は `PreparedRendererMap`（Find は null を呼び出し側に返す・Require は例外）と `Publish` にまとめる。null の扱い（瞬きは全部 Require・UE は selectedRawRoutes・capturedWeights・materialScopes だけ読み飛ばす）と衝突の文言は経路ごとの仕様なので呼び出し側に残す。4 種類の辞書を扱う共通型は作らない。
- ForClone の Target は `ExportRecoveryCopySession.Resolve` に寄せる。Renderer がアバターの外にあるときの例外が NullReferenceException から ArgumentException になるが、Validate が Renderer をアバターの下に限っているので届かない。
- 拡張の置き方は新しい補助を足さず `PlaceExtension` に寄せる。姿勢の注入は、重複時の例外が Dictionary.Add の ArgumentException から文言つきの InvalidOperationException になり、extensions が無いときは例外でなく作り、extensionsUsed の重複を除くようになる。どれも UniVRM の出力では起きず、ガード段はどちらの例外も同じく省略として扱う。起きない入力のためだけに引数を足さない。
- UE の Add に `inferRawNames` を足し、false なら supportsUnified の targetNames の項と ResolveRaw の候補を空にする。書き換えと復元の 13 行は消す。既定は true で、他の呼び出しは変えない。mesh や index の番号が `1.0` のように実数で書かれた入力は、書き直しを経なくなるので例外になるが、glTF として不正で UniVRM の出力では起きない。
- A2b-08（瞬きの Apply の前倒し）・A2b-11（視線を UniVRM に書かせる）・A2b-01（表情を VRM10Object だけで組む）は出力が変わる、またはハーネスが成り立たなくなるのでやらない。

## 確かめたこと

- 瞬きのテスト 4 クラス 127 件、UE のテスト 3 クラス 200 件（赤 10 は Avatar Optimizer 1.8.16 由来で前後同じ）が緑。
- 最適化後の UE について、旧手順（書き換え→Add→復元）をテストの中で組み直し、新しい `Add(..., inferRawNames: false)` と custom のキー集合・warnings の文言と並び・GLB の書き出し結果が一致することを見た。
- 全 EditMode、ホストのハーネス、書き出しの回帰比較は PR 本文に書く。
