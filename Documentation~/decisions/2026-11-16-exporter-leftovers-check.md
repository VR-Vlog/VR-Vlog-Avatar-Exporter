# 外したギミックのReceiver・入れ子のBone Proxy・祖先が動いた部品・ビルドで足されたContactは、今は直さない

- 日付: 2026-11-16
- 対象: `Editor/AvatarContactsExport.cs`・`Editor/AvatarAnimatorParts.cs`（どちらも変えていない）
- 前の記録: `2026-11-16-avatar-contacts-extension.md`・`2026-11-16-fx-part-enabled.md`

## 背景

Contactの表とFXの部品の`enabled`を足したときに、次の4つを「残したこと」にしていた。

1. 書き出しで外したギミックのReceiverも、64個の枠を使う
2. Bone Proxyが入れ子になったとき、取り付け先が見つからないときに、Modular Avatar（MA）と違う場所にContactを置く
3. ビルドが部品の祖先を動かしたときと、部品がアバターの根に付いているときに、FXの曲線がその部品を指さない
4. VRCFuryがビルドで足したContactと、Merge Armatureで動くギミックのContactを、どう書いているか確かめていない

それぞれ、回帰の確認に使うA1〜A11で起きるか、起きたら見た目や動きがどう変わるかを確かめた。

## 確かめ方

書き出しの3か所（エフェクトとContactを読む直前、ビルドのあとにFXを読んだところ、Contactを読み直したところ）に一時のログを入れ、A1〜A11を`Tools/run-export-regression.ps1`で書き出した。ログのコードはコミットしていない。A1〜A11にはModular AvatarのギミックもVRCFuryも入っていないので、Modular Avatarのギミックを入れた検証用のアバター7体でも同じログを取った。

| 見たもの | A1〜A11（11体） | Modular Avatarのギミックを入れた7体 |
|---|---|---|
| Receiverの数（1体の最大） | 2 | 8 |
| 外したギミックのReceiverで、どの層も読まないパラメーターを書くもの（1体の最大） | 0 | 4 |
| Bone Proxy（1体の最大） | 0 | 13 |
| 入れ子のBone Proxy・取り付け先が見つからないBone Proxy | 0 | 0 |
| ビルドで祖先が動いた部品 | 0 | 1体に1つ（そのパスをFXの曲線は動かさない） |
| 根に付いた部品 | 0 | 0 |
| VRCFuryのコンポーネント・ビルドで増えたContact | 0 | 0 |
| Merge Armatureの下のContact | 0 | 0 |

## 決めたこと

4つとも今は直さない。A1〜A11では1つも起きず、Modular Avatarのギミックを入れた7体でも、見た目や動きが変わる形では起きなかった。直すと書き出しの流れに分岐が増えるが、今それで良くなるアバターが無い。

### 1. 外したギミックのReceiverが64個の枠を使う

- 起きる条件: Receiverが65個以上あり、そのうちの一部が書き出しで外したギミックのもの
- 今の振る舞い: `Capture`がビルドの前に、階層の順に64個まで表に入れる。外したギミックのReceiverも数えるので、そのパラメーターを読む層が残っていなくても枠を使い、後ろのReceiverが`contactLimit`で外れる
- 起きたときの違い: 外れたReceiverのギミックがアプリで動かない。外したギミックのReceiverは、読む層が無いのでアプリで動いても何も変わらない
- 結果: 外したギミックの読み手の無いReceiverは、7体のうち5体に1〜4個あった。Receiverは多くて8個で、枠を使い切ったアバターは無い
- 直すときの形: Epicで決めたとおり、枠を数える前に、外したギミックのReceiverで残ったFXのどの層も読まないパラメーターを書くものを表から除く。FXはビルドのあとにしか分からないので、上限で切るのを`Capture`から`Rebind`のあとへ移す。除いた数は`contactLimit`とは別に数える

### 2. 入れ子のBone Proxy・取り付け先が見つからないBone Proxy

MAの`BoneProxyProcessor`は、すべてのBone Proxyのワールドの位置と回転を最初に覚え、取り付け先へ移したあと、親から子の順に、モードが保つ位置・回転を覚えた値に戻す。

