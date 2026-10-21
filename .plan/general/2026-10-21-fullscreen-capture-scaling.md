# 2026-10-21 — 전체 화면 캡처 축소 옵션

- Date: 2026-10-21
- GitHub Issue: #121
- Status: Implemented; FAST/MEDIUM/HEAVY plan review and independent pair review passed

## Goal
Allow capture_screen({maxEdge:256}) and its perform_actions equivalent without a target, preserving image metadata and defaults.

## Non-goals
No renderer changes, new tools, version bump, game edits or release publishing.

## Context / Constraints
Unity already supports [null, options]. The MCP layer rejected maxEdge without target and discarded its options. padding still requires a target. Changes are isolated to tools.ts and capture tests; #118 independently owns instructions/prompts.

## Approach (Checklist)
- [x] **Step 0: Recon**: verify Unity reader and renderer accept null target and apply MaxEdge.
- [x] **Step 1: Implementation**: relax only maxEdge validation, use shared captureScreenParams for standalone/batch, describe scaling.
- [x] **Step 2: Tests**: registered tool/batch transport, invalid padding, schema, coordinate metadata, Unity reader/downscale regression.
- [ ] **Step 3: Rollout / Rollback**: independent draft PR; revert this commit to roll back.

## Validation
- **Commands to run:** npx tsc; node --test dist/test/*.test.js; pinned Unity EditMode tests.
- **Expected output:** default [] and crop wire unchanged; full-screen options encoded [null,{maxEdge:256}]; 1920x1080 downscales to256x144 without upscaling smaller images.
- tsc passed, targeted79/79 and full MCP387/387 passed. git diff --check passed. Independent critic PASS.
- Unity EditMode 322/322 passed, including the new reader/downscale regression. Real framebuffer capture is not exercised by these stub/geometry tests.

## Risks & Rollback
- **Risks:** no renderer behavior change; tests validate protocol and geometry, not GPU readback.
- **Rollback steps:** revert independent issue commit.

## Open Questions
None.