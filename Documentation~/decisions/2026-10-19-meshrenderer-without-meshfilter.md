# MeshFilter のない Mesh Renderer で書き出しを止めない

- 日付: 2026-10-19
- 対象: `Editor/ExportGimmickDetection.cs`・`Editor/AlphaMaskBaker.cs`・`Editor/LilToonMainTextureBaker.cs`、`Vrm10Instance` を `GetComponent` で取っていた 8 ファイル、`Tests/Editor/ExportGimmickTests.cs`・`Tools/BakeTestShim.cs`・`Tests/Editor/ExportFidelityTests.cs`・`CHANGELOG.md`

## 背景

アバターの中に MeshFilter の付いていない Mesh Renderer があると、書き出しが補助ギミックの判定（`ExportGimmickDetection.Inspect`）で `MissingComponentException` を投げて止まった。手持ちのアバターと衣装を総当たりで書き出したときに 1 件だけ起きた。

原因は `renderer.GetComponent<MeshFilter>()?.sharedMesh` という書き方にある。Unity のエディターでは、付いていないコンポーネントを `GetComponent<T>()` で取ると本当の null ではなく「偽の null」（`== null` は true になるが、C# の参照としては null でないオブジェクト）が返る。`?.` は C# の参照で判定するので先へ進み、`sharedMesh` を読んだところで例外になる。

同じ書き方は `Vrm10Instance` の取得にも 10 か所あった。VRM の設定を持たない VRChat アバターでは毎回この経路を通るが、`.Vrm` の読み取りは偽の null でも例外にならず null が返るため、今は止まっていない。Unity の版によって振る舞いが変わる前提に頼らないよう、同じ変更で直す。

## 決定

- `GetComponent<T>()?.` を `TryGetComponent<T>(out var c) ? c.… : null` に置き換える。`TryGetComponent` は付いていなければ false を返し、偽の null を作らない。
- 直すのは `GetComponent<T>()?.` を 1 行で書いた所だけにする。`GetComponent<T>() ?? AddComponent<T>()` の形と、取得と `?.` を 2 行に分けた形は、総当たりの書き出しで失敗が出ておらず、別に確かめてから扱う。
- MeshFilter のない Mesh Renderer は「メッシュがない」と同じに扱う。ギミック判定では既存の「メッシュまたは材質が不足しているため自動判定できません。」の確認行になり、撫でシステムの判定ではシステム全体を残す。焼き込みでは UV の範囲を調べられないものとして扱う（警告が 1 件出る）。

## 確認したこと

- `ExportGimmickTests.MeshRendererWithoutMeshFilterIsReviewedAsMissingMesh`: MeshFilter のない Mesh Renderer で `Analyze` が例外を投げず、確認行になる。変更前のコードでは `MissingComponentException` で失敗する。
- `ExportFidelityTests.BakersTreatMeshRendererWithoutMeshFilterAsMissingMesh`: 焼き込みの検査 2 つが同じ構成で例外を投げない。変更前のコードでは 2 件とも `MissingComponentException` で失敗し、変更後は `ExportGimmickTests`・`ExportFidelityTests` の 54 件が全件合格した（Unity 2022.3.22f1）。
- Unity なしで焼き込みを検査するテスト（`Tools/run-bake-tests.ps1`）の代用の `Component` に `TryGetComponent` を足した。
- 総当たりで止まった 1 件を、この変更を入れたパッケージで書き出し直して通ることを確かめた（結果は PR に書く）。
