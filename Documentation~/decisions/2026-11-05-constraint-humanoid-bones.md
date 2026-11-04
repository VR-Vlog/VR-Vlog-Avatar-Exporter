# 揺れものと拘束の除外も HumanoidBones.Resolve でボーンを引く

- 日付: 2026-11-05
- 対象: `Editor/ConstraintExport.cs`・`Tests/Editor/InactiveRootExportTests.cs`

## 背景

`ConstraintExport.HumanoidBones` は人型のボーンの一覧を返し、PhysBone の変換と拘束の変換がこれを除外に使う。ここだけ `Animator.GetBoneTransform` を直接呼んでいた。一度も表示していない Animator は `GetBoneTransform` が null を返すことがあるとされ、[非表示のまま書き出す決定](2026-10-18-inactive-root-export.md)で `HumanoidBones.Resolve` を作って他の呼び出しは移したが、ここは残っていた。null だと一覧が空になり、体が揺れものの鎖に入って、警告なしで以前の動きに戻る。

## 決定

ほかの呼び出しと同じく `HumanoidBones.Resolve` で引く。`Resolve` は `GetBoneTransform` が答えたときはその値を返すので、結果が変わるのは未初期化の Animator で null が返ったときだけになる。Animator と Avatar の確認も `Resolve` がするので、`ConstraintExport` 側の確認はなくした。

## 再現できていないこと

この null は Unity 2022.3.22f1 の batchmode で再現しなかった。テストの Animator は `isInitialized` が false でも `GetBoneTransform` がボーンを返し、修正前のコードでも `HumanoidBones` は全部のボーンを返す。無効にした Animator をコピーしても初期化済みになった。修正前に落ちるテストは作れていない。

足した assert は、未表示の Animator から全部のボーンが返ることだけを確かめる。修正の前後どちらでも通るので、呼び出しを戻しても気づけるテストではない。

書き出しの本線では、PhysBone の変換より前に `ValidatePreparedHumanoid` が必須のボーンを `GetBoneTransform` で確かめて、取れなければ止める。ここは警告なしで進むのではなくエラーになるので、変えていない。

## 確認

`PhysBoneSpringExportTests`・`InactiveRootExportTests`・`ConstraintExportTests` を Unity 2022.3.22f1 で回して確かめた。
