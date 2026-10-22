# 元のアバターの変更の判定から、書き出す VRM に届かない値を外す

- 日付: 2026-10-22
- 対象: `Editor/ExportSourceFingerprint.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Tests/Editor/SourceFingerprintPruneTests.cs`・`Tests/Editor/ExportRecoverySessionTests.cs`・`Tests/Editor/ExportRecoveryTests.cs`・`CHANGELOG.md`

## 背景

表情ボードは、複製を作ったあとに元のアバターが変わると「元のアバターが変更されました。「変更」からアバターを指定し直してください。」で保存と転送を止める。判定は `ExportSourceFingerprint` のハッシュで、元のアバターの階層・コンポーネントの値・参照するアセットに加えて、ルートのシーン上の位置、パッケージの設定ファイルのバイト列なども入れていた。シーンでアバターを動かしただけ、lilToon の設定を保存しただけで止まり、作業をやり直すことになっていた。

オーナーから、明らかに要らない判定は外すよう指示があった。外すのは、コードを読んで「変わっても書き出す VRM が変わらない」と言えるものだけにし、少しでも疑わしいものは残す。

## 決定

次の値を判定から外す。

1. ルートのシーン上の置き場所。ルートのワールド位置・回転と親、ルートの `m_LocalPosition`・`m_LocalRotation`・`m_LocalEulerAnglesHint`・`m_Father`・`m_RootOrder`。書き出す複製は `NdmfExportPreparation.InstantiateOwnedCopy` で原点の staging の下に置かれ、`RootScaleBake` が書き出しの間ルートの位置と回転を 0 と単位回転にする。UniVRM の `ModelExporter` もルートの TRS を書かない。ルートのワールドの大きさ（`lossyScale`）は子に移して書き出されるので残す。
   - 例外として、子にシーンの上方向を使う AimConstraint（Unity の `WorldUpType.SceneUp`・`Vector`、VRC の `SceneUp`・`Vector`）があり、ルートの回転をコンストレイントが決めていないときは、ルートの回転を残す。シーンに対してルートが傾くと、この Aim の解く向きが変わる。
   - コンストレイントが読むアバターの外のオブジェクトは、ルートからの相対の位置・回転で比べる。アバターと一緒に動かしただけなら止めず、アバターに対して動いたら止める。
2. `Packages/manifest.json`・`Packages/packages-lock.json`・`ProjectSettings/lilToonSetting.json` のバイト列。lilToon の設定（`LIL_FEATURE_CLIPPING_CANCELLER` など）は書き出すときに `LilToonFullSnapshot.Capture` が読み直す。パッケージを変えるとドメインのリロードが起き、`AssemblyReloadEvents.beforeAssemblyReload` で表情ボードの複製を閉じるので、古い複製のまま書き出すことはない。
   - ルートの大きさは有効数字 6 桁、外のオブジェクトの相対の行列は 1e-5 単位に丸めて比べる。回転した親を通して求めたワールドの値には浮動小数点の誤差が乗り（大きさ 1 が 0.99999994 になる）、ルートを回しただけで止まるため。
3. Shader と MonoScript のアセットの依存ハッシュ。シェーダーのファイルは VRM に書かれず、書き出すときに今のシェーダーを読む。lilToon の設定を保存するとシェーダーのインクルードが作り直され、マテリアルの値は同じなのに止まっていた。スクリプトの変更はドメインのリロードで複製を閉じる。マテリアル・テクスチャ・メッシュ・アニメーションなど、ほかのアセットの依存ハッシュは残す。シェーダーを読み込み直すと Unity がシェーダーのオブジェクトを作り直すことがあるので、マテリアルが指すシェーダーはインスタンス ID ではなくアセットの GUID とファイル内 ID で比べる。
4. 表情ボードの比較を、書き出しの復旧画面と同じ `ExportRecoverySourceStamp` にする。部分的に効く VRC Rotation Constraint の解き直しで回転の四元数の最後の桁が揺れても止めない（`PartialRotation.Matches` の許容幅。2026-10-18 に復旧画面に入れたもの）。表情ボードだけが厳密な比較のままだった。

## 残したもの

疑わしいので残した。

- ルートの `activeInHierarchy`: 2026-10-18 の無効なルートの書き出しの決定で、意図して残している。
- テクスチャの `updateCount`: 読めないテクスチャへ GPU 側で書いた変更を見分ける手段がほかにない。
- Unity の部分的な重み・軸のコンストレイントと、Position・Parent・Scale・LookAt コンストレイントの出力: 既存のテストが厳密に比べることを決めており、解き方が書き出しに届かないとまでは言えない。
- ルート以外の `m_LocalEulerAnglesHint`: VRC の部分的なソルバーが、軸を外した Euler の値をそのまま使う。
- コンストレイントが読む外のオブジェクトの位置: 変換できないコンストレイントは今の姿勢を焼き込むので、書き出しに届く。
- Shader・MonoScript 以外のアセットの依存ハッシュ（AudioClip など）と、`ExecuteAlways`・`OnValidate` で値を書き換えるツール。

## 検討した代替案と不採用理由

- 判定をやめて、書き出す直前に毎回複製を作り直す: 表情ボードで記録した内容は複製に乗っているので、作り直すと記録が消える。
- ルートの位置を外さず、止めたときに「位置だけが違う」と案内する: 止める理由がないのに止まる点は変わらない。
- パッケージの設定ファイルを中身の一部（lilToon の設定値）で比べる: 書き出すときに読み直すので、比べる必要がない。

## 影響

- シーンでアバターを動かす・回す・別の親に入れる、lilToon の設定やパッケージを変える、では表情ボードが止まらなくなる。ルートの大きさ、メッシュ・マテリアル・テクスチャ・BlendShape・オブジェクトの有効と無効・ボーンの位置・ツールの設定を変えると、今までどおり止まる（`SourceFingerprintPruneTests`）。
- 書き出しの復旧画面も同じ判定を使うので、同じ操作で止まらなくなる。`ExportRecoveryTests` の親の位置・回転を変えると止まることを確かめていたテストは、位置と回転では止まらず、大きさと有効・無効で止まる形に改めた。

## 確認したこと

- Unity 2022.3.22f1 の EditMode で、判定にかかわるテストクラス（`SourceFingerprintPruneTests`・`ConstraintSourceFingerprintTests`・`ExportRecovery*Tests`・`InactiveRootExportTests`・`ExportSourceFingerprintSerializationTests`・`SourceFingerprintCacheTests`）を、VRChat SDK 3.10.1 があるプロジェクトとないプロジェクトの両方で回し、すべて通った。
- テストアセンブリ全体（4033 件）では 50 件が失敗したが、同じテストを develop で回しても同じ 50 件が失敗した（Modular Avatar・ポーズ用パッケージ・VRChat SDK がない、色空間が Gamma など、テスト用プロジェクトの構成によるもの）。
