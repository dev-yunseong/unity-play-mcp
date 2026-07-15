# 2026-07-15 — Update WordVenture tracking sample

- Date: 2026-07-15
- Jira Issue: ARTEL-9
- Status: Complete

## Goal

Update the `samples/WordVenture` submodule with a scene fixture that exercises
`[ArtelState]` and `[ArtelAction]`, then pin the resulting commit in the SDK.

## Approach

- [x] Add attributed state/action fixture to the remote-control PoC scene.
- [x] Resolve the SDK's Newtonsoft.Json and Mono.Cecil dependencies.
- [x] Commit fixture changes inside the WordVenture repository.
- [x] Update the SDK submodule pointer and repository ignore rules.
- [x] Keep generated solution files out of Git.

## Validation

- `git -C samples/WordVenture diff --cached --check`
- Verify the scene references `TrackingTest.cs` by its Unity GUID.
- Verify the parent repository records the new WordVenture gitlink.
- Unity batch compilation was skipped because the project was already open in
  another Unity Editor instance.

## Rollback

Revert the SDK gitlink commit, then revert the corresponding WordVenture sample
commit if the fixture itself must be removed.
