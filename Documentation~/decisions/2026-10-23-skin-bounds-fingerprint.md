# SkinnedMeshRenderer の Bounds は getter の値ではなく m_AABB で比べる

- 日付: 2026-10-23
- 対象: `Editor/ExportSourceFingerprint.cs`・`Tests/Editor/SourceFingerprintTests.cs`（`SkinnedBoundsAreComparedThroughTheirSerializedCacheWithoutRounding`）・`CHANGELOG.md`

## 背景

アバターを変えていないのに「元のアバターが変更されました」と出て VRM を保存できない報告があった。PR #178 の作者が問題の出たプロジェクトで調べ、元のアバターのハッシュのうち `SkinnedMeshRenderer` 6 個の `localBounds`（getter が返す値）だけが、center・extents とも最大 5.22e-8 ほど違っていたことを確かめた。同じ調査で、シリアライズされたプロパティ（`m_AABB` を含む）には差が無かった。

ハッシュは getter を読んで Unity の遅延計算の Bounds を確定させたあと、その値をハッシュに入れ、さらにシリアライズされた全フィールドを入れていた。getter の値を入れていたのは確定させるためで、比べる中身としては `m_AABB` と重なっている。

2026-10-23 の「表情ボードは元のアバターと食い違っても止めずに複製を作り直す」で表情ボードの保存は止まらなくなったが、この誤差でも複製の作り直しと Console への出力が起きる。失敗時の画面からの再試行（`ExportRecoverySession`）と、アプリに転送で前回と同じ中身かを見る判定（`AppTransferExport.SourceUnchanged`）も同じハッシュを使うので、誤差を変更として扱っていた。

## 決定

- getter は確定のために読むが、その値はハッシュに入れない（PR #178 の案のとおり）。
- `m_AABB` を含むシリアライズされた値、メッシュ、ボーン、Transform はこれまでどおり比べる。値の丸めはしない。`m_AABB` が 5e-8 違えば変更として扱う。

## 確かめたこと

- Unity 2022.3.47f1 で、ルートボーンの有無・回転と拡大縮小を入れたレンダラー・BlendShape の有無を組み合わせ、ボーンを動かして戻す・レンダリング・BakeMesh・BlendShape の重みを変えて戻す・`m_DirtyAABB` を立てる操作のあとに getter と `m_AABB` を比べた。2022.3.22f1（PR #178 の環境）でもレンダリングと `m_DirtyAABB` を立てる操作を同じ組み合わせで比べた。どの組み合わせでも getter は `m_AABB` と同じ値を返した。報告の環境で getter だけが変わった原因は再現できていない。
- このため、追加したテストは「getter を何度読んでもハッシュが変わらない」ことと「`m_AABB` の 5e-8 の変更は検出する」ことを確かめるもので、修正前のコードでも通る。修正前に失敗するテストは作れていない。
- 2022.3.47f1 で `SourceFingerprintTests`・`SourceFingerprintCacheTests`・`ExportSourceFingerprintSerializationTests`・`QuickTransferMatchesDefaultNormalTests` は失敗 0（138 件成功・VRChat SDK が要る 16 件はスキップ）。`ExperimentalExpressionCaptureTests` は 14 件失敗したが、VRChat SDK と Modular Avatar の無い検証用プロジェクトによるもので、develop のままでも同じ 14 件が失敗する。

## 見送ったこと

- Bounds を丸めて比べる。ほかの値の比べ方と揃わず、小さな本当の変更を見逃すため。
