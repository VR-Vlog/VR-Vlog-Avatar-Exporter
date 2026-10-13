# Organizationと公式ドメインへの移行

ソース・Issue・PR・Releaseは [VR-Vlog/VR-Vlog-lilToon-Exporter](https://github.com/VR-Vlog/VR-Vlog-lilToon-Exporter) で管理します。
公式導入ページはアプリと同じドメインの [vrvlog.fun/exporter/](https://vrvlog.fun/exporter/) です。
公開済みのバージョン・タグ・ZIPとパッケージIDは変更しません。

## 利用者の登録を維持する

新規導入には https://vrvlog.fun/exporter/index.json を使用します。
VCC／ALCOMへ次のURLを登録済みの場合も、その登録を使い続けられます。

- https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/index.json
- https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json

3つのURLはHTTP 200のVPM JSONを返します。ブラウザの導入HTMLだけを転送し、JSONは転送HTMLにしません。
同じrepository ID `com.vrvlog.packages`、全公開version、ZIPのURL・SHA-256・依存を保持し、ルート `url` だけを各登録先に合わせます。
新URLの追加、登録の削除、再インストール、Unityプロジェクトやアバターの作り直しは不要です。通常の更新操作は従来どおりです。
ZIPで手動導入した利用者の更新方法も変わりません。既存の配布候補のrun・commit・版・ZIP SHA-256の照合を維持します。
GitHubの移管redirectを維持するため、元のリポジトリ名を個人側で再作成しません。

## 公開時の自動同期

正式な一覧の生成元は組織側のまま維持します。既存 `Build VPM Listing` のmain手動起動・公開release経路、environment、直列化とリリース承認を保持します。PR検証へ公開権限や同期Secretを渡しません。

1. `Tools/sync-vpm-mirrors.py --preflight` が専用認証と公開先を読み取りで確認し、公式サイトに保存済みの公開記録と今回の実行番号を照合します。
2. 全公開履歴を生成し、既存の `Tools/check-listing.py` で検証します。
3. `Tools/prepare-exporter-publication.py` が実際に生成済みの公式ページ・一覧を保存し、組織側の導入HTMLだけを転送ページにします。組織側JSONは変更しません。
4. 既存Pagesが組織側を公開し、同期ツールが今回の検証済みsnapshotとの一致を待ちます。
5. `Tools/sync-vpm-mirrors.py` が2つの公開先を同期し、3つの実URLのHTTP 200と内容一致を確認して完了します。

生成済みHTML・一覧・全資産のSHA-256を束ねた `publication.json` を組織側と公式サイトへ公開します。固定した生成元workflow、[GitHubの実行番号](https://docs.github.com/en/actions/reference/workflows-and-actions/variables)、run IDとソースcommitも記録します。実行番号は新しいrunごとに増え、再実行では変わりません。公式サイトに保存済みの記録を基準に古い実行を拒否し、同番号では同じ公開内容だけを再試行できます。準備時にも記録を照合し、古いrunによる組織側の再配置前に止めます。一覧が同じでもページだけ新しくなった場合を区別し、3つの一覧と公式サイトの公開記録が一致するまで同期成功とは扱いません。

| リポジトリ | 自動更新する範囲 | 既存の公開方法 |
| --- | --- | --- |
| `mitsuya0077/vr-vlog-website` | `public/exporter/` の導入ページ・資産・一覧 | 既存Next.js静的ビルドとPages |
| `mitsuya0077/mitsuya0077.github.io` | `VR-Vlog-lilToon-Exporter/index.json` | 既存mainの静的Pages |

生成済み公式サイトを直接編集せず、Exporterの `Website/` を編集します。同期は対象範囲外のファイルを変えません。
通常のサイトビルドが一覧を消さないよう、生成済みファイルもサイトリポジトリへ保存します。
新しいworkflow、定期実行、Unityビルド、パッケージ版の自動公開は追加しません。

## 認証と復旧

Exporterの既存main限定 `github-pages` EnvironmentのSecret `VPM_MIRROR_TOKEN` に、上記2リポジトリだけの `Contents: Read and write` を持つFine-grained tokenを設定します。自動で付くMetadata読み取り以外は不要です。リポジトリ全体の同名Secretは保存せず、他ブランチのworkflowに専用認証を渡しません。
専用の事前確認・同期stepの環境変数だけへ渡し、ログ・引数・サイト・ZIP・snapshotには含めません。有効期限前にSecretを更新します。
期待するmainのheadを指定して通常のコミットを作り、同時変更時は最新headを読み直して計画し直します。force pushやbranch保護の回避は行いません。
既存versionの削除、ZIP hashの変更、不正JSON、別repository ID、異なるZIP配布元、symlinkを拒否します。
複数サイトの配置は同時確定ではないため、CDNの反映待ちを上限付きで行います。途中失敗はworkflowの失敗として報告し、反映済みコミットのURLを残します。
同じ公開を再試行すると、反映済みの内容は再コミットせず、残りの同期・公開確認を続行できます。新しい公開が完了済みなら、古いrunの再実行は拒否します。その場合は必要な承認を保持してmainから新しい一覧公開runを起動してください。公開済み履歴を失う古いsnapshotも拒否します。
組織側の公開成功だけで互換配信も完了したとは扱いません。旧登録を使う利用者がいる間は、両方の既存JSON URLと導入ページの転送を保持します。
検証では3つの一覧の全公開version・ZIP hashと、登録済みクライアントからの一覧取得を確認してください。
