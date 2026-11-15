# アバターのParticleSystemを拡張`VRVLOG_avatar_effects`に書く

- 日付: 2026-11-15
- 対象: `Editor/AvatarEffectsExport.cs`、`Schema/VRVLOG_avatar_effects.1.schema.json`、`Editor/UniVrmOneClickExporter.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Editor/Locales/ExporterLocale_*.json`、`Tests/Editor/AvatarEffectsExportTests.cs`

## 背景

VRMはParticleSystemを持てないので、今の書き出しではアバターのパーティクルがアプリに届かない。アプリの判断記録（2026-11-12 VRChatアバターの再現、節「段4〜7のデータの形と当て方」）は、エフェクトをルートの別の拡張`VRVLOG_avatar_effects`（`particles`・`trails`・`lines`・`lights`）に書き、モジュールはUnityのシリアライズ名で書き、マテリアルは1つのunlitのシェーダーで描ける近似にする、と決めている。この単位（7-1）はそのうちParticleSystemを書く。

## 決めたこと

- 拡張は書くパーティクルが1つ以上あるときだけ書き、`extensionsUsed`に載せて`extensionsRequired`には載せない。知らないアプリは今までどおり読む。`requiredAppVersions`は`avatar_animator`と同じ仮の`1.2.8`
- 書くモジュールはMain（ParticleSystem本体の値を`system`、粒の初期値を`InitialModule`）・Emission・Shape・Color over Lifetime・Size over Lifetime・Velocity over Lifetimeと、レンダラー（`ParticleSystemRenderer`）。値はUnityのシリアライズのまま（整数と列挙は数、色は`[r, g, b, a]`、曲線はキー、グラデーションはキー、構造体はオブジェクト）。メッシュ・テクスチャなどオブジェクトの参照は書かない
- レンダラーからは、ライティング・ソート・ライトマップ・参照のフィールドを落とし、描き方（`m_RenderMode`・粒の大きさの上下限・向き・ピボットなど）と`m_Enabled`を残す
- ほかのモジュール（Noise・Collision・Sub Emitters・Trails・Texture Sheet Animationなど）は、書いたパーティクルの有効なものを1つずつ`omitted`の`particleModule`に数える。Sub Emittersで生む側のパーティクルも、独立した1つのパーティクルとして書く
- 置き場所: パーティクルの付いたオブジェクトは、書き出しのギミック除外か空ノードの刈り込みで消えることが多い。そこで、パーティクル自身のオブジェクトから祖先へ順に見て、書き出されたいちばん近いノードに吊るし、そのノードから見た位置・回転・スケールを付ける。書き出されたノードが祖先に無いものは、アバターのルート（`node: -1`）にVRMのモデル空間の姿勢で吊るす。書き出しはルートの位置と回転を捨ててスケールだけを子へ移す（`RootScaleBake`）ので、ルートから見た姿勢にルートのスケールを掛ける。ルートのTRSはglTFに書かれず、ルートを指すノード番号が無いので-1にした
- 姿勢の取り方: モジュールの値は除外の前の複製から読み、そのときのワールドの姿勢を覚えておく。NDMFのビルドなどがオブジェクトを別の親へ移したり消したりした後、空ノードを刈り込む前に、残っているいちばん近いオブジェクトから今の親をたどり直し、覚えた姿勢をそれぞれから見た形に測り直す（`Rebind`）。祖先がルートまで全部消えたものだけを`missingNode`に数える
- 既定でオフのオブジェクトのパーティクルも書き、`active: false`を付ける。アプリは何かがオンにするまで再生しない
- 上限はアバターごとに16個、`maxParticles`の合計2000。階層の順に入れ、超えた分は`effectLimit`に数える。上限は置き場所を決める前に当てる
- マテリアルは`{color, blend, softParticles: false}`の近似にする。色は旧Particlesのシェーダーなら`_TintColor`の2倍、そうでなければ`_Color`、`_BaseColor`、どれも無ければ白。`blend`は`_DstBlend`（One→additive、OneMinusSrcAlpha・OneMinusSrcColor→alpha、Zero→opaque）、無ければシェーダー名の`Additive`、それも無ければレンダーキューで決める。同じマテリアルは1つの記録にまとめ、書かれなかったパーティクルだけが使うマテリアルは落として番号を詰める。`effectTexture`は書いたマテリアルのうちテクスチャを持つものを数える
- 書き出しの流れでは、除外の前の複製から`Capture`し、出力ノードが決まった`afterExport`で`Resolve`して、`AvatarAnimator`の拡張と同じ所で`Inject`する。どこかで例外が出ても書き出しは止めず、拡張を書かずにログに出す
- 省いたものがあれば、書いたパーティクルが1つも無いときも、書き出しの結果に「アプリに渡すパーティクルで省いたもの: 未対応のモジュールN個、書き出せなかったパーティクルN個、テクスチャN枚」を出す

## テクスチャを今回書かない理由

設計ではマテリアルの`texture`はglTFの`textures`の番号になっている。しかし、パーティクルのテクスチャをglTFの`images`・`textures`に足すと、次の問題がある。

- アプリのテクスチャの上限（`LilToonMobileProfile.MaximumTextures`）を数え直す必要があり、超えると拡張を知らない今のアプリも読めなくなるおそれがある
- BINのチャンクが変わり、パーティクルの無いアバター以外では今の書き出しとバイト単位で比べられなくなる

設計どおりに書けないので、`texture`はスキーマにだけ置いて書かず、テクスチャを持つマテリアルを`effectTexture`に数える。書き方は次の2案から選ぶ。

- A: 拡張の中に自前の`bufferView`で画像を持つ。今のアプリの画像の数と上限は変わらない
- B: glTFの`textures`に足し、上限を超えるときは落として`effectTexture`に数える

## 残る課題

- パーティクルのオブジェクトを除外すると、FXがそのオブジェクトの`activeSelf`を動かす曲線は、`VRVLOG_avatar_animator`では`missingNode`に数えられる。パーティクルの出し入れをアプリで再現するには、消したパーティクルのオブジェクトの`active`を、`enabled`の`particle`の対象（パーティクルの番号）に読み替える必要がある。これは`AvatarAnimatorCapture`の側の単位で入れる
- Shapeのメッシュ、メッシュで描くレンダラーのメッシュは参照なので書かない
- `trails`・`lines`・`lights`は7-2で同じ`schemaVersion`の中に足す
- 姿勢は位置・回転・スケールに分けて書くので、負のスケール（鏡写し）やせん断を含む親の下では元の形にならない

## 採らなかった案

- パーティクルのオブジェクトをノードとして残す: 今の書き出しでは除外と刈り込みの規則を変えることになり、パーティクルの無いアバターの出力まで変わる
- モジュールを名前を付け直した独自の形で書く: 設計がUnityのシリアライズ名で書くと決めている。値の意味をアプリがUnityの資料で引ける

## 今の出力への影響

ParticleSystemの無いアバターは、今までとバイト単位で同じ。あるアバターは、ルートの`extensions`・`extensionsUsed`・`asset.extras.VRVLOG_requiredAppVersions`に`VRVLOG_avatar_effects`が足されるだけで、BINのチャンクは変わらない。

## 確かめたこと

- EditModeのテスト（`AvatarEffectsExportTests`と`AvatarAnimatorCaptureTests`）をUnity 2022.3.22f1で回した
- パーティクルの無い9体と、パーティクルのある9体の書き出しを、変更前（de3bc92a）と変更後で`Tools/compare-exports.py --allow-added-extension VRVLOG_avatar_effects`で比べた