- 起きる条件: 外したギミックの中にContactがあり、その上のBone Proxyが次のどちらか
  - 保つモード（AsChildKeepWorldPose・AsChildKeepPosition・AsChildKeepRotation）で、取り付け先が別のBone Proxyの下にある。MAは位置や回転を覚えた値に戻すので、取り付け先が親のBone Proxyと一緒に動いても付いていかない。ここでは取り付け先と一緒に動かす
  - 取り付け先が見つからない。MAは移さずにその場で、保たない位置と回転を0にし、Match Scaleならスケールを1にする。ここでは元の場所に置く
- ビルドで残るContactはビルドのあとに読み直すので、MAが置いた場所になる。違うのは外したギミックのContactだけ
- 起きたときの違い: アプリでContactの当たる場所がずれる。外したギミックのReceiverは読む層が無いことが多く（上の1）、その場合は動きが変わらない
- 結果: 入れ子や取り付け先の無いBone Proxyは、18体のどれにも無かった
- 直すときの形: `Walk`で、Bone Proxyの最終的なワールドの姿勢を「取り付け先の最終的な姿勢」と「MAが保つ値（覚えた位置・回転）」から組む。取り付け先が見つからないときは今の親を取り付け先として同じ式に通す

### 3. ビルドが部品の祖先を動かしたとき・根に付いた部品

- 起きる条件: FXが部品の自分の`enabled`の曲線か、ノードの無い祖先の`activeSelf`の曲線を持ち、さらに次のどちらか
  - ビルド（Bone ProxyやMerge Armature）がエフェクトかContactの祖先を別の場所へ動かした。エフェクトとContactのパスはビルドの前に読むので、ビルドが書き換えたFXの曲線のパスと合わない。PhysBoneはビルドのあとに読むので起きない
  - 部品がアバターの根に付いている。部品は根から下のパスしか持たないので、根の曲線と合わない
- 今の振る舞い: その曲線は今までどおり`enabled`・`other`・`missingNode`で数え、部品の`enabled`の対象は作らない
- 起きたときの違い: アプリで、その部品がメニューで切り替わらず、書き出したときの状態のままになる
- 結果: Bone Proxyで頭へ動いたContactが1体に1つあったが、そのパスを動かす曲線はFXに無かった。根に付いた部品は無かった
- 直すときの形: FXを読むとき、ビルドのあとも残っているエフェクトとContactは今のパスで読み直す。根に付いた部品は空のパスを持たせる

### 4. VRCFuryがビルドで足したContact・Merge Armatureで動くギミックのContact

- 今の振る舞い: Contactはビルドの前に読み、ビルドのあとは読んだものだけを読み直す。VRCFuryがビルドで足したContactは表に入らない。Merge Armatureの下のContactは、ビルドで残ればMerge Armatureが動かしたあとの場所で読み直す。書き出しで外したギミックの中にあれば、Merge Armatureの移動を追わず、ビルドの前の場所から一番近い書き出すノードに付ける
- 起きる条件: VRCFuryでContactを足すアバター。外したギミックの中のMerge Armatureの下にContactがあり、衣装の骨とアバターの骨がずれている
- 起きたときの違い: VRCFuryで足したContactはアプリで動かない。Merge Armatureの場合は、衣装の骨とアバターの骨のずれだけContactの場所がずれる。骨がそろった衣装ではずれない
- 結果: 18体のどれにもVRCFuryのコンポーネントも、Merge Armatureの下のContactも無かった
- 直すときの形: VRCFuryで足したContactは、ビルドのあとに表に無いContactを読み足す。Merge Armatureは、Bone Proxyと同じく`Walk`で取り付け先の骨へたどる

## 古い出力との関係

コードを変えていないので、書き出すファイルは変わらない。

## 残したこと

- A1〜A11にModular AvatarとVRCFuryのギミックが無く、回帰の比較ではこの4つの振る舞いが変わっても分からない。これらを使うアバターを回帰の対象に足すかは、Epicで決める
- 上の4つは、起きるアバターが見つかったときに「直すときの形」で直す
