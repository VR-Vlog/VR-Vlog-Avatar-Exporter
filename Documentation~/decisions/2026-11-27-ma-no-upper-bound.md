# Modular Avatarの上限をなくす

- 日付: 2026-11-27
- 対象: `Compatibility/dependencies.json`・`Tools/generate-compatibility.py`・`Editor/Compatibility/DependencyPolicy.cs`・`Editor/MaAppearanceSnapshot.cs`・`Editor/Locales`・`Tools/DependencyCompatibilityTests.cs`

## 背景

Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterの現在の表示を固定する処理は、Modular Avatar（MA）のバージョンが `modularAvatarMinimum`（1.12.2）以上 `modularAvatarBelow`（1.19.0）未満か、`modularAvatarPrereleases`（1.19.0-alpha.2）のときだけ動き、それ以外は書き出しの前に止めていた（[2026-10-18の決定記録](2026-10-18-modular-avatar-appearance-range.md)・[2026-10-22の決定記録](2026-10-22-ma-1-19-alpha.md)）。

MAの正式リリース1.19.0と1.19.1が出たあと、1.19以降を入れたプロジェクトでは、これらのコンポーネントを使うアバターが書き出せなくなった。上限は「そのバージョンでまだ確かめていない」ことだけを理由に止めていて、壊れていると分かっているわけではなかった。mkから、上限を外し、問題の報告があれば対応するよう指示があった（2026-11-27）。

## 決定

- `modularAvatarBelow` と `modularAvatarPrereleases` を消す。`modularAvatarMinimum`（1.12.2）以上なら、上限なしで固定を試す。
- プレリリースも正式リリースと同じに扱う。`X.Y.Z-pre` は `X.Y.Z` として下限と比べる。一覧で絞らない。
- 下限より前のバージョンだけを、MAのAPIに触る前に止める。文言は「{1}以上が必要です。VCC／ALCOMでModular Avatarを更新してください」にする。
- バージョンを読めたが、固定で読むMAの型やメンバーが無かったとき（新しいMAでAPIが変わったとき）は、今までどおり固定の途中で止める。文言は日本語だけで書いていたので、4言語に訳した `ModularAvatarFreezeFailedError` にし、入っているバージョンと動作を確かめたバージョン（`modularAvatarReference`、1.18.7）を出す。アバターは変更しない。
- バージョンを読めないとき（`Assets` の下に置いたMA、`+build` 付きなど）は、今までどおりAPIで決める。`1.19.0-alpha.2+local` のようにプレリリースの後ろにビルド情報が付いたものも「読めない」に入れ、範囲の外として止めない。
- 新しいMAが出ていないかを毎日確かめ、出たら固定が今までと同じ結果になるかを確かめる。

## 残る危険

- 型やメンバーの名前を変えずに意味を変えたMAでは、止まらずに違う見た目で書き出すことがある。たとえば、解析結果に新しい種類の動作が足されると、固定はその動作を読み飛ばすので、Unityで見えている表示とアプリの表示がずれる。解析の対象の種類が足されたときは、その対象を切り替えるコンポーネントを固定のあとに消すので、メニューの切り替えがVRMから抜けることもある。型やメンバーが消えたり名前が変わったりしたときは、固定の途中で止まる。
- 1.19.0-alpha.0と1.19.0-alpha.1は、alpha.2とアニメーションのまとめ方が違うので今まで止めていた。この変更で通るようになる。今から使う人はほとんどいないと見て、一覧で絞らない指示に合わせる。
- NDMFのプレリリースは今までどおり `ndmfPrereleases`（1.15.0-beta.1・beta.2）だけを通す。MAの1.19系はNDMF 1.15.0-beta.1以上を求めるが、正式リリースの1.15.0以降はNDMFの範囲（1.6.0以上2.0.0未満）に入るので、VCC／ALCOMで正式リリースを入れた場合は止まらない。
