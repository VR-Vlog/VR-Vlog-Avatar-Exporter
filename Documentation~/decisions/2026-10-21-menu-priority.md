# Hierarchy の右クリックで VR Vlog をほかのツールの後ろに出す

- 日付: 2026-10-21
- 対象: `Editor/LanTransfer/AppTransferWindow.cs`（`QuickMenuPriority`・`DetailedMenuPriority`）・`Documentation~/decisions/2026-10-20-app-transfer-modes.md`

## 背景

Hierarchy でアバターを右クリックすると、**VR Vlog → アプリに転送（かんたん）／アプリに転送（通常）** がほかのアバター用ツールのメニューの間に出ていた。優先度がかんたん 48・通常 49 で、Avatar Optimizer（20）・lilToon（21〜23）・VRCQuestTools（30〜）の後ろ、AudioLink（49）の前に入るためだった。オーナーから「下の方にしておいて」と要望があった。

48・49 にしたのは、Hierarchy の右クリックに出るのは GameObject メニューの優先度 49 までだと考えたからだった（2026-10-20 の決定記録）。今回、実際の右クリックで確かめると、この前提は誤りだった。

## 決定

優先度をかんたん 10000・通常 10001 にする。

- 値は、アバター用のツールをまとめて入れた検証用のプロジェクト（Unity 2022.3.22f1・VRChat SDK・Modular Avatar・NDMF ほか）で、各ツールが GameObject メニューに付けている優先度を読んで決めた。Modular Avatar は -1000〜-997、NDMF・AudioLink・Light Limit Changer は 49、Avatar Optimizer は 20、lilToon は 21〜23、VRCQuestTools は 30〜133、TexTransTool は 1000、Light Volumes は 9999 だった。Unity 自身の項目は 2000 番台（AI・UI）まで。いちばん大きい 9999 より後ろにするため 10000 にした。
- 2 つの差は 1 にする。Unity は優先度の差が 10 より大きいところに区切り線を入れる（右クリックの確認でも、30 と 49 の項目の間に区切り線が入った）ので、差が 1 なら **VR Vlog** の中に区切り線は増えない。
- Hierarchy の右クリックでは、トップの階層に優先度による区切り線は入らなかった（49 から 1000 に飛ぶところにも入らない）。上のメニューバーの GameObject メニューでは、直前の項目との差が 10 より大きいので **VR Vlog** の前に区切り線が入る。ほかのツールのサブメニューの前にも同じように入っているので、そのままにした。
- サブメニューは、中の項目のいちばん小さい優先度の位置に並ぶ。かんたんを先にしているので、**VR Vlog** の位置はかんたんの 10000 で決まる。

2026-10-20 の決定記録の「49 まで」の記述は、この決定記録を指すように直した。

## 入れなかったもの

- `int.MaxValue` など、さらに大きい値。右クリックには出るが、後から足すツールが VR Vlog より後ろに出せなくなる。
- 区切り線を自分で入れること。MenuItem の優先度で区切り線を足すと、ほかのツールの並びにも影響する。
- メニューの文言の変更。今回は位置だけを変える。

## 確認したこと

- 検証PC の Unity 2022.3.22f1 で、上のプロジェクトに変更前（develop）と変更後の Exporter を入れ、アバターを 1 つ選んで Hierarchy を右クリックした。開いたメニューの項目を Windows のメニューから読み、画面を撮った。
- 優先度 49・50・60・100・1000・10000・99999・`int.MaxValue` の確認用の項目を足すと、どれも右クリックに出て、優先度の順に並んだ。Unity が右クリックから外すのは **Center On Children**・**Make Parent** など Unity 自身の一部の項目で、優先度では外していなかった。
- 優先度 30 と 49 の項目を持つサブメニューは 30 の位置に並んだ。
- 変更前は **VR Vlog** が VRCQuestTools と AudioLink の間に出た。変更後は Light Volume・Point Light Volume の後ろ、**Properties...** の前に出た。
- 変更前・変更後とも、右クリックから **VR Vlog → アプリに転送（かんたん）** を押すと **アプリに転送** のウィンドウが開いた。
