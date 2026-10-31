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
- 衣装の MA のレイヤーとアバターの Write Defaults が混ざっているとき、VRCFury は直すかどうかを尋ねる画面を出し、答えに応じて FX を直す。答えは名前の末尾の「(Clone)」で探した元のアバターに保存されるが、書き出しの一時コピーは元と同じ名前なので、答えは消えるコピーに保存され、書き出しのたびに画面が出ていた。答えで表情の元になる FX が変わるので、かんたん転送と通常の書き出しの中身も揃わなくなる。そこで、アバターに「Fix Write Defaults」の設定が無いときは、一時コピーに「Skip and stop asking」と同じ設定（Disabled）を付けてから VRCFury を呼ぶ。画面は出ず、FX は直さずに書き出す。アバターに設定があるときはそれに従うので、アップロードと同じになる。
  - 設定を付けたあとに同じ 4 件を書き出し直し、Unity のログで確かめた。Write Defaults の確認の画面は、付ける前は書き出しから 4 回出ていたが、付けたあとは 0 回になった（比べる相手のビルド前コールバックからの 3 回は、アップロードと同じなので残る）。衣装の形の誤差は 1.8 mm のまま変わらなかった。
- 衣装を Armature Link で付けたアバターだけ、書き出した VRM の基準の顔の口の形が、同じ衣装を MA の Merge Armature で付けたときと違う（口の 750 頂点、最大 3.9 mm）。そのため、表情 15 個で口を基準から戻すシェイプキーが 1 つずつ無い。VRCFury の Toggle だけのアバターと、VRCFury 版の GoGo Loco を付けたアバターでは違いは出なかった。シーン上のシェイプキーの値はどちらも 0 で、FX のレイヤーの並びも同じだった。基準の顔を決める処理が、VRCFury が組み直した FX の待機表情を別に扱っているとみられるが、原因の箇所はまだ特定していない。このPRでは変えない。
- VRCFury のテスト用のコピー（`VRCFuryTest`）は VRCFury が作り終えたアバターなので、対象にしない。
- 結果一覧の文言は、NDMF の失敗の案内（2026-10-26）と同じく、句読点と「ください」を使わず、失敗と更新の案内は状態の行とやることの行の 2 行にした。
- VRCFury の処理が失敗したときは、VRCFury 自身のエラーの画面が出て、フックは false だけを返す。Exporter の結果一覧には原因が届かないので、文言で VRCFury の画面を見るよう案内する。パラメーターの容量を超えたときの画面など、Write Defaults 以外の VRCFury の画面はそのまま出る。

## 影響

- VRCFury のコンポーネントがあるアバターは、アップロードと同じ位置で VRCFury の処理がかかり、Armature Link の衣装などが書き出しに入る。トグルの既定の表示は、上に書いたとおり書き出しには入らない。書き出しの時間は VRCFury の処理の分だけ増える。
- VRCFury の処理がエラーを出すアバターは、これまで設定を無視して書き出せていたが、準備の失敗になる。既定の設定では、処理を省いた書き出しに切り替える。
- かんたん転送も同じ `Prepare` を通るので、通常の書き出しと中身は変わらない。
