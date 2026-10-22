# コントリビューション / Contributing

不具合報告・改善提案・文書修正・Pull Requestを受け付けています。Issue・PRは日本語または英語で記載できます。実装を変更する前に、対応範囲と依存パッケージの方針を確認してください。以下に開発環境と検証方法をまとめます。

Bug reports, improvement proposals, documentation fixes and pull requests are welcome. Issues and PRs may be written in Japanese or English.

## Reports and proposals

Search [existing issues](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/issues) and [pull requests](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/pulls) before opening a new report.

For bugs, include the exporter, Unity, lilToon and UniVRM versions, relevant optional plugin versions, reproduction steps, expected behavior and actual behavior. For proposals, describe the problem you want to solve and a concrete example. Discuss changes to dependency support or export formats in an issue before implementing them.

For vulnerabilities, use the [private reporting instructions](SECURITY.md) instead of public issues or pull requests.

Ordinary bug reports and proposals are public. Do not attach purchased or private avatars, complete Unity projects, credentials, private links or full logs. Use the exporter's shared diagnostics where available, and redact personal names and machine paths from error excerpts. Use a minimal fixture made from assets you can share.

## Development setup

1. Fork the repository, then clone with LF line endings and create a branch for your change. For a fork, replace the URL below with your fork URL:

   ```sh
   git -c core.autocrlf=false clone https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter.git
   cd VR-Vlog-Avatar-Exporter
   git config core.autocrlf false
   git switch develop
   git switch -c feature/your-change
   ```

   The clone option controls the initial checkout; the local setting keeps later checkouts from converting LF to CRLF. If an existing checkout already has CRLF conversion, use a separate fresh clone with these settings and preserve your local work.
2. For Python checks, use Python 3.12, as in [Validate CI](.github/workflows/validate.yml). Install the hash-pinned workflow parser as described under local checks; the remaining Python checks use the standard library.
3. For Unity work, use a separate Unity 2022.3 project with lilToon 2.3.4 (the catalogue version; also test 2.0.0 and other versions in the supported range when changing lilToon version handling) and matching supported UniGLTF / VRM packages. Follow the exact versions and prerequisites in [Compatibility/README.md](Compatibility/README.md).
4. Add this checkout as a local package using Unity Package Manager's **Add package from disk**, selecting the root `package.json`. Avoid also installing a release copy of the exporter in that project.
5. For Unity tests, add `"testables": ["com.vrvlog.liltoon-vrm-exporter"]` to the test project's manifest and follow the compatibility guide's Test Framework, Collections and optional integration package requirements.

