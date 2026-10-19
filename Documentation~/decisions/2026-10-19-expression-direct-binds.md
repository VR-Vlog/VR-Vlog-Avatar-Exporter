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
  - 最適化ツールが別々のRendererの同じ名前のBlendShapeを1つにまとめ、同じ表情でそれぞれに違う値が指定されていたときは、まとめた先の1つの変形に2つの値は持たせられないので最初の値を使い、書き出しの詳細に1行出す（以前は別々の差分を焼いていたので、それぞれの形が保たれていた）。
- 直接参照する元のBlendShapeは、最適化ツールで消せない変形として記録する（`requireUsableEndpoint: true`）。そのため、表情で使っているBlendShapeを利用者が最適化ツールで固定・削除する設定にしていると、以前は焼いた差分が残って書き出せたところが、出力先がないとして止まる。表情で使うBlendShapeを固定する設定は表情を壊すので、黙って書き出すより止める方を選んだ。
- アニメーションする表情の処理と`RemapMenuExpressions`は変えていない。`RemapMenuExpressions`が`Direct`を書き換えないのは、直接参照の出力先を後で`MenuBinding`が最適化ツールの経路から解決するため。
- `VrmMenuExpressions`はホスト上の挙動テスト（`Tools/run-behavior-tests.ps1`）でもコンパイルするので、出力先の解決は`IDirectBindings`のインターフェイス越しにしている。

## 確認したこと

- 実装の前に、計測用のコードで8体のアバターの表情を「焼き込んだ差分」と「元のBlendShapeを重み付きで足したもの」の両方で作って比べた。直接参照の条件を満たすチャンネルでは、位置の差は最大7.5e-9、法線・接線の差は0だった。
- 8体のアバターを同じPC・同じUnityプロジェクト（Unity 2022.3.22f1）で、変更前（この変更の親のコミット）と変更後のそれぞれで1回ずつ書き出した。書き出せる6体（A〜F、`2026-10-19-expression-inspect-cache.md`と同じ記号）で、VRMの表情ごとに、参照しているmorph targetを重み付きで足した変位をnodeごとに比べた。

| アバター | 表情 | 参照の数（前→後） | morph target（前→後） | ファイルの大きさ（前→後） | 位置の差の最大 |
|---|---|---|---|---|---|
| A | 15 | 15 → 34 | 193 → 192 | 7.90 → 7.83 MB（−0.9%） | 1.6e-9 |
| B | 19 | 19 → 86 | 372 → 360 | 24.66 → 24.23 MB（−1.8%） | 9.6e-6 |
| C | 41 | 41 → 262 | 526 → 496 | 17.72 → 17.03 MB（−3.9%） | 9.8e-6 |
| D | 26 | 26 → 147 | 278 → 262 | 11.93 → 11.44 MB（−4.1%） | 8.9e-6 |
| E | 3 | 3 → 3 | 7 → 7 | 7.37 → 7.37 MB（同じ） | 0 |
| F | 47 | 47 → 238 | 696 → 660 | 39.86 → 37.99 MB（−4.7%） | 9.8e-6 |

  - 法線の差はすべて0。表情の名前と数、`isBinary`・`override*`、材質の色とテクスチャの変化、表情アニメーションの拡張、nodeの並びは6体とも変更前と同じだった。
  - 位置の差（最大9.8e-6、単位はメートル）は、UniGLTFがmorph targetを書き出すときに、長さが1e-5未満の頂点の変位を0にする（sparseの対象を`!= Vector3.zero`で選び、Unityの`Vector3`の比較は差の長さが1e-5未満を等しいとみなす）ことから来ている。変更前は表情全体の差分に、変更後は元のBlendShapeそれぞれにこの丸めがかかるので、1e-5未満の変位の扱いだけが変わる。差が出たのは各表情で数十頂点、どれも1e-5未満で、変更前・変更後とも全morph targetの変位の最小の長さは1e-5以上だった。変更後の表情の形は、元のBlendShapeを書き出したmorph targetの重み付きの和そのものになる。
  - 書き出しの時間（1回ずつ、同じPCで別のUnityが動いていたので揺れがある）は、A 13.6 → 13.7秒、B 42.6 → 48.6秒、C 176.3 → 200.8秒、D 67.0 → 65.3秒、E 3.1 → 2.9秒、F 128.5 → 115.8秒で、この変更は時間を目的にしていない。
  - 残りの2体（G・H）は変更前と同じ理由で、変更後も同じところで止まる。
- Unityのテスト（EditMode）は684件のうち652件が通過、13件が失敗、19件がskip。失敗した13件は変更前の基準（676件のうち644件が通過）で失敗していたものとすべて同じで（インストールされた最適化ツールを使う`ExportOptimizationBindingsTests`の一部）、この変更で新しく失敗したものはない。`ExpressionDirectBindTests`はすべて通過した。
