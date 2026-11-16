# 書けなかった表をFXが指さないようにし、Contactの表で外したものを数える

- 日付: 2026-11-16
- 対象: `Editor/UniVrmOneClickExporter.cs`、`Editor/AvatarContactsExport.cs`、`Editor/AvatarEffectsExport.cs`、`Editor/ExportTestHooks.cs`、`Formats/Runtime/AvatarContactsData.cs`、`Schema/VRVLOG_avatar_contacts.1.schema.json`、`Formats/README.md`

## 背景

段7-5（#833）と段6-3b（#829）のレビューで後に回したP2のうち3件。

1. FXの`enabled`の対象（`component`が`particle`・`trail`・`line`・`light`・`contact`）は、`VRVLOG_avatar_effects`と`VRVLOG_avatar_contacts`の表の位置（`index`）を指す。FXは表を組み立てた後に作り、表をファイルに書く（`Inject`）のはFXを書いた後だった。表の`Inject`で例外が出ると表は書かれず、FXの対象はファイルに無い表の位置を指したまま残っていた
2. Senderが4096個を超えると、`AvatarContactsData.Write`が上限で例外を出し、Contactの表が丸ごと書かれなかった
3. 知らない`shape`・Receiver TypeのContact、65個目以降のタグと1024文字を超えるタグ、1024文字を超える`parameter`は、数えずに落としていた

## 決めたこと

### 書けなかった表をFXが指さない

- FX、エフェクト、Contactの順に書くのは今までどおり。エフェクトかContactの表を書けなかったときは、その表を外してFXを作り直し（PhysBoneと制約の表が書けなかったときと同じ作り直し）、FXの前のバイト列からFX・エフェクト・Contactを書き直す。作り直しは1回だけ
- 書く順を変えない（表を先に書く）案は採らなかった。`extensionsUsed`と`extensions`の並びが変わり、Contactを持つA1・A2・A10の出力が変わる
- 作り直したFXでは、外した表を指していた曲線は表が無いときと同じ種類で数える（部品自身の曲線は`enabled`か`other`、祖先の`activeSelf`は`missingNode`）
- テストのために、表の`Inject`の始めで例外を出せるフック（`ExportTestHooks.TableFaultInjection`）を足した。書き出しでは`null`

アプリ（VR-Vlogのdevelop、`com.vrvlog.avatar-formats`は1dae76a6）は、表に無い`index`を指す対象を何も動かさず、警告を1行出す（`AvatarAnimatorPlayer.BindEffects`・`BindContacts`）。表そのものが無いときも同じで、落ちはしない。今回の変更で、その警告の元になるファイルを書かなくなる。

### Contactの表で外したものを数える

`omitted`に種類を足した。並びは`contactLimit`・`senderLimit`・`unknownShape`・`unknownReceiverType`・`tagLimit`・`parameterLength`。

- `senderLimit`: 先頭から4096個を超えたSender。`Capture`で上限で切る。Receiverの`contactLimit`と同じく、知らない形のものは数に入れない
- `unknownShape`: 知らない形のContact（SenderとReceiverの両方）。Contactごと書かない
- `unknownReceiverType`: 知らないReceiver TypeのReceiver。Receiverごと書かない
- `tagLimit`: 1つのContactの65個目以降のタグと、1024文字を超えるタグの数。空のタグは何にも当たらないので、今までどおり書かず、数えもしない
- `parameterLength`: `parameter`が1024文字を超えたので空で書いたReceiverの数
- 書き出しの結果の警告は今までどおりReceiverの上限だけに出す。ほかは数がまれで、アプリの動きへの影響も小さい

### 読み手は新しい版が足した種類を読み飛ばす

`AvatarContactsData.Read`は、知らない`kind`で例外を出していた。アプリは例外が出るとContactの表を丸ごと捨てる（Receiverのパラメーターが既定値のまま）。種類を足すたびに古い読み手が表を捨てないよう、`VRVLOG_avatar_animator`の`omitted`と同じく、知っている種類の後ろに新しい版が足した種類を置けるようにした（各1回・64種類まで）。

この変更の前の読み手（アプリのdevelopが固定している1dae76a6）は、今回足した種類が出たファイルでContactの表を捨てる。今回の種類は、4096個を超えるSender、知らない形、65個以上のタグなどを持つアバターにしか出ない。アプリが固定を上げれば読める。

## 古い出力との関係

- 表を書けたファイル、上限や長さに当たらないContactのファイルは今までと同じ
- 表の`Inject`で例外が出たときだけ、FXの拡張が今までと変わる（指す先の無い対象が消える）

## 確かめたこと

PRの本文に書く。
