# InstalledNdmfNeutralExportTests の 3 件を、使われない BlendShape を外す書き出しに合わせる

- 日付: 2026-10-23
- 対象: `Tests/Editor/InstalledNdmfNeutralExportTests.cs`

## 背景

`InstalledNdmfNeutralExportTests` の次の 3 件が develop で落ちていた。

- `InstalledModularAvatarRetargetsMovedRendererDefaultFxAndAuthoredEndpointInRealVrm(False)` と `(True)`: `Array index (0) is out of bounds (size=0)` のエラーログ
- `InstalledModularAvatarReplacementUsesFinalRendererKeyForNeutralWithoutAnAuthoredRoute`: `GetBlendShapeIndex("Prepared opening")` が -1

NDMF の下限を下げたときの確認では MA 1.11.2〜1.13.0 だけで落ちると見えていたが、MA 1.18.7 + NDMF 1.14.8 でも同じ 3 件が同じ理由で落ちる。MA の版には依らない。

## 原因

2026-10-21 から、書き出しは表情に使われずアプリからも動かせない BlendShape を VRM に入れない（`Editor/UnusedMorphTargets.cs`、コミット 1d48392）。Unity のログにも `left 2 unused BlendShapes out of the VRM: ...Opening` と出ている。

- 1 件目・2 件目: 表情が参照しない `Back` の `Opening` が外れ、`Back` の BlendShape が 0 個になる。テストの `NativeWorldVertices` が BlendShape の有無を見ずに `GetBlendShapeWeight(0)` を呼んでエラーログを出していた。
- 3 件目: どの表情も `Prepared opening` を参照しないので外れる。テストはこのシェイプが残ることを前提にしていた。

どちらも書き出しの仕様どおりで、テストの前提が古い。

## 決定

- `NativeWorldVertices` は BlendShape が無いレンダラーでは重みを読み書きせずに頂点を求める。
- 3 件目の `Prepared opening` が残ることの確認は外す。置き換え後のレンダラーが使われることは名前（`Prepared face`）で、FX の 75 が基本の形に焼き込まれていないことは頂点の比較で、引き続き確かめる。
- 書き出しのコードは変えない。
