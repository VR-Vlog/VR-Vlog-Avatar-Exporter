# 表情のサンプリングは、固定レイヤーの判定だけを使い回す

- 日付: 2026-10-19
- 対象: `Editor/ExpressionInspectCache.cs`・`Editor/ExpressionDependencies.cs`・`Editor/VrChatExpressionSampler.cs`・`Tests/Editor/StationaryLayerCacheTests.cs`・`CHANGELOG.md`

## 背景

Inspect のキャッシュ（#212）を入れたあとも、表情の読み込み（`VrChatExpressionSampler.Analyze`）の大半は `ApplyPermanentOverrides` の中の `SampleNeutral` が占めている。`SampleNeutral` と `Evaluate` は、呼ばれるたびにプレビューシーンを作り、アバターを複製し、PlayableGraph を作って 120 フレーム進め、3 回の検査点で値を取り直す。

手元だけで区間を測った（計測のコードはコミットしていない）。6 体（A〜F）を 1 回ずつ書き出した合計（Unity 2022.3.22f1、変更前）:

| 区間 | 秒数 | 回数 |
|---|---|---|
| `Analyze` 全体 | 313.7 秒 | 6 回 |
| `ApplyPermanentOverrides` | 268.6 秒 | 129 回 |
| `SampleNeutral` | 189.5 秒 | 252 回（1 回 0.75 秒） |
| 　3 回の検査点 | 108.5 秒 | |
| 　0 秒の `Capture` | 35.2 秒 | |
| 　`ValidateFixedPose` | 19.9 秒 | |
| 　120 フレームの `Advance` | 14.4 秒 | |
| 　準備（グラフの前の入力・層の計算） | 7.4 秒 | |
| 　複製（シーン・`Instantiate`・Behaviour の停止） | 1.3 秒 | |
| 　graph の作成・後片付け | 0.6 秒 | |
| `Evaluate` | 32.5 秒 | 146 回 |
| `ApplyPermanentOverrides` の表情ごとの前処理 | 3.4 秒 | 129 回 |

時間がかかっているのは複製や graph の作成ではなく、検査点での `Capture`（全体で 984 回・135.4 秒）と `ValidateFixedPose` だった。

同じ引数の呼び出しがどれだけあるかも数えた。

- `SampleNeutral` に渡る Animator Controller は、呼び出しのたびに作る未保存のプローブ（`HideAndDontSave`）なので、インスタンス ID が同じ呼び出しは 1 回も無かった。
- プローブの中身（レイヤー・ステート・クリップのカーブ）、入力の値、`neutralPlan`、除外、`dependencies` の状態で比べると、252 回のうち 34 回（13.3 秒）が前の呼び出しと同じだった。34 回とも結果は前と同じだった。重複は B（8 回・1.9 秒）と D（26 回・11.4 秒）だけで、ほかの 4 体には無い。
- `Evaluate` の重複は 0 回。
- 120 フレーム進めた時点で遷移中のレイヤーがあったサンプルは 280 回中 0 回。

## 決定

- 入れたもの: `ApplyPermanentOverrides` の表情ごとに変わらない前処理のうち、固定レイヤー（`StationaryLayers`）の判定を使い回す。
  - グラフだけで決まる部分（レイヤーの重み・既定のステート・遷移・パラメーターで動く設定・Override の差し替え）を `StationaryCandidates` に分け、#212 と同じ Analyze のスコープの中で、保存済みのコントローラーに限ってキャッシュする。Override Controller は元のコントローラーも保存済みのときだけ。
  - 呼び出し元の除外（`excludedPath`）に関係する判定（バインディングの種類・定数かどうか）は毎回やり直す。クリップのバインディングの一覧と、カーブが定数かどうかは、保存済みのクリップに限ってスコープの中で使い回す。
  - キャッシュから返す配列は毎回複製するので、呼び出し側が書き換えてもキャッシュは変わらない。
  - `ApplyPermanentOverrides` が 1 回の呼び出しの中で `StationaryLayers` を 2 回計算していたのを 1 回にした。
