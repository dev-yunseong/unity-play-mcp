# 2026-10-21 — Live play guidance

- Date: 2026-10-21
- GitHub Issue: #118
- Status: Implemented; independent pair review PASS

## Goal

Guide agents through current observation, normal player input, and explicit verification of the intended effect using existing APIs.

## Non-goals

No SDK/runtime changes, new tools, game-specific APIs, ArcaneCasters changes, gameplay, release publishing or version bumps. Runtime validation, release recovery and capture scaling are independent issues #119–#121.

## Context / Constraints

Plan approved in the shared fast/medium/heavy review before implementation. Branch docs/118 owns only mcp/src/instructions.ts, mcp/src/prompts.ts and their matching tests. Keep the existing 2000-character server instruction budget and legacy unsupported-capability fallback.

## Approach

- [x] Prefer get_play_capabilities then observe with freshness current before each live step.
- [x] Use basedOnObservationId and expect predicates grounded in observed facts or inspect_action, distinguishing accepted input from confirmed effects.
- [x] Retry an identical transport request with the same operationId; use a new id for new input. Re-observe unconfirmed effects instead of repeating input automatically.
- [x] Preserve legacy input fallback with actual scene/member checks, visual captures, and stop at mismatch or unconfirmed result.
- [x] Update guidance contract tests, typecheck, run focused and full MCP tests.
- [x] Independent pair review passed (release_flow critic). Commit and draft PR follow validation.

## Validation (2026-10-21)

- mcp/node_modules/.bin/tsc.cmd passed.
- node --test dist/test/instructions.test.js dist/test/prompts.test.js: 18/18 passed.
- node --test dist/test/*.test.js: 383/383 passed.
- serverInstructions is 1769 characters; existing limit is 2000.
- git diff --check passed. No Unity runtime or gameplay validation required for this guidance-only change.

## Risks / Rollback

Instructions guide behavior rather than enforce it. Unknown outcomes must remain explicit; no automatic repeat input is recommended. Revert this branch independently; there is no persisted state migration.
