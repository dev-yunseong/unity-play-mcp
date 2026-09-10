# 2026-09-10 — game-ci CLI 버전 고정 및 라이선스 갱신

- Date: 2026-09-10
- GitHub Issue: #66
- Status: In progress

## Goal

Restore deterministic Unity CI by pinning the game-ci CLI to v0.1.56 and refreshing the repository's Unity license secret from the workstation's active license.

## Non-goals

- Upgrade Unity or other GitHub Actions.
- Change package behavior or tests.
- Store license contents in Git.

## Context / Constraints

- `game-ci/unity-test-runner@v4` currently defaults `cliVersion` to `latest` and the failing runs resolved v0.1.57, released on 2026-09-09.
- v0.1.56 is the immediately preceding official release.
- Run 34437174074 resolved v0.1.57 and failed before executing tests. The most recent green develop run is 34297755794, but GitHub no longer retains its job log, so its resolved CLI version cannot be verified; v0.1.56 is selected from the official release order rather than claimed as a previously proven version in this repository.
- The failing jobs stop during license activation with `TimeStamp validation failed` before tests execute.
- The local license is at `C:\ProgramData\Unity\Unity_lic.ulf`; only its contents may be sent to the encrypted GitHub Actions secret.
- `UNITY_EMAIL` and `UNITY_PASSWORD` already exist, so preflight will remain satisfied.

## Approach (Checklist)
- [x] **Step 0: Recon** Confirm the resolved CLI version, failure point, previous release, workflow input, and local license path.
- [x] **Step 1: Implementation** Add `cliVersion: 0.1.56` to `.github/workflows/unity-tests.yml`. Verify the local ULF exists and is non-empty, then stream it through stdin to `gh secret set UNITY_LICENSE --repo dev-yunseong/unity-play-mcp` without printing or persisting its contents.
- [ ] **Step 2: Tests** Confirm the secret's updated timestamp with `gh secret list`, validate the workflow diff, open the draft PR, and use that PR's EditMode and PlayMode jobs as the gate. Require logs to show CLI v0.1.56, successful activation, actual test execution, and passing results; skipped jobs do not count.
- [ ] **Step 3: Rollout / Rollback** Merge the CI-fix PR after its requirements are met. Revert the pin to roll back the workflow; rotate `UNITY_LICENSE` to another known-good license if the new secret must be replaced.

## Validation
- **Commands to run:** local ULF existence/non-empty check; `gh secret list --repo dev-yunseong/unity-play-mcp`; `git diff --check`; inspect the workflow; CI-fix PR EditMode and PlayMode checks and logs.
- **Expected output:** refreshed `UNITY_LICENSE` timestamp, clean diff validation, logs showing game-ci CLI v0.1.56 and successful activation, executed tests, and both Unity test modes green.

### Secret update command

Run this PowerShell sequence after recording the current `updatedAt`. It resolves a regular, non-empty file, verifies GitHub authentication, keeps the contents in memory without displaying them, checks the CLI exit code, and clears the variable afterward.

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
- **Risks:** A refreshed node-locked Unity Personal license may still fail in the Linux runner; v0.1.56 may contain a separate activation defect.
- **Rollback steps:** Revert the workflow commit. Since GitHub does not expose prior secret values, replace `UNITY_LICENSE` only with another available known-good license.

## Open Questions
- None.