- 入れなかったもの:
  - `SampleNeutral` の結果の使い回し: 重複は全体の 4% ほど（`Analyze` 313.7 秒のうち 13.3 秒）で、2 体に限られる。プローブは毎回作り直されるため、キーには中身を全部入れる必要がある。遷移の条件と時間、Blend Tree、StateMachineBehaviour のフィールド、Avatar Mask、クリップのカーブ、複製元のアバターの状態に加え、`SampleNeutral` が書き換える `dependencies` の再現も要る。計測に使ったキーは遷移を数でしか比べておらず、結果が一致したのは今回の 6 体で確かめた範囲に限られる。キーが漏れなく揃っていると示せないので入れない。
  - 複製の再利用: グラフは Transform・BlendShape・材質・有効状態・Write Defaults の既定値を書き換え、Animator を足すこともある。止めた Behaviour もアニメーションで有効に戻りうる。エディターではグラフを壊したあとも書き換えた値が残る。安全に戻せると示せないので入れない。そもそも複製は全体で 1.3 秒しかかかっていない。
  - 120 フレームを早く打ち切る案: exit time の遷移、Parameter Driver、パラメーターのカーブ、レイヤーの重みの制御が、あとから効くことがある。`NativeTerminalClip` と `PreserveTemporalRest` は `normalizedTime` で結果が変わる。120 フレームの時点で遷移中のレイヤーが無かったことは、それより前に打ち切ってよいことの証明にならないので入れない。
  - メニューの二重読み込み: `ApplyPermanentOverrides` の中の `VrChatExpressionMenu.Read` は、呼び出し元がいつも `metadata` を渡すので実行されていなかった。書き出し 1 回の読み込みは 3 回（呼び出し元は 3 か所）で、6 体の合計で 0.03 秒しかかからない。
  - `Overrides` のキャッシュ: 129 回で計測上 0 秒だったので入れない。`ValidateProbeBehaviours` は #212 のキャッシュにもう入っている。

## 確認したこと

- 6 体（A〜F）を同じ PC・同じ Unity プロジェクトで変更前（`f5b9402`）と変更後に 1 回ずつ書き出し、VRM の sha256 がすべて一致した。
- 書き出しの時間（1 回ずつ。同じ PC で別の Unity が動いていたので揺れがある）:

| アバター | 変更前 | 変更後 |
|---|---|---|
| A | 11.6 秒 | 13.0 秒 |
| B | 35.8 秒 | 32.9 秒 |
| C | 151.0 秒 | 145.2 秒 |
| D | 54.4 秒 | 54.1 秒 |
| E | 3.1 秒 | 2.8 秒 |
| F | 114.6 秒 | 90.3 秒 |

  この変更で減ったのは、6 体の合計で `StationaryLayers` が 0.12 秒から 0.05 秒（呼び出しは 900 回から 771 回）、`StationaryBindings` が 0.04 秒から 0.00 秒。表の差はほとんどが揺れで、F の差も呼び出しの回数が同じ `SampleNeutral` の中（60.1 秒 → 44.5 秒）で出ている。書き出しの時間はほぼ変わらない。
- `StationaryLayerCacheTests` で、次の 3 点を確かめる。
  - コントローラー・除外・Override の組み合わせ 5 通りが互いに違う結果になり、スコープの中でもキャッシュなしと 2 巡とも同じ結果になること。
  - 当たったときに返した配列と結果を呼び出し側が書き換えても、キャッシュが変わらないこと。
  - 未保存のプローブはスコープの中でもキャッシュせず、クリップを書き換えると結果が変わること。
- Unity の EditMode テストは 3820 件中 31 件が失敗した。変更前から失敗していた 31 件と同じで、新しい 3 件は通る。
