# ジェスチャー表情の Clip にある手のポーズの曲線で、表情全体を捨てない

- 日付: 2026-10-22
- 対象: `Editor/VrChatGestureExpressions.cs`（`ReadPose`）・`Editor/PoseSampling.cs`・`Tests/Editor/VrChatMenuExpressionTests.cs`

## 背景

ジェスチャーの表情 Clip に、顔の BlendShape と一緒に指のポーズ（Humanoid の筋肉の曲線。`LeftHand.Index.1 Stretched` など）が入っていることがある。手の形と表情を 1 つの Clip で切り替える作り方で、VRChat のアバターでは珍しくない。

この Clip を `ReadPose` が「BlendShape以外の変化を含む表情アニメーションです」として丸ごと拒否していた。無料アバターの一部では、ジェスチャーの表情がすべてこの形なので、喜怒哀楽が 1 つも取り込めなかった。

## 原因

同じ曲線を、処理によって受け付けたり拒否したりしていた。

- FX の評価（`VrChatExpressionSampler`）は、Controller のパラメーター名ではない Animator の曲線を「Humanoid の筋肉か Root の動きで、BlendShape を動かさない」として通していた。
- 顔の形状の評価が要るかを決める `FacialProjectionScope.NeedsProjection` も、Animator の曲線を対象外にしている。そのため BlendShape と筋肉の曲線だけの Clip では評価計画が作られず、`ReadPose` の「BlendShape 以外は拒否」にそのまま当たっていた。

シェーダー・ボーン構成・表情の組み方に関係なく、Humanoid のアバターでジェスチャー Clip に手のポーズを入れていれば起きる。

## 決定

`ReadPose` で、Unity の Humanoid の曲線を BlendShape を動かさないものとして読み飛ばす。

- 何を Humanoid の曲線とみなすかは、名前の推測ではなく Unity の定義から作る。`HumanTrait.MuscleName` の全筋肉（Clip での指の表記 `LeftHand.Index.1 Stretched` と元の表記の両方）、`RootT`・`RootQ`・`MotionT`・`MotionQ`、手足の IK（`LeftHandT` など）。Animator 型・パスが空・参照曲線でないものに限る。
- 値の検査（有限か）は、これまでどおり `SelectedExpressionAppearance.ValidateClipData` が全曲線に対して行う。

## 入れなかったもの

- **Animator のパラメーターの曲線**（ジェスチャー中に瞬きを止めるフラグなど）。ほかのレイヤーを動かしうるので、今までどおり拒否する。BlendShape だけを取り込むと「この表情では瞬きしない」という意味が落ちる。
- **BlendTree の子 Clip**（`AnimatedGestureTree`）。こちらは Animator 上で実際に合成して確かめるので、筋肉の曲線を通すと評価の範囲が変わる。今回の不具合の範囲外なので、BlendShape だけの子 Clip という条件はそのままにした。
- **Transform など、Animator 以外の曲線**。顔の位置を動かしうるので、これまでどおり拒否する。
- 特定のアバター・Clip 名・BlendShape 名に合わせた分岐、しきい値の調整。

## 確認したこと

- EditMode テスト（表情・ジェスチャー・顔の形状評価まわりの 259 件）がすべて通った。追加したテストは、指・脚・Root の曲線を読み飛ばすこと、Animator のパラメーターの曲線は引き続き拒否すること、Humanoid の曲線の判定が Unity の定義どおりであることを確かめる。
- 無料アバター 40 体を、修正前と修正後の同じ環境で書き出して比べた。原因を調べるときに警告を読んだ 7 体と、それ以外の 33 体に分けて数えた。
  - 7 体のうち 2 体で、拒否されていたジェスチャー表情が取り込まれるようになった（追加の表情が 8 個と 14 個、うち 1 体は喜怒哀楽が 1 種増えた）。
  - 33 体は、喜怒哀楽の数・追加の表情の数とも修正前と同じだった。
  - 拒否が消えた残りの 1 体は、FX 以外のレイヤーでも同じ BlendShape を動かしているため、別の既存の制限で取り込まれない。これは今回の変更の範囲外。
