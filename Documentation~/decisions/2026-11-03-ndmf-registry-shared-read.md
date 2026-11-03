# NDMF の登録情報の読み取りを 1 か所にし、FaceEmo の表情は Source で選ぶ

## 背景

- NDMF のビルドが残す IObjectRegistry から「元のオブジェクト」を読む 13 行の定型（インターフェイスを完全名で探す → GetReference(Object, bool) を引く → 参照の Object プロパティを読む）が、NdmfExportPreparation・ExpressionGestureCapture・FaceEmoExpressions・ExportOptimizationBindings の 4 か所に写されていた。
- FaceEmo の共通 DEFAULT FACE を当てる表情を、Id が "faceemo/" で始まるかで選んでいた。表情には Source（ExpressionSource.FaceEmo）があり、Id の先頭を見る必要がない。
- FaceEmo の部品の型の完全名 2 つが FaceEmoExpressions に直接書かれ、他のプラグインの型名の表 ExportTypeNames から外れていた。
- GLB の拡張を置く定型（A2a-17）は export_pipeline その 2（#617）で GlbDocument.PlaceExtension に寄せ済みで、今回は何も変えていない。

## 決定

- NdmfExportPreparation に `RegistryGetReference(registry)`（メソッドを返す。登録が無い・読めないときは null）と `RegisteredOriginal(registry, value)`（元のオブジェクトを返す。登録が読めない・記録が無いときは null。登録側の例外は呼び出し側へ）を置き、4 か所はそれを呼ぶ。読めないときの扱い（読み飛ばす・false・例外を出す）と例外の捕まえ方は、呼び出し側ごとに今までの形で残す。ExportOptimizationBindings は参照を作る呼び出し（create = true）を同一性に使うので、元のオブジェクトでなくメソッドを受け取る。
- FaceEmo の表情は `entry.Source == ExpressionSource.FaceEmo` で選ぶ。本体で Id を "faceemo/" で始める箇所（VrChatExpressionSampler）は同じ行で Source も FaceEmo にしているので、選ばれる表情は変わらない。
- FaceEmo の型名 2 つは ExportTypeNames に置く。VrChatSdkTypes は SDK3／SDKBase の 2 つの名前空間で照合する表なので、そこには入れない。
- Modular Avatar の MMD レイヤー制御の判定と、FaceEmo の表情の一覧を NDMF のビルド結果から取る案は変えない（一覧と既定の表情がビルド後の FX から取れないため）。

## 確かめたこと

- 全 EditMode（lilToon 2.3.4・VRChat SDK 3.10.1・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.8.16）で基準に対して新しい赤 0。端末の走査で落ちた 1 件はクラス単位の再実行で緑。
- ホストのハーネス 6 本・validate.py・test-package.py・test-compatibility-tooling.py が緑。
- 書き出しの回帰比較は PR 本文に書く。
