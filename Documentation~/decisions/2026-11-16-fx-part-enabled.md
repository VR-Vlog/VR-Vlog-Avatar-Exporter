# FXでエフェクト・Contact・PhysBoneを切る曲線を書く

- 日付: 2026-11-16
- 対象: `Editor/AvatarAnimatorParts.cs`（新規）、`Editor/AvatarAnimatorCapture.cs`、`Editor/AvatarEffectsExport.cs`、`Editor/AvatarContactsExport.cs`、`Editor/AvatarPhysBonesExport.cs`、`Editor/UniVrmOneClickExporter.cs`

## 背景

設計書の段4は、FXの`enabled`の`component`に段6で`constraint`・`physBone`・`contact`、段7で`particle`・`trail`・`line`・`light`を足すとしている。今までの書き出しが`enabled`で書いていたのは`renderer`（Mesh RendererとSkinned Mesh Renderer）と`constraint`だけで、ParticleSystem・Trail Renderer・Line Renderer・Light・Contact Sender・Contact Receiver・PhysBoneの`m_Enabled`の曲線は書かずに`omitted`の`enabled`（ParticleSystemの`EmissionModule.enabled`は`other`）で数えていた。

もう1つ、エフェクトやContactだけを持つオブジェクトは書き出しで消える（ギミックの除外、空のノードの自動除外）。そのオブジェクトの`activeSelf`の曲線は指す先が無く、`missingNode`で数えていた。FXでメニューのトグルがエフェクトを出し入れするのは、ほとんどがこの`activeSelf`の曲線なので、これを書かないとFXからエフェクトを切れない。

## 決めたこと

- 部品（エフェクト・Contact・PhysBone）の自分の`enabled`の曲線を、`enabled`の対象で書く。キーは`node`・`property`（`enabled`）・`component`・`index`・`rest`
  - `index`はその部品を書いた拡張の表の番号。`particle`は`particles`、`trail`は`trails`、`line`は`lines`、`light`は`lights`、`physBone`は`physBones`の番号
  - `contact`は`VRVLOG_avatar_contacts`の`senders`を先に数え、続けて`receivers`を数える（Receiverの番号は`senders`の数を足したもの）。設計書は`contact`の`index`の数え方を書いていないので、ここで決めた
  - `node`は表のその行の`node`（無ければ`-1`）。アプリは`index`で部品を引き、`node`は使わない
  - ParticleSystemは`EmissionModule.enabled`の曲線と、ParticleSystem Rendererの`m_Enabled`の曲線の両方を`particle`の対象にする（別の対象）
  - `rest`は書き出したときの値（エフェクトとContactはビルドの前、PhysBoneはビルドの後に読む。どちらも部品が消える前）
- 書き出しで消えたオブジェクト、またはファイルにノードが無いオブジェクトの`activeSelf`の曲線は、その下（子孫を含む）の部品ごとに1つの`enabled`の対象にする。同じ曲線を部品の数だけのトラックで指す。`rest`はそのオブジェクトの書き出したときの`activeSelf`
- 部品が自分の`enabled`の曲線も持つときは、祖先の`activeSelf`から来た対象と別の対象で書く。同じ`component`と`index`に対象が2つ以上できる。アプリは同じ部品を指す`enabled`の対象をすべて掛け合わせ（AND）、全部が1のときだけ部品を動かす。Unityが非アクティブなオブジェクトの下の部品を動かさないのと同じになる
- 表の上限（`effectLimit`・`contactLimit`）で外れた部品、表がノードを見つけられず外した部品（PhysBoneの根が書かれていない、エフェクトの祖先が無い）は指さない。その曲線は今までと同じ種類で数える（自分の曲線は`enabled`か`other`、`activeSelf`の曲線は`missingNode`）
- 新しい対象は`targets`の最後に足し、トラックは各クリップの最後に置いて、クリップのトラックの上限の残りだけを使う。キーの数の上限（`MaximumKeysPerClip`）も、ほかの曲線が使った残りだけを使う。1本の`activeSelf`の曲線を部品の数だけ写すので、写すたびにそのキーの数を数える。入りきらないトラックは`enabled`で数える。これで今までファイルに入っていた曲線は1本も減らない
- 拡張が大きさの上限（4MB）を超えたら、まず部品の`enabled`の対象とトラックを全部やめて（曲線は今までと同じ種類で数える）作り直し、それでも超えるときだけ今までどおりBlendShapeの曲線をやめる。部品の曲線のせいで表情の曲線や拡張が落ちることはない
- 部品の祖先のパスは、Unityが曲線をつなぐのと同じく、アバターの根からそのパスを引いて最初に見つかるオブジェクトがその祖先のときだけ使う。同じ名前の兄弟が前にあるときは、そのパスの曲線はその兄弟を動かすので、この部品を指さない
- 共有の読み手（`AvatarAnimatorData`）の`Components`には足さない。キーは対象の`Fields`に持たせ、読み手は知らない`component`の対象として書いたまま保つ（`Known`が偽）。`Components`に足すと読み手が同じ`component`と`index`の重なりを拒むので、上の2つ目の対象が書けない

