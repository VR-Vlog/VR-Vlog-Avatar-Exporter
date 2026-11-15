# VRVLOG_avatar_animator に Humanoid の muscle、Gesture と Action のコントローラー、レイヤーのマスクを書く

- 日付: 2026-11-15
- 対象: `Formats/Runtime/AvatarAnimatorData.cs`、`Editor/AvatarAnimatorCapture.cs`、`Editor/AvatarAnimatorCrossCheck.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Tests/Editor/AvatarAnimatorDataTests.cs`、`Tests/Editor/AvatarAnimatorCaptureTests.cs`
- Issue: VR-Vlog/VR-Vlog#3666（Epic VR-Vlog/VR-Vlog#3658 の段5-1）

## 背景

段4 までの `VRVLOG_avatar_animator` は FX のコントローラーだけを書き、Animator がアバターのルートで動かす Humanoid のカーブは `omitted` の `muscle` に数えていた。手の形（Gesture）や体の動き（Action）はアプリで再生できなかった。設計（アプリのリポジトリの `Docs/decisions/2026-11-12-vrchat-avatar-reproduction.md` の段5）では、段5-1 で書き出しの形を決め、評価器が Gesture と Action を回すのは段5-2、muscle の突き合わせは段5-3、マスクを当てるのは段5-7 で行う。

## 決定

形の版（`schemaVersion`）は 1 のまま変えない。今までのファイルは新しい読み手でもそのまま読める。

- ターゲットに `muscle` を足す。`{"property": "muscle", "muscle": i, "rest": 0}` で、`i` は Unity の `HumanTrait.MuscleName` の番号（0〜94）。ノードは持たない。クリップは指の muscle を `LeftHand.Thumb.1 Stretched` のように書き、`HumanTrait` は `Left Thumb 1 Stretched` と書くので、読み替えて番号にする。ターゲットは今までの種類のあとに番号の順で並べ、加算でないレイヤーのクリップが動かすものだけを書く
- ルートの位置と回転（`RootT`・`RootQ`・`MotionT`・`MotionQ`）と手足の IK（`LeftFootT` など）は、アプリのトラッキングと歩きが腰の位置を決めるので書かず、新しい `omitted` の種類 `humanoidRoot` に数える。加算のレイヤーの muscle（Unity はポーズに足すが、float を混ぜる再生ではそうならない）、`LeftFootTDOF.x` のような移動の自由度、クリップの枠からあふれたものは今までどおり `muscle` に数える
- `controllers` に Gesture と Action を足す。順は FX、Gesture、Action で、それぞれ 1 つまで。読み手はこの順を確かめる。表情（`expressions`）が名前で指すレイヤーは今までどおり FX のもの。記述子で既定のまま（`isDefault`）の Gesture と Action、Base・Additive は読まない。拡張を書くかどうかは今までどおりカスタムの FX があるかで決める
- パラメーターはコントローラーごとに宣言を読み、オーバーライドもコントローラーごとに当てる。データのパラメーターは名前ごとに 1 つ（VRChat でも値は 1 つ）なので、同じ名前を後のコントローラーが別の型で宣言しても、型と既定値は最初に読んだコントローラー（FX）のものにする。後のコントローラーでの使い道は、そのコントローラー自身の型がデータの型と同じときだけパラメーターとして読み、違うときはクリップのカーブを `omitted` の `parameter` に数える
- モーションとクリップの番号、読まなかったブレンドツリーの記録もコントローラーごとに持つ。同じブレンドツリーやクリップでも、コントローラーごとにオーバーライドとパラメーターが違えば中身が変わるので、2 つのコントローラーが共有するモーションは 2 回書く
- レイヤーに `mask` を足す。`{"humanoid": [体の部位], "nodes": [ノード]}` で、`humanoid` は動かしてよい `AvatarMaskBodyPart`（0〜12）、`nodes` は Transform のマスクが動かしてよいノード。どちらも昇順。Transform のパスは、書き出しの途中で名前や親が変わることがあるので、読んだときにアバターのオブジェクトへ解決しておき、出力のノードが決まってからノードにする。Transform を指さないマスクは `nodes` を持たず、全ノードを動かしてよい。ファイルにノードの無い Transform は書かない。マスクの無いレイヤーと、全部位を通して Transform を指さないマスクは `mask` を書かない（今までのアバターの出力を変えないため）
- 突き合わせ（`AvatarAnimatorCrossCheck`）は muscle を比べない。ポーズを通して比べるのは段5-3
- 評価器はまだ `Controllers[0]`（FX）だけを回す。ただ `AvatarAnimatorEvaluator.Supports` はすべてのコントローラーのレイヤーとモーションを見るので、Gesture や Action に加算のレイヤーなど評価器が扱わないものがあると、今まで回せていたアバターでも断るようになる。設計の「加算は Supports で断る」に沿っている

## この段で扱わないこと

- VRChat は、FX のレイヤー 0 にマスクが無いと FX の muscle を当てない（`PoseMenuResolver` が見ている決まり）。この段では FX の muscle のカーブもそのまま書く。当てるかどうかは段5-2・段5-7 で評価器とマスクを入れるときに決める
- 記述子のプレイアブルレイヤーに付いたマスク（`CustomAnimLayer.mask`）は書かない
- Gesture や Action だけがカスタムで FX が既定のアバターは、今までどおり拡張を書かない
- VRChat の Action のプレイアブルは既定で重みが 0 で、Playable Layer Control で上がる。この段ではコントローラーをそのまま書くだけで、重みを扱うのは段5-2 の評価器
- 記述子が FX と同じコントローラーを Action にも置くアバターでは、同じ中身のコントローラーが 2 つ書かれる。VRChat も両方で再生するので、そのまま書く

## 確かめたこと

- EditMode テストに、muscle のターゲット（体と指の名前の読み替え・ルートと IK は `humanoidRoot`・加算と `TDOF` は `muscle`）、Gesture と Action（オーバーライド・同じ名前のパラメーター・既定のプレイアブルを読まないこと）、マスク（ノードへの解決・全部位を通すマスクは書かない）、読み手が壊れた形を断ることを足した。capture のテストの fixture は、今まで記述子のすべてのプレイアブルに FX を置いていたので、FX だけをカスタムにした。マスクが出力を変えないことを確かめていたテストは、マスクを書き、それ以外は変えないことを確かめる形にした
- 検証用のアバター 4 体を変える前と後で書き出し、`Tools/compare-exports.py` で比べた。カスタムの Gesture も Action も持たない 2 体はファイルが同じだった。カスタムの Gesture を持つ 1 体は Gesture のコントローラーと指の muscle 40 個のターゲット、FX と Gesture の手のレイヤーのマスクが増えた。Action に FX と同じコントローラーを置く 1 体は Action のコントローラーと手のレイヤーのマスクが増えた。どちらも `expressions` と警告の数は変わらなかった
