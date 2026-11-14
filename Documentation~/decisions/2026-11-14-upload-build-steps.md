# アップロードと同じビルド前処理を書き出しの一時コピーにかけ、そのコピーから Animator・メニュー・パラメーターを読めるようにする

- 日付: 2026-11-14
- 対象: `Editor/AvatarBuildCallbacks.cs`・`Editor/AvatarPlayables.cs`・`Editor/NdmfExportPreparation.cs`・`Tests/Editor/NdmfPreparationTests.cs`・`Tests/Editor/UploadedAvatarPlayablesTests.cs`・`Tests/Editor/Support/VrcAvatar.cs`

## 背景

VRChat のアップロードでは、VRChat SDK が登録されたビルド前コールバック（`IVRCSDKPreprocessAvatarCallback`）を順番（`callbackOrder`）の小さい順に、アバターの複製にかける。NDMF（Transforming まで -11000、Optimizing から -1025）と VRCFury（-10000）もこのコールバックで動く。ほかに、VRChat SDK 自身の EditorOnly のオブジェクトの削除とネットワーク ID の割り当て（-1024）、Modular Avatar と VRCFury の後片付け（`int.MaxValue` など）、VRCFury のパラメーターの圧縮（`int.MaxValue - 100`）などが登録されている。

書き出しは、このうち NDMF と VRCFury だけを自分で呼んでいた（2026-11-01 の決定）。ほかのコールバックはかからないので、書き出しの一時コピーはアップロードされるアバターと同じにならない。アプリ側で Animator をそのまま動かす段（#773・#774）は、アップロードされるのと同じ FX・Gesture・Action・Expression Menu・Expression Parameters を読む必要がある。

## 決定

- VRChat SDK の登録済みのコールバックの一覧（`VRCBuildPipelineCallbacks._preprocessAvatarCallbacks`）を、NDMF や VRCFury と同じくリフレクションで読み、アップロードと同じ順番で一時コピーにかける（`AvatarBuildCallbacks`）。一覧を順番で並べ、同じ順番どうしは一覧の並びを保つのも SDK と同じ。
- NDMF の 2 つのフックと VRCFury のフックは、これまでどおり書き出しが自分の位置で呼ぶ。一覧のほかのコールバックは、その前後の順番の位置で呼ぶ。
  - -11000 より前のもの: NDMF の Generating の前。
  - -11000 と -10000 の間のもの: NDMF の Transforming のあと、VRCFury の前。
  - -10000 と -1025 の間のもの: VRCFury のあと、書き出しが一時コピーを読む前。
  - -1025 以降のもの: NDMF の Optimizing と Finish のあと。
- 書き出しが表情などを読む位置（`afterTransforming`）は、これまでと同じ NDMF の Optimizing の直前に置いたまま。今の書き出しの読み方は変えない。
- 最後まで処理したコピーから、FX・Gesture・Action と Expression Menu・Expression Parameters を返す入口 `NdmfExportPreparation.Playables()` を足した（`AvatarPlayables`）。-1025 以降のコールバックまで走ったときだけ返し、プレビューのように読む位置で止めたときは例外を投げる。レイヤーが既定のままのとき・Custom Expressions がオフのときは null を返す。VRChat がそのレイヤーやメニューを使わないため。
- ビルドの要求（`IVRCSDKBuildRequestedCallback`）かビルド後の処理（`IVRCSDKPostprocessAvatarCallback`）も受け取るコールバックは呼ばない。アップロードの前にプロジェクトの設定を変え、アップロードのあとで戻す組になっているため（lilToon のシェーダーの設定など）で、一時コピーには戻す時点が無い。名前の一覧ではなく、この形で決める。
- プレビュー（`PrepareForPreview` と表情ボードの読み込み）は読む位置で止まるので、-1025 以降のコールバックはかけない。これまでどおり Optimizing もかけない。
- コールバックは VRC Avatar Descriptor があるアバターにだけかける。SDK も同じ。
- コールバックが失敗したとき（例外か false）は、NDMF の処理の失敗と同じく扱う。失敗で止めない設定（既定）では、そのコールバックだけを外して続け、結果一覧に名前（パッケージ名とクラス名）を出す。止める設定では準備の失敗にする。読む位置より後の失敗は Optimizing の失敗として扱い、Optimizing から後を外した書き出しに切り替える。
- NDMF の処理の結果をアセットとして保存する（Serialize）のは、これまでどおり VRCFury を呼ぶときだけにした。コールバックごとに保存はしない。

## 検討した代替案と不採用理由

- `VRCBuildPipelineCallbacks.OnPreprocessAvatar` をそのまま呼ぶ: 2026-11-01 に退けた理由（NDMF の二重実行と、書き出しの後処理を Transforming のあとに挟めないこと）はそのまま残る。一覧を読んで書き出しが自分で並べれば、NDMF と VRCFury を二重に走らせずに、ほかのコールバックをアップロードと同じ順番でかけられる。
- 必要なコールバックを名前で選んでかける: どのツールが何を登録するかは版で変わり、名前の一覧を Exporter が追いかけることになる。SDK の一覧と順番に従うほうが、アップロードとずれにくい。
- 読む位置を最後に動かし、今の読み方も最後まで処理したコピーから読む: Optimizing のあとは AAO がメッシュを結合・BlendShape を消すなど、表情の読み方の前提が変わる。今の出力を変えないため、読む位置は動かさず、最後まで処理したコピーは別の入口で返す。

