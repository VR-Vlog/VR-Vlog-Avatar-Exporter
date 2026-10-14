# PhysBone の Plane コライダーで書き出しを止めない

- 日付: 2026-10-13
- 対象: `Editor/PhysBoneSpringExport.cs` の `ConvertColliders`

## 背景

販売中の VRChat 用アバター 8 体で書き出しを試したところ、1 体が `Collider_plane: PhysBoneを変換できません: Collider Shape` で止まった。このアバターの PhysBone コライダーは Shape Type が Plane で、Inside Bounds にチェックが入っていた。既存の変換は Plane を VRM の拡張 plane コライダー（`VRM10SpringBoneColliderTypes.Plane`）へ出せるが、「Plane かつ Inside Bounds」と「未知の Shape Type」「PhysBone コライダー以外の型」を例外にしていた。Inside Bounds の無い Plane は別の 2 体で問題なく変換できている。

## 決定

- Plane かつ Inside Bounds のコライダーは、Inside Bounds を外した通常の plane として書き出し、コライダー名を挙げた警告を出す。
- 未知の Shape Type と、PhysBone コライダー以外の型が `colliders` に入っている場合は、そのコライダーだけを当たり判定から外し、警告を出して変換を続ける。
- 非有限の値など、データが壊れている場合の例外はこれまでどおり残す。

## 検討した代替案と不採用理由

- plane の法線を反転して Inside Bounds を表す: VRChat が Plane の Inside Bounds をどう扱うか（無視するのか、裏側を内側とみなすのか）を公開資料で確認できなかった。根拠の無い反転は揺れ物の向きを逆にするおそれがあるので採らなかった。
- これまでどおり止める: 書き出し自体ができず、利用者は原因のコライダーを探して設定を変える必要がある。他の部分は正しく変換できるので、止める利点が小さい。

## 影響

- 該当コライダーを含むアバターが書き出せるようになる。plane の当たり判定は保たれ、Inside Bounds の効果だけが再現されない。
- 既存の Sphere・Capsule・Inside Bounds 無しの Plane の変換結果は変わらない。

## 未決

- VRChat における Plane＋Inside Bounds の挙動の確認。確認できたら、裏返しの plane で再現するかを決める。

## 出典

- VRChat Creators: PhysBones（Collider の Shape Type と Inside Bounds）
- UniVRM 0.131 `VRM10SpringBoneColliderTypes`
- 販売アバター 8 体での書き出し計測（Unity 2022.3.22f1・購入データはリポジトリに含めない）
