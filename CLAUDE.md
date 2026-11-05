# このリポジトリで作業するときの決まり

## 守ること

- アプリに転送（かんたん）は、表情ボードを開いたまま何も変えずに書き出す通常と同じ VRM を書き出す。設定項目は表情ボードの既定値（`ChangeAvatar` と `Prepare`）に 1 か所だけ足し、かんたん側に項目ごとの処理を書かない。`interactive` は確認を出すかどうかだけを決め、書き出す中身を変えない。一致は `QuickTransferMatchesDefaultNormalTests` で確かめる。

## テスト

- EditMode テストは `Tests/Editor` にあり、Unity 2022.3 で回す。
- EditMode を全件回すと、Unity の Android のモジュールが出す例外のログ（スタックに `TargetScanWorker` がある）で、そのとき動いていたテストがたまに失敗する。Exporter の不具合ではないので、全件の結果の失敗はスタックを見て、これに当たったテストは単独で回し直して確かめる。
- GPU から読み戻すテストを書くときは、`-nographics`（`SystemInfo.graphicsDeviceType` が `Null`）で読み戻せないときの結果（元の画像のまま・警告に出る）を確かめて抜ける分岐を入れる。CI は GPU ありで回すが、`-nographics` で回しても失敗にしない。
- `Tools/run-unity-compatibility.py` の `BEHAVIOR_CASES`・`INTEGRATION_CASES` に載せたクラスは、そのクラスのテストメソッドをすべて、どちらか一方の表にケース数つきで書く（両方に書くと止まる）。`exporter-integration` はクラス単位で回した結果と突き合わせる。`exporter-behavior` は両方の表に載るクラスをメソッド名で選んで回すので、表に無いメソッドはそこでは動かず、結果との突き合わせでは見つからない。`Tools/test-compatibility-tooling.py` がソースと表を比べて見つけるので、メソッドを足す・消す・名前を変えるときはここも直し、これを回す。