Contributor Unity projects, local outputs and private fixtures belong outside tracked source. `Editor/` contains export logic and diagnostics; `Runtime/` contains avatar components; `Formats/Runtime/` contains the System-only format code shared with the app; `Schema/` defines the custom data formats; `Tests/` and `Tools/` contain validation and packaging tools. See the [repository map](README.md#開発に参加する) for the remaining directories.

The app consumes `Formats/` as the separate package `com.vrvlog.avatar-formats` through a git URL pinned to a commit (`?path=/Formats#<commit>`). Keep it free of Unity Engine and Editor APIs. A change to it reaches the app only through an app PR that bumps the pinned commit, so prepare that PR together with the exporter change. See [Formats/README.md](Formats/README.md).

## Local checks

Run these from the repository root:

```sh
python3 Tools/validate.py
python3 Tools/check-public-content.py
python3 Tools/test-package.py
python3 Tools/test-release-policy.py
python3 Tools/test-listing.py
python3 Tools/generate-compatibility.py
python3 Tools/test-compatibility-tooling.py
python3 Tools/check-liltoon-structure.py
python3 Tools/test-liltoon-catalogue.py
python3 Tools/test-aao-vertex-buffer-patch.py
git diff --check
```

`check-public-content.py` scans tracked files for common disclosure patterns; stage intended new files so they are included. It is not a complete security audit. `generate-compatibility.py` without `--write` checks generated policy drift and does not change source.

### Third-party names

Do not write the names of avatars, outfits, products or their creators and shops in source, tests, documentation, commit messages, branch names, issues, pull requests or release notes. Use generic words such as "avatar A" or "outfit B". `Tools/check-names.py` finds them using a name list that you keep outside the repository (never commit the list):

```sh
python3 Tools/check-names.py --names /path/outside/repo/names.txt            # tracked files
gh pr view 123 --json body -q .body | python3 Tools/check-names.py --names /path/outside/repo/names.txt --text -
```

To check every push locally, add a pre-push hook. Hooks are not versioned, so each clone needs this once:

```sh
cat > .git/hooks/pre-push <<'EOF'
#!/bin/sh
exec python3 Tools/check-names.py --pre-push "$@"
EOF
chmod +x .git/hooks/pre-push
export EXPORTER_BANNED_NAMES=/path/outside/repo/names.txt   # e.g. in your shell profile
```

The hook scans the pushed branch name, commit messages, added lines and file paths. Without `EXPORTER_BANNED_NAMES` it skips with a message instead of blocking.

Host behavior checks also use PowerShell 7. The [Validate workflow](.github/workflows/validate.yml) is the complete command reference, including dependency, exporter, transfer, pose, bake and NDMF host checks. These host checks do not prove Unity integration or device behavior.

For implementation changes, run the relevant pinned Unity profiles from the [compatibility guide](Compatibility/README.md#exporter-behavior-and-integration-regression-profiles). Record the actual Editor and package versions, tested commit, results and any skips. Preserve the original avatar and its referenced assets. Do not use host adapters, relabeled dependencies or older test results as evidence of a successful Unity run.

PR, main, release-candidate and publication host checks share `Tools/run-validation.py`. With Python 3.12 and PowerShell 7, install the hash-pinned parser using `python3 -m pip install --require-hashes --only-binary=:all: -r Tools/pipeline-requirements.txt`, then run `python3 Tools/run-validation.py --report work/validation.json`. This receipt covers host checks only. The [development pipeline](Documentation~/DevelopmentPipeline.md) explains triage, latest-head review, Unity acceptance, non-publishing candidates, publication approval, recovery and the Actions cost model.

For documentation changes, verify relative links and examples and check that the documented behavior matches source. If a change is intended to preserve implementation, confirm that product source, dependency policy, version metadata and package tooling are unchanged.

### Checking that a change keeps the export identical

A change that must not alter the output, such as a performance change, needs evidence that the exported VRM files stay the same. Export the same avatars with the base package and with the changed package, then compare the results. Keep the job list and the results outside the repository.

1. Write a job list with one avatar per line, `ID|prefab path|runs`, for example `A1|Assets/AvatarA/AvatarA.prefab|2`. Use generic IDs such as `A1`.
2. Install the base package in a Unity project that no other Editor has open, and export:

   ```sh
   pwsh -File Tools/run-export-regression.ps1 -Unity /path/to/Unity.exe -Project /path/to/project -Jobs jobs.txt -Out /path/outside/base -Label base
   ```

   The script copies `Tools/ExportRegressionRunner.cs` into a temporary folder under the project's `Assets`, exports each avatar the way **GameObject > VR Vlog > アプリに転送（通常）...** does (through the expression board) and removes the folder when Unity exits. The runner is not part of the package. Each job writes `<ID>_<run>.vrm` and `<ID>_<run>.json` (success or failure, warnings and the export time in seconds).
3. Run each avatar at least twice and confirm that the base is deterministic: `python3 Tools/compare-exports.py --runs /path/outside/base 1 2`. If two runs of the same package differ, list the paths with `python3 Tools/compare-exports.py --diff-paths <run 1 .vrm> <run 2 .vrm>`, and add only paths that change on every export to `VOLATILE` in `compare-exports.py`, with the reason. When only part of a string value changes (for example a random GUID at the end of a name), add the path with a regex and a replacement to `VOLATILE_SUB` instead, so that only that part is masked and the rest of the value is still compared.
4. Replace the package with the changed version in the same project, export again with `-Out /path/outside/changed -Label changed` and check `--runs` there too.
5. Compare the two versions: `python3 Tools/compare-exports.py /path/outside/base /path/outside/changed`. Every pair must match (exit code 0). The script compares success or failure, the export details warnings, every field of every expression, and the SHA-256 of the glb JSON chunk (without `VOLATILE` paths and with the `VOLATILE_SUB` parts masked) and of the BIN chunk.

Report the number of pairs and the result in the PR, without avatar names.

## Reproducing a package ZIP

ZIPの再現には、同じコミット・追跡ファイルの内容と改行が必要です。上記の手順でLFの作業コピーを作り、生成前に未保存・未コミットの変更がないことを確認してください。改行を自動変換した作業コピーは、同じコミットでも配布ZIPとSHA-256が異なります。

The package builder preserves the bytes of the tracked files in the working tree, including line endings. It fixes ZIP entry order, timestamps, permissions and storage format, but does not normalize those file bytes. Use the LF checkout above, a clean working tree and the exact release tag; then build from the repository root:

```sh
git checkout --detach v0.11.14
git status --short
```

Stop if `git status --short` shows local changes. Once the working tree is clean:

```sh
python3 Tools/build-package.py --output ../exporter-0.11.14.zip
python3 -c "import hashlib; from pathlib import Path; print(hashlib.sha256(Path('../exporter-0.11.14.zip').read_bytes()).hexdigest())"
```

Replace the tag, version and output filename together for another release. Compare the SHA-256 with that version's [VPM listing](https://vrvlog.fun/exporter/index.json). The comparison is valid for the exact tagged source with unchanged checkout bytes; a different commit or edited documentation can change the ZIP. The release workflow's `expected_package_sha256` check uses this same byte equality.

## Pull requests

- Explain the concrete problem and resulting behavior, and link the related issue if one exists.
- Keep changes focused. Update affected guides and third-party notices when relevant.
- Describe what you actually verified, including commands, results and remaining limitations.
- Preserve dependency version gates, published tags and release channel policy. Do not combine unrelated version bumps or release operations with a documentation PR.

通常の変更は `develop` から `feature/<slug>` / `fix/<slug>` を作り、`develop` 向けPRで統合します。`develop` が既定の開発ブランチ、`main` がステージングです。`develop` の昇格は `release/X.Y.Z` を作って `main` 向けPRを開き、Merge commitで統合します。緊急修正は `main` から `hotfix/<slug>` を作ります。`main` 向けPRでは、同じリポジトリのrelease/hotfixブランチだけを許可します。昇格ごとにpackage.jsonの版を増やし、未公開の `X.Y.Z-beta.N` と同じ版の変更履歴を用意します。stable化は対応する `release/X.Y.Z` の版更新PRとして準備し、自動公開しません。

`main` へのマージで検証用ZIPと候補の証跡を自動生成します。候補が成功すると、未公開の `X.Y.Z-beta.N` はGitHubのプレリリースとして自動公開し、VPM一覧も同じタグから更新します。stableの公開だけは別途承認後の手動操作です。`main` 更新後は `main` → `develop` のPRをMerge commitで戻し、緊急修正と昇格履歴を次の開発へ反映します。詳しい手順は[開発・検証・配布の運用](Documentation~/DevelopmentPipeline.md#ブランチとステージング)を参照してください。

Both `develop` and `main` require a pull request and successful `schema` and `listing-validation` checks against an up-to-date branch. Use Squash for ordinary changes into `develop`, and Merge commit for promotion to `main` and back-merges into `develop`. Address review findings before integration. Passing CI does not replace required Unity or visual checks for changes that need them.

## Licenses and attribution

Contributions to exporter code use the repository's [MIT License](LICENSE). Preserve existing copyright and license notices. Identify the source and license of third-party material you add; the existing [third-party notices](ThirdPartyNotices/) and [dependency patch notice](Documentation~/DependencyPatches/README.md) show the current attributions. Avatar assets and images retain their owners' terms.
