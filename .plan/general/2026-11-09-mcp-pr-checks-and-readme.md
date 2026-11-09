# 2026-11-09 — MCP 서버 PR 검사와 README 검증 안내 정리

- Date: 2026-11-09
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/127
- Status: Implemented, draft PR #128

## Goal

Run the TypeScript server build and tests during pull request review. Provide a single Korean repository README for installation, agent setup, and usage.

## Non-goals

Product behaviour, Unity licence handling, release automation, package publication, and branch protection settings.

## Context / Constraints

Unity CI already covers EditMode and PlayMode. The npm publication workflow runs the server checks through prepack on Node 24. The new check uses Node 22.x, the lowest supported Node major, and requires no Unity licence.

## Approach (Checklist)

- [x] Inspect the README files, package scripts, and existing Actions workflows.
- [x] Add a standalone read-only PR and develop push workflow with the MCP lockfile.
- [x] Consolidate the repository README into a single Korean user guide.
- [ ] Verify local commands, relative paths and workflow syntax. Open a draft PR and inspect its checks.

## Validation

- Run `npm ci`, `npm run build` and `npm test` from `mcp/` in a Bash environment. On Windows, explicitly configure npm to use Git Bash for its scripts.
- Parse the workflow, check local Markdown targets, and confirm that the server job succeeds on the PR.
- Local Windows verification on Node 24.19.0 with Git Bash passed the TypeScript build and all 390 server tests.

## Risks & Rollback

- Node 22 may reveal a regression that is hidden by the Node 24 release path.
- GitHub fork PRs use the regular pull request event, read-only contents access, and no persisted checkout credentials.
- Revert the commit to remove the server check and restore the README links.

## Open Questions

None. The plan reviews confirmed the workflow boundary and made the Bash requirement explicit.
