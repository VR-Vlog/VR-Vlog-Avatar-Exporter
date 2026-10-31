# VRCFury の設定を書き出しの一時コピーに適用する

- 日付: 2026-11-01
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/UniVrmOneClickExporter.cs`・`Tests/Editor/NdmfPreparationTests.cs`

## 背景

VRCFury は NDMF のプラグインではなく、VRChat SDK のビルド前コールバック（`IVRCSDKPreprocessAvatarCallback`）で動く。アップロードでは、NDMF の Transforming までの処理（順番 -11000 付近）のあと、VRCFury（-10000）、NDMF の Optimizing の順に走る。

書き出しは NDMF の `AvatarProcessor` だけを呼び、VRChat SDK のコールバックは呼ばない。そのため VRCFury の設定は書き出しに何も反映されず、Armature Link で付けた衣装はアバターのボーンに付かないまま浮き、トグルの初期状態も反映されなかった。

## 決定

- 書き出しの一時コピーに、VRCFury のアップロード用のフック（`VF.Hooks.VrcPreuploadHook.OnPreprocessAvatar`）をリフレクションで呼ぶ。呼ぶ場所はアップロードと同じ順番に合わせる。
  - NDMF のビルドをかけるときは Transforming のあと、書き出しの後処理と Optimizing の前。
  - NDMF のビルドが要らないときは、VRCFury だけをかける。
- VRCFury のコンポーネント（`VF.Component.VRCFuryComponent` の派生。VRCFury 自身と同じく `VRCFuryDebugInfo` は除く）があるときだけ呼ぶ。無いアバターの書き出しは変わらない。
- VRCFury のコンポーネントがあるときは、NDMF のビルドをかけるときと同じく、ウェイトの無いメッシュの保護（`SkinnedMeshFallbackWeights`）をかける。VRCFury もメッシュやボーンを組み替えるため。
- VRCFury は VRC Avatar Descriptor が無いと動かない。ルートに無いときは呼ばずに、結果一覧に「適用せずに書き出した」と出す。
- フックが見つからない・形が違うとき（VRCFury の版が違うとき）と、フックが失敗を返したときは、準備の失敗として扱う。既定の設定（`ExportStageGuard.ContinueByDefault`）では、処理を省いた書き出しに切り替えて結果一覧に出す。
- VRCFury が作ったメッシュ・マテリアル・アニメーションのうちアセットとして保存されていないものは、NDMF の処理で作られたものと同じく書き出しのあとに破棄する。VRCFury が `Packages/com.vrcfury.temp` に保存したものは VRCFury が自分で片付けるので、Exporter は触らない。アップロードのときと同じ扱いになる。

## 選ばなかったもの

- VRChat SDK のビルド前コールバックを全部呼ぶ（`VRCBuildPipelineCallbacks.OnPreprocessAvatar`）: アップロードと最も近いが、NDMF も同じコールバックから二重に走り、アップロード専用の検査（パラメーターの数の上限など）で止まることがある。書き出しの準備の流れ（Transforming のあとに Exporter の後処理を挟む）とも合わない。
- VRCFury の内部の `VRCFuryBuilder.RunMain` を直接呼ぶ: 一度だけ走らせる判定やエラー表示を自分で真似ることになり、VRCFury の更新で壊れやすい。公開メソッドのフックを呼ぶほうが変わりにくい。

## 確かめたこと

検証用の Unity プロジェクトで、MA の Merge Armature の代わりに VRCFury の Armature Link で衣装を付けたアバターを書き出し、VRChat SDK のビルド前コールバックをかけた複製と比べた。形の誤差の 99 パーセンタイルは、変更前は基本の姿勢で 21.6 mm、決まったポーズで 51.8 mm あり衣装が浮いていたが、変更後はどちらも 1.8 mm になった（衣装の無い基準のアバターは 1.9 mm）。

- VRCFury の Toggle で既定をオフにしたオブジェクトは、書き出しでは表示のまま残る。アバター自身の FX で既定をオフにしたオブジェクトも同じ扱いで、VRCFury に限らない Exporter の既存の動きなので、ここでは変えない。
- 衣装の MA のレイヤーとアバターの Write Defaults が混ざっているとき、VRCFury はその旨の確認の画面を出し、答えに応じて FX を直す。書き出しでもアップロードと同じ画面が出る。直した FX から作る表情は、表情ごとにシェイプキーが 1 つ違うことがある。

## 影響

- VRCFury のコンポーネントがあるアバターは、書き出しにアップロードと同じ VRCFury の結果が入る。書き出しの時間は VRCFury の処理の分だけ増える。
- VRCFury の処理がエラーを出すアバターは、これまで設定を無視して書き出せていたが、準備の失敗になる。既定の設定では、処理を省いた書き出しに切り替える。
- かんたん転送も同じ `Prepare` を通るので、通常の書き出しと中身は変わらない。
