# チェックを外した PhysBone のボーンを、Ignore Other PhysBones がオンの上の PhysBone に渡さない

- 日付: 2026-11-07
- 対象: `Editor/PhysBoneSpringExport.cs`・`Tests/Editor/PhysBoneSpringExportTests.cs`

## 背景

胸の揺れを止めようとして PhysBone のチェックを外してから書き出すと、衣装の胸の辺りのポリゴンが崩れるという報告があった。VRChat では崩れない。胸の PhysBone を有効に戻して Pull 1.0・Spring 0.0 にすると崩れない。

報告の元の構成（アバターと衣装）は手元に無い。書き出しの中で PhysBone の有効・無効で結果が変わるのは `PhysBoneSpringExport` だけで、ここに VRChat の SDK とのずれが 1 つあった。これを報告の原因と見て直し、同じ症状を作った構成で確かめた。

`PhysBoneSpringExport` は、チェックを外した PhysBone と非表示の PhysBone を揺れ物にしない。ここは前から正しい。ずれていたのは Ignore Other PhysBones の扱いだった。オンの PhysBone は、自分の Root から先にある他の PhysBone の Root で連鎖を止める。止める先の一覧を有効で表示中の PhysBone からだけ作っていたので、チェックを外すとその PhysBone の Root が一覧から抜けた。すると上の階層の PhysBone（体幹や衣装の胸元に付けたもの）がそのボーンまで連鎖を伸ばし、自分の柔らかい設定で揺らしていた。

VRChat の SDK（`VRCPhysBoneBase.InitTransforms`）は、アバターの PhysBone を非表示のものも含めて集め、有効・無効・表示を問わず、自分以外の PhysBone の Root をすべて止める先に入れる。そのため VRChat では、チェックを外した胸のボーンは上の PhysBone の設定では揺れず、親のボーンの動きに付いて動くだけになる。Pull 1.0・Spring 0.0 で回避できたのは、胸の PhysBone が有効になって自分のボーンを持ったから。

## 決定

- Ignore Other PhysBones で止める先の一覧を、全 PhysBone（有効・無効・非表示を問わない）の Root から作る。Root の表示状態も見ない。VRChat の SDK と同じ作りにした。
- 揺れ物にするのは前と同じく有効で表示中の PhysBone だけ。チェックを外した PhysBone は今までどおり警告に出す。
- Ignore Other PhysBones がオフの PhysBone は、前と同じく内側のボーンも自分の設定で動かす。VRChat でも動くので変えない。
- 書き出しの途中で Modular Avatar・NDMF・AAO が消した PhysBone は一覧に入らない。消えた後の構成をアップロードする VRChat と同じになる。

## 確かめたこと

`AnInactiveNestedPhysBoneStaysOutOfAParentThatIgnoresOthers` を足した。親の PhysBone の下に別の PhysBone を置き、そのチェックを外したときと、置き場の GameObject を非表示にしたときを、親の Ignore Other PhysBones のオン・オフそれぞれで見る（4 ケース）。親の Ignore Other PhysBones がオンなら、子のボーンは親の連鎖に入らない。オフなら入る。修正前のコードで回すと、オンの 2 ケースが失敗した。

実際のアバターで、修正の前後に同じ構成を書き出して比べた。

- 胸の PhysBone を有効・無効・Pull 1.0 と Spring 0.0 にした 3 状態を、素体だけのアバター 3 体と、素体に衣装を着せた組み合わせ 2 組で書き出した。AAO ありの組み合わせも含めた。
- 揺れ物の多い改変済みのアバター 4 体（1 体は髪と尻尾の PhysBone を無効・Pull 1.0 と Spring 0.0 にした状態も）、PhysBone を持つ衣装を着せた組み合わせ 2 組も書き出した。
- 書き出した実アセットの PhysBone 1,989 件には、「Ignore Other PhysBones がオンで、内側に別の PhysBone がある」ものが無かった。そこで素体 1 体と素体と衣装の組み合わせ 1 組に、Chest を Root にした柔らかい PhysBone（Ignore Other PhysBones がオン）を足した構成を作り、報告の状態を再現した。

結果:

- 静止形状: 修正前の書き出しがある 34 組すべてで、全メッシュの頂点の差が 0.000mm だった。
- 揺れ物: 34 組のうち 32 組は、連鎖と設定が修正前と同じだった（Modular Avatar がボーン名に付ける GUID の違いを除いて比べた）。違ったのは、作った構成で胸の PhysBone のチェックを外した 2 組だけだった。
- 素体の構成では、足した PhysBone が胸のボーンを取り込まなくなった。1 秒の上下運動（8cm・1.5Hz）を SpringBone で回したときの胸のメッシュの最大の動きは、修正前の 24.9mm から 0.0mm になった。足した PhysBone が無い構成でチェックを外したときと同じ値になる。Pull 1.0・Spring 0.0 にした状態は修正前も後も 2.5mm だった。
- 素体と衣装の構成では、胸のボーンは足した PhysBone から外れた。胸の親のボーンは、胸の PhysBone の置き場の GameObject を末端にして、足した PhysBone に揺らされる。これは VRChat と同じで、胸の PhysBone を有効にした状態でも修正前から同じ連鎖になっている。修正後のチェックを外した状態は、Pull 1.0・Spring 0.0 の状態から胸の PhysBone の連鎖を除いたものと同じになった。

## 残したもの

VRChat の SDK は、連鎖をたどるときに非表示の子 Transform も含める。Exporter は表示中の子だけをたどる。非表示の GameObject をメッシュのボーンに使っているときだけ揺れ方が変わる。そうした作りはまれで、今回の報告とは別の作りなので、この変更では変えていない。
