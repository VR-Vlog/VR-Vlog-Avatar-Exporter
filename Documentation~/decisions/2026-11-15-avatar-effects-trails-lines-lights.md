# Trail・Line・Lightとエフェクトの画像を拡張`VRVLOG_avatar_effects`に書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarEffectsExport.cs`、`Schema/VRVLOG_avatar_effects.1.schema.json`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/AvatarEffectsExportTests.cs`

## 背景

7-1（`2026-11-15-avatar-effects-particles.md`）で、拡張`VRVLOG_avatar_effects`にParticleSystemを書いた。アプリの判断記録（2026-11-12 VRChatアバターの再現、節「段4〜7のデータの形と当て方」の段7）は、同じ拡張に`trails`・`lines`・`lights`を持たせ、TrailとLineは時間・幅の曲線・色のグラデーション・マテリアル、Lightは`type`・`color`・`intensity`・`range`・`spotAngle`を書き、影は書かない、と決めている。この単位（7-2）はそれを足す。7-1で書かなかったマテリアルのテクスチャも、ここで書く。

## 決めたこと

- 拡張は`particles`・`trails`・`lines`・`lights`・`materials`・`images`・`omitted`を毎回すべて書き、使わない表は空にする。どれか1つでも書くエフェクトがあれば拡張を書く。`schemaVersion`は1のまま（拡張を読むアプリはまだ出していない）
- Trail・Lineは、パーティクルのレンダラーと同じ規則でレンダラーのシリアライズの値を、`TrailRenderer`・`LineRenderer`の下に書く。時間（`m_Time`）・幅の曲線と色のグラデーション（`m_Parameters`の`widthCurve`・`colorGradient`）・点（`m_Positions`）などが入る。ライティング・ソート・参照のフィールドは落とし、`m_Enabled`を残す
- ワールド空間のLine（`useWorldSpace`）は、点を書き出し時の位置のまま自分の空間に直し、`m_UseWorldSpace: false`で書く。アプリはエフェクトをノードの下に置くので、ワールドの座標はアプリの中で意味を持たない。アバターが動いても点がその場に残る、という振る舞いは再現しない
- Lightは`type`（`point`・`spot`・`directional`）・`color`（保存されている値）・`intensity`・`range`・`spotAngle`と、コンポーネントの`enabled`を書く。影・Cookie・カリングは書かない。エリアライトとBakedのライトは、アバターでは実行時に何も照らさないので書かず、数えもしない
- `EditorOnly`タグのオブジェクト（自身か祖先）にあるエフェクトは、パーティクルも含めて読まない。VRChatはアップロードでこれを消すので、アバターの中のプレビュー用のLightなどがアプリで点いたり、上限の枠を取ったりしないようにする
- 置き場所・姿勢・`active`・`missingNode`は、パーティクルと同じ規則（書き出されたいちばん近い祖先のノード、無ければルートの`node: -1`）にした
- 上限: Trail・Lineは合わせて16本、Lightは2灯。階層の順に入れ、超えた分を`effectLimit`に数える。パーティクルの上限（16本・粒の合計2000）はそのまま
- マテリアルはパーティクル・Trail・Lineで1つの表にし、同じマテリアルは1つの記録にまとめる。表の上限を16から32に上げた（パーティクル16本とTrail・Line16本が別々のマテリアルを持てるように）
- 書き出しの結果の行を「アプリに渡すエフェクトで省いたもの」「書き出せなかったエフェクトN個」に変えた。Lightなどもまとめて数えるので、「パーティクル」では数が合わない

## テクスチャ（Epicの判断A）

7-1の記録で挙げた2案から、Epicのセッションが A（拡張の中に自前の画像の表を持つ）に決めた。

- 拡張に`images`（`{bufferView, mimeType: "image/png"}`）を持たせ、マテリアルの`texture`はこの表の番号にする。glTFの`images`・`textures`には足さない。今のアプリが数えるテクスチャの数と上限（`LilToonMobileProfile.MaximumTextures`）は変わらない
- 画像はマテリアルのメインのテクスチャ（`_MainTex`、無ければ`_BaseMap`）。長い辺が512pxを超えるものは縮め、sRGBのPNGにする。読めるRGBA・RGBの8bitの画像はCPUで画素から書き、それ以外（圧縮・読めない画像）はGPUで描いて読み戻す。同じテクスチャは1枚にまとめる
- GPUの無い環境（`-nographics`）では読めない画像を書けないので、`texture`を付けずに`effectTexture`に数える。読めるテクスチャはGPUが無くても書く
- PNGのバイトは、ほかの拡張を足し終えた最後にBINのチャンクの末尾へ足し、`bufferViews`を足す。既存のbufferViewの番号とBINの前の部分は変わらない

## 採らなかった案

- B（glTFの`textures`に足し、上限を超えるときは落とす）: 拡張を知らない今のアプリまでテクスチャの数が増え、上限に近いアバターで読めなくなる。Epicの判断でAにした
- TrailとLineの値を名前を付け直した独自の形で書く: パーティクルと同じく、設計がUnityのシリアライズ名で書くと決めている

## 今の出力への影響

ParticleSystem・TrailRenderer・LineRenderer・Light（実行時に照らすもの）のどれも無いアバターは、今までとバイト単位で同じ。あるアバターは、ルートの`extensions`・`extensionsUsed`・`asset.extras.VRVLOG_requiredAppVersions`に`VRVLOG_avatar_effects`が足され、画像があればBINの末尾にPNGと`bufferViews`が足される。

## 残る課題

- FXが消えたオブジェクトの`activeSelf`を動かす曲線を、`enabled`の`trail`・`line`・`light`の対象に読み替えるのは、`AvatarAnimatorCapture`の側の単位で入れる（パーティクルと同じ）
- テクスチャのアニメーション（Texture Sheet Animation）とTrailのテクスチャの`textureMode`の再現は、アプリの7-4で決める
- 線形（sRGBでない）として読み込まれたテクスチャは、読める画像ならCPUで値のまま書き、読めない画像はGPUで描くのでsRGBへ変換されて値が変わる。Read/Writeの設定で色が変わることになるが、エフェクトの画像はほぼ色の画像なので扱いを分けていない
- GPUで縮めるときは1回のバイリニアなので、ミップマップの無い大きな画像はCPUで縮めたものより粗くなる
