# PhysBoneのImmobile（All Motion）を揺れの強さと減衰に反映し、Gravity Falloff・骨の長さ・Advancedは今の式のままにする

- 日付: 2026-10-20
- 対象: `Editor/PhysBoneSpringExport.cs`・`Editor/Locales/ExporterLocale_*.json`・`Tests/Editor/PhysBoneSpringExportTests.cs`・`Tools/run-unity-compatibility.py`・`Compatibility/PhysBone.md`

## 背景

PhysBoneからVRMのSpringBoneへの変換は、stiffness = 4p、dragForce = 0.6(1-s)、gravityPower = |g|(1-f)（PhysBone 1.1では4pを掛ける）で近似している（pはPull、sはSpring / Momentum、gはGravity、fはGravity Falloff）。Immobileは変換せず、アプリの安定化に任せていた。アプリはCenterが空のSpringBoneにモデルのルートをCenterとして入れ、dragForceを0.85側へ寄せる（安定度0.65）。

改善案として次の4つを検討した。

1. Immobileを、他の実装と同じく stiffness += 0.5i、dragForce += 0.5i（iはカーブ評価後のImmobile）として反映する。Advanced積分のときは独立したStiffnessも足す
2. Gravity Falloffを、無視して警告だけ出すか、骨の向きで重力を減らす近似にする
3. 骨の長さで力を変える
4. 変更しない

## 比べ方

Unity上で、元のPhysBoneと書き出したVRMのSpringBone（UniVRMのFastSpringBone）を同じ動きで並べて回した。動きは60fpsの660コマで、静止（0〜1.5秒）・歩く（1.5〜4.8秒）・止まって180度旋回（4.8〜5.8秒）・首振りとうなずき（5.8〜9秒）・ジャンプ（9〜10秒）・静止（10〜11秒）の順。

誤差は、揺れものの連鎖ごとに骨の先端ベクトルの差を連鎖の長さで割り、骨の長さで重み付けした二乗平均の平方根にした。アプリの読み込み状態（上の安定化あり）を主な評価にし、素のVRM（安定化なし）は参考にした。この検証用の仕組みはリポジトリに含めない。

ダンプできた34体を、係数を決める調整用6体（素体A〜F）と確認用28体に分けた。確認用のうち10体は調整用と揺れものの構成が同じで、残り18体は構成が同じものをまとめると12組（素体G〜R）になる。表の「同じ構成」は確認用に数えた。係数は調整用6体だけで決め、確認用で悪化が無いことを確かめた。悪化は、全区間の誤差が今の式より0.5%を超えて増えたものとした。今の式を2回回した差（再現の誤差）は最大0.1%だった。

| 素体 | 揺れもの | Immobileの型 | 今の式の誤差 |
|---|---|---|---|
| A（調整・同じ構成1体を含む） | 34 | なし | 0.6169 |
| B（調整・同じ構成5体を含む） | 189 | All Motion | 0.3518 |
| C（調整・同じ構成2体を含む） | 98 | All Motion | 0.3948 |
| D（調整・同じ構成1体を含む） | 98 | World | 0.2427 |
| E（調整・同じ構成1体を含む） | 73 | World | 0.2417 |
| F（調整） | 115 | All Motion | 0.3248 |
| G・H（確認） | 2 | なし | 0.374 |
| I（確認・2体） | 22 | World | 0.2678 |
| J（確認・3体） | 40 | World | 0.2197 |
| K（確認・2体） | 23 | World | 0.2630 |
| L（確認・2体） | 53 | 両方 | 0.1796 |
| M（確認） | 40 | World | 0.1796 |
| N（確認・2体） | 197 | All Motion | 0.3249 |
| O（確認） | 36 | All Motion | 0.3133 |
| P（確認） | 34 | All Motion | 0.2918 |
| Q（確認） | 78 | World | 0.2417 |
| R（確認） | 28 | World | 0.2632 |

## 結果

### Immobile

全区間の誤差の、今の式からの変化（アプリの状態）。

