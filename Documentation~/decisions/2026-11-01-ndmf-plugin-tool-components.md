# MA・AAO の型を持たない NDMF プラグインのコンポーネントでも書き出し用のビルドを走らせる

- 日付: 2026-11-01
- 対象: `Editor/NdmfExportPreparation.cs`・`Tests/Editor/NdmfPreparationTests.cs`

## 背景

書き出しは、複製したアバターに NDMF の `AvatarProcessor` をかけるかどうかを `NeedsProcessing` で決めていた。判定は「MA の `AvatarTagComponent`・AAO の `AvatarTagComponent`・NDMF の `INDMFEditorOnly` のどれかを持つコンポーネントがあるか」だけだった。

NDMF でビルド時に動くツールの中には、どれにも当たらないものがある。

- TexTransTool: `TexTransMonoBase : MonoBehaviour, ITexTransToolTag`（デカール・アトラス化）
- lilycalInventory: 独自の `AvatarTagComponent : MonoBehaviour`（トグル・マテリアルの変更）
- LAC: Avatar Compressor: `TextureCompressor : MonoBehaviour, IEditorOnly`（テクスチャの圧縮・縮小）

これらは VRChat 向けのビルドでは NDMF のプラグインとして適用されるのに、MA のコンポーネントが無いアバターでは書き出しのビルドが始まらず、何も適用されないまま書き出されていた。検証用の Unity プロジェクトで次を確かめた（develop の書き出しと、VRChat SDK のビルド前コールバックをかけた複製との比較）。

- デカール: VRChat では出るが、書き出しでは消える。
- アトラス化: VRChat では 34 枠のマテリアルが 1 つの共通のマテリアルにまとまるが、書き出しでは元のマテリアルのまま。
- マテリアルの色の変更: VRChat では色が変わるが、書き出しでは元の色のまま。
- テクスチャの圧縮: VRChat では 128／256 px に縮むが、書き出しでは元の大きさのまま。

## 決定

- `NeedsProcessing` に「NDMF のプラグインを出しているパッケージのコンポーネントがあるか」を足す。プラグインを出しているかは、読み込まれたアセンブリの `[assembly: ExportsPlugin]` で調べ、そのアセンブリのパッケージ名を集める。コンポーネントのアセンブリのパッケージ名がこの中にあればビルドを始める。
- ツールの型名を並べた一覧は持たない。NDMF が実際に動かすプラグインと同じ範囲を、NDMF 自身の登録の仕組みから引くので、新しいツールが増えても Exporter を直さずに済む。
- Exporter 自身のパッケージは集める対象から外す。テスト用のプラグインがあるとき、Exporter の同梱 UniVRM のコンポーネントでビルドが始まらないようにするため。
- 書き出しで写すパッケージの見つけ方は `PackageInfo.FindForAssembly` を使い、アセンブリごとに結果を覚えておく。`Assets` 直下に置かれた（パッケージでない）ツールは対象にしない。VRChat 向けのツールは VPM のパッケージとして入るのが普通なので、今回はここまでにする。

## 影響

- MA・AAO・NDMF の型を持つアバターの判定は変わらない。
- 上のツールを使っていて MA が無いアバターは、書き出しのビルドが走るようになる。書き出しにかかる時間は MA のあるアバターと同じ程度に増える。
- かんたん転送も同じ `Prepare` を通るので、通常の書き出しと中身は変わらない。
