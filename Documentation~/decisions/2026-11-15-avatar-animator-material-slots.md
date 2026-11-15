# マテリアルの差し替えの曲線を`VRVLOG_avatar_animator`に書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/AvatarAnimatorEvaluator.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Editor/Locales/ExporterLocale_*.json`、`Tools/compare-exports.py`、`Tests/Editor/AvatarAnimator{Data,Capture}Tests.cs`、`Tools/test-compare-exports.py`

## 背景

表情のアニメーションには、レンダラーのマテリアルのスロットを別のマテリアルに差し替える曲線（`m_Materials.Array.data[N]`、オブジェクト参照の曲線）がある。4-4までは`omitted`の`material`に数えて落としていた。アプリの判断記録（2026-11-12 VRChatアバターの再現）の「段4〜7のデータの形と当て方」は、これを`materialSlot`の対象（ノード・スロット・`rest`）にし、キーをglTFのマテリアルの番号の定数の列にして補間しないこと、差し替え先がどのメッシュにも使われないマテリアルなら`materials`の末尾に足すこと、lilToon以外への差し替えを`materialSlotShader`に数えることを決めている。この単位（4-5）はそれを書く。

## 決めたこと

- `materialSlot`の対象は`node`・`slot`・`rest`を持つ。`slot`はノードのメッシュのサブメッシュの番号、`rest`とキーはそのスロットが描くglTFのマテリアルの番号で、空のスロットは-1。読み手は`rest`とキーが-1以上の整数でないものを拒む
- 評価器は`materialSlot`を補間しない（`Target.Stepped`）。ブレンドツリーの中では重みが最も大きいモーションの値を取り、重みの合計が1に届かない分は`rest`（または下のレイヤーの値）のほうが重ければそちらを取る。遷移・レイヤーの重み・FXの重みでは、重みが0.5を超えたら次の値に切り替える。Unityがオブジェクト参照の曲線を混ぜるときの切り替わりの位置はまだ測っていない
- 曲線が差し替え先にするマテリアルは、曲線を読んだ時点でそのマテリアルを持っていたいずれかのレンダラーのスロットが、書き出しで描くglTFのマテリアルの番号に置き換える。lilToonのマテリアルは書き出しの途中で複製に置き換わるので、マテリアルそのものでは引けない
- 対象の表では`materialTextureST`の後に足し、クリップの中では色とテクスチャのトラックの後に置く。4096のトラックの余りが無ければ`material`に数える。今まで書いていたトラックの位置と対象の番号は変えない
- lilToon以外のシェーダーのマテリアルにする曲線は`materialSlotShader`に数える（`omitted`の種類の最後）。アプリは差し替えたスロットを`VRVLOG_liltoon_full`で描くため
- 書き出しに無いノードのレンダラーの曲線、書き出しで落ちたスロットの曲線は`missingNode`に数える。今までは`material`に数えていた
- 比較ツールに`--allow-added-materials-from VRVLOG_avatar_animator`を足した。変更後の側の`materials`のうち基準の側の数より後ろにあり、拡張が指し、どのプリミティブも使わないものを、末尾から外す。そのマテリアルの`VRVLOG_liltoon_full`の記録も外す。テクスチャと画像は外さない

## 設計どおりにしなかったこと

- 差し替え先がどのメッシュにも使われないマテリアルを`materials`の末尾に足すことは、この単位では入れなかった。設計は足したマテリアルにも`VRVLOG_liltoon_full`の記録を付けるとしているが、今の`VRVLOG_liltoon_full`の読み手（`LilToonFullContract`）は、どのプリミティブにも結び付かない記録を拒む。足すと、今のアプリはそのファイルの`VRVLOG_liltoon_full`を丸ごと読まなくなる。そのため今は、書き出しが既に書いているマテリアルへの差し替えだけを書き、書いていないマテリアルへの差し替えは`material`に数える。比較ツールの判定は、足すようにしたときに使うため先に入れた

## 残る課題

- 足したマテリアルの記録の置き場所を決める。案は (A) 古い読み手が無視する別の拡張に記録を置く、(B) 標準のMToonだけで足して記録を付けない、(C) `LilToonFullContract`を緩める（今のアプリが読まなくなる）。Epicで決める
- 照合（`AvatarAnimatorCrossCheck`）は`materialSlot`をまだ比べない
- アプリが`materialSlot`の対象を動かすのは4-7で入れる