| 素体 | 全型に0.5 | All Motionだけ0.5（採用） | All Motionだけ0.75 | All Motionだけ1.0 |
|---|---|---|---|---|
| B | -0.16% | -0.16% | -0.23% | -0.31% |
| C | -0.01% | +0.01% | +0.05% | +0.13% |
| D | -2.48% | +0.01% | -0.03% | -0.03% |
| E | -3.55% | 0.00% | 0.00% | 0.00% |
| F | -0.33% | -0.33% | -0.49% | -0.66% |
| I | **+4.30%** | -0.01% | -0.01% | +0.01% |
| J | **+3.37%** | -0.01% | +0.02% | -0.01% |
| K | **+4.33%** | 0.00% | 0.00% | 0.00% |
| N | -0.34% | -0.33% | -0.50% | -0.67% |
| O | -1.45% | -1.45% | -1.85% | -2.14% |
| P | -2.12% | -2.12% | -2.75% | -3.21% |
| R | **+4.24%** | -0.06% | -0.05% | -0.06% |

全型に足す案は調整用のD・Eで良くなったが、確認用のWorld型の4組（I・J・K・R）で4%前後悪くなった。同じWorld型でも、Dはstiffnessを足した分、Iはdragを足した分だけが効いており、Immobileの効果ではなく基本の式のずれを補っているだけと判断した。World型はアプリが入れるCenter（モデルのルート）で同じ働きになっているので、足さない。

All Motion型だけに足す案は、34体すべてで悪化が無く（最大+0.02%）、All Motion型のB・F・N・O・Pで良くなった。首振りとうなずきの区間ではCが-1.24%、Pが-3.50%。区間別ではCのジャンプの区間だけ+1.41%増える。係数を0.75・1.0にすると全区間ではさらに良くなる素体があるが、Cの全区間（+0.13%）とジャンプの区間（+1.83%・+2.07%）の悪化が増えるので、0.5にした。stiffnessだけ・dragだけに分けた案は、O・Pでdragだけだと+0.16%・+0.20%悪化し、B・Fでは両方足す方が良かった。

素のVRMでは、All Motionだけ0.5の案で全区間がC -9.5%・P -6.5%と大きく良くなる一方、歩く区間でF・Nが+5.0%、O・Pが+6.2%・+5.3%増えた。アプリの状態を優先して採用した。

### Advancedの独立したStiffness

Advanced積分の揺れものにStiffnessを1倍で足すと、OとPで全区間が+3.9%・+2.4%悪化した。採用しない。

### Gravity Falloff

Gravity Falloffを無視する案は、アプリの状態でBが+9.0%、Cが+6.0%、Fが+12.3%悪化した。骨の向きで重力を減らす案も、Bが+5.1%、Cが+4.6%、Fが+5.2%悪化した（素のVRMでは向きで減らす案が全体に良かった）。アプリでは今の式が最も近いので、式は変えない。

### 骨の長さで力を変える

基準の長さ0.06m（効き方を平方根にした案を含む）で、首振りの区間では大きく良くなる素体があったが、全区間でFが+0.5%、Bが+0.3%、Gが+0.4%、歩く区間でGが+3.7%悪化した。採用しない。

## 決定

- Immobile TypeがAll Motionのときだけ、stiffnessに0.5i、dragForceに0.5iを足す（dragForceは1が上限）。gravityPowerに掛ける4pは変えない
- World型は従来どおりアプリの安定化に任せる
- Gravity Falloff・Advancedの独立したStiffness・骨の長さは今の式のまま
- 警告は、Immobile（All Motion）・Immobile（World）・Gravity Falloffを別々の行にして、それぞれの扱いを書く

## 違いが出る場合

ImmobileがありImmobile TypeがAll Motionの揺れものだけ、書き出すstiffnessとdragForceが上がり、揺れが小さく早く収まる。World型・Immobileが0の揺れものの書き出す値は変わらない。Immobile Typeの項目が無い古いSDKではAll Motion（0）として扱う。

## 検証

- `PhysBoneSpringExportTests.AllMotionImmobileRaisesStiffnessAndDragWhileWorldIsLeftToTheApp`: Pull 0.4・Spring 0.2・Immobile 0.6で、All Motionはstiffness 1.9・dragForce 0.78、Worldはstiffness 1.6・dragForce 0.48。警告はどちらか一方だけ
- `PhysBoneSpringExportTests.AllMotionImmobileDragNeverExceedsOne`: Spring 0・Immobile 1でdragForceが1に止まる
- 上の比較（34体×アプリの状態・素のVRM）
