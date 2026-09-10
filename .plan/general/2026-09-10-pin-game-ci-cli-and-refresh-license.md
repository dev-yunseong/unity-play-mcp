# 2026-09-10 — game-ci CLI 버전 고정 및 라이선스 갱신

- Date: 2026-09-10
- GitHub Issue: #66
- Status: In progress

## Goal

Restore deterministic Unity CI by pinning the game-ci CLI to v0.1.53 and refreshing the repository's Unity license secret from a newly issued workstation license.

## Non-goals

- Upgrade Unity or other GitHub Actions.
- Change package behavior or tests.
- Store license contents in Git.

## Context / Constraints

- `game-ci/unity-test-runner@v4` defaults `cliVersion` to `latest`; failing run 34437174074 resolved v0.1.57 and stopped before tests.
- A first pin to v0.1.56 downloaded correctly but selected personal licensing and also failed before tests.
- The most recent green develop run is 34297755794 at 2026-09-09T01:05:29Z. The official release timeline shows v0.1.53 was the newest CLI then; v0.1.54 was released later at 2026-09-09T12:40:09Z. Pin v0.1.53 as the last version supported by a successful repository run.
- The workstation ULF uploaded at 2026-09-10T05:20:40Z has a signed `UpdateDate` in the future from the runner's perspective and fails with `TimeStamp validation failed`.
- Current GameCI guidance supports a Windows-generated ULF on Linux. Unity Hub must reissue the local file before it is uploaded again.
- `UNITY_EMAIL` and `UNITY_PASSWORD` already exist, so preflight remains satisfied.

## Approach (Checklist)

- [x] **Step 0: Recon** Confirm the resolved CLI versions, failure points, last green run, workflow input, and local license metadata.
- [x] **Step 1: Implementation** Add `cliVersion: v0.1.53` to `.github/workflows/unity-tests.yml`. The `v` prefix is required because the runner interpolates the input directly into the GitHub release tag URL.
- [ ] **Step 2: Local license recovery** In Unity Hub, use `Preferences > Licenses > Add > Get a free personal license` to force creation of a newly issued ULF. Confirm its modification time and signed dates changed, then stream `C:\ProgramData\Unity\Unity_lic.ulf` to `UNITY_LICENSE` with the command below. Never print or copy the contents into the repository.
- [ ] **Step 3: Tests** Confirm the secret's updated timestamp with `gh secret list`, then use PR #67's EditMode and PlayMode jobs as the gate. Require logs to show CLI v0.1.53, successful activation, actual test execution, and passing results; skipped jobs do not count.
- [ ] **Step 4: Rollout / Rollback** Merge the CI-fix PR after its requirements are met. Revert the pin to roll back the workflow; reissue and rotate `UNITY_LICENSE` if the new secret must be replaced.

## Validation

- **Commands to run:** reissued ULF metadata check; `gh secret list --repo dev-yunseong/unity-play-mcp`; `git diff --check`; inspect the workflow; PR #67 EditMode and PlayMode checks and logs.
- **Expected output:** refreshed `UNITY_LICENSE` timestamp, clean diff validation, logs showing game-ci CLI v0.1.53 and successful activation, executed tests, and both Unity test modes green.

### Secret update command

Run this PowerShell sequence only after Unity Hub has reissued the file and its metadata changed. It keeps the contents in memory without displaying them.

```powershell
$licensePath = 'C:\ProgramData\Unity\Unity_lic.ulf'
$licenseFile = Get-Item -LiteralPath $licensePath -ErrorAction Stop
if ($licenseFile.PSIsContainer -or $licenseFile.Length -le 0) { throw 'Unity license file is missing or empty.' }
gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI authentication failed.' }
$licenseValue = [System.IO.File]::ReadAllText($licensePath)
if ([string]::IsNullOrWhiteSpace($licenseValue)) { throw 'Unity license content is empty.' }
$licenseValue | gh secret set UNITY_LICENSE --repo dev-yunseong/unity-play-mcp
if ($LASTEXITCODE -ne 0) { throw 'Updating UNITY_LICENSE failed.' }
Remove-Variable licenseValue
```

Before and after the update, run `gh secret list --repo dev-yunseong/unity-play-mcp --json name,updatedAt --jq '.[] | select(.name == "UNITY_LICENSE")'` and require `updatedAt` to advance to the current operation time.

## Risks & Rollback

- **Risks:** Unity Hub may reuse the current invalid ULF instead of issuing a new one; verify file metadata before rotation. A correctly reissued license can still fail if the Unity account credentials no longer match.
- **Rollback steps:** Revert the workflow commit. Since GitHub does not expose prior secret values, replace `UNITY_LICENSE` only with another newly issued known-good license.

## Open Questions

- None.
