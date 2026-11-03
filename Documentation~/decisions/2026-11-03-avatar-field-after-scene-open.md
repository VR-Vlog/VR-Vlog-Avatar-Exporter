# シーンを開いたあと、アバター欄に消えたアバターの名前を残さない

- 日付: 2026-11-03
- 対象: `Editor/LanTransfer/AppTransferPanel.cs`・`Tests/LanTransfer/CloudTransferWindowTests.cs`

## 背景

「アプリに転送」の画面を開いたままシーンを開くと、アバターは無くなるのに、アバター欄に名前が残っていた。

`RefreshForm` は `avatarField.value != avatar` のときだけ欄を更新していた。Unity の `!=` は破棄されたオブジェクトを null と同じに扱うので、破棄された値と null は等しいとみなされ、更新されない。`ObjectField.SetValueWithoutNotify` の中の比較も同じなので、null を入れても表示は書き直されない。

## 決定

- 欄の値が破棄されたオブジェクトのときは、値を null にして `objectType` を一度変えて戻す。`objectType` の変更で欄が書き直される。公開 API だけで済み、欄を作り直すより変更が小さい。
- 欄の外の動き（書き出しやボタンの無効化）は変えない。

## 確かめたこと

`TheAvatarFieldStopsShowingADestroyedAvatar` は、実際に空のシーンを開いてアバターを破棄し、欄のラベルの文字を見る。元のコードではアバターの名前が残って失敗し、この修正で通る。Unity 2022.3.22f1 のバッチで回したもので、画面の見た目は確かめていない。
