# Contact SenderとContact Receiverの表を新しい拡張に書く

- 日付: 2026-11-16
- 対象: `Editor/AvatarContactsExport.cs`（新規）、`Formats/Runtime/AvatarContactsData.cs`（新規）、`Schema/VRVLOG_avatar_contacts.1.schema.json`（新規）、`Editor/UniVrmOneClickExporter.cs`、`Editor/Locales/ExporterLocale_*.json`、`Formats/Runtime/AvatarFormatVersions.cs`、`Formats/README.md`

## 背景

VRChatのContact Receiverは、Contact Senderなどの形と重なるとParameterの名前のパラメーターに値を書く。FXはこれを読んで、頭をなでたときの表情などを切り替える。今までの書き出しはContactを持つオブジェクトを書き出しから外していて、アプリはどのパラメーターがContactから来るかも、重なりを解くための形も知らなかった。

## 決めたこと

新しい拡張`VRVLOG_avatar_contacts`に、SenderとReceiverの表を書く。形は設計書の段6のとおり。

- `senders`と`receivers`の1件が1つのコンポーネントで、ヒエラルキーの順に並べる。有効か無効かは問わない（`VRVLOG_avatar_physbones`と同じ）。EditorOnlyのタグが付いたオブジェクトの下のもの、利用者が書き出しから外したオブジェクトの下のものは読まない
- 共通のキーは`node`・`shape`（`sphere`か`capsule`）・`radius`・`height`・`position`・`rotation`（x, y, z, w）・`tags`・`active`。Receiverは`type`（`constant`・`onEnter`・`proximity`）・`parameter`・`allowSelf`・`allowOthers`・`localOnly`・`minVelocity`を足す
- `node`はContactのRoot Transform（未設定ならContactの付いたオブジェクト）のノード。書き出しがそのオブジェクトを外したとき（ギミックの除外、空のノードの自動除外）は、書かれている一番近い祖先のノードにし、`position`と`rotation`をそのノードの空間で書き直す。祖先が1つも書かれていなければ`-1`（アバターの根＝VRMのモデル空間）にする。`AvatarEffectsExport`のエフェクトと同じ置き方
- `radius`と`height`はノードの単位で書く。アプリはノードのワールドのスケールの絶対値の一番大きい成分を掛ける。VRChatがRoot Transformのスケールで形を大きくするのに合わせた。ノードを祖先に付け替えたときは、間のスケールを掛けてから書く
- `height`はカプセルのY方向の全長（両端の半球を含む）で、球は0
- `active`は書き出したときにContactが働いているか。オブジェクトが非アクティブ（`activeInHierarchy`が偽）か、コンポーネントが無効のときに`false`を書く。`VRVLOG_avatar_effects`の`active`と同じく必須の真偽値で、全件に書く。エフェクトはオブジェクトの状態だけを見るが、Contactはコンポーネントの有効無効でも止まるので両方を見る。アプリは`false`のContactを、FXが有効にするまで重なりの判定に入れない。値はビルドの前（`Capture`）に読み、`Rebind`では読み直さない。書き出しはビルドの途中でコピーのオブジェクトを付けたり消したりする（既定でオフのメニューのトグルを点ける、MAの固定の見た目を焼く）ので、読み直すと利用者が置いた状態と変わる。エフェクトも同じ時点の値を書く
- Modular AvatarのBone Proxyが付いたオブジェクトの下のContactは、Bone Proxyの取り付け先の下に移したものとして置く。ギミックの除外はBone Proxyが働く前にオブジェクトを消すので、そのまま読むと移す前の位置（多くはアバターの根、`node`が`-1`）に書かれる。MAの`BoneProxyProcessor`と同じく、取り付け先の子にしてワールドの姿勢を保ち、モード（`attachmentMode`）が保たない位置と回転を0にし、`matchScale`のときはスケールを1にする。祖先をたどるときも取り付け先から先をたどり、取り付け先がまたBone Proxyを持つときはその取り付け先へ続ける
- 書き出しの途中でModular Avatarなどがパラメーターの名前を変えたときに合うよう、ビルドの後にコピーに残っているContactを読み直す（`Rebind`）。ビルドの前に外したContactは、ビルドの前に読んだ値のまま書く
- Receiverは先頭から64個までを書き、65個目からは数えて`omitted`に`kind`が`contactLimit`の1行で書く。書き出しの結果にも1行の警告を出す（4言語）
- 知らない形・知らないReceiver Type（新しいSDK）のContactは読まない
- `schemaVersion`（1）と`omitted`を持たせる。`VRVLOG_avatar_effects`と同じく、何を外したかは拡張の中で数える
- 読み手（`AvatarContactsData.Read`）はキーの過不足、負の`radius`・`height`・`minVelocity`、数の合わない`position`・`rotation`、空のタグ、知らない`shape`・`type`・`schemaVersion`、64個を超えるReceiverを拒む。`Write`も同じ確認を通す
- 拡張を読む最初のアプリの版は仮に1.2.8とする（`AvatarFormatVersions`）。ほかの段6の拡張と同じ扱い

設計書の段6の形に、`schemaVersion`と`omitted`を足した。

## 古い出力との関係

- 古いアプリは知らない拡張を読まないので、今と同じに出る
- Contactを持たないアバターのファイルは変わらない
- 読み取り・組み立て・書き込みで例外が出たときは、ログに出して拡張を書かない。そのときのファイルは今までと同じになる

## 採らなかった案

- ビルドの後だけで読む: ギミックの除外がビルドの前にContactのオブジェクトを消すので、表がほぼ空になる
- `omitted`をFXの拡張（`VRVLOG_avatar_animator`）に書く: Contactの表を読まないアプリにも関わりのない数が増える。エフェクトの`omitted`と同じく、表の拡張に置いた
- 根のノードが書かれていないContactを表から外す（PhysBoneの表のやり方）: Contactのオブジェクトはギミックの除外で消えることが多く、外すとほとんどのReceiverが動かない。祖先に付け替えれば位置は保てる

## 確かめたこと

- EditModeテスト（Unity 2022.3、batchmode、VRChat SDK入りの使い捨ての複製）: `AvatarContactsExportTests`（新規7件、Bone Proxyの5件を足して12件）と、書き出しの流れを共有する`AvatarEffectsExportTests`・`AvatarPhysBonesExportTests`
- 回帰の比較: PRの本文に書く

## 残したこと

- アプリが表を読み、重なりを解いてReceiverのパラメーターに値を入れる（アプリ側の単位）
- FXでContactの有効無効を切る曲線（`enabled`の`component`が`contact`）は段6の後の単位で足す
- 書き出したときに無効になっているContactは`active`で見分ける（2026-11-16にEpicで決めて足した）。FXで有効無効を切る`enabled`は上のとおり後の単位
- Bone ProxyのAsChildKeepWorldPoseが入れ子になったとき（取り付け先が別のBone Proxyの下にある）、MAは子のワールドの位置を戻すが、ここでは親と一緒に動かす。取り付け先が見つからないときは移さずに元の場所に置く（MAはその場で位置と回転を0にする）。A1〜A11で起きないので今は直さない（`2026-11-16-exporter-leftovers-check.md`）
- ビルドの前に外したContactのパラメーターの名前は、Modular Avatarが後で変えても追わない
- VRChatがカプセルの`height`を半球を含む全長として扱うこと、スケールを一番大きい成分で掛けることは、SDKの説明に合わせた前提で、VRChatの中で測っていない
