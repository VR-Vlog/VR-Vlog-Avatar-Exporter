# 前提のパッケージや色空間が無いテストは失敗でなく飛ばす

- 日付: 2026-11-05
- 対象: `Tests/Editor/ExperimentalExpressionCaptureTests.cs`・`Tests/Editor/AppearancePreparationTests.cs`・`Tests/Editor/SelectedExpressionAppearanceTests.cs`

## 背景

Modular Avatar・VRChat SDK が入っていない、色空間が Gamma のプロジェクトで EditMode を全部回すと、次の 19 件が失敗していた。

| クラス | 件数 | 失敗の中身 |
| --- | --- | --- |
| `ExperimentalExpressionCaptureTests` | 14 | Modular Avatar の型が `null` のまま `Assert.That(.., Is.Not.Null)` で止まる・`AddComponent(null)` で `ArgumentNullException`・VRChat SDK の型が `null` で `NullReferenceException`・リフレクションの `Invoke` 越しの `Assert.Ignore` が `TargetInvocationException` に包まれる |
| `SelectedExpressionAppearanceTests` | 4 | 色空間が Linear であることを `Assert.That` で確かめていて、Gamma で止まる |
| `SectionedToonSubstitutionTests` | 1 | デカールの焼き込みの結果が Gamma で違う |

Modular Avatar・VRChat SDK・Linear がそろったプロジェクトでは、develop（bdaccb29）でも 19 件とも通る。

このリポジトリでは、任意のパッケージが無いときは `OptionalTools.Require` か `Assert.Ignore` で理由を出して飛ばしている。上の 18 件はその確認が抜けていて、無いことを失敗として出していた。

## 決定

18 件は、前提が無ければ理由を出して飛ばす。

- Modular Avatar と VRChat SDK の型は `OptionalTools.Require` で探す。`AppearancePreparationTests.AddRule` も同じにしたので、これを使うほかのテストも、Modular Avatar が無いときは飛ばす。
- `PoseRegistrationTests.Descriptor` をリフレクションで呼ぶ前に、VRChat SDK があるかを確かめる。`Invoke` の中で飛ばすと、包まれて失敗になるため。
- 実際の描画と比べるテストは、Linear でなければ飛ばす。期待する値を Linear のプロジェクトとして計算しているため。

期待する値と、前提がそろったときに確かめる中身は変えていない。`Tools/run-unity-compatibility.py` は飛ばしたテストも失敗として扱うので、互換性の確認で見逃すことはない。

## 残したもの

デカールの 1 件（`APlacedDarkenDecalIsBakedOnItsAreaOnly`）は飛ばさない。テストの作りではなく、Gamma のプロジェクトで画像の読み取りが Linear の値にならないまま混ぜている、書き出し側の問題のため。この PR では直していない。
