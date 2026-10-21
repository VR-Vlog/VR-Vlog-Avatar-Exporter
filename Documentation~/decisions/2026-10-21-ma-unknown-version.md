# Modular Avatar の版が読めないときは止めずに固定を試す

- 日付: 2026-10-21
- 対象: `Editor/Compatibility/DependencyPolicy.cs`・`Editor/MaAppearanceSnapshot.cs`・`Editor/Locales/*.json`・`Tools/DependencyCompatibilityTests.cs`・`Compatibility/README.md`・`CHANGELOG.md`

## 背景

`MaAppearanceSnapshot` は、表示の固定の対象になる MA のコンポーネントがあると、MA のパッケージ情報から版を読み、`DependencyPolicy.ModularAvatarAppearanceError` が対応範囲外と判定したらリフレクションに入る前に止める（2026-10-18-modular-avatar-appearance-range）。版が読めないとき、つまり MA を VPM パッケージではなく `Assets` の下に置いたとき（`PackageInfo.FindForAssembly` が null）や、プレリリースなど `X.Y.Z` の数字だけでない版のときも「範囲外」として止めていた。MA の API がそろっていて固定できる場合でも、書き出しが必ず止まっていた。

## 決定

- 止めるのは、`X.Y.Z` として読めて対応範囲（1.18.0 以上 1.19.0 未満）の外にある版だけにする。版が読めないとき（null・空・プレリリース・ビルドメタデータ付き・ほかの書き方）は `ModularAvatarAppearanceError` が null を返し、これまでどおりリフレクションで MA の API を呼んで固定を試す。読めないかどうかは `DependencyPolicy.ModularAvatarVersionUnknown` で判定する。
- 版が読めずに試して、API が見つからないなどで固定できなかったときは、`DependencyPolicy.ModularAvatarUnknownVersionError` の文で止める。版を読めなかったこと・対応範囲・1.18.x を VCC／ALCOM で入れれば固定できること・アバターは変更していないことを書く。文は 4 つの言語のファイルにも足した。版が読めて固定できなかったときの文は変えていない。

## 検討した代替案と不採用理由

- プレリリースの数字の部分（`1.19.0-alpha.2` の 1.19.0）で範囲を判定する: 10-18 の決定では 1.19.0 のプレリリースは解析結果の形が変わるので止めていた。今回は、読めない版は API で判定し、API が無ければ止めるので、形が変わった版も固定の途中で止まり、原本は変わらない。判定を 1 つにするため、数字の部分は読まない。

## 確認したこと

- `Tools/run-compatibility-tests.ps1` を流し、読めて範囲外の版（1.17.1・1.19.0・2.0.0 など）は止まり、読めない版（null・空・`1.19.0-alpha.0`・`1.18.7+local`・`v1.18.7` など）は止まらないこと、読めないときの文に対応範囲と直し方が入ることを確かめた。
- `AppearancePreparationTests` と `MaReactiveExpressionExportTests` を Unity の EditMode テストで流した（MA 1.18.x のプロジェクト）。

## 影響

- MA を `Assets` の下に置いたプロジェクトやプレリリースの MA で、API がそろっていれば固定して書き出せるようになる。API がそろっていなければ、版が読めなかったことを示して止まる。
- 対応範囲内の正式版と、読めて範囲外の正式版の動きは変わらない。
