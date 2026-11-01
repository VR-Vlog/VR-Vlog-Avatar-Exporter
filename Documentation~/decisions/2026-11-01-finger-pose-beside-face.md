# 顔の横にある指の形の曲線は、ジェスチャーで入らないクリップでも読まない

- 日付: 2026-11-01
- 対象: `Editor/ExperimentalExpressionCaptureSession.Clips.cs`（`BuildClipPoseUnchecked`）・`Editor/PoseSampling.cs`（`IsFingerMuscle`）・`Tests/Editor/ExperimentalExpressionCaptureTests.cs`

## 背景

1.4.3（#546）から、表情ボードは顔のクリップが動かすボーンと表示の切り替えを、ジェスチャーで入るかどうかに関係なく書き出す。一方、Humanoidの筋肉の曲線を読み飛ばすのは、2026-10-23の決定（`2026-10-23-gesture-face-bone-curves.md`）のとおりジェスチャーで入るクリップだけだった。

スマホなどの小物を手に持たせる表情では、シェイプキーと小物の位置の曲線に、持つ指の形の曲線が一緒に入っている。同じ作りの表情が2つあり、1つはジェスチャーのレイヤーから入り、もう1つはアバターのAnimatorでだけ再生されるアバターがあった。後者だけが「顔の変形・ボーン・表示切替以外の変更を含みます: / LeftHand.Index.1 Stretched」で未対応になり、表情ボードの一覧にもVRMにも入らなかった。

VRMの表情は指の形を持てない。ジェスチャーで入るクリップでも指の形は書き出しておらず、表情が回すHumanoidのボーン（Transformの曲線）も書き出さずに通している。指の筋肉の曲線だけを、ジェスチャーで入るかどうかで分ける理由はない。

## 決定

- ボーンの書き出しが有効なとき（`ExpressionTransformCurves.Enabled`）、ジェスチャーで入らないクリップでも、左右の手の指の筋肉の曲線は読まずに、シェイプキーとボーン・表示の切り替えを表情として扱う。
- 指以外の筋肉（体・腕・脚・頭）、IKのゴール、ルートの曲線は、ジェスチャーで入らないクリップではこれまでどおり未対応にする。全身のポーズを持つクリップが一覧に増えないようにするため。
- ボーンの書き出しが止まっているとき（1.4.4からの既定）は何も変えない。ジェスチャーで入らないクリップは、指の曲線があればこれまでどおり未対応になる。
- 標準の表情（プリセット）は、これまでどおりシェイプキーだけを持つ。ジェスチャーで入らないクリップに指の曲線があればプリセットには使わない。

## 確認

- `ExperimentalExpressionCaptureTests.GestureFacesIgnoreBoneCurvesBesideTheFace` に、指の曲線（ジェスチャーで入る・入らない）、体の筋肉の曲線（ジェスチャーで入る・入らない）、ジェスチャーで入らないクリップのIKのゴールとルートの曲線、顔の変化が無いクリップの指の曲線のケースを足した。
- `ExperimentalExpressionCaptureTests.WhileTheExportLeavesBonesOutTheBoardTreatsThemAsBefore` に、ボーンの書き出しが止まっているとき、ジェスチャーで入らないクリップの指の曲線は未対応のままであることを確かめるケースを足した。
- `VrChatMenuExpressionTests.HumanoidChannelsComeFromUnityVocabulary` に、`IsFingerMuscle` が左右の手の指を両方の書き方で拾い、手首・前腕・体・頭・脚・IKのゴール・ルート・Animator以外のパス・Transformの曲線を拾わないことを足した。
- 上の表情を持つアバターで、ボーンの書き出しを有効にして表情ボードを開いて何も変えずに書き出し、2つの表情がどちらも一覧とVRMに入ること、VRMの `transforms` に小物の位置が入ることを確かめた。

## 欠点

- 指の形はVRMの表情に入らない。アプリでは、小物は動くが指は持つ形にならない。
- 指の形を読み飛ばしても警告は出さない。ジェスチャーで入るクリップでも出していないのに合わせた。
