# マテリアルの色とテクスチャのタイリングの曲線を`VRVLOG_avatar_animator`に書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarAnimatorCapture.cs`、`Editor/AvatarAnimatorCrossCheck.cs`、`Formats/Runtime/AvatarAnimatorData.cs`、`Formats/Runtime/LilToonFullContract.cs`、`Schema/VRVLOG_avatar_animator.1.schema.json`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/AvatarAnimator{Data,Capture,CrossCheck}Tests.cs`

## 背景

4-3でメッシュのレンダラーのマテリアルのfloatの曲線（`material._Cutoff`）を`materialFloat`の対象に書いた。色のチャンネル（`material._Color.r`）とテクスチャのタイリング・オフセット（`material._MainTex_ST.x`）の曲線は、まだ`omitted`の`material`に数えて落としている。アプリの判断記録（2026-11-12 VRChatアバターの再現）は、これを`materialColor`・`materialTextureST`の対象にし、アプリのlilToonのマテリアルに無い名前は`materialProperty`、アプリが読み込み時に固定する機能のオン・オフは`materialSwitch`に数える、と決めている。この単位（4-4）はそれを書く。

## 決めたこと

- `materialColor`は色のプロパティ`name`のチャンネル`channel`（`r`・`g`・`b`・`a`）、`materialTextureST`はテクスチャのプロパティ`name`のタイリングとオフセット（Unityの`name_ST`。`x`・`y`がタイリング、`z`・`w`がオフセット）のチャンネル`channel`（`x`・`y`・`z`・`w`）。どちらも`materialFloat`と同じく1本の曲線が1つのfloatを動かす。Unityの曲線が1チャンネル1本なので、それに合わせた
- `materialTextureST`の`name`は`_ST`を除いたテクスチャの名前（`_MainTex`）にする。アプリはテクスチャのプロパティごとにタイリングとオフセットを持つので、テクスチャの名前で引けるほうが当てやすい
- 曲線が動かすのは`LilToonFullContract`がアプリのマテリアルに同じ名前・同じ型で持たせるプロパティ（`LilToon234Catalogue`で`:runtime`のもの）だけにする。名前が無いもの、型が違うもの（色をfloatとして動かす曲線など）は`materialProperty`に数える。4-3の`materialFloat`も同じ判定になり、契約に無いfloatの曲線は今まで`material`に数えていたものが`materialProperty`に移る（数は変わらない）
- `materialSwitch`の対象はアプリの`LilToonFeatureSpecialization`が固定する15個の`_Use*`のfloat（`LilToonFullContract.Switches`）。アプリはそれが全部0のグループのシェーダーのコードを落とすので、アニメーションで1にしても何も出ない。アプリ側の一覧のもう1つ、`_Main2ndDissolveParams`のxはベクトルのチャンネルで、ベクトルの曲線はもともと書かないので`material`のままにした
- ベクトルのチャンネルの曲線（`_Main2ndDissolveParams.x`など）は今までどおり`material`に数える。設計に対象の種類が無い
- 対象の表では`materialFloat`の後に`materialColor`、`materialTextureST`の順に足す。クリップの中では、色とテクスチャのトラックをconstraintの`enabled`のトラックの後に置き、4096のトラックの余りもconstraintの後の残りから取る。今まで書いていたトラックの位置と対象の番号を変えず、上限に近いクリップで新しい種類が今までのトラックを押し出さないようにした
- `omitted`の読み手は、知らない種類を知っている種類の後に1回ずつなら受け取って残すようにした（合わせて64個まで）。今までは知らない種類で拡張全体を読まなかったので、種類を足すたびに古い読み手が拡張ごと捨てていた
- 照合（`AvatarAnimatorCrossCheck`）はUnityがプロパティブロックに置く色全体と`name_ST`のベクトルを読み、色は8ビットの1段（1/255）、タイリングとオフセットはfloatと同じ相対1e-4で比べる

## 残る課題

- アプリが今固定しているExporterのFormats（4-1の時点）は`omitted`の知らない種類を受け取らないので、`materialProperty`か`materialSwitch`が1つでもあるファイルの拡張を読まない。アプリの固定を、この単位を含むFormatsへ上げる必要がある（4-7・4-11）
- アプリが`materialColor`・`materialTextureST`の対象を動かすのは4-7で入れる