## 出力が変わる場合

書き出しが読む位置より前に走るのは、-11000 より前・-11000 から -1025 の間のコールバックで、確かめた構成（VRChat SDK 3.10.4・NDMF 1.14.8・Modular Avatar 1.18.7・Avatar Optimizer 1.9.22・VRCFury 1.1463.0・lilToon 2.3.4）では次のものだった。どれも VRM に入るメッシュ・ボーン・マテリアルは変えない。

- VRCFury の準備のフック（VRCFury のコンポーネントが無いアバターでも、VRCFury が入っていれば走る）。元のパスを覚える、失敗を見張る印を付ける（プレイモードの外では何もしない）、VRC Avatar Descriptor の Contact の設定を VRChat SDK のインスペクターで計算し直す。
- VRChat SDK の `PreprocessCallbackBehaviours`。対応するコンポーネントが無ければ何もしない。

読む位置より後のコールバックは VRM に入るオブジェクトを変えうる。

- EditorOnly のタグのオブジェクト: NDMF のビルドをかけるアバターでは、NDMF が Resolving で消すので、これまでも VRM に入らなかった。NDMF のビルドが要らない VRChat のアバターでは、これまで VRM に入っていたが、VRChat SDK の削除（-1024）で消えるようになる。アップロードでも消えるオブジェクトで、NDMF のビルドの有無で扱いが分かれていたのが揃う。
- IEditorOnly のコンポーネント（Modular Avatar・VRCFury の設定など）: 最後に消える。VRM には入らないので、出力は変わらない。
- VRCFury のパラメーターの圧縮: パラメーターが上限を超えたときに FX とパラメーターを書き換える。書き出しが読んだあとなので VRM の表情は変わらない。`Playables()` が返すものには入る。

## 既知の制限

- 人型のアバターの Gesture か FX に、レイヤーが 1 つも無いコントローラーを置いていると、上の VRCFury のフックが開く VRChat SDK のインスペクターが例外をコンソールに出す。アップロードでも同じ例外が出る。書き出しは続き、VRM は変わらない。テストの代わりのコントローラーは、エディターで作ったものと同じく Base Layer を 1 つ持たせた（`VrcAvatar.EmptyController`）。
- コールバックの一覧は VRChat SDK の private のフィールドで、NDMF と VRCFury も同じフィールドを読んでいる。見つからない SDK では、ほかのコールバックをかけずに今までどおり書き出す。`Compatibility/Evidence/VrchatSdkRange/members.md` の各版での有無はまだ調べていない。
- アップロード専用の検査（パラメーターの数の上限など）をするコールバックがあれば、書き出しでも失敗する。既定の設定ではそのコールバックだけを外して書き出すので、書き出しは止まらない。

## 確かめ方

- `NdmfPreparationTests` で、偽のコールバックを登録して、NDMF の各段との順番、NDMF のビルドが要らないときの順番、プレビューで読む位置より後が走らないこと、ビルドの前後を囲むコールバックを呼ばないこと、失敗したコールバックだけを外して警告に出すこと、止める設定では失敗にし、読む位置より後なら Optimizing の失敗として扱うことを確かめる。
- `UploadedAvatarPlayablesTests` で、インストールした VRChat SDK・NDMF・Avatar Optimizer をそのまま使い、次を確かめる。
  - `AvatarPlayables` が、カスタムにしたレイヤーとメニュー・パラメーターを返し、既定のままのものは返さないこと。
  - FX が BlendShape を動かす 2 つのメッシュを AAO で 1 つに結合したアバターで、最後まで処理したコピーの FX の BlendShape のカーブが、すべて結合後のメッシュとそのメッシュにある BlendShape を指していること。
  - 登録された VRChat SDK のコールバックが、書き出しが読んだあとに EditorOnly のオブジェクトを消し、プレビューでは消さないこと。
- `Tools/run-export-regression.ps1` と `Tools/compare-exports.py` で、変更前（develop）と変更後を比べた。アバターは VRChat SDK に付属の人型のサンプルをもとに、何も足さないもの・EditorOnly のオブジェクトを足したもの・その上に Modular Avatar の Merge Animator を足したもの・Avatar Optimizer の Trace and Optimize を足したもの・VRCFury の Toggle を足したものの 5 体を作った。
  - EditorOnly のオブジェクトを足しただけのアバターだけが違い、変更後の VRM からはそのオブジェクトが消えた（上の「出力が変わる場合」のとおり）。書き出した VRM の正面と背面の画像も、そのオブジェクトの範囲だけが違った。
  - ほかの 4 体は、VRM のファイルが変更前と 1 バイトも違わなかった。Modular Avatar のアバターは NDMF のビルドで EditorOnly のオブジェクトを消すので、変更前から VRM に入っていなかった。
- この 5 体は手で作った小さいアバターで、実際に配布されているアバターと衣装の組み合わせでの比較はまだしていない。
