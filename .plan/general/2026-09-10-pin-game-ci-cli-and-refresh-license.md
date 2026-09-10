# 2026-09-10 — game-ci Test Runner 버전 고정 및 라이선스 갱신

- Date: 2026-09-10
- GitHub Issue: #66
- Status: In progress

## Goal

Restore Unity CI by pinning the last known-good `unity-test-runner` action and refreshing the repository's Unity license secret from the workstation.

## Non-goals

- Pin or change the separate game-ci CLI input.
- Upgrade Unity or other GitHub Actions.
- Change package behavior or tests.
- Store license contents in Git.

## Context / Constraints

- The last green develop run 34297755794 started at 2026-09-09T01:05:29Z.
- `game-ci/unity-test-runner` v4.4.0 was released at 2026-09-09T01:06:31Z and moved the mutable `@v4` tag to SHA `32e57712352b500e17974b245a6dce9e11a73213`.
- The first failing run 34298725134 started at 2026-09-09T01:19:28Z and downloaded that v4.4.0 SHA. The action then delegated to the new game-ci CLI and failed during activation.
- v4.3.1 is the release that preceded v4.4.0 at the time of the last green run. Pin the action itself to v4.3.1 and leave its internal behavior unchanged.
- `UNITY_LICENSE` was refreshed from `C:\ProgramData\Unity\Unity_lic.ulf` at 2026-09-10T05:20:40Z without printing or committing its contents. `UNITY_EMAIL` and `UNITY_PASSWORD` remain configured.

## Approach (Checklist)

- [x] **Step 0: Recon** Correlate the last green run, first failing run, action SHA, and upstream release times.
- [x] **Step 1: Implementation** Replace `game-ci/unity-test-runner@v4` with `game-ci/unity-test-runner@v4.3.1`. Remove the incorrect `cliVersion` pin.
- [x] **Step 2: License secret** Verify the local ULF is a regular non-empty file, stream it through stdin to `UNITY_LICENSE`, and confirm the secret timestamp advanced without exposing its value.
- [ ] **Step 3: Tests** Use PR #67's EditMode and PlayMode jobs as the gate. Require logs to show v4.3.1, successful activation, actual test execution, and passing results; skipped jobs do not count.
- [ ] **Step 4: Rollout / Rollback** Merge the CI-fix PR after its requirements are met. Revert the action pin to roll back; rotate `UNITY_LICENSE` to another known-good file if needed.

## Validation

- **Commands to run:** `git diff --check`; inspect the workflow; PR #67 EditMode and PlayMode checks and logs.
- **Expected output:** clean diff validation, action v4.3.1, successful Unity activation, executed tests, and both test modes green.

## Risks & Rollback

- **Risks:** The refreshed local ULF may have an independent activation problem; the pinned action run will distinguish that from the v4.4.0 regression.
- **Rollback steps:** Revert the workflow commit. GitHub does not expose prior secret values, so replace `UNITY_LICENSE` only with another known-good license.

## Open Questions

- None.
