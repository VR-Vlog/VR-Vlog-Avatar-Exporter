# 口の形のVRM設定をAvatar DescriptorのLip Syncから作る

- 日付: 2026-10-19
- 対象: `Editor/VrmExpressionBindings.cs`・`Editor/VrChatVisemeExport.cs`・`Editor/UniVrmOneClickExporter.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExportOptimizationBindings.cs`

## 背景

VRMの口の形（aa・ih・ou・ee・oh）は、元アバターにVRMの設定が無いとき、メッシュの変形名を既知の名前（`vrc.v_aa`・`mouth_a` など）と照合して作っていた。既知の名前に無い変形はどれにも結び付かず、警告も出ないまま口の形が欠けていた。次の3通りを確認している。

- `lip_aa` のような独自の名前
- かなの名前
- メッシュ統合などの最適化ツールが名前の衝突を避けるために先頭へ番号や接頭辞を付けた名前（例 `13_…__vrc.v_aa`）

VRChatでは、Avatar DescriptorのLip Syncが **Viseme Blend Shape** のとき、`VisemeSkinnedMesh` と `VisemeBlendShapes` が口の形を決める。`VisemeBlendShapes` の並びは sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U で、10〜14番目がVRMの aa, ee, ih, oh, ou に当たる。

## 決定

- 書き出し用のコピー（NDMFなどの処理後）のAvatar Descriptorを読み、Lip Syncが Viseme Blend Shape のときだけ使う。メッシュ統合の最適化ツールは変形名を変えたときにDescriptorの `VisemeSkinnedMesh` と `VisemeBlendShapes` も書き換えるので、処理後のコピーを読めば出力の名前と一致する。
- `VisemeSkinnedMesh` は瞬きと同じく、UniVRMが書き出したGameObjectとnodeの対応で出力のnodeを決める。名前からnodeを推測しない。変形はそのnodeのmeshの `targetNames` から、Descriptorの名前と完全一致（大文字小文字も区別）で1つに決まったものだけを使う。
- 優先順位は、元アバターのVRMの設定（空の設定も含む）、Descriptor、既知の名前との照合、の順にする。
- Descriptorで割り当てた口の形は `VisemeSkinnedMesh` のnodeだけに結び付け、既知の名前を持つほかのメッシュには結び付けない。VRChatでも口が動くのはFace Meshだけなので、それに合わせる。
- Descriptorで変形を指定した口の形のどれかを作れなかったときは、警告を1つ出す。Face Meshが出力に無いときは、変形が無いときと別の文言にする。変形を指定していない枠は数えないので、Lip Syncの口の枠が空のアバターでは従来どおり警告しない。Descriptorで割り当てた口の形には「既知の表情名から…」の確認の警告を出さない。利用者が指定したものなので。
- 表情を記録して書き出す経路は、書き出しの前にコピーからAvatar Descriptorを外す。この経路では瞬きと同じく、外す前にLip Syncを読み、Face Meshを書き出し用のコピー・NDMFの処理後のレンダラーへ順に対応付ける。Descriptorが無いと最適化ツールが口の変形を使われていないと見なすので、変形を保護対象として宣言し、名前の変更を最適化ツールの対応表で追う。最適化ツールが変形を別のメッシュへ移した場合は、そのメッシュにはDescriptorの割り当てが無いので結び付けず、警告する。

## 検討した代替案と不採用理由

- 既知の名前の一覧に `lip_` などを足す: 名前の付け方は作者ごとに違い、かなの名前もあるので追いつかない。
- 元アバター（処理前）のDescriptorを読み、最適化ツールの名前の対応表で変換する: 処理後のコピーのDescriptorが同じ結果を持っているので不要。Descriptorを外す表情の記録の経路だけは、処理後のコピーにDescriptorが残らないので、この方法を取る。
- 名前の末尾一致（`…vrc.v_aa` を `vrc.v_aa` と見なす）: 別の変形を拾うおそれがあり、部分一致で照合しない既存の方針にも反する。

## 未決

- Lip Syncが **Default**（変形名の自動判定）のときはDescriptorに変形名が無いので、従来どおり既知の名前との照合だけで作る。最適化ツールで名前が変わった変形はこの場合は結び付かない。
