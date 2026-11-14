# NDMF のビルドが要らない VRChat のアバターでも、書き出しが読む前に EditorOnly のオブジェクトを外す

- 日付: 2026-11-15
- 対象: `Editor/NdmfExportPreparation.cs`・`Editor/AvatarBuildCallbacks.cs`・`Tests/Editor/UploadedAvatarPlayablesTests.cs`・`Tools/run-unity-compatibility.py`

## 背景

2026-11-14 の決定（`2026-11-14-upload-build-steps.md`）で、VRChat SDK に登録されたビルド前処理を書き出しの一時コピーにかけるようにした。NDMF のビルドが要らないアバターでは、EditorOnly のタグのオブジェクトを消すのは VRChat SDK のコールバック（-1024）で、書き出しが表情などを読んだ後に走る。

配布されているアバターと衣装の組み合わせ 464 組を変更の前後で書き出して比べたところ、2 組で次のことが起きた。

- まばたきなどの表情の経路が、EditorOnly のタグの付いた非アクティブなメッシュ（差し替え用の体など）に乗っていた。書き出しはそのメッシュも読んで経路に入れる。
- 読んだ後に SDK のコールバックがそのメッシュを消し、最適化の後の検査（`ExportOptimizationBindings.ValidateAndApply`）が「表情が失われた」として失敗する。
- 書き出しは最適化の段ごと外して書き出し直すので、EditorOnly のオブジェクトは消えないまま VRM に入り、結果一覧には最適化を外したという的外れな警告が出る。

NDMF のビルドをかけるアバターでは、NDMF が Resolving の始め（-11000 のフックの中）で EditorOnly のオブジェクトを消すので、書き出しは消えた後のコピーを読んでいた。アップロードでも、NDMF が入っていれば同じ位置で消える。

## 決定

- NDMF のビルドが要らない VRChat のアバター（VRC Avatar Descriptor があるもの）では、-11000 より前のコールバックの後、VRCFury の前に、書き出しが EditorOnly のタグのオブジェクトを一時コピーから消す（`NdmfExportPreparation.RemoveEditorOnlyObjects`）。NDMF の `RemoveEditorOnlyPass` と同じく、ルートの下のタグの付いたオブジェクトを子ごと消す。
- 書き出し・プレビュー・最適化を外した書き出し直しのどれでも消す。NDMF のビルドをかけるアバターと同じ扱いになる。
- VRC Avatar Descriptor が無いアバターでは消さない。アップロードが無く、これまでの出力を変える理由が無いため。
- 失敗のたびに書き出し直す「処理をかけずに書き出す」経路（`skipProcessing`）では、これまでどおり何も消さない。

## 検討した代替案と不採用理由

- 読んだ後のコールバックで壊れた経路を、失われたものとして黙って落とす: 順番は変えずに済むが、利用者はその表情が無くなったことを知らないまま書き出す。読む前に消せば、そもそも消えるメッシュを読まない。
- VRChat SDK の削除のコールバック（-1024）だけを読む位置より前に動かす: SDK の一覧の順番を書き出しが入れ替えることになり、ほかのコールバックとの前後がアップロードとずれる。NDMF と同じ位置で消すほうが、NDMF の有無で扱いがそろう。

## 出力が変わる場合

- NDMF のビルドが要らない VRChat のアバターで、表情などの経路が EditorOnly のオブジェクトに乗っていたもの: その経路は書き出さない。アップロードされるアバターにも無いもの。これまでは最適化の段ごと外れていたので、最適化がかかるようになる。
- それ以外の、EditorOnly のオブジェクトがあるアバター: 2026-11-14 の決定で VRM から消えるようになったのは同じで、消す時点が読む前に移るだけ。

## 確かめ方

- `UploadedAvatarPlayablesTests` で、インストールした VRChat SDK を使い、NDMF のビルドが要らない VRChat のアバターの書き出しとプレビューの両方で、書き出しが読む時点で EditorOnly のオブジェクト（非アクティブな親の下にあるものを含む）が無いこと、元のアバターは変わらないこと、VRC Avatar Descriptor が無いアバターでは消さないことを確かめる。
- `Tools/run-export-regression.ps1` と `Tools/compare-exports.py` で、EditorOnly のオブジェクトがある組み合わせを変更の前後で書き出して比べた（結果は PR に書く）。
