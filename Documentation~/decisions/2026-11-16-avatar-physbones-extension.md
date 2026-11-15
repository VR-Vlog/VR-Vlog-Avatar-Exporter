# PhysBoneのパラメーターの表を新しい拡張に書き、コントローラーが持つPhysBoneのパラメーターをbuiltinにする

- 日付: 2026-11-16
- 対象: `Editor/AvatarPhysBonesExport.cs`（新規）、`Formats/Runtime/AvatarPhysBonesData.cs`（新規）、`Schema/VRVLOG_avatar_physbones.1.schema.json`（新規）、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Formats/README.md`

## 背景

VRChatは、PhysBoneのParameterに名前（接頭辞）があると、その名前に`_IsGrabbed`・`_IsPosed`・`_Angle`・`_Stretch`・`_Squish`を付けたパラメーターに値を書く。FXはこれを読んで尻尾を掴んだときの表情などを切り替える。これまでの書き出しは、FXが宣言するこれらのパラメーターを`VRVLOG_avatar_animator`の`origin`が`animator`（＝既定値のまま動かない）として書いていた。アプリが値を入れるには、どれがVRChatの書く名前かと、値を作るためのPhysBoneの設定がファイルに要る。

揺れ自体は今までどおりVRMのSpringBone（`PhysBoneSpringExport`）が受け持つ。

## 決めたこと

新しい拡張`VRVLOG_avatar_physbones`に、PhysBoneの表を書く。

- 1件が1つのPhysBoneのコンポーネントで、ヒエラルキーの順に並べる。有効か無効かは問わない
- `node`はPhysBoneの根（Root Transform、未設定ならPhysBoneの付いたオブジェクト）のノード。根のノードが書かれていないPhysBoneは表に入れない。1件も残らなければ拡張を書かない
- `parameter`はParameterの接頭辞。無ければ空文字で、行は残す。段6のFXの`enabled`（`component`が`physBone`、`index`が表の番号）が同じ表を指すため
- `maxStretch`・`maxSquish`はMax StretchとMax Squishの基本値（カーブは含めない）。`allowGrabbing`・`allowPosing`はVRChatの設定がTrueかOtherのとき真（古いSDKのboolはそのまま）
- `limitAngle`は`_Angle`が1になる角度（度）で、常に180を書く。VRChatは`_Angle`をPhysBoneの角度の制限に関係なく0〜180度で正規化するため。アプリは設計どおり`limitAngle`で割ればVRChatと同じ値になる
- `schemaVersion`（1）を持たせる。ほかの`VRVLOG_avatar_*`と同じく、読み手が知らない版を拒めるように
- FXのパラメーターのうち、コピーの上のどれかのPhysBone（無効のものも含む）の接頭辞に5つの接尾辞を付けた名前と一致するものを、`VRVLOG_avatar_animator`で`origin`を`builtin`にする。名前はVRChatが持つもので、無効のPhysBoneにアプリが入れる0は既定値と同じになる
- 読み手（`AvatarPhysBonesData.Read`）はキーの過不足、負の`maxStretch`・`maxSquish`、範囲外の`limitAngle`、知らない`schemaVersion`を拒む。`Write`も同じ確認を通す
- 拡張を読む最初のアプリの版は仮に1.2.8とする（`AvatarFormatVersions`）。`VRVLOG_avatar_constraints`と同じ扱い

設計書の段6は、この拡張のキーを`node`・`parameter`・`maxStretch`・`maxSquish`・`allowGrabbing`・`allowPosing`・`limitAngle`と書いている。これに`schemaVersion`を足した。

## 古い出力との関係

- VRMのSpringBoneは変えない。古いアプリは知らない拡張を読まないので、今と同じに出る
- `origin`が`animator`から`builtin`に変わるのは、PhysBoneの名前と一致するFXのパラメーターだけ。値の既定値は変えない。今のアプリは`builtin`の名前を知らなければ既定値のまま置く
- PhysBoneの読み取りは`PhysBoneSpringExport.Convert`の前に行う。読み取り・組み立て・書き込みで例外が出たときは、ログに出して拡張を書かない。そのときのファイルは今までと同じになる

## 採らなかった案

- `limitAngle`にPhysBoneの角度の制限（Max Angle）を書く: VRChatの`_Angle`はこの制限で割らないので、アプリの値がVRChatとずれる
- 有効なPhysBoneだけを`builtin`にする: FXでPhysBoneを有効にしてから掴む作りでは、書き出したときに無効でもVRChatが名前を持つ
- 書き出しの段（`ExportStage`）を増やす: Constraintの表と同じく、拡張が無くても今と同じファイルになるので、try/catchで包むだけにした

## 確かめたこと

- EditModeテスト（Unity 2022.3、batchmode）: 結果は下の回帰の比較とあわせてPRに書く
- 回帰の比較: 手元のテストのアバター4体を、この変更の前と後で書き出して`Tools/compare-exports.py`で比べる

## 残したこと

- アプリの単位6-5が表を読み、`builtin`のPhysBoneのパラメーターに値を入れる
- FXでPhysBoneの有効無効を切る曲線（`component`が`physBone`）は段6の後の単位で足す
- Max StretchとMax Squishのカーブ、Grab Movement・Snap To Handなど掴み方の設定は表に入れていない
