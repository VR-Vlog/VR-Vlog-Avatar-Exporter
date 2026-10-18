# メニューの静的な表情は元のBlendShapeを重み付きで直接参照する

- 日付: 2026-10-19
- 対象: `Editor/VrChatExpressionBaker.cs`・`Editor/VrmMenuExpressions.cs`・`Editor/ExportOptimizationBindings.cs`・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/ExpressionDirectBindTests.cs`・`Tests/Editor/ExportOptimizationBindingsTests.cs`・`CHANGELOG.md`

## 背景

アバターの表情メニュー（VRChatのメニュー・ジェスチャー・FaceEmo）の静的な表情は、これまで表情ごとに「初期値からの差分」を新しいBlendShape（`__VRVlog_Menu_*`）として焼き込み、VRMのカスタム表情からその1つを重み1で参照していた。表情が多いアバターでは、表情の数×対象の頂点数ぶんの変形データがVRMに増える。

書き出し用の複製は`AvatarBaseShape.Preserve`で初期値を基準形へ焼き込み、各BlendShapeを「100の1フレームに`Evaluate(F) − Evaluate(初期値)`を持つ形」に置き換えている（初期値がすべて0のRendererは置き換えず、元のフレームのまま）。元のBlendShapeが1フレームだけなら、初期値`r`から重み`w`への変化はその置き換えたBlendShapeの`(w − r) / (F − r)`倍と一致する。UniVRMは最後のフレームを書き出すので、glTFの重み`x`は「最後のフレームの差分×`x`」になり、置き換えていないRendererでも`w / F`で同じ形になる。

## 決定

- 表情のチャンネルごとに、次をすべて満たすときは焼き込まずに元のBlendShapeを直接参照する（`VrChatExpressionBaker.DirectWeight`）。
  - 元のBlendShapeが1フレームで、そのフレームの重み`F`が正の有限値。
  - `r ≤ w ≤ F`かつ`F > r`（`PlayerSettings.legacyClampBlendShapeWeights`が有効なときは`r`と`w`を`AvatarBaseShape.ClampSourceWeight`で丸めてから比べる）。
  - 書き出し用の複製に同じ名前の1フレームのBlendShapeがある。
- 参照の重みは`(w − r) / (F − r)`。0になるチャンネルは参照を作らない。
- 条件を満たさないチャンネル（初期値より下げる・複数フレームの曲がった変形など）だけを従来どおり差分として焼き込む。焼き込む差分と名前のハッシュには、その残りのチャンネルだけを使う。
  - 直接参照があり、残りの差分が0のときは焼き込まない。
  - 直接参照がなく残りの差分が0のときは、従来どおりメッシュごとに1つのゼロのマーカーを共有して、表情が選べる状態を保つ。
  - 128 MiBの上限の見積もりには、実際に焼き込む差分だけを数える。
- `VrmMenuExpressions.Expression`に`Direct`（複製のRenderer・BlendShape名・重み）を足し、`VrmMenuExpressions.Add`は焼き込んだ表情を重み1、直接参照を各自の重みで登録する。同じ表情の中で同じ（node, index）が重なったときは最初の1つだけを残す（UniVRMは1つの表情の中の重複を最初の1つだけ使うので、それに合わせる）。
- 直接参照の出力先は、最適化ツールの統合や改名のあとでも追えるように`ExportOptimizationBindings`で解決する。`Capture`に`menuMorphs`を渡すと、その参照を使える変形として記録し（`requireUsableEndpoint: true`）、`BindNodes`はその出力先のnodeも確定する。`MenuBinding`は記録した参照から最終的なnode・index・重みを返す。出力先のnodeがないときは、その段階で止める。
- アニメーションする表情の処理と`RemapMenuExpressions`は変えていない。
- `VrmMenuExpressions`はホスト上の挙動テスト（`Tools/run-behavior-tests.ps1`）でもコンパイルするので、出力先の解決は`IDirectBindings`のインターフェイス越しにしている。

## 確認したこと

TODO
