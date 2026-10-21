# 2026-10-21 — 플레이 신뢰성과 검증 개선

- Date: 2026-10-21
- GitHub Issue: #118, #119, #120, #121
- Status: Approved — FAST, MEDIUM, HEAVY reviews passed

## Goal

Implement the six reviewed improvements in four independently reviewable branches: current observation and effect verification guidance (#118), real reload regression and idle performance measurement (#119), release PR/CI recovery (#120), whole-screen capture scaling (#121).

## Non-goals

No ArcaneCasters edits, gameplay, release publishing, new game-specific APIs, credential changes or repository permission changes. No claim of zero SDK overhead. Existing APIs and defaults remain compatible.

## Context / Constraints

Each worker owns an isolated .worktrees/<issue> checkout from origin/develop. Parent owns integration and #121. All plans must pass fast/medium and heavy review before implementation. Draft PRs stay independent and no version bump is included.

## Approach (Checklist)
- [x] **Step 0: Recon**: Existing observe(current) and act_and_observe(expect) already cover live-play needs. Unity reader accepts [null, options]. Release PR creation is blocked by repository policy; YAML permissions cannot override it.
- [ ] **Step 1: Implementation**:
  - #118 owns mcp/src/instructions.ts, prompts.ts and respective tests only. Prefer current observation and explicit outcome predicates in live-play guidance; retain legacy fallback, retry same operationId only for identical request, fresh id for new input. Keep instructions under existing size budget.
  - #119 owns new template Editor RuntimeLifecycleProbe, Python runner and its unit tests, opt-in PlayMode RuntimePerformanceTests and docs/runtime-validation.md. SessionState probe survives actual RequestScriptReload; verify changed static token, reconstructed service, no duplicate overlays, actual 15 second idle shutdown, and selective status/readings reactivation. Bounded timeout, machine-readable artifact, error exit required. Use isolated test project; fail clearly on occupied transport port instead of killing another Editor. Performance benchmark reports CPU marker mean/p50/p95 and main-thread GC with attribution caveat, warmup and fixed sample count; opt-in only, no timing thresholds in ordinary CI. Runtime state assertions are required. No workflow changes in this branch.
  - #120 owns bump-version.yml, unity-tests.yml, new open-release-pr.mjs and node tests, docs/releasing.md. Testable argument-array gh helper creates/reuses draft PR using body-file, validates full HEAD SHA, explicitly dispatches Unity CI with expected_sha. CI checkout must match expected_sha before test execution, otherwise fail. Preserve existing fork-secret protections. Denied creation reports branch/compare URL and exact recovery steps, remains failure; do not mutate permissions or bypass policy. Queued CI is not passed CI. Existing PR path must also dispatch. Use PR label chore per pull-request.md.
  - #121 owns mcp/src/tools.ts, relevant schema/tools/capture tests and Unity CaptureScreenTests. Allow maxEdge without target, keep padding target-only, encode [null,{maxEdge}] through shared single/batch path; preserve [] and crop defaults. No renderer change needed. Update capture tool description; coordinate instructions ownership by keeping #121 changes out of instructions.ts.
- [ ] **Step 2: Tests**: targeted Node tests then full MCP suite/typecheck; Python runner tests; Unity EditMode/PlayMode including existing lifecycle regressions; run real probe and opt-in benchmark in isolated throwaway Unity project. No game project changes. If port occupied, report and validate non-network pieces without stopping the user's game; actual probe remains outstanding rather than claiming pass.
- [ ] **Step 3: Rollout / Rollback**: pair-review each independent diff, commit per issue, draft PR per branch with actual evidence and limitations. Revert each PR independently; no persisted data migration.

## Validation
- **Commands to run:** npx tsc; node --test dist/test/*.test.js; node --test .github/scripts/open-release-pr.test.mjs; Python runner unittest; Unity pinned 2022.3.34f1 test runner; documented standalone lifecycle runner and explicit performance filter.
- **Expected output:** targeted/full suites pass; probe JSON reports real reload, 15 second idle, selective wake evidence; benchmark produces numeric samples with environment metadata without claiming SDK-only GC; workflow tests exercise denied PR, existing PR, stale SHA and dispatch failure.

## Risks & Rollback
- **Risks:** real reload execution and profiling vary by Editor environment; opt-in and timeout isolate them. CI dispatch cannot solve forbidden PR creation; preserve a clear actionable failure. Schema relaxation must remain identical for standalone and batch captures.
- **Rollback steps:** revert the independent issue commit/PR. No live game or repository settings rollback required.

## Open Questions
- None required from user. Actual runtime benchmark/probe feasibility will be verified and reported.

## #119 validation evidence

- Python runner: 4 tests passed, including occupied port, timeout, stale output, invalid/nonfinite/boundary artifact checks.
- Unity 2022.3.34f1 EditMode: 321/321 passed; new Editor probe and benchmark compile successfully.
- Actual lifecycle runner stopped at preflight: port 17311 is occupied by the user's ArcaneCasters Editor. No game process was stopped.
- Explicit benchmark attempted: 1 failed due SocketException from that same occupied port before sampling. No numeric performance result is claimed. Full PlayMode and actual reload remain outstanding until the port is free.
- Pair review: PASS after adding pre-15-second active assertions and bounded [15,20) shutdown evidence. The benchmark remains opt-in and no workflows or runtime production code changed.
