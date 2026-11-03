# FX の走査・曲線の判定・プレビューでの再生を、呼び出し側ごとの写しから共有の型にまとめる

- 日付: 2026-11-03
- 対象: `Editor/AnimatorGraph.cs`・`Editor/CurveChecks.cs`・`Editor/AnimatorProbe.cs`・`Editor/FacialProjectionScope.cs` と、それらを使う `Editor/ExpressionDependencies.cs`・`Editor/HeldAutomaticExpressionLayers.cs`・`Editor/FixedExpressionContext.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/ExpressionGestureCapture.cs`・`Editor/ObjectToggleCapture.cs` ほか

## 背景

大規模リファクタリング（[2026-11-02-big-refactor.md](2026-11-02-big-refactor.md)）の VRChat FX 解析の 2 回目。1 回目は出力に影響しない所見（死にコード・テスト専用の観測口・SDK の型名の表）だけを直した。残った所見のうち、FX のステートマシンの走査（状態の持ち主・親の索引・遷移の検証・mute と solo・条件の比較）は 7 ファイルが、曲線が一定かの判定は 13 ファイルが、プレビューシーンに複製して Animator で再生する枠は 3 ファイルが、それぞれ少しずつ違う写しを持っていた。写しごとの違い（空の曲線を一定とみなすか・端の接線を見るか・NaN の扱い・未知の条件で例外を投げるか）は、それぞれの呼び出し側が自分の目的で決めたもので、揃えると読める表情の集合が変わり得る。

## 決定

- `AnimatorGraph.Build` がステートマシン 1 つにつき 1 回、状態→持ち主のステートマシン・ステートマシン→親の索引を作り、`Valid` で遷移の行き先を検証する。重複した状態・null の状態があれば null を返し、呼び出し側は今までどおり「解析できない」として扱う。`AnimatorConditions` は条件の比較を三値（真・偽・分からない）で返し、未知の mode と NaN を含む大小比較は「分からない」にする。呼び出し側は `== true`・`== false` のどちらを求めるかで今までの結果を保つ（`IsTrue` は NaN の大小比較を偽と数えず通す、`PoseMenuResolver` は未知の mode で例外、`ExpressionEvaluationSession` は有限でない値を先に落とす、型つきの偽の判定は Int の値が整数でなくてもよい）。`Enabled` は mute と solo を Unity の規則（solo が 1 つでもあれば solo でない兄弟は mute でも偽でも抑えられる）で絞り、null の要素は落とさず呼び出し側に渡す。
- `CurveChecks` に `Constant(curve, emptyIsConstant)`・`Constant(curve, out value, emptyIsConstant, endTangents, finite)`・`HoldsAfter(curve, time)`・`Flat` を置き、各所の違いは引数で残す。既定は `VrChatExpressionSampler.IsConstant` と同じ（空は一定・両端の接線も見る・有限かは見ない）。`SelectedExpressionAppearance` の規則にすべて揃える案は、一定と判定される曲線が増えて読める表情が変わるので、回帰比較つきの別の変更にする。
- `AnimatorProbe`（IDisposable）がプレビューシーンへの複製・Behaviour の無効化・コントローラー無しの Animator の初期化・手動で進める `PlayableGraph` の作成（途中で失敗したらグラフを壊してから投げ直す）・型つきの入力（`Write`。Trigger は書かずに偽を返し、呼び出し側が今までの例外を投げる）を持つ。グラフの所有は呼び出し側に残し、`Dispose` は複製を壊してからシーンを閉じる（順番は以前と同じ）。
- `FacialProjectionScope.Create` を、記述子の顔の設定（リップシンクの経路と目線のまぶた）を読む `DescriptorFaceShapes` と、根・頭・首・種のモーフだけから推定する `Estimate` に分ける。推定はメッシュ・ボーン・トランスフォームしか読まない。
- 採らなかった所見:
  - `FxAnalysis`（1 回の書き出しで変わらない解析結果を項目ごとのループの外で 1 回作る。A2a-02・A2a-18・A2a-22）。触るのが 10 ファイル・入口 15 か所・テストの参照 19 か所に及び、書き出し時間の計測も要るので、この PR には入れず単独の PR にする。`Session` をメニューのデータから外す案（A2a-12 の後半）も 4 つの署名を変えるので同じ PR で行う。
  - `FixedExpressionContext` とジェスチャーの読み込みの組み込みパラメーターの表を 1 つにする案（A2a-13）。表は同じではない。`FixedExpressionContext` は `AvatarVersion` を 3 に固定し他を 0 に留めるが、ジェスチャーの読み込みは `AvatarVersion` を置かず `EyeHeightAsMeters`・`EyeHeightAsPercent` を持つ。揃えると中立の証明かジェスチャーの読み込みのどちらかの前提が変わるので、この所見は前提が違うとして見送る。

## 確かめたこと

- 全 EditMode（Unity 2022.3.22f1・lilToon 2.3.4・VRChat SDK 3.10.1・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.8.16）で新しい赤 0・消えたケース 0・足したケース 0。他の Unity が同じ PC で動いている間に回した 1 回で `QuickTransferMatchesDefaultNormalTests.PosesOverTheAppLimitMatch` が「動くポーズの解析が 30 秒を超えました」で赤になったが、クラス単位の再実行で 7 件すべて緑。
- `Tools/validate.py`・`Tools/test-compatibility-tooling.py` と、ホストのハーネス（compatibility・behavior・pose-contract・bake・ndmf-preparation・lan-transfer）は緑。
- 書き出しの比較（既定のアバターの quick セット）は PR 本文に書く。
