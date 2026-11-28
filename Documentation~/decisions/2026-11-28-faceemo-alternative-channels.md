# FaceEmo のほかの表情が動かす BlendShape を、選んだ表情でも共通の顔の値で書く

- 日付: 2026-11-28
- 対象: `Editor/FaceEmoExpressions.cs`（`ApplyPreparedDefaultFace`・`AlternativeChannels`・`BindingSnapshot.ApplyDeferredOverrides`）・`Editor/VrChatExpressionSampler.cs`（`ApplyPermanentOverrides` の `alternativeMorphs`）・`Editor/Locales/*.json`・`Tests/Editor/FaceEmoPreparedFxIntegrationTests.cs`・`Tools/run-unity-compatibility.py`
- 関連: `2026-10-29-clip-zero-resets-default-face.md`・`2026-10-25-restore-fx-rest-face.md`

## 背景

FaceEmo で「両手 = Neutral」の分岐に表情を割り当てたアバターを書き出すと、アプリでほかの FaceEmo の表情の口が崩れた。報告されたアバターでは、両手 Neutral の分岐が `mouth_smile` 100・`mouth_smile2` 30.3 の笑い口を入れ、ほかの分岐は `mouth_e`・`mouth_□2` などの口だけを持っていた。アプリでは笑い口の上にほかの口が重なり、大きく開いた口になった。眉や目は崩れなかった。

VRChat では、FaceEmo の分岐は FACE EMOTE PLAYER レイヤーの代わり合うステートで、その下に共通の DEFAULT FACE レイヤーがある。DEFAULT FACE はレンダラーの値（このアバターでは `mouth_smile` 0）を書く。選んだ分岐がキーを持たない BlendShape は DEFAULT FACE の値になるので、ほかの分岐では笑い口は消える。

## 原因

- 書き出しは FX の既定の状態を基準形に焼き込む（`FxRest`）。両手 Neutral の分岐は通常の入力で再生されるので、VRM の表情なしの顔に `mouth_smile` 100・`mouth_smile2` 30.3 が入る。ここは VRChat の普段の顔と一致している。
- FaceEmo の表情は、PLAYER のステートをネイティブに評価する probe（`VrChatExpressionSampler.ApplyPermanentOverrides`）で値を決める。読み戻す BlendShape は「表情のクリップがキーを持つもの」と「静止レイヤーのもの」だけだった。
- FaceEmo 1.7 の DEFAULT FACE は `DEFAULT ⇄ BYPASS` を `FaceEmo_CN_BYPASS` で行き来するので、静止レイヤーにならない（`ExpressionDependencies.StationaryCandidates` は既定のステートに遷移があると外す）。probe の中で `mouth_smile` は 0 になっていたが、読み戻されなかった。
- 読み戻されない BlendShape は表情の bind に入らず、VRM では基準形の値（笑い口）のまま残った。
- 書き出しの経路は `deferPermanentOverrides: true` なので、DEFAULT FACE のクリップの値を合成する `ComposeCommonValues` も通らない。
- 眉・目が崩れないのは、両手 Neutral の分岐が眉・目にキーを持たず、基準形の眉・目が DEFAULT FACE の値（レンダラーの値）と同じだから。崩れるのは「普段のステートがキーを持ち、選んだ分岐がキーを持たない BlendShape」だけで、このアバターでは `mouth_smile` と `mouth_smile2` だった。
- テストのフィクスチャは DEFAULT FACE を遷移のない 1 ステートで作っていたので、静止レイヤーとして読み戻され、この不具合が出なかった。

## 決定

- FaceEmo の表情の probe では、次の BlendShape も読み戻す対象（`intendedMorphs`）に入れる（`FaceEmoExpressions.AlternativeChannels`）。
  - DEFAULT FACE のクリップが書き、かつ PLAYER レイヤーのどれかのステートの（Override Controller で差し替えた後の）クリップがキーを持つ。
  - 選んだ表情のクリップ自身がキーを持たない。
  - まばたき・リップシンクの自動チャンネル（`NeutralShapeSampler.AutomaticChannels`）ではない。まばたきやリップシンクのレイヤーが時間で動かすので、表情の値として固定しない。
  - 除外したパスにない、表示されている書き出し対象のレンダラーの、メッシュにある BlendShape。
- 値は DEFAULT FACE のクリップから写さず、probe のネイティブな評価の結果を使う。DEFAULT FACE の重みや、PLAYER より上のレイヤーが保持する値も、VRChat と同じに反映される。
- これは読み戻す範囲を足すだけなので、足した probe が拒否されたら（`InvalidOperationException`）、表情を probe 前の状態に戻し、これまでどおりクリップの BlendShape だけで評価し直して、表情に警告を 1 行付ける。今より悪くはしない。
- 表情ボードの収録（`CompletedFacePlayer`）は顔全体をネイティブに再生しているので変えない。FaceEmo 以外の表情（メニュー・ジェスチャー）も変えない。

## 採らなかったもの

- DEFAULT FACE のクリップの値を、無い BlendShape にそのまま足す（`ComposeCommonValues` を書き出しでも使う）。DEFAULT FACE の重みが 1 で、ほかのレイヤーがその BlendShape を書かないときしか正しくない。FaceEmo の口変形キャンセラーなど上のレイヤーが同じ BlendShape を持つので、条件を正しく判定するには結局ネイティブな評価が要る。
- DEFAULT FACE の BlendShape をすべて足す。まばたきなど時間で動く BlendShape が入り、probe が表情ごと拒否するおそれがある。PLAYER のどのステートもキーを持たない BlendShape は、どの分岐でも基準形と同じ値なので、足す必要もない。
- 静止レイヤーの判定を「通常の入力で遷移しないレイヤー」まで広げる。メニュー・ジェスチャーの経路と多くの既存テストに及ぶ。
- 両手 Neutral の分岐を基準形に入れない。VRChat の普段の顔がその分岐なので、アプリの普段の顔が変わる。

## 確かめたこと

- 報告されたアバター（FaceEmo 1.7.0、9 分岐）を、2.0.0 にこの変更を当てて書き出した。`kawaii2`・`kawaii11`・`kawaii5`〜`kawaii9` の 7 表情すべてに、笑い口を打ち消す残差（782 頂点）が入った。打ち消しの形は、`mouth_smile`・`mouth_smile2` を 0 にするメニューの表情（`mouth_ a`）の残差と頂点単位で一致し、`mouth_smile2` を 27 に下げる `kawaii2` は期待値と 1e-9 以内で一致した。プリセットの表情と FaceEmo 以外の表情の bind は変わらなかった。警告は出なかった。
- `kawaii54`・`kawaiine` には、涙のメッシュの動かない残差（全頂点 0）が 1 つ足された。見た目は変わらない。

## テスト

- `FaceEmoPreparedFxIntegrationTests.RegisteredBranchReadsTheCommonValueOfAChannelOnlyAnotherStateKeys` を足した。フィクスチャの DEFAULT FACE に FaceEmo と同じ BYPASS ステートを足し（`bypass: true`）、モードの既定のステートだけがキーを持つ `Open` が、登録した分岐の表情でネイティブの値（共通の顔の値。重み 0.5 なら下の値と混ざった値）になり、モードの既定の 60 にならないことを、Write Defaults のオン・オフの両方で確かめる。変更前は `Open` が値に入らず失敗する。
