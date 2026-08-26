# 2026-08-26 — Clean scene-walk persistent objects

- Date: 2026-08-26
- GitHub Issue: None
- Status: Complete

## Goal

Ensure the affordance scene walk removes `DontDestroyOnLoad` objects created by each visited scene before another scene is visited.

## Non-goals

- Rework `AllSceneScanner` or registration scanning.
- Change game-owned persistent objects that existed before the walk.
- Change the sample game's singleton implementation.

## Context / Constraints

`AffordanceBootstrap.WalkAllScenes()` uses `Affordance.Scan.SceneWalk`, while the current branch only added cleanup and tests around `AllSceneScanner`. `SceneWalk` loads scenes in `Single` mode, so objects moved to Unity's persistent scene survive into later scenes unless returned to the visited scene before its unload.

## Approach (Checklist)

- [x] **Step 0: Recon** Inspect both scene-walk implementations and the `TutorialController` lifecycle.
- [x] **Step 1: Implementation** Track roots around every `SceneWalk` visit and return new persistent roots to the visited scene.
- [x] **Step 2: Tests** Drive the actual `AffordanceBootstrap.WalkAllScenes()` entry point with a persistent fixture.
- [x] **Step 3: Rollout / Rollback** Review the diff; rollback is a normal commit revert.

## Validation

- **Commands to run:** Run the focused PlayMode scene-walk tests when a Unity editor is available; otherwise perform static diff and repository checks.
- **Expected output:** The fixture created by a visited scene is absent after the walk and cleanup is named in the log.

## Risks & Rollback

- **Risks:** Misclassifying SDK-owned or preexisting persistent roots; addressed-scene loaders with behavior outside their documented single-load contract.
- **Rollback steps:** Revert the implementation and test commit.

## Open Questions

- None.
