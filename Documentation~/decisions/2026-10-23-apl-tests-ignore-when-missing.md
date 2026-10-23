# Avatar Pose Libraryが無いプロジェクトではAPLのポーズのテストを飛ばす

- 日付: 2026-10-23
- 対象: `Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

`ExperimentalExpressionCaptureTests.RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces`の`registration`が`"APL"`の4ケースは、Avatar Pose Libraryのコンポーネントの型を探し、見つからないと`Assert.That(type, Is.Not.Null)`で失敗していた。Avatar Pose Libraryは任意で入れるパッケージなので、入れていないプロジェクトでテストを回すと、Exporterに不具合が無くても4件が失敗になる。

## 決定

- 型が見つからないときは`Assert.Ignore("Install the real Avatar Pose Library package to verify its serialized registration.")`で飛ばす。VRChat SDK・Modular Avatar・NDMFが無いときに飛ばすほかのテストと同じ扱いにする。
- `"menu"`の2ケースはAvatar Pose Libraryを使わないので、これまでどおり回る。

製品の動作は変えない。

## 欠点

- Avatar Pose Libraryを入れていない環境では、APLの登録を読む処理がテストされない。入れた環境で回したときだけ確かめられる。
