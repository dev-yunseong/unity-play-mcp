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
- [x] **Step 3: Keep the walk off the test runner's own scene** The Test Framework appends its temporary play-mode scene to Build Settings, so the walk reloaded it in `Single` mode and a second runner restarted the whole play-mode run. `SceneWalkTests` now narrows Build Settings to its own two scenes before starting the walk.
- [x] **Step 4: Rollout / Rollback** Review the diff; rollback is a normal commit revert.

## Validation

- **Commands to run:** Run the focused PlayMode scene-walk tests when a Unity editor is available; otherwise perform static diff and repository checks and read the CI PlayMode results.
- **Expected output:** The fixture created by a visited scene is absent after the walk and cleanup is named in the log.
- **Observed:** The first CI PlayMode run reported 21 passed / 2 failed. Both failures were in `StraySpawnTrackerTests`, and both were collateral: the walk visited the Test Framework's temporary scene, a second runner started, and every fixture ran a second time on top of the first run's objects. Narrowing Build Settings inside the test removes that second run.

## Risks & Rollback

- **Risks:** Misclassifying SDK-owned or preexisting persistent roots; addressed-scene loaders with behavior outside their documented single-load contract. `SceneWalkTests` now leaves its own last visited scene active for the rest of the play-mode run, because the walk has no origin scene to return to once the runner's temporary scene is out of Build Settings.
- **Rollback steps:** Revert the implementation and test commit.

## Open Questions

- None.
