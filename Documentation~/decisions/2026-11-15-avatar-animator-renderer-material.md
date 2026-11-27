# 拡張に、レンダラーのenabledとマテリアルのfloatの対象を書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/AvatarAnimatorCrossCheck.cs`、`Editor/UniVrmOneClickExporter.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Formats/README.md`、`Tests/Editor/AvatarAnimator*Tests.cs`

## 背景

FXのクリップには、メッシュのレンダラーの`m_Enabled`と、マテリアルの値（`material._Cutoff`のような曲線）を動かすものがよくある。今までの拡張（`VRVLOG_avatar_animator`）は、`activeSelf`・BlendShapeの重み・FXのfloatのパラメーターだけを書き、ほかの曲線は`omitted`に`enabled`・`material`として数えていた。アプリで同じ見た目を出すには、この2つを対象として書く必要がある。形はアプリの判断記録（2026-11-12 VRChatアバターの再現、節「段4〜7のデータの形と当て方」）に従う。

## 決めたこと

対象の種類に`enabled`と`materialFloat`を足した。`schemaVersion`は1のまま。

- `enabled`: キーは`node`・`property`・`component`・`rest`。`component`は今は`renderer`（ノードが描くメッシュのレンダラー）だけで、`rest`は0か1。重複は(`node`, `component`)で見る
- `materialFloat`: キーは`property`・`material`（glTFの`materials`の番号）・`name`（シェーダーのプロパティ名）・`rest`。ノードを持たない。重複は(`material`, `name`)で見る
- `component`が文字列で`renderer`以外の`enabled`は、4-1で足した「知らない種類の対象」として読む。`component`が無い・文字列でない`enabled`は壊れたものとして拡張ごと断る
- `rest`は書き出す前のアバターが持つ値（レンダラーの`enabled`、マテリアルの値）
- 書き出しはlilToonのマテリアルをMToonの代わりのマテリアルに差し替えてからglTFにする。マテリアルがそのfloatを持つかと`rest`は、曲線を読んだとき（差し替えの前）のスロットのマテリアルで見て、番号だけを書き出しのときにそのスロットにあるマテリアルから引く。代わりのマテリアルで見ると、lilToonにしかないプロパティ（`_AlphaMaskValue`など）がすべて落ち、`rest`も代わりのマテリアルの値になる
- ファイルを書くときに無効なレンダラーや、非アクティブなオブジェクトの下のレンダラーは、glTFのメッシュにならない（UniVRMの`ModelExporter`）。その`enabled`の対象は書かず、曲線を`enabled`に数える
- Unityはレンダラーの`material.X`の曲線を、そのレンダラーのすべてのマテリアルに当てる（MaterialPropertyBlock）。そこで1本の曲線を、レンダラーが描くマテリアルのうち、ファイルに書かれていてそのfloatを持つものの対象ごとのトラックに展開する。マテリアルを共有するレンダラーは対象も共有する
- マテリアルの番号は、書き出しの`afterExport`で`model.Materials`（glTFの`materials`と同じ順）の位置から引く。`Resolve`にマテリアルを渡さないときは、`material`として数える
- 1つのクリップの中で、2本目以降の曲線が同じ対象に当たったら、その曲線は捨てて`material`に数える（例: 同じマテリアルを使う2つのレンダラーを同じクリップが動かす）
- 加算レイヤーのクリップが動かす`materialFloat`の対象は、基準のポーズを持たないので、その対象のトラックごと捨てて`material`に数える（BlendShapeと同じ扱い）
- 色・ベクトルの1チャンネル（`material._Color.r`）は今までどおり`material`に数える。パーティクル・トレイル・ラインのレンダラーの`m_Enabled`は、glTFのメッシュにならないので今までどおり`enabled`に数える
- クリップのトラックの枠は、`activeSelf`→パラメーター→BlendShape→`enabled`→マテリアルの順に配る。新しい種類は表の末尾に置き、今までの種類の番号と順を変えない
- 1本のマテリアルの曲線は、読むときはトラックの枠を1つと数える。展開したトラックでクリップのトラックが4096を超えるときは、その曲線をトラックごと捨てて`material`に数える
- `Inject`は、`materialFloat`の`material`がglTFの`materials`の数以上なら断る
- 突き合わせ（`AvatarAnimatorCrossCheck`）は、`enabled`をレンダラーの`enabled`と、`materialFloat`をレンダラーのMaterialPropertyBlockの値（無ければマテリアルの値）と比べる。floatの許容差は大きさあたり`1e-4`

## 採らなかった案

- マテリアルのプロパティ名を契約（`LilToonFullContract`）で絞る: 単位4-4で、名前の検査と`materialProperty`・`materialSwitch`を足すときに入れる
- レンダラーごとに対象を分ける: glTFではマテリアルは番号で共有され、アプリもマテリアル単位で値を持つ。共有マテリアルの他のレンダラーにも効く差は、正本の節のとおり受け入れる
- 同じクリップで同じ対象に当たる曲線を平均する・後の値を採る: どちらもUnityの見え方（レンダラーごとに違う値）とずれる。数えて残す方が、何が落ちたかを追える

## 今の出力への影響

`materialFloat`と`enabled`は、拡張の`targets`と`clips`の末尾に足すだけで、今までの対象の番号・トラックの順は変わらない。その分`omitted`の`enabled`・`material`の数が減る。拡張の外（メッシュ・マテリアル・テクスチャ）は変わらない。アプリは4-0（知らない`property`の対象を書かない対象として読む）が入っているので、この2つを知らないアプリでも、今までの種類は今までどおり動く。

## 確かめたこと

- Unity 2022.3.22f1のEditModeで、`AvatarAnimatorDataTests`・`AvatarAnimatorCaptureTests`・`AvatarAnimatorCrossCheckTests`を回した。足したテストは、往復と拒否、知らない`component`の保持、Captureでの書き出し・共有マテリアル・加算レイヤー・パーティクル、実際の書き出しでglTFのマテリアル番号に結び付くこと、突き合わせでUnityの`enabled`とマテリアルの値が評価器と一致し、違うデータでは食い違いとして出ること
- `Tools/run-avatar-animator-evaluator-tests.ps1`で、評価器の単体の確認109件と69個の記録の再生が通った

## 残したこと

- 書き出しのときに無効なレンダラーは、glTFのメッシュにならない。FXがそれを有効にしても、アプリでは出せない（`enabled`に数える）。オブジェクトのトグルと同じく書き出す前にオンにして`rest` 0で隠す形は、メッシュが増えて今の出力が変わるので、この単位では入れない
- 共有マテリアルの対象は、突き合わせで最初のレンダラーだけを見る
- プロパティ名の検査・`materialProperty`・`materialSwitch`は単位4-4、`materialSlot`は4-5