## 古い出力との関係

- 部品の表（`VRVLOG_avatar_effects`・`VRVLOG_avatar_contacts`・`VRVLOG_avatar_physbones`）を書かないファイルは変わらない
- 古いアプリは知らない`component`の`enabled`の対象を読み飛ばすので、今と同じに出る
- 今のアプリ（develop）は`particle`・`trail`・`line`・`light`を`index`で読み、対象ごとに順に当てる（最後に当てた値が残る）。同じ部品に2つの対象があるとANDにならないので、アプリ側でANDにする変更が要る。また、アプリは書き出したときに非アクティブだったエフェクトを出さない扱いをしているので、消えた祖先の`activeSelf`から来た対象で出すにはそこも変える必要がある。どちらもアプリの単位で直す

## 採らなかった案

- 消えたオブジェクトの`activeSelf`の曲線を、部品の自分の`enabled`の曲線とまとめて1つの対象にする: Unityのアニメーションカーブのまま書くという段4の決まりに合わない（2本の曲線の積は1本のカーブにならない）。ブレンドツリーと遷移の補間も対象ごとに今の評価器に乗せられる
- 消えたオブジェクトをノードとして残す: ノードを足すとVRMの中身が変わり、今の回帰の比較が全部崩れる。部品の表がすでに部品の置き場所を持っているので、切り替えだけを対象で書けば足りる
- `Components`に新しい`component`を足し、読み手でキーを確かめる: 上のとおり重なりを拒むので2つ目の対象が書けない。アプリはExporterの共有の読み手を固定の版で取り込んでいるので、読み手を変えるとアプリ側も合わせる必要がある

## 確かめたこと

- EditModeテスト（Unity 2022.3、batchmode、VRChat SDK入りの使い捨ての複製）: `AvatarAnimatorCaptureTests`の`PartEnabledCurvesAreTargetsOfTheirTableEntry`と`ActiveCurvesOfObjectsWithoutANodeTurnThePartsUnderThem`（3ケース）、`CurvesOnASharedPathTurnTheFirstSiblingsParts`、`PartTracksStayWithinTheKeysOfAClip`、`ExportWritesPartEnabledTargetsOfTheEffectsTable`（書き出し全体を通す）、`AvatarEffectsExportTests.IndexOfIsTheEntryInItsOwnTable`、`AvatarContactsExportTests.IndexOfCountsTheSendersThenTheReceivers`、`AvatarPhysBonesExportTests.CaptureReadsTheSettingsAndResolveBindsTheRoots`
- 回帰の比較: PRの本文に書く

## 残したこと

- アプリが同じ部品の`enabled`の対象をANDで当てる。書き出したときに非アクティブだったエフェクトを、`enabled`の対象が1にしたら出す
- ビルドが部品の祖先を別の場所へ動かしたとき、FXの曲線のパスと部品の読んだときのパスが合わず、置き換えられない。そのときの曲線は今までどおり`missingNode`で数える
- アバターの根に付いた部品は、祖先のパスを持たないので指さない。根の部品の自分の`enabled`の曲線は今までどおり`enabled`で数える
- FXはエフェクトとContactの表より先に書く。表を書くときに例外が出て表が落ちると、FXに残った`particle`・`contact`などの対象の`index`は指す先が無い。ConstraintとPhysBoneのように表が落ちたらFXを作り直す形にはしていないので、アプリが表に無い`index`を読み飛ばすことを確かめるか、Exporterで作り直す
