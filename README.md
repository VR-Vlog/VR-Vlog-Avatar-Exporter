# VR Vlog lilToon VRM Exporter

[日本語](README.md) · [English](README.en.md)

lilToonのアバターを、VR Vlogで使えるVRM 1.0形式に書き出すUnity用ツールです。

MITライセンスで公開しています。

[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## 対応環境

| 必要なもの | バージョン・導入方法 |
| --- | --- |
| Unity | 2022.3 |
| lilToon | 2.0.0〜2.3.4 |
| UniVRM | 0.131.x（0.131.0〜0.131.3で確認済み）。VCC・ALCOMでは自動導入 |
| VR Vlog | 0.1.1（ビルド345）以降 |
| VRChat SDK | 任意。音声再生（VRC Animator Play Audio）を含む表情ファイルを使う場合は3.5.2以上3.11.0未満 |
| Modular Avatar | 任意。Shape Changer・Object Toggle・Material Setter・Material Swap・Mesh Cutterを使うアバターは1.18.0以上1.19.0未満。これらを使わないアバターは版を問いません |

Modular Avatarを使う場合は、事前に導入してください。

**注意**

- lilToonの対応範囲外・プレリリース版では書き出せません。
- 対応版でも、シェーダーの構成が異なる場合は停止します。[互換性の詳細](Compatibility/README.md#supported-liltoon-versions)

---

## インストール

**[インストールページを開く](https://vrvlog.fun/exporter/)** · [変更履歴](CHANGELOG.md)

1. 「VCC / ALCOMに追加」を押す。
2. VCC・ALCOMで、使いたいプロジェクトのパッケージ管理画面を開く。
3. 「VR Vlog lilToon VRM Exporter」を追加する。

<details>
<summary>追加ボタンで開かない場合</summary>

VCC・ALCOMのリポジトリ追加画面に、次のURLを貼り付けてください。

```text
https://vrvlog.fun/exporter/index.json
```

以前の個人側・組織側のURLを登録済みなら、そのまま使えます。

</details>

<details>
<summary>ZIPで手動導入する場合</summary>

1. [インストールページ](https://vrvlog.fun/exporter/)の「ZIPで導入」を開く。
2. エクスポーター・UniGLTF・VRMの3つのZIPをダウンロードする。
3. `Packages` 内の別々のフォルダーへ展開する。
4. 各フォルダーの直下に `package.json` があることを確認する。
5. Unityの読み込みが終わるのを待つ。

同じUniVRMパッケージを重複して導入しないでください。

</details>

---

## 使い方

安定版0.11.15の手順です。

### 1. アバターを準備

Unityで、書き出すアバターと衣装を表示します。親オブジェクトも有効にしてください。

### 2. 書き出し画面を開く

メニューの「VR Vlog → VRMを書き出す」を選びます。

### 3. アバターを指定

Hierarchyのアバター最上位を「アバター」欄に指定します。「書き出しを準備」を押します。

### 4. 表情を追加（任意）

- **追加：** 「候補ファイル」から選択。`.anim` のドロップでも追加できます。
- **確認：** 表情名か「確認」を押すとプレビューできます。
- **喜怒哀楽：** 「標準の表情（喜怒哀楽）」で選択します。

表情を追加せずに保存することもできます。

### 5. 保存

任意の全身ポーズと **ライセンス設定** を確認し、**作者名** を入力して **VRMを保存** を押します。

保存後は「保存先を開く」でファイルを確認できます。

### ライセンス設定

利用条件を設定する場合は、保存欄の **ライセンス設定** を開き、元の利用規約に合わせてプルダウンを選びます。規約URL（VN3等）、著作権表示、第三者ライセンスも入力できます。設定は出力VRMのメタデータに保存されます。設定を開かない場合は、従来の既定値（作者のみ・個人非営利・クレジット表記が必要・表現用途の各許可なし・再配布と改変なし）で書き出します。

---

## 書き出したVRMをスマホで読み込む

### QRコードで転送する

QR対応の最新iPhone TestFlight版が必要です。Android配布版では受信できません。

PCとスマホをインターネットに接続してください。

1. UnityでVRMを保存し、「QRコードでスマホに送る」を押す。
2. プライバシーポリシーを確認し、同意にチェックを入れる。
3. 「アップロードしてQRを表示」を押し、QRコードを表示させる。
4. VR Vlogアプリで「モデルを変更 → PCから受け取る」を開き、QRを読み取る。
5. ファイル名と容量を確認して受け取る。

**注意**

**QRを持つ人はアバターを受け取れます。他人へ見せないでください。**

転送時は、暗号化したVRMをクラウドへ一時保存します。[転送の詳細・同意事項](Documentation~/CloudTransfer.md)

### ファイルを手動で読み込む

1. 書き出した `.vrm` をスマホに保存する。
2. VR Vlogの設定で「lilToon専用表示」をオンにする。
3. 「モデルを変更 → 端末のVRMを選ぶ」でファイルを選ぶ。

---

## 対応範囲

### 対応するもの

- lilToonの材質・画像
- 表情、ポーズ、瞬き、顔トラッキング用の変形
- Modular Avatarで設定した髪・衣装
- PhysBoneの揺れ物

### 再現しないもの・違いが出るもの

- VRChatのギミック全般、一部のシェーダー効果
- 揺れ方の完全な再現。VRM向けの近似になります。
- VRChatと同じ顔・体形。設定によって見た目が異なる場合があります。

他のビューアーではMToon表示になり、見た目や動く表情が異なる場合があります。

---

## 困ったとき

<details>
<summary>エラーで書き出しが止まる</summary>

書き出し画面の「書き出しの詳細」を開きます。エラーが起きた対象と、表示された対処方法を確認してください。

対策候補が表示された場合は、再試行できます。成功したら、見た目を確認してVRMを保存してください。

</details>

<details>
<summary>必要なパッケージが足りない・対応版ではない</summary>

「動作環境の確認」画面に、不足しているパッケージや未対応のバージョンが表示されます。画面の案内に従って導入・更新し、もう一度書き出してください。

</details>

<details>
<summary>衣装が出力されない</summary>

Unityで、その衣装が表示されているか確認してください。非表示の衣装は出力されません。表示しても出ない場合は、アバターへの接続設定を確認してください。

</details>

<details>
<summary>瞬きで目がうまく閉じない</summary>

閉眼用の変形を自動で決められないときは、瞬きなしで保存し、警告に理由を表示します。瞬きを入れるときは次の手順で選んでください。

1. 保存欄にある瞬きの「確認・調整」を開く。
2. 「手動」に切り替え、顔のメッシュと閉眼用の変形を選ぶ。
3. プレビューで目の開閉を確認し、「設定を適用」を押す。
4. VRMを保存する。

</details>

<details>
<summary>解決しない場合の問い合わせ</summary>

[不具合報告](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/issues/new/choose)に、次の内容を添えてください。

- Unityとエクスポーターのバージョン
- 問題が起きるまでに行った操作
- エラー画面でコピーした共有用の診断

VR Vlogアプリ内の問い合わせも利用できます。

</details>

---

## 開発に参加する

脆弱性は[非公開の報告窓口](SECURITY.md)へ連絡してください。再現手順や悪用条件を公開Issue・PRへ投稿しないでください。

不具合報告・改善提案・文書の修正・Pull Requestを受け付けています。[投稿フォーム](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/issues/new/choose)から、使用環境・再現手順、または解決したいことを記載してください。変更の進め方と検証方法は[CONTRIBUTING.md](CONTRIBUTING.md)にまとめています。

| ディレクトリ | 内容 |
| --- | --- |
| `Editor/` | Unity Editorの書き出し処理・設定画面・依存環境診断 |
| `Runtime/` | アバターに付ける設定コンポーネント |
| `Schema/` | VRMに保存する独自拡張の定義とlilToonのカタログ |
| `Tests/`・`Tools/` | Unityテスト、ホスト検証、配布パッケージの生成 |
| `Compatibility/` | 対応する依存パッケージの方針と検証記録 |
| `Documentation~/` | 利用ガイドと明示的な依存修正パッチ |
| `Website/` | VCC／ALCOM向けのインストールページ |
| `ThirdPartyNotices/` | 第三者コード・ライブラリの出典とライセンス |

---

## ライセンス

エクスポーターのコードは[MIT License](LICENSE)で公開しています。第三者コード・依存パッケージの出典とライセンスは、それぞれの文書を参照してください。

- [lilToon由来のシェーダー処理](ThirdPartyNotices/lilToon.md)
- [UniVRM／UniGLTF](ThirdPartyNotices/UniVRM.md)
- [転送用のBouncy Castle・ZXing.Net](ThirdPartyNotices/LanTransfer.md)（0.11.15の安定版にも同梱）
- [Avatar Optimizerの明示的な修正パッチ](Documentation~/DependencyPatches/README.md)・[同梱ライセンス](Documentation~/DependencyPatches/AAO-LICENSE.txt)

アバターなどの素材・掲載画像には各権利者の利用条件が適用されます。[画像の出典・クレジット](Website/assets/README.md)
