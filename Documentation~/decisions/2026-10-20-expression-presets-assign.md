# 表情の割り当てで VRM の標準表情をすべて扱う

- 日付: 2026-10-20
- 対象: `Editor/ExperimentalExpressionCaptureSession.Presets.cs`・`Editor/ExperimentalExpressionCaptureSession.cs`・`Editor/ExpressionBoard.cs`・`Editor/ExperimentalExpressionCaptureWindow.cs`・`Editor/Locales/*.json`・`Documentation~/ExperimentalExpressionCapture.md`

## 背景

表情の割り当て（**VRMを書き出す** の表情ボード）で選べる標準表情は感情の5つ（happy・angry・sad・relaxed・surprised）だけだった。`SetPreset`・`ParsePreset`・`PresetLabel` がそれ以外を拒否していた。残りの標準表情は次のように書き出し側が決めている。

- まばたき（blink・blinkLeft・blinkRight）は `BlinkExportSession` が書く。元のアバターを見て、VRChat の閉眼設定・元の VRM 設定・名前の順で決める。元の VRM 設定を残す場合を除き、clone に足した blink は `BlinkExportSession.Apply` が上書きする。
- 口の形（aa・ih・ou・ee・oh）は `VrmExpressionBindings.AddMissing` が決まった名前（`ExpressionNameInference.MouthShapes`）で書く。VRM に既にある標準表情は飛ばす。
- 視線（lookUp・lookDown・lookLeft・lookRight）と neutral は作らない。lookAt はボーン型で書き出す（`VrmLookAtExport`）。

次の画面（PR2）で、これら 18 個の枠をグループごとに出す。この変更はその土台になる。今の画面の見た目は変えない。

## 決定

### 割り当てられる標準表情

- `AssignablePresets` を感情 5・まばたき 3・口の形 5・視線 4・基準の顔（neutral）の 18 個にする。グループと並び順は `PresetGroups` の 1 か所で定める。
- 枠の名前は「両目」「あ」「上」のように短くする。エラー文など単独では意味が通らない箇所では、グループ名を付けた `PresetFullLabel`（「まばたき 左目」「視線 上」）を使う。
- 保存形式（`ExpressionPresetStore` の Version 1・設定の `Presets`）は変えない。感情だけを書いた古いファイルはそのまま読める。
- override は感情だけ従来どおり `blend` にする。まばたき・口の形・視線・neutral は、それ自体がその動きなので `none` にする。

### まばたきは瞬きの書き出しに手動の設定として渡す

- まばたきの枠に自分で選んだもの（自動の候補ではないもの）が1つでもあれば、まばたきの3枠の割り当てから `BlinkExportOptions`（Manual）を組んで瞬きの書き出しに渡す。空の枠は書かない。clip の割り当ては各 morph の値を、BlendShape の割り当ては 100 を重みにする。
- まばたきの割り当ては、通常の書き出しで clone に足す標準表情（`AuthorPresets`）にも、記録した表情の書き出しの標準表情にも入れない。
- 理由: clone に blink を足しても、瞬きの書き出しが元のアバターから決めた設定で上書きする。瞬きの書き出しに一本化すれば、どちらの書き出し経路でも同じ処理（左右の検査・メッシュ統合後の付け替え）を通る。
- 自動の候補は、瞬きの設定が自動のときに書き出しが選ぶもの（`BlinkExportSession.Resolve`）と同じにする。1つの枠が書き出すメッシュの BlendShape 1つに決まるときだけ候補にする。元の VRM の設定を残す場合・瞬きなし・手動の設定のときは候補を出さない。まばたきの枠が全部自動の候補なら、渡された瞬きの設定をそのまま使うので出力は従来と同じになる。オーナーが承認した画面案で、まばたきの3枠にも自動の候補を出しているため。
- 割り当てが無いときに書き出しが使う設定を画面に出すため、`BlinkFallbackDescription` を用意する（瞬きなし・手動設定・元のVRM設定・VRChatの閉眼設定・自動設定、決められないときはその理由）。

