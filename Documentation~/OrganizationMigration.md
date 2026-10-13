# Organizationへの移管

ソース・Issue・PR・Releaseは
[VR-Vlog/VR-Vlog-lilToon-Exporter](https://github.com/VR-Vlog/VR-Vlog-lilToon-Exporter)
で管理します。個人アカウントから履歴を維持して移管しました。
公開済みのバージョン・タグ・ZIPとパッケージIDは変更しません。

導入ページとVPM一覧の新URLは次のとおりです。

- https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/
- https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/index.json

GitHubの旧リポジトリURLは新所有者へredirectされますが、
旧個人アカウントのGitHub Pages URLにはredirectがありません。
旧URLは既存の個人Pagesで互換配信します。VCC／ALCOMで次のURLを
登録済みの場合、登録変更やエクスポーターの再インストールは不要です。

- https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json

互換一覧は同じrepository ID `com.vrvlog.packages` と全公開パッケージを
保持し、ZIPのURL・SHA-256・version・依存も正式な一覧と揃えます。
HTML/JavaScriptによる転送ではなく、HTTP 200のVPM JSONを配信します。
新規導入には組織側のURLを使用してください。既に新URLへ変更した場合も、
旧URLを追加し直す必要はありません。

導入済みのUnityプロジェクトやアバター、書き出し済みVRMを作り直す必要は
ありません。ZIPで手動導入した利用者にも登録先の移行は不要です。
移行前に生成した配布候補は、元のrun・commit・版・ZIP SHA-256を
照合する検証を維持して利用できます。

## メンテナーの互換配信更新

互換窓口は既存の
[mitsuya0077.github.io](https://github.com/mitsuya0077/mitsuya0077.github.io)
の静的Pagesで管理します。元のExporterリポジトリ名を個人側で再作成すると
GitHubの移管redirectが失われるため、再作成しません。

正式なPages/VPM更新が成功した後、既存の個人Pages checkoutに対して
次のローカルコマンドを実行します。新しいCIや自動同期は導入していません。

```sh
python3 Tools/sync-legacy-listing.py --site-root /path/to/mitsuya0077.github.io
```

ツールは正式な公開JSONを取得し、旧URL用のルート `url` だけを変更します。
既に公開したversionの削除やZIP hashの変更、別repository ID、不正なJSON、
同時同期を拒否し、失敗時は既存JSONを保持します。差分をレビューしたPRを
個人Pagesのmainへ統合し、既存のPages公開完了を確認します。

旧・新URLのJSONを取得し、ルート `url` 以外が一致すること、全versionの
ZIP hash、既存登録からの一覧取得と必要な導入・更新を確認してください。
旧窓口は新しいリリースのたびに同期し、旧登録を使う利用者がいる間は
保持します。正式な一覧の更新だけで互換配信の更新完了とは扱いません。
