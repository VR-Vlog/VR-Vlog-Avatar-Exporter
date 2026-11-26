# PhysBoneのパラメーターの表を新しい拡張に書き、コントローラーが持つPhysBoneのパラメーターをbuiltinにする

- 日付: 2026-11-16
- 対象: `Editor/AvatarPhysBonesExport.cs`（新規）、`Formats/Runtime/AvatarPhysBonesData.cs`（新規）、`Schema/VRVLOG_avatar_physbones.1.schema.json`（新規）、`Editor/AvatarAnimatorCapture.cs`、`Editor/UniVrmOneClickExporter.cs`、`Formats/Runtime/AvatarFormatVersions.cs`、`Formats/README.md`

## 背景

VRChatは、PhysBoneのParameterに名前（接頭辞）があると、その名前に`_IsGrabbed`・`_IsPosed`・`_Angle`・`_Stretch`・`_Squish`を付けたパラメーターに値を書く。FXはこれを読んで尻尾を掴んだときの表情などを切り替える。これまでの書き出しは、FXが宣言するこれらのパラメーターを`VRVLOG_avatar_animator`の`origin`が`animator`（＝既定値のまま動かない）として書いていた。アプリが値を入れるには、どれがVRChatの書く名前かと、値を作るためのPhysBoneの設定がファイルに要る。

揺れ自体は今までどおりVRMのSpringBone（`PhysBoneSpringExport`）が受け持つ。

## 決めたこと

新しい拡張`VRVLOG_avatar_physbones`に、PhysBoneの表を書く。

- 1件が1つのPhysBoneのコンポーネントで、ヒエラルキーの順に並べる。有効か無効かは問わない。EditorOnlyのタグが付いたオブジェクトの下のものは、VRChatがアップロードのときに消すので読まない（`AvatarEffectsExport`と同じ）
- `node`はPhysBoneの根（Root Transform、未設定ならPhysBoneの付いたオブジェクト）のノード。根のノードが書かれていないPhysBoneは表に入れない。1件も残らなければ拡張を書かない
- `parameter`はParameterの接頭辞。無ければ空文字で、行は残す。段6のFXの`enabled`（`component`が`physBone`、`index`が表の番号）が同じ表を指すため
- `maxStretch`・`maxSquish`はMax StretchとMax Squishの基本値（カーブは含めない）。PhysBone 1.0の`maxSquish`は0にする。`allowGrabbing`・`allowPosing`はVRChatの設定がTrueかOtherのとき真（古いSDKのboolはそのまま）
- `limitAngle`は`_Angle`が1になる角度（度）で、常に180を書く。VRChatは`_Angle`をPhysBoneの角度の制限に関係なく0〜180度で正規化するため。アプリは設計どおり`limitAngle`で割ればVRChatと同じ値になる
- `schemaVersion`（1）を持たせる。ほかの`VRVLOG_avatar_*`と同じく、読み手が知らない版を拒めるように
- FXのパラメーターのうち、表の行（無効のPhysBoneも含む）の接頭辞に5つの接尾辞を付けた名前と一致するものを、`VRVLOG_avatar_animator`で`origin`を`builtin`にする。名前はVRChatが持つもので、無効のPhysBoneにアプリが入れる0は既定値と同じになる。表に行が無いPhysBone（根のノードが空のノードの自動除外で消えたなど）の名前は、アプリが値を作る材料を持たないので`animator`のまま
- `builtin`はFXの拡張を組み立てる（`Resolve`）ときに、表を組み立てて確かめた後で決める。表を書けなかったときは、Constraintの表と同じくFXの拡張を`builtin`なしで組み直す
- 読み手（`AvatarPhysBonesData.Read`）はキーの過不足、負の`maxStretch`・`maxSquish`、範囲外の`limitAngle`、知らない`schemaVersion`を拒む。`Write`も同じ確認を通す
- 拡張を読む最初のアプリの版は仮に1.2.8とする（`AvatarFormatVersions`）。`VRVLOG_avatar_constraints`と同じ扱い

設計書の段6は、この拡張のキーを`node`・`parameter`・`maxStretch`・`maxSquish`・`allowGrabbing`・`allowPosing`・`limitAngle`と書いている。これに`schemaVersion`を足した。

## 古い出力との関係

- VRMのSpringBoneは変えない。古いアプリは知らない拡張を読まないので、今と同じに出る
- `origin`が`animator`から`builtin`に変わるのは、PhysBoneの名前と一致するFXのパラメーターだけ。値の既定値は変えない。今のアプリは`builtin`の名前を知らなければ既定値のまま置く
- PhysBoneの読み取りは`PhysBoneSpringExport.Convert`の前に1回だけ行う。読み取り・組み立て・書き込みで例外が出たときは、ログに出して拡張を書かず、FXの`origin`も`animator`のままにする。そのときのファイルは今までと同じになる
- Expression Parametersで宣言した名前は、今までどおり`origin`が`expression`のまま。メニューから操作できるパラメーターとして書く

## 採らなかった案

- `limitAngle`にPhysBoneの角度の制限（Max Angle）を書く: VRChatの`_Angle`はこの制限で割らないので、アプリの値がVRChatとずれる
- 有効なPhysBoneだけを`builtin`にする: FXでPhysBoneを有効にしてから掴む作りでは、書き出したときに無効でもVRChatが名前を持つ
- FXを読むときにコピー上のすべてのPhysBoneから`builtin`を決める: 表に行が無いPhysBoneの名前まで`builtin`になり、表を書けなかったときに今までと違うファイルになる
- 書き出しの段（`ExportStage`）を増やす: Constraintの表と同じく、拡張が無くても今と同じファイルになるので、try/catchで包むだけにした

## 確かめたこと

- EditModeテスト（Unity 2022.3、batchmode、GPUあり、VRChat SDK入りの使い捨ての複製）: `AvatarPhysBonesExportTests`（新規5件）、`AvatarAnimatorCaptureTests`、`AvatarConstraintsExportTests`、`PhysBoneSpringExportTests`
- 回帰の比較: 手元のテストのアバター4体を、この変更の前と後で書き出して`Tools/compare-exports.py`で比べた。`VRVLOG_avatar_physbones`が足された以外の違いは0件だった。2体に表が足され（1行ずつ、VRMのSpringBoneの数と同じ）、PhysBoneの無い2体はファイルが同じだった。4体ともParameterを持つPhysBoneが無いので、`builtin`はEditModeテストだけで確かめている
- 敵対的レビュー: P0は無し。P1の1件（表を書けなかったときもFXの`builtin`が残る）と、P2（表に行が無いPhysBoneの名前の`builtin`、PhysBoneの読み取りが2回、EditorOnly、PhysBone 1.0の`maxSquish`、`parameter`の長さ、テストの穴）を直した

## 残したこと

- アプリの単位6-5が表を読み、`builtin`のPhysBoneのパラメーターに値を入れる
- FXでPhysBoneの有効無効を切る曲線（`component`が`physBone`）は段6の後の単位で足す
- FXの`enabled`が表の番号を指すための、コンポーネントから表の番号を引く手段（段6の後の単位で足す）
- 表を書けなかったときにFXを組み直す経路は、例外を起こすテストの仕掛けが無いので、テストで確かめていない
- Max StretchとMax Squishのカーブ、Grab Movement・Snap To Handなど掴み方の設定は表に入れていない