### 口の形の候補の順

1. 元のアバターの VRChat の descriptor が Viseme Blend Shape 方式なら、`VisemeBlendShapes` の aa・E・I・O・U（並びの 10〜14 番目）を aa・ee・ih・oh・ou の候補にする。descriptor はまばたきの閉眼設定と同じくリフレクションで読む。
2. 1 で決まらない枠は、`MouthShapes` の決まった名前に完全一致する BlendShape を候補にする。書き出すメッシュの1つだけにある場合に限る。複数のメッシュにある名前は通常の書き出しが全部のメッシュに割り当てるので、候補にしない。
3. どちらも書き出すメッシュの BlendShape（`PresetShapeOptions`）にあるものだけにする。

2 の候補が自動のままなら書き出しでは割り当てとして扱わず、通常の書き出し（`AddMissing`）に任せる。割り当てとして書くと、標準表情を足す経路に切り替わってメッシュに終端用の変形が増えるため。これで決まった名前だけのアバターの出力は従来と同じになる。1 の候補は決まった名前と違う BlendShape を指すので、自動のままでも割り当てとして書く。

### 視線と neutral

- 割り当ては感情と同じ経路で書く。自動の候補は作らない（名前から決める規則が無い）。
- 視線を割り当てられるようにしたのは、VRM の標準表情を全部扱える方が分かりやすく、表情で視線を動かすアプリにも渡せるため。lookAt はボーン型で書き出すので、多くのアプリは視線の表情を使わない。このことは説明書に書く。

### 今の画面

- `ExpressionBoard` の枠は 18 個にする。グループごとの件数を返す `SlotCount(group)`・`AutomaticCount(group)` と、グループを指定できる `ConfirmAutomatic(group)` を足す。
- 今の画面は感情の5枠だけを描き、件数と **自動の○件をすべて確定** も感情だけで数える。まばたきの候補は瞬きの設定を変えたときに出し直す。
- **次にやること:** の「○○の割り当てを直す」は、画面に出ない枠も含めて全部の枠を見る。画面に出ない枠の不正も書き出しを止めるため。

## 検討した代替案と不採用理由

- まばたきに自動の候補を作らない: 当初の案。画面が書き出しの結論を真似ると食い違うおそれがあった。`BlinkExportSession.Resolve` をそのまま使えば食い違わないので、承認された画面案に合わせて候補を出す。
- まばたきの割り当てを clone の VRM 設定に足す: 瞬きの書き出しが上書きするので効かない。元の VRM 設定を残す扱いにすると、左右の検査やメッシュ統合後の付け替えを通らない。
- develop の PR #223（`Editor/VrChatVisemeExport.cs`）を取り込む: このブランチは書き出しの作り直しの統合ブランチで、develop をマージしない方針。リップシンク設定を読む部分だけを候補づくりに書き、#223 の書き出し側の変更とは独立させる。

## 影響

- 割り当てが無い場合の出力は従来と同じ。自動の候補のうち、まばたきと決まった名前の口の形は自動のままなら従来と同じ出力になる。リップシンク設定の候補は自動のままでも割り当てとして書くので、決まった名前と違う BlendShape を指すアバターでは口の形が変わる。
- 口の形を割り当てとして書くと、その枠は通常の書き出しの名前による割り当てより優先し、複数のメッシュにまたがる名前の割り当ては置き換わる。
- まばたきの BlendShape の割り当ては重み 100 で書く。自動の瞬きは元のメッシュの終端の値を使うので、終端が 100 でない BlendShape を手で選ぶと動きが変わることがある。
- まばたきの左目だけを選んで右目を空にすると、今の瞬きの書き出しと同じく「左右別の瞬きは左目・右目を両方指定してください。」で書き出しが止まる。
- 保存形式は変えないので、古い版はまばたき・口の形などの保存を読めずに飛ばす（読み込みの失敗件数に数える）。
