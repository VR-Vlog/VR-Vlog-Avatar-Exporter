# ギミックの行に、指している GameObject の ObjectField を出す

- 日付: 2026-10-21
- 対象: `Editor/ExperimentalExpressionCaptureWindow.cs`（`DrawGimmicks`・`GimmickRow`）・`Editor/LilToonExporterWindow.cs`（`DrawGimmicks`）・`Tests/Editor/ExportGimmickChoicesTests.cs`

## 背景

**VRM書き出し** のギミックの一覧は、1 行に名前・中身の要約・**VRMに入れる** の切り替えを並べている。名前を押すと Hierarchy の対象が ping されるが、Unity の ObjectField の見た目ではないので、どのオブジェクトを指しているかが分かりにくかった。値を変更できない ObjectField も出してほしいという要望があった。

## 決定

各行の名前の右、中身の要約の左に、その行が指す GameObject の ObjectField を出す。

- 値は `finding.Target` の 1 つ。コンポーネント単位の行（`HandCamera／Lens` のように名前にコンポーネント名が付く行）も、そのコンポーネントが付いている GameObject を出す。行が複数の GameObject を指すことはない。
- `EditorGUI.DisabledScope(true)` の中で描く。Inspector の Script 欄と同じ読み取り専用の見た目で、クリックで ping、ダブルクリックで選択できる。ドラッグでの差し替え・オブジェクトの選択画面・None にする操作はできない。グレーにするのは ObjectField だけで、名前と切り替えはこれまでどおり。
- 行の高さは 1 行のまま変えない。切り替えは右端に 90pt 固定のまま。残りの幅は ObjectField に 35%（ただし残り幅の半分を上限に 120pt を下限）を取り、残りを名前 55%・中身の要約 45% に分ける。狭いウィンドウでは名前と要約を先に縮め、ObjectField の名前が読めるようにした。文字は枠の中で省略する。
- 列の割り当ては `GimmickRow` に切り出し、どの幅でも切り替えの位置と行の高さが変わらないこと、ObjectField が下限の幅を持つことをテストで確かめる。

## 揃えたもの・揃えなかったもの

- 旧ウィンドウ（`LilToonExporterWindow`）のギミック一覧は、もともと ObjectField を出していたが、編集できる見た目のまま変更を捨てていた。同じく読み取り専用の表示に揃えた。
- **書き出さないオブジェクト** の一覧の ObjectField は、ユーザーが対象を入れ替える編集用の欄なので、変更できるまま残した。
- 表情の一覧は行ではなくサムネイルのタイルで、指す先は GameObject ではなくアニメーションなので、対象外とした。PhysBone などの含める／除外を選ぶ行は、このウィンドウにはギミック一覧のほかに無い。

## 入れなかったもの

- 名前のボタンを ObjectField に置き換える案。名前の重複は減るが、依頼は追加で、コンポーネント名とアバターからのパス・判定理由のツールチップが名前側にあるため、名前は残した。
- 一覧の上に「参照先は変更できません」のような説明を足す案。Unity 標準の読み取り専用欄と同じ動きなので、文言は増やさない。
- 狭いウィンドウだけ 2 段にする案。行の高さを幅で変えない方針に反する。

## 確認したこと

- 検証PC の Unity 2022.3.22f1 で、テスト用の人型アバターにギミック 6 行（5 グループ・コンポーネント単位の行 1 つ・親の指定で入る行 1 つ）を付け、変更前（develop）と変更後のギミックの節をウィンドウ幅 640・460・340pt で撮り比べた。どの幅でも 6 行とも高さ 18pt、行の縦位置と切り替えの位置は変更前と同じだった。ObjectField の幅は 640pt で 163.5pt、460pt で 120pt、340pt で 83.5pt。
- 変更後の ObjectField をクリックすると Hierarchy の対象が ping され、値と保存済みの設定は変わらなかった。右端の選択ボタンを押してもオブジェクトの選択画面は開かず、別の GameObject をドラッグして落としても受け付けなかった。
- `ExportGimmickChoicesTests` を同じ検証PC で回し、13 件すべて成功した。
- 旧ウィンドウ（`LilToonExporterWindow`）は、読み取り専用にしただけなので実機では撮っていない。
