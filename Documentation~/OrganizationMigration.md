# Organization・公式ドメイン・リポジトリ名の移行

ソース・Issue・PR・Releaseは [VR-Vlog/VR-Vlog-Avatar-Exporter](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter) で管理します。同一リポジトリ（GitHub ID `1355769104`）を個人からOrganizationへ移管した後、改名しています。
公式導入ページはアプリと同じドメインの [vrvlog.fun/exporter/](https://vrvlog.fun/exporter/) です。
公開済みのバージョン・タグ・ZIPとパッケージIDは変更しません。

## 利用者の登録を維持する

新規導入には https://vrvlog.fun/exporter/index.json を使用します。
VCC／ALCOMへ次のURLを登録済みの場合も、その登録を使い続けられます。

- https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/index.json
- https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json
- https://vr-vlog.github.io/VR-Vlog-Avatar-Exporter/index.json

公式URLを含む4つのURLはHTTP 200のVPM JSONを返します。ブラウザの導入HTMLだけを転送し、JSONは転送HTMLにしません。
同じVPM repository ID `com.vrvlog.packages`、全公開version、ZIPのSHA-256・依存を保持し、ルート `url` だけを各登録先に合わせます。ZIPのURLは新旧の同一リポジトリのRelease URLを許可します。既存versionでは、所有者・リポジトリ名以外のタグ・ファイル名・hash・パッケージ情報の変更を拒否します。
新URLの追加、登録の削除、再インストール、Unityプロジェクトやアバターの作り直しは不要です。通常の更新操作は従来どおりです。
ZIPで手動導入した利用者の更新方法も変わりません。既存の配布候補のrun・commit・版・ZIP SHA-256の照合を維持します。
GitHubの移管・改名redirectを維持するため、元のソースリポジトリ名を個人側・Organization側のどちらにも再作成しません。[GitHubの改名手順](https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository)ではproject PagesのURLは転送対象外です。そのため、旧組織パスの実JSONを `VR-Vlog/vr-vlog.github.io` のmain・root Pagesで配信します。このaccount PagesにCNAMEを設定すると他のproject Pagesにも影響するため、独自ドメインは設定しません。

## 公開時の自動同期

正式な一覧の生成元は組織側のまま維持します。既存 `Build VPM Listing` のmain手動起動・公開release経路、environment、直列化とリリース承認を保持します。PR検証へ公開権限や同期Secretを渡しません。

1. `Tools/sync-vpm-mirrors.py --preflight` が専用認証と公開先を読み取りで確認し、公式サイトに保存済みの公開記録と今回の実行番号を照合します。
2. 全公開履歴を生成し、既存の `Tools/check-listing.py` で検証します。
3. `Tools/prepare-exporter-publication.py` が公式サイトの最新Git treeに保存済みの公開記録を再取得して順序を照合します。確認後、実際に生成済みの公式ページ・一覧を保存し、組織側の導入HTMLだけを転送ページにします。組織側JSONは変更しません。再取得失敗や新しい公開との競合では、配置用HTMLを変更する前に止めます。
4. Pages artifactのアップロード後、既存配置Actionの直前に `Tools/sync-vpm-mirrors.py --verify-before-deployment` が公開先のGit treeを再取得し、公開順序と履歴を再確認します。書込みやCDNの反映待ちは行いません。
5. 既存Pagesが組織側を公開し、同期ツールが今回の検証済みsnapshotとの一致を待ちます。
6. `Tools/sync-vpm-mirrors.py` が3つの公開先を同期し、4つの実URLのHTTP 200と内容一致を確認して完了します。

生成済みHTML・一覧・全資産のSHA-256を束ねた `publication.json` を組織側と公式サイトへ公開します。固定した生成元workflow、[GitHubの実行番号](https://docs.github.com/en/actions/reference/workflows-and-actions/variables)、run IDとソースcommitも記録します。新しい公開のwriterは改名後のリポジトリ名とGitHub IDを要求します。保存済みの旧名のschema 2記録は、元のpublisher・digestを変更せず読み取ります。改名後の初回runは既存記録より大きい実行番号を使い、同じ番号で旧名と新名の公開を取り違えないようにします。

実行番号は新しいrunごとに増え、再実行では変わりません。公式サイトに保存済みの記録を基準に古い実行を拒否し、同番号では同じ公開内容だけを再試行できます。準備時にも記録を照合し、古いrunによる組織側の再配置前に止めます。一覧が同じでもページだけ新しくなった場合を区別し、4つの一覧と公式サイトの公開記録が一致するまで同期成功とは扱いません。

| リポジトリ | 自動更新する範囲 | 既存の公開方法 |
| --- | --- | --- |
| `mitsuya0077/vr-vlog-website` | `public/exporter/` の導入ページ・資産・一覧 | 既存Next.js静的ビルドとPages |
| `mitsuya0077/mitsuya0077.github.io` | `VR-Vlog-lilToon-Exporter/index.json` | 既存mainの静的Pages |
| `VR-Vlog/vr-vlog.github.io`（GitHub ID `1418361148`） | `VR-Vlog-lilToon-Exporter/index.json` | main・rootの静的Pages |

生成済み公式サイトを直接編集せず、Exporterの `Website/` を編集します。同期は対象範囲外のファイルを変えません。
通常のサイトビルドが一覧を消さないよう、生成済みファイルもサイトリポジトリへ保存します。
Exporterのworkflow・job・起動条件は増やしません。Organizationのaccount Pagesは、承認を受けて作成・有効化したGitHub管理の標準Pages公開を使います。定期実行、Unityビルド、パッケージ版の自動公開は追加しません。

## 認証と復旧

Exporterの既存main限定 `github-pages` Environmentに2つのFine-grained tokenを保存します。`VPM_MIRROR_TOKEN` は個人側の上記2リポジトリだけ、`VPM_ORG_MIRROR_TOKEN` はOrganizationの `vr-vlog.github.io` だけを対象とし、どちらも `Contents: Read and write` と自動で付くMetadata読み取りを使います。対象所有者が異なるため、別々の認証でアクセスします。組織用は本人が承認した無期限設定を使います。失効・権限変更時は同じEnvironmentのSecretを更新してください。リポジトリ全体の同名Secretは保存せず、他ブランチのworkflowに専用認証を渡しません。

個人用は事前確認・準備時の再確認・配置直前の再確認・同期の4つのstep、組織用は準備stepを除く3つのstepの環境変数だけへ渡します。組織用を個人側のAPIへ送らず、認証のfallbackやredirect追従は行いません。ログ・引数・サイト・ZIP・snapshotには含めません。
期待するmainのheadを指定して通常のコミットを作り、同時変更時は最新headを読み直して計画し直します。force pushやbranch保護の回避は行いません。
既存versionの削除、ZIP hashの変更、不正JSON、別repository ID、異なるZIP配布元、symlinkを拒否します。
複数サイトの配置は同時確定ではないため、CDNの反映待ちを上限付きで行います。途中失敗はworkflowの失敗として報告し、反映済みコミットのURLを残します。
通常の公開処理は既存の `pages` groupで直列化します。生成済み `public/exporter/` の直接編集や別経路からの同時公開は行わず、Exporter側の検証・承認済み公開経路を使用します。配置直前の再確認後から配置Action完了までに行われた手動変更まで、複数リポジトリで原子的に確定することはできません。公開記録や既存version・ZIP hashの競合は同期・完了前にも再照合し、4つの配信先が一致するまで成功とは扱いません。
同じ公開を再試行すると、反映済みの内容は再コミットせず、残りの同期・公開確認を続行できます。新しい公開が完了済みなら、古いrunの再実行は拒否します。その場合は必要な承認を保持してmainから新しい一覧公開runを起動してください。公開済み履歴を失う古いsnapshotも拒否します。
組織側の公開成功だけで互換配信も完了したとは扱いません。旧登録を使う利用者がいる間は、個人・組織の既存JSON URLと導入ページの転送を保持します。
検証では4つの一覧の全公開version・ZIP hashと、登録済みクライアントからの一覧取得を確認してください。

## 再度改名する場合

旧project Pagesの実JSONとHTMLをaccount Pagesへ用意して公開を確認し、改名対応の検証・レビューを終えてから同一ソースリポジトリを改名します。改名後はGitHub ID、Issue・PR・Release・タグ、保護・Environment・Secret名、旧GitHub URLのredirectを読み戻します。新しいmainから承認済みの一覧公開を行い、4つの一覧・公開記録・旧Release URLを検証します。改名だけでVPMの登録先やパッケージIDを変更しません。
