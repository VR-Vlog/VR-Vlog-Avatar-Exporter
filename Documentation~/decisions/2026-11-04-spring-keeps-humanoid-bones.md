# HumanoidのボーンをSpringBoneの連鎖に入れない

- 日付: 2026-11-04
- 対象: `Editor/PhysBoneSpringExport.cs`

## 背景

PhysBoneのRoot TransformにHipsを指定した全身の揺れものを変換すると、胴や手足もSpringBoneの関節になっていた。SpringBoneのソルバーは、関節の回転を親の回転と初期の回転から毎フレーム計算して上書きする。そのため関節になったHumanoidのボーンはアニメーションに従わない。

## 決定

- 連鎖を作るときにHumanoidのボーンとその親を関節にしない。割り当ての無い中間のボーンも、手足や頭を載せているので同じ扱いにする。
- その先にあるボーンは、それぞれ別の連鎖として揺らす。1本だけでEndpointも無いときは揺らす区間が無いので省略する。
- 外したことで出力が変わるPhysBoneにだけ警告を出す。Multi Child TypeがIgnoreで子が複数あるボーンは、もともと動かないので警告しない。
- Humanoidでないアバターは今までと同じ。

## 考えた別案

全身をアニメーションに従わせたまま揺らす換算は採らなかった。VRMのSpringBoneには動く静止姿勢がなく、手足を揺らしつつアニメーションに従わせる方法がない。

## 確かめ方

`HumanoidBonesStayOutOfChainsAndWhatHangsFromThemStillSwings`が、HipsをRootにしたPhysBoneでHumanoidのボーンが関節に入らず、脚の先のボーンは関節に入り、警告が出ることを見る。`BonesBetweenHumanoidBonesStayOutOfChains`が割り当ての無い中間のボーンを、`AHumanoidRootThatNeverMovedDoesNotWarn`が警告の条件を見る。

検証環境の実アバターでは、Humanoidを根元にした連鎖が4本から0本になった。元と比べた差の平均は47度台のままだが、これは元の体が全身のPhysBoneで動き、比べる基準が変わるため。全身のPhysBoneを両方から外して測ると、残る11本の差は61.92度から1.40度になり、髪や耳などの換算に問題はなかった。
