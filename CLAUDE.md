# このリポジトリで作業するときの決まり

## 守ること

- アプリに転送（かんたん）は、表情ボードを開いたまま何も変えずに書き出す通常と同じ VRM を書き出す。設定項目は表情ボードの既定値（`ChangeAvatar` と `Prepare`）に 1 か所だけ足し、かんたん側に項目ごとの処理を書かない。`interactive` は確認を出すかどうかだけを決め、書き出す中身を変えない。一致は `QuickTransferMatchesDefaultNormalTests` で確かめる。

## テスト

- EditMode テストは `Tests/Editor` にあり、Unity 2022.3 で回す。
- `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES` に載せたクラスは、そのクラスのテストメソッドをすべてケース数つきで書く。クラス単位で回した結果と突き合わせるので、メソッドを足す・消す・名前を変えるときはここも直し、`Tools/test-compatibility-tooling.py` を回す。
