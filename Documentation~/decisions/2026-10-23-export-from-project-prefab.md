# Projectでアバターのprefabを右クリックして書き出す・転送する

- 日付: 2026-10-23
- 対象: `Editor/ExportRendererSelection.cs`（`HiddenInScene`）・`Editor/Compatibility/ExporterMenu.cs`（`Assets`・`AssetsPriority`・`ValidateOrder`）・`Editor/Compatibility/DependencyDiagnostics.cs`（`OpenForAvatarFromMenu`）・`Editor/LanTransfer/AppTransferWindow.cs`（`OpenFromMenu`・`OpenDetailedFromMenu`）・`Editor/LilToonExportMenuValidation.cs`・`Tests/LanTransfer/VrVlogMenuTests.cs`・`Tests/Editor/VrVlogMenuExportTests.cs`・`Tests/Editor/QuickTransferMatchesDefaultNormalTests.cs`・`Tools/run-unity-compatibility.py`・`README.md`・`CHANGELOG.md`

## 背景

書き出しと **アプリに転送** はHierarchyの右クリックとメニューバーからしか始められず、prefabとして保存したアバターはいったんシーンに置く必要があった。Projectでprefabを右クリックしたときの **VR Vlog** には、`.vrm` を調べる **Validate lilToon VRM** しかなく、prefabを選んでいるときは灰色だった。

## 決定

- Projectの右クリック（Unityの `Assets` メニュー）の **VR Vlog** に、Hierarchyの右クリックと同じ3項目（**アプリに転送（かんたん）...**・**アプリに転送（通常）...**・区切り線・**VRMを書き出す...**）を同じ順・同じ区切りで足す。どれもHierarchyの項目と同じメソッドを呼ぶので、選ばれているものの扱いは1か所にある。
- 選べる条件もHierarchyと同じ `ExporterMenu.OneAvatarRootSelected`（1つだけ選んでいて、Animatorがあり、親にAnimatorが無い）にした。Projectでprefabを選ぶとその一番上のオブジェクトが `Selection.gameObjects` に入るので、追加の判定は要らない。prefabの中のオブジェクト・Animatorの無いprefab・2つ以上の選択・フォルダでは灰色になる。
- prefabアセットは、シーンに置かずにそのまま表情ボードのアバターにする。書き出しは元のアバターを専用のプレビューシーンに複製してから処理する（`NdmfExportPreparation.InstantiateOwnedCopy`）ので、prefabアセットを渡しても開いているシーンに Undo・未保存の変更は出ず、prefabも変わらない。シーンにある同じアバターを書き出したときと同じVRMになる。
- prefabアセットのオブジェクトは、Unityでは表示中でも `activeInHierarchy` が常に偽になる。書き出しはもともと非表示のアバターに対応していて（複製のルートだけを表示にし、中の表示はルートからの `activeSelf` で見る）、prefabアセットもこの経路を通る。そのままだと結果一覧に「非表示のアバター」が出るので、`ExportRendererSelection.HiddenInScene` はprefabアセットではルートの `activeSelf` だけを見るようにした。
- もとからある **Validate lilToon VRM** は、アバターに対する項目ではないので区切り線のあとの末尾に移した。`Assets` メニューの優先度は、もとの位置と同じUnityの既定値1000から始める。

## 確認

- `VrVlogMenuTests.TheProjectWindowListsTheHierarchyEntries` で、Projectの項目がHierarchyと同じ名前・同じ相対の優先度・同じメソッドで、**Validate lilToon VRM** がそのあとの区切り線の先にあることを確かめる。
- `VrVlogMenuTests.TheProjectWindowEntriesNeedOneAvatarPrefab` で、アバターのprefabだけで選べ、Animatorの無いprefab・prefabの中のオブジェクト・2つのprefab・フォルダでは選べないことを確かめる。
- `VrVlogMenuExportTests.ExportFromAPrefabsContextMenuOpensTheExportWindowWithThatPrefab` で、Projectの **VRMを書き出す...** がそのprefabを指定した表情ボードを開き、シーンに何も足さないことを確かめる。
- `QuickTransferMatchesDefaultNormalTests.APrefabAssetMatchesTheSameAvatarInTheSceneAndStaysUnchanged` で、prefabアセットから書き出したVRMがシーンの同じアバターから書き出したVRMと一致し、書き出したあともシーンが未保存にならず、prefabのファイルが変わらないことを確かめる。
- Unity 2022.3.22f1で、変更前と変更後のProjectの右クリックメニューを撮って比べ、変更後のメニューからダミーのアバターのprefabを書き出した（PRに画像）。

## 検討して採らなかった案

- prefabを開いているシーンに一時的に置いて書き出し、終わったら消す案は採らなかった。置いた時点でシーンが未保存になり、Undoの履歴も残るため。書き出しは複製を作るので、置く必要もない。
- 選べる条件をVRChatのAvatar Descriptorがあるかで決める案は採らなかった。Hierarchyの右クリックとメニューバーがAnimatorで決めていて、同じアバターがHierarchyとProjectで選べたり選べなかったりするため。
- Projectの項目を別のサブメニュー（例 **VR Vlog → Prefab**）に分ける案は採らなかった。Hierarchyと違う場所になり、同じ操作を探し直すことになるため。

## 欠点

- Projectで選べるのはprefabの一番上がアバターのときだけで、prefabの中に置いたアバター（一番上が空のオブジェクトなど）はシーンに置いてから右クリックする。
- シーンにあるものを探す処理は、prefabアセットからは見つからない。FaceEmoのランチャーをアバターの外の別のオブジェクトとしてシーンに置いている場合、prefabから書き出すとFaceEmoの表情が入らない。Modular Avatarの解析は除外が無いときに元のアバターに対して行うので、prefabアセットで同じ結果になるかはMA側の実装に依存する。テストでそろうことを確かめたのはAnimatorとSkinned Mesh Rendererだけのアバターまで。
- `Assets` メニューはUnityのメニューバーの **Assets → VR Vlog** にも同じ項目として出る。そちらは選んでいるものを対象にするので、Hierarchyでアバターを選んでいるときも選べる（動作はメニューバーの **VR Vlog** と同じ）。
