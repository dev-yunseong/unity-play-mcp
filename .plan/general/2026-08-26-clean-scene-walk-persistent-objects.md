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
- [x] **Step 2: Tests** Cover the behaviour at `StraySpawnTracker`, the unit both scene walks share.
- [x] **Step 3: Drop the full-walk PlayMode test** `AffordanceBootstrap.WalkAllScenes()` cannot run inside Unity's PlayMode test runner. See `## The full-walk test`.
- [x] **Step 4: Rollout / Rollback** Review the diff; rollback is a normal commit revert.

## Validation

- **Commands to run:** Run the PlayMode suite when a Unity editor is available; otherwise perform static diff and repository checks and read the CI PlayMode results.
- **Expected output:** A root a visited scene left behind is moved into that scene and named in the returned list.
- **Observed:** With the full-walk test present, CI reported 21 passed / 2 failed — both failures collateral damage from the restarted run described below. With it removed, every `StraySpawnTrackerTests` case passes.
- **Not covered by automation:** that `AffordanceBootstrap.WalkAllScenes()` itself carries the leftover root away. That stays a manual check in WordVenture.

## Risks & Rollback

- **Risks:** Misclassifying SDK-owned or preexisting persistent roots; addressed-scene loaders with behavior outside their documented single-load contract. The walk's own entry point has no automated regression test, so a change to `SceneWalk.Collect` can only be caught by the tracker tests plus a manual run.
- **Rollback steps:** Revert the implementation and test commit.

## The full-walk test

A PlayMode test that drove `AffordanceBootstrap.WalkAllScenes()` over two fixture
scenes was written and then removed. It cannot coexist with Unity's PlayMode test
runner:

- The Test Framework appends its temporary run scene (`Assets/InitTestScene<ticks>.unity`)
  to Build Settings when it enters play mode. The walk reads that list as it is, so it
  reloads that scene in `Single` mode. A second `PlaymodeTestsController` comes up with
  it and the whole run starts over — every fixture executes a second time on top of the
  first run's objects. That is what failed `StraySpawnTrackerTests` in CI, not the
  tracker.
- Narrowing `EditorBuildSettings.scenes` from inside the test does not help. The runtime
  scene list is baked when play mode starts; `SceneManager.sceneCountInBuildSettings`
  still reported the runner's scene afterwards (measured in CI).
- Even a walk that skipped the runner's scene would reload it at the end: the walk
  returns to `SceneManager.GetActiveScene().buildIndex`, which is that scene.

The walk exists to throw away the running game, and under the test runner the running
game is the runner. Scoping it for a test would mean a seam in production code, which is
a separate decision from this fix.

## Open Questions

- Whether `SceneWalk` should take an explicit scene list, which would make the real entry
  point testable. Not decided here.
