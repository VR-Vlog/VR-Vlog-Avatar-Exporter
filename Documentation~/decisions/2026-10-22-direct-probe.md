# 常時適用FXの差分取りで、選んだクリップだけが効くときは再生せずに値を決める

- 日付: 2026-10-22
- 対象: `Editor/VrChatExpressionSampler.cs`・`Tests/Editor/DirectProbeTests.cs`・`CHANGELOG.md`

## 背景

表情を書き出すとき、`VrChatExpressionSampler.ApplyPermanentOverrides` は表情ごとに、選んだレイヤーを「表情のBlendShapeをすべて0にするクリップ」と「すべて100にするクリップ」に差し替えたプローブ用のAnimatorControllerを作り、それぞれを `SampleNeutral` で再生して、ほかのFXレイヤーが重ねる値（Offset）と効き具合（Scale）を求めている。1回の再生でアバターを複製し、PlayableGraphを120フレーム以上進めて静止を確かめるので、表情が多いアバターでは書き出し時間の大半をこの差分取りが占めていた（10/19の計測で、表情の多い3体は書き出しの67〜81％）。

オーナーから、遷移やBlendTreeの無い単純なクリップは再生せずにカーブから直接読んで速くするよう指示があった。単純かどうかは、遷移・BlendTree・Write Defaults・レイヤーの重み・ブレンド・重なるレイヤー・ほかのツールが足したものまで含めて厳しく判定し、判定から外れるものは今までどおり再生する。

最初はAvatarMaskとStateMachineBehaviourがあれば一律に再生にしたが、計測に使った6体では直接決めた表情が1つも無かった（判定外の理由はAvatarMask・Behaviour・レイヤーの重みの制御・射影）。Transformを持つAvatarMaskは除いたTransformのBlendShapeも止める（UnityのAvatarMaskの仕様）ので、描画先が有効かを見れば足りる。トラッキング・ロコモーション・ポーズ空間の制御はFXのBlendShapeを書かないので、プローブの結果を変えない。そこでこの2つを判定に入れた。

## 決定

プローブのグラフで、表情のBlendShapeを書くのが差し替えた選んだレイヤーだけのとき、再生した結果は入力の値（0か100）がそのまま全BlendShapeに乗る。このときは再生せず、入力の値をそのまま結果にする（Offset 0・Scale 1になり、表情の値は変わらない）。

直接決めるのは、次をすべて満たすときだけにする。1つでも外れたら今までどおり再生する。

1. 選んだステートが元のFXのレイヤーにあり（`nativeSlot`）、レイヤーの重みを変える制御・射影（`projection`）・常時適用のレイヤー・パラメータで動くレイヤーが無い。
2. 選んだレイヤーは重み1（レイヤー0は常に1）。AvatarMaskは、無いか、Transformを持たないか、表情のBlendShapeの描画先をすべて有効にしているもの。アバターにAnimatorがある。
3. 選んだステートとそのステートマシンのStateMachineBehaviourは、トラッキング・ロコモーション・ポーズ空間の制御（VRChatの `VRCAnimatorTrackingControl`・`VRCAnimatorLocomotionControl`・`VRCAnimatorTemporaryPoseSpace`）と、Modular Avatarの処理を持たない目印だけ。Parameter Driverやレイヤーの重みの制御など、それ以外が1つでもあれば再生する。
4. プローブの依存の解析（`ExpressionDependencies.AnalyzeNeutral`）で、表情のBlendShapeを書くレイヤーが選んだレイヤーだけで、支えのレイヤー（Write Defaultsやadditiveで値を足すレイヤー）・重みの制御・Parameter Driverが無い。解析が見つけた最上位のOverrideのレイヤーは、選んだレイヤー自身であってよい。
5. 解析した表情のBlendShapeが、クリップのBlendShapeと完全に同じ集合で、保存の対象（`CommittedMorphs`）とも同じ。
6. 選んだレイヤーが書き出しで除くレイヤーに入っておらず、すべてのBlendShapeの描画先が除外されていない・有効・メッシュにそのBlendShapeがある。

Probe(0)とProbe(100)は入力の値以外は同じグラフなので、Probe(0)で判定した結果をProbe(100)にも使う。外部入力の記録（`UsedParameters`）は、直接決めたときも再生したときと同じ依存の解析から行う。

## 検討した代替案と不採用理由

- クリップのカーブを読んで値を出す: 単純なときに再生の結果が入力そのものになるのは、プローブのクリップが入力の値の定数だからで、元のクリップのカーブを読む必要はない。判定を厳しくしておけば、カーブを読むより間違いの余地が少ない。
- プローブを1回（Probe(100)だけ）にする: Offsetを求めるのにProbe(0)が要る。直接決められないときに再生を減らせない点は変わらない。
- プローブのグラフの進め方（120フレーム）を短くする: 遅れて効くレイヤーやコールバックを見落とす。

## 影響

- 判定を満たす表情は、差分取りの2回の再生が無くなる。書き出すVRMは変わらない（「確認したこと」）。
- 判定から外れる表情は今までどおり再生する。
- 確かめるために、`VrChatExpressionSampler.CompareDirectProbe` を立てると直接決めた表情も再生し、結果が入力と一致するかを `DirectProbeObserver` に知らせる（テストと計測用。既定はオフ）。

## 確認したこと

- Unity 2022.3.22f1 の EditMode で `DirectProbeTests` を回し、すべて通った。重み1のレイヤーだけのクリップは直接決めて再生の結果と値・メッセージが一致し、顔の描画先を有効にしたAvatarMaskでも直接決めて一致した。重み0.5・同じBlendShapeを書く下のレイヤー・顔の描画先を外したAvatarMaskでは再生した。
- テストアセンブリ全体（4039件）では51件が失敗した。このうち50件は develop で回しても同じく失敗するもの（テスト用プロジェクトにModular Avatar・VRChat SDKなどが無いため）で、残りの1件は `DirectProbeTests` で、上の修正で通った。
- 書き出しの計測に使っている29体（10/19の6体を含む）を、この変更の前（4b34153）と後で2回ずつ書き出した。差分取り397回のうち直接決めたのは1体の8回で、残りは同じBlendShapeを書くほかのレイヤー142回・FXのどこかにあるレイヤーの重みの制御179回・常時適用のレイヤー28回・Parameter Driver 28回・射影6回・保存の対象の不一致6回で再生した。直接決めた1体は1.13秒から0.75秒になった。書き出したVRMは、すべての組で書き出し元の版の文字列（`exporterVersion`）以外が一致した。1体は前後とも同じ理由で書き出しに失敗した。
- この判定の厳しさでは、表情の多いアバターの書き出し時間はほぼ変わらない。重みの制御が顔を書くレイヤーに届かないことを依存の解析で示して判定に入れれば重いアバター3体に効くが、解析の網羅性を確かめる必要があるので、この変更には入れていない。
