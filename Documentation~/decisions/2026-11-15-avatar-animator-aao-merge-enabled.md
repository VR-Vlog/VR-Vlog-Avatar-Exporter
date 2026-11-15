# AAO の Merge Skinned Mesh が有効化アニメーションを写したとき、まとめた Renderer の enabled の rest と表示切り替えの引き継ぎを VRChat に合わせる

- 日付: 2026-11-15
- 対象: `Editor/ObjectToggleCapture.cs`、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Tests/Editor/ObjectToggleCaptureTests.cs`
- Issue: VR-Vlog/VR-Vlog#3665（Epic VR-Vlog/VR-Vlog#3658 の段4-3a）

## 背景

段4-3（`2026-11-15-avatar-animator-renderer-material.md`）で、FX が Renderer の `m_Enabled` を動かすカーブを `enabled` のターゲットとして書くようになった。AAO（Avatar Optimizer）1.8 の Merge Skinned Mesh で「Copy Enablement Animation」（`copyEnablementAnimation`）をオンにすると、AAO はまとめる元のメッシュから共通の親までの道のりにある有効化のアニメーション（元の Renderer の `m_Enabled` か、途中のオブジェクトの `m_IsActive`。1 か所だけ許される）を、まとめた Renderer の `m_Enabled` に写す。そのときまとめた Renderer の `enabled` を、その場所の値（オブジェクトなら `activeSelf`）にする。

書き出しでは、AAO が走る前に ObjectToggleCapture が既定でオフの表示切り替えの対象をオンにしている（メッシュを書くため）。このため AAO が見る値はオンで、まとめた Renderer の `enabled` は 1 になる。VRChat のビルドでは同じ Renderer は 0 で、アプリでは既定でオフのはずのメッシュが出ていた。AAO がまとめて消したオブジェクトは、表示切り替えから「書き出しで無くなったオブジェクト」の警告つきで外れていた。FX の `enabled` のターゲットがそのメッシュを切り替えるので、この警告は誤りだった。

検証用のプロジェクトで AAO 1.8.16 を使って確かめた。AAO の `Utils.CommonRoot` は 2 番目の Transform を読み飛ばす。元のメッシュが 1 つのまとめでは共通の親が元のメッシュ自身になり、有効化のアニメーションは何も写らない。元が 2 つ以上なら写る。

## 決定

- ObjectToggleCapture は、対象をオンにしたあと、`copyEnablementAnimation` がオンの Merge Skinned Mesh を探す。元のメッシュ（`renderersSet` と `staticRenderersSet`）から共通の親（元とまとめ先のすべてを含む一番深い Transform。親自身は含まない）までの道のりに表示切り替えの対象があるかを見る。対象は FX が動かすものなので、あれば AAO が写す 1 か所はその対象になる
  - そのうち 1 つでもさっきオンにしたものなら、まとめた Renderer の `enabled` の rest を 0 にする（`CopiedEnablementRest`）。AvatarAnimatorCapture は、この Renderer の `enabled` のターゲットの rest を、書き出し時の `enabled` でなくこの値にする
  - 道のりにある対象が書き出しで無くなったときは、FX がまとめた Renderer を切り替える（AvatarAnimatorCapture がその Renderer の `enabled` のカーブを読んだ）ときに限り、警告を出さずに FX に任せる。AAO 1.8 が元 1 つで何も写さないときは、今までどおり警告を出す。AAO の読み飛ばしを真似ずに FX の結果を見るので、AAO 側が直っても合う
- Optimizing を走らせない書き出し（プレビューの複製など）では、この値を使わない
- `copyEnablementAnimation` がオフのまとめと、AAO を使わないアバターの出力は変えない

検証用のプロジェクトで、5 通りのアバター（元 2 つで既定オフ・既定オン、写さないまとめ、静的メッシュのまとめ、親が切り替わる静的メッシュのまとめ）を、変える前と後で書き出し、`Tools/compare-exports.py --diff-paths` で比べた。変わったのは既定オフの 1 通りの `/extensions/VRVLOG_avatar_animator/targets/0/rest`（1 から 0）だけで、ほかの 4 通りは同じだった。既定オフと既定オンでは、無くなったオブジェクトの警告も出なくなった。

## 書き出し時にオフの Renderer（段4-3 から持ち越した P2）

書き出し時にオフ（またはオフのオブジェクトの下）で、FX がオンにする Renderer は、ファイルにメッシュが無いので `enabled` のカーブを書かず、`omitted` の `enabled` に数えている。この段では変えない。書くには、そのメッシュを書き出して rest 0 で隠す必要がある。表示切り替えの対象と同じやり方だが、出力のメッシュが増え、今までのアバターの出力がすべて変わる。copyEnablementAnimation とは別の話なので、要るなら別の段で、増えるメッシュとファイルサイズを測ってから決める。
