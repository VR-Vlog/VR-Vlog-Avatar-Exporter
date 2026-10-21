# 表情のクリップにある PhysBone・コライダー・Contact の有効/無効の曲線を、表情の評価の妨げにしない

- 日付: 2026-10-21
- 対象: `Editor/NeutralShapePlan.cs`・`Editor/VrChatExpressionSampler.cs`・`Editor/VrChatGestureExpressions.cs`・`Editor/PhysBoneSpringExport.cs`・`Tests/Editor/NeutralShapeSamplerTests.cs`・`Tests/Editor/SelectedExpressionAppearanceTests.cs`・`Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`・`CHANGELOG.md`

## 背景

あるアバターでは、FX の表情とハンドジェスチャーのクリップが、顔の BlendShape と一緒に`AvatarDynamics` の下にある地面用のコライダー（`VRCPhysBoneCollider`）の `m_Enabled` も切り替えていた。VRChat の表情を読むとき、`VrChatExpressionSampler.HarmlessAppearance` と `NeutralShapePlan.EvaluationBindingRejection` はこの曲線を「BlendShape 以外の変化」と判定し、表情メニューの項目とハンドジェスチャーの表情がすべて次の理由で外れていた。

> 表情への遷移にBlendShape以外の変化が含まれます: <クリップ> / <パス> / m_Enabled（判定理由: 対象プロパティは存在しますが、通常入力での到達範囲外、またはBlendShapeへの影響が未対応です）

その結果、VRM にはメニュー由来の動く表情（`VRVLOG_expression_animations`）が入らず、動かない仮の表情だけになっていた。

`EvaluationBindingRejection` は、見た目として扱う曲線（`IsAppearanceBinding`）でも、対象の無い曲線でもない、存在するプロパティの曲線を一律に「到達範囲外」として拒否する。PhysBone・コライダー・Contact の Component はどちらにも当たらないので、値によらず拒否されていた。ハンドジェスチャーの `VrChatGestureExpressions.ReadPose` も、`SelectedExpressionAppearance.IsUnchanged` と `AllowsEvaluationBinding` のどちらも通らないので例外になっていた。

## 決定

- `NeutralShapePlan.IsInertDynamicsBinding` を足す。曲線の対象の型の FullName が `VRCPhysBone`・`VRCPhysBoneCollider`・`VRCContactReceiver`・`VRCContactSender` のどれかなら true を返す。型名は既存の `PhysBoneSpringExport.PhysBoneType`・`PhysBoneSpringExport.ColliderType`（`internal` にした）・`ExportGimmickRules.ContactReceiver`・`ExportGimmickRules.ContactSender` を使う。
- `EvaluationBindingRejection` は、この曲線に対して null（評価の補助として通す）を返す。
- `VrChatExpressionSampler.HarmlessAppearance` は、対象の無い Transform の曲線（`IsUnboundTransform`）と同じ形で、この曲線なら `SelectedExpressionAppearance.ValidateClipData` でクリップの AnimationEvent と曲線の値（NaN など）を確かめてから true を返す。
- `VrChatGestureExpressions.ReadPose` は、この曲線を読み飛ばす。
- `IsAppearanceBinding` は変えない。

## 安全な理由

- 表情を評価するプローブは、アバターの複製の Behaviour をすべて無効にしてから Animator だけを有効にする（`VrChatExpressionSampler` の 2 か所のプローブ）。曲線がコライダーや PhysBone を有効にしても、プローブの中で動く処理は無い。
- プローブから読むのは `SkinnedMeshRenderer.GetBlendShapeWeight` の値だけで、姿勢・材質・Component の状態は書き出しに使わない。PhysBone・コライダー・Contact は揺れと接触の入力で、BlendShape の値にも見た目にも関わらない。
- 書き出す揺れ物とコライダーは、表情とは別に `PhysBoneSpringExport` が元のアバターの Component から作る。表情のクリップの曲線は使わない。
- Constraint の `m_Enabled` は、有効にすると SDK の処理や外部の参照が動きうるので、これまでどおり評価を制限する。今回の 4 つの型だけを通す。BoxCollider など、ほかの Component の未知の曲線も、これまでどおり拒否する。

## 採らなかった案

- 4 つの型を `IsAppearanceBinding` に足す: 見た目を変える曲線として扱われ、そのクリップは見た目が変わる表情として基準形の保持や見た目の照合の対象になる。実際には見た目を変えないので、通るべき表情が別の理由で外れる。
- 使う人の設定（書き出しから外すパス）でコライダーのパスを外してもらう: 原因が判定理由の文からは分かりにくく、アバターごとに使う人が設定する手間がかかる。Component の性質で決まることなので、書き出しの側で判定する。

## 確認したこと

- `Tests/Editor/NeutralShapeSamplerTests.cs` に `DynamicsEnabledSupportIsHarmless` を足した。4 つの型それぞれで `m_Enabled` を 0 と 1 にする補助レイヤーがあっても基準形の評価が成功し、`m_Enabled` を理由に保持する警告が出ず、Component の有効状態と BlendShape の値が変わらず、`AllowsEvaluationBinding` が true・`EvaluationBindingRejection` が null になる。
- `Tests/Editor/SelectedExpressionAppearanceTests.cs` に `DynamicsEnabledCurveDoesNotRejectTheGesturePose` を足した。`VRCPhysBoneCollider` の `m_Enabled` を 1 にする曲線と BlendShape の曲線を持つクリップから、`ReadPose` が BlendShape の値を返す。BoxCollider の `m_Enabled` を拒否する `ExistingUnknownPropertiesAreNotAuthorizedByCoincidentValues` はそのまま残した。
- 2 つのテストを CI の必須一覧（`Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`）に足し、`Tools/test-compatibility-tooling.py` の `SelectedExpressionAppearanceTests` の件数を 62 にした。
- どちらも VRChat SDK が無い環境では Ignore になる。Unity の EditMode テストは CI で回す。
