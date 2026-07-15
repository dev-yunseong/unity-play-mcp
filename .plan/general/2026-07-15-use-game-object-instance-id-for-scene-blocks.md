# 2026-07-15 — Scene block ID를 GameObject instance ID로 변경

- Date: 2026-07-15
- Jira: ARTEL-19
- Status: Complete

## Goal

Use Unity's scene handle and each active `GameObject`'s instance ID as scene
and block IDs so the same live objects keep stable IDs across repeated scans.

## Non-goals

- Persist scene or block IDs across Unity sessions.
- Change the JSON field shape.
- Modify action routing beyond its existing integer target lookup.

## Context / Constraints

`SceneScanner` currently assigns traversal-order IDs from a counter reset on
every scan. Hierarchy edits can therefore change an existing object's ID, and
the scene always receives `1`. Unity scene handles and instance IDs are stable
only for live objects in the current process, matching the scan/action boundary.

## Approach (Checklist)
- [x] **Step 0: Recon** (Inspect `SceneScanner`, action lookup, DTO mapping, and tests)
- [x] **Step 1: Implementation** (Use `Scene.handle` and `GameObject.GetInstanceID()`; document lifetime)
- [x] **Step 2: Tests** (Run available Unity EditMode tests or compile validation)
- [x] **Step 3: Rollout / Rollback** (No migration; revert the focused commit if consumers require traversal IDs)

## Validation
- **Commands to run:** Unity 2022.3.34f1 batchmode EditMode tests against a clean local sample clone with `kr.artel.sdk` enabled in `testables`.
- **Expected output:** Passed: 8, Failed: 0, including both `SceneScannerTests` cases.

## Risks & Rollback
- **Risks:** Consumers may incorrectly cache IDs across object destruction or Unity restarts; instance IDs may be negative and must remain opaque integers.
- **Rollback steps:** Revert the focused implementation commit to restore traversal-order IDs.

## Open Questions
- None. Jira branch context and session-scoped identity requirement are established.
