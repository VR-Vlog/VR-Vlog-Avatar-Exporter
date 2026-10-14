# Exporter public OSS CI policy

`VR-Vlog/VR-Vlog-Avatar-Exporter` is a public OSS repository. Its CI uses only the existing GitHub-hosted standard Ubuntu jobs (`runs-on: ubuntu-latest`). Do not register, restore, bootstrap or automatically deploy a self-hosted Exporter runner for this repository. GitHub-hosted CI is independent of mySI's implementation, database, configuration and application lifecycle.

The existing workflows, triggers, permissions and publication approvals stay in force. Required `schema` and `listing-validation` checks must succeed on an up-to-date PR branch. Host checks do not establish Unity integration, device behavior or publication approval. Follow [CONTRIBUTING.md](../../../CONTRIBUTING.md) and the [development pipeline](../../../Documentation~/DevelopmentPipeline.md).

## Retired self-hosted runtime

The Python modules, updater and fixtures in this directory are retained as historical implementation and recovery evidence. Their presence does not enable a runner or authorize installation, source rollout, registration or restoration. They must not be used to activate self-hosted CI for the public repository. The existing host-validation suite continues to discover its regression fixtures; this policy clarification adds no workflow, job, trigger or test route.

The former isolated Exporter role used `_mysi_ci_exporter` UID/GID 402, label `exporter-general-mac-arm64`, service `jp.mitsuya0077.mysi.general-exporter`, `/Library/Application Support/mySI-CI` state and `/opt/mySI-CI` tools/runtime. These names are legacy CI compatibility records, independent of mySI's application lifecycle. The old controller used the root-protected `/opt/LocalGitHubRunners/tools/gh`; jobs did not receive the owner's credentials.

The former supervisor checked repository/workflow/event/run/head/job provenance and exact-head external-PR approvals, used private per-generation HOME/cache and native host/iOS locks, and required native UID 402 stop proof before releasing reservations. Unknown or interrupted work kept its evidence. Those safeguards describe the retired implementation, not an active self-hosted operating policy.

## Retirement and retained evidence

Stopping and disabling the previously installed service is a separate administrator operation. Neither this document nor a successful hosted check proves live retirement. Confirm that the service cannot restart or re-register, and record any old GitHub registration disposition only after the established native stop/identity checks. Do not restore the service to obtain an online status or validation result.

Keep protected registry history, approvals, current/recovery records and native journals. Do not manually delete credentials, overwrite unknown records, recursively change the shared passwd HOME, or remove other CI accounts' application-group aliases. The former `/opt/mySI-CI/home` anchor was shared and root-protected; historical repair evidence must not become a new activation instruction.

The old updater was a stopped-service source migration tool: its `installed_stopped` result explicitly left `online_proven=False`. Old staging, update and bootstrap instructions are retired for this public repository. Historical online rows, old CURRENT data and a briefly observed root PID are not proof of an active new-generation Listener or of retirement.

`registries/exporter.json` remains a protected external history contract. Its schema, installation source SHA, approval input and registration ID/name/labels/group provenance are retained for result consumers. The recorded repository-name migration required old/canonical GitHub GETs to prove repository ID 1355769104; that proof does not authorize another registration. Runtime or registry state alone is not evidence that a job succeeded.

## Historical source and validation

The runtime was extracted into this product repository from the former standalone CI implementation. The original source-only checkout was fetched from canonical `origin/main` at `98dfb7a8f602b941dbbba50715c070d38b495893`; it had no `AGENTS.md`. The original ProjectOrchestrator checkout, Unity Library and build caches were not copied or edited. VR Vlog/mobile/cohort/persistent policy was excluded from this Exporter package.

The reviewed runtime source remains `ced515df7f8a58c7872cb4d72c7637614ad83f4d`. Existing hosted validation still checks the retained fixtures. Reuse observed results only for their recorded source and environment, and leave final mandatory verification with one owner. This README policy change does not modify runtime code or perform service, registration, workflow dispatch or publication operations.
