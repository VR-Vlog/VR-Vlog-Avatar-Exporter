# リリースにVPMのインストーラーのunitypackageを添付する

- 日付: 2026-10-23
- 対象: `Tools/build-installer.py`・`Tools/test-installer.py`・`.github/workflows/release-vpm.yml`・`.github/workflows/validate.yml`・`Tools/check-pipeline.py`

## 背景

導入方法はVCC・ALCOMへのリポジトリ追加と、ZIPの手動展開の2つだった。Unityで開いているプロジェクトにunitypackageを読み込むだけで、VPMのリポジトリ登録とパッケージの追加まで済む方法がほしいという要望があった。

lilToonの配布を確かめると、GitHub Releasesの添付はVPM用のZIPだけで、unitypackageを読み込んでVPMに登録する仕組みは無かった（公式の導入手順もunitypackageの手動読み込みとVCC・ALCOMへの追加を別の方法として並べている）。VRChatのアバター向けツールでは、公開されているインストーラー作成ツールでunitypackageを作る方式が広く使われている。このunitypackageは読み込まれると、同梱の設定に書いたリポジトリと依存をプロジェクトの `Packages/vpm-manifest.json` に足してVPMの解決を走らせ、終わると自分のファイルを消す。

## 決定

- インストーラーは上流の作成ツールのCLI版で作る。版とSHA-256を `Tools/build-installer.py` に固定してReleaseからダウンロードし、Node.jsで実行する。自前でunitypackageを組み立てない。
- 設定は `package.json` から作る。リポジトリはREADMEで案内している `https://vrvlog.fun/exporter/index.json`、依存は `^<版>`、版にプレリリースの印（`-`）があればプレリリースも選ぶ、Unityの下限は `package.json` の `unity`。
- 作ったunitypackageの中の `config.json` を読み、渡した設定と同じものが1つだけ入っていることを確かめてから出力する。
- `release-vpm.yml` は既存の「Build exporter package」でインストーラーも作り、「Publish release」でZIPと一緒に添付する。新しいRelease・公開先・トリガーは足さない。VPMの一覧（`build-listing.yml`）はZIPだけを読むので変えない。
- 「Build exporter package」では、インストーラーをZIPより先に作る。外部の作成ツールが作業ツリーを書き換えても、そのあと作るZIPが検証済みの候補とSHA-256で一致しなければ止まる。作成ツールのダウンロードは一時的な障害でリリースを止めないよう3回まで試す。
- PRの `Validate` でも同じスクリプトでインストーラーを作り、artifact（`vpm-installer`、7日）に上げる。リリース前に実機で試せるようにするため。`Tools/check-pipeline.py` のartifactの許可に `work/installer` を足し、リリースでインストーラーを作って添付していることも検査する。

## 検討した代替案と不採用理由

- 作成ツールのWeb API でunitypackageを作る: 外部サービスの稼働と応答の中身に依存する。CLI版はwasmを埋め込んだ1ファイルでネットワークを使わずに動き、ハッシュで固定できる。
- Unityのエディタ拡張版で作る: CIでUnityが要る。
- インストーラー専用のワークフローを足す: 既存のリリースの流れ（候補の検証→Release）に乗せれば、検証済みの版にだけ添付される。

## 影響

- エクスポーターのパッケージ（ZIP）の中身は変わらない。Releaseの添付が1つ増える。
- PRごとに作成ツール（約280KB）をGitHubから1回ダウンロードする。3回とも失敗するとPRの `Validate` とリリースが失敗する。
- インストーラーは同じ系列の、VPMの一覧にある最新の版を入れる。Releaseに添付した直後は一覧への反映が済むまでその版は入らない。
