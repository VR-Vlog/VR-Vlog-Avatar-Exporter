# テストの表の検査を、緩めると落ちるようにする

- 日付: 2026-11-05
- 対象: `Tools/run-unity-compatibility.py`・`Tools/test-compatibility-tooling.py`・`CLAUDE.md`

## 背景

`BEHAVIOR_CASES` と `INTEGRATION_CASES` は、回すテストメソッドとケース数を書いた表で、Unity の結果と突き合わせる。ここに抜けが 3 つあった。

- 同じメソッドが両方の表に別のケース数で載ると、`exporter-integration` では `INTEGRATION_CASES` の数で上書きされ、`BEHAVIOR_CASES` の数は突き合わせに使われない。今の表に重複は無い。
- ソースと表を比べる検査がテストメソッドの中に直接書かれていて、検査を緩める変更を止めるものが無かった。
- `CLAUDE.md` は「クラス単位で回した結果と突き合わせる」と書いていたが、`exporter-behavior` は両方の表に載るクラスをメソッド名で選ぶので、表に無いメソッドはそもそも動かず、結果との突き合わせでは見つからない。

## 決定

- 2 つの表をまとめる処理を `table_cases()` の 1 か所にし、同じメソッドが両方にあれば `exporter-integration` の実行を止める。上書きしない。
- 比較の処理を `case_table_problems()` に出し、見つけた問題を文字列の一覧で返す。実際の表はこれが空であることを確かめる。
- `CaseTableCheckTests` で、小さいソースと実際の共有クラスを渡して、抜け・余り・ケース数の違いをそれぞれ見つけることを確かめる。
- `CLAUDE.md` は、どちらのプロファイルが何で抜けを見つけるかを書き直す。

## 範囲外

- テスト宣言の正規表現が取りこぼす書き方（`[Test]` と `public` の間に別の属性やコメントが入るもの）は変えていない。今の `Tests/Editor` には無い。

## 確認

- `Tools/test-compatibility-tooling.py` の全件が通る。
- 検査を 1 か所ずつ緩めて、`CaseTableCheckTests` が落ちることを確かめた。未掲載のメソッドを許す、ケース数を下限にする、表だけのメソッドを許す、両方の表の重複を止めない、の 4 通り。
- Unity の実行は変えていない。表の中身も変えていないので、Unity では回していない。
