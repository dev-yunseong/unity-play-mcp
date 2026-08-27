# 2026-08-27 — KeyCode.Mouse0 이 마우스 이벤트를 내게 한다

- Date: 2026-08-27
- Jira: ARTEL-601
- Branch: `fix/sdk-keycode-mouse0-이-마우스-이벤트를-내지-못한다-ARTEL-601` (from `origin/develop`)
- Status: Implemented, EditMode 320 green, PlayMode 32 green

## Goal

Make a mouse `KeyCode` mean the mouse button it names. An agent that sends `key_down` with
`KeyCode.Mouse0` must reach everything a `mouse_down` reaches — `Input.GetMouseButton(0)`, the uGUI
pointer handlers, and the `OnMouse*` messages on whatever the pointer is over — and an agent that
sends `mouse_down` must be visible to a game polling `Input.GetKey(KeyCode.Mouse0)`.

Today the two are separate stores that never consult each other. `VirtualKeyboardState` holds keys,
`VirtualMouseState` holds buttons, and `KeyCode.Mouse0` — which in Unity *is* the left mouse button —
only ever lands in the first one. The string `Mouse0` does not appear anywhere in the SDK.

## Non-goals

- New Input System support.
- Picking objects rendered through a camera other than `Camera.main`.
- `KeyCode.Mouse3` through `KeyCode.Mouse6`. `VirtualMouseState.ButtonCount` is 3 and Unity's mouse
  events cover three buttons.
- Unifying event dispatch onto state edges. Driving `PointerEventDispatcher` from `VirtualMouseState`
  transitions inside `ArtelInput.AdvanceFrame` — the way `VirtualMouseMessenger` already works —
  would remove the whole class of "one entry point forgot to fire the events". It also moves the
  dispatcher's ownership out of `ActionExecutor`, delays every pointer event by one frame, and
  rewrites the constructor used by ten test files. Recorded as follow-up, not done here.

## Context / Constraints

- `VirtualMouseMessenger` sends `OnMouse*` only while `VirtualMouse.OwnsPointer` is true
  ([Input.cs:232](../../Packages/kr.artel.sdk/Runtime/UnityEngine/Input.cs)). A click that never had
  a `move_mouse` in front of it stays silent. That is correct: there is no pointer position to pick
  a target with, and the engine would deliver nothing either.
- `VirtualMouseMessenger` sends `OnMouse*` for the left button only (`DrivingButton`), because the
  engine does. `KeyCode.Mouse1` and `KeyCode.Mouse2` reach `Input` polling and uGUI, no further.
- `VirtualMouseState` buttons do not expire; only an explicit release ends one. `key_click` carries
  a duration, so the timed release has to come from somewhere.
- `PointerEventDispatcher` is owned by `ArtelManager` and threaded into `ActionExecutor`. It is not
  reachable from the static `ArtelInput`.
- Existing `mouse_down` / `mouse_up` callers must behave exactly as they do now.

## Approach (Checklist)

- [x] **Step 0: Recon** — done. The seams are
      [ArtelInput](../../Packages/kr.artel.sdk/Runtime/UnityEngine/Input.cs),
      [VirtualMouseState](../../Packages/kr.artel.sdk/Runtime/UnityEngine/VirtualMouseState.cs), and
      [ActionExecutor](../../Packages/kr.artel.sdk/Runtime/ActionExecutor.cs).

- [x] **Step 1: Map the keycodes.** A small `MouseButtonKeyCode` helper next to `VirtualMouseState`
      that converts `KeyCode.Mouse0` / `Mouse1` / `Mouse2` to 0 / 1 / 2 and reports when a keycode is
      not one. One place holds the correspondence, so no caller open-codes it.

- [x] **Step 2: Read the mouse through the key calls.** `ArtelInput.GetKey`, `GetKeyDown`, and
      `GetKeyUp` additionally consult `VirtualMouse` when the keycode maps to a button. The `string`
      overloads parse to a `KeyCode` first, so they inherit this without a second code path. `anyKey` and
      `anyKeyDown` consult `VirtualMouse.IsAnyButtonHeld` and a new `IsAnyButtonDown` — Unity's
      `Input.anyKey` counts mouse buttons, so ours has to as well. This is a widening: nothing that
      answered true before answers false now, and OR-ing a real physical click with itself is
      idempotent.

- [x] **Step 3: Make a repeat press a no-op.** `VirtualMouseState.Press` currently re-stamps
      `StartFrame` on a button that is already held, so a second press makes `GetButtonDown` true a
      second time. Return early when the button is held and unreleased, matching the guard
      `PointerEventDispatcher.Press` already has. This is what keeps `mouse_down 0` followed by
      `key_down Mouse0` from reading as two presses.

- [x] **Step 4: Route the key actions to the button path.** In `ActionExecutor`, extract the press
      and release bodies of `ExecuteMouseButton` into one private `PressButton(int)` /
      `ReleaseButton(int)` pair — `ArtelInput.PressMouseButton` plus `pointerEvents.Press`, and the
      mirror. `mouse_down` / `mouse_up` and the mouse keycodes in `key_down` / `key_up` then call the
      same pair, so the two paths cannot drift.

      `key_click` splits by keycode. A non-mouse keycode keeps today's behavior exactly:
      `ArtelInput.ClickKey`, returning Success immediately, duration handled by
      `VirtualKeyboardState` expiry. A mouse keycode takes a coroutine that presses, waits, and
      releases, so the timed release fires the pointer and `OnMouse*` events instead of expiring
      silently inside a state object. `ExecuteKeyClick` stops being static, matching
      `ExecuteMoveMouse` and `ExecuteButtonClick`.

      The wait is on unscaled time (`WaitForSecondsRealtime`). `VirtualKeyboardState` measures its
      durations against `Time.unscaledTime`, and the SDK can drive `Time.timeScale`; a scaled wait
      would never end on a paused game.

      A teardown during that wait is safe without extra bookkeeping.
      `ReleaseAllVirtualInput` and `pointerEvents.ReleaseAll` already let the button go, and both
      releases are idempotent — `VirtualMouseState.Release` returns early once `ReleaseFrame` is set,
      and `PointerEventDispatcher.Release` returns early once its `pointers[button]` is null. The
      coroutine's late release lands on an already-released button and does nothing.

- [x] **Step 5: Tests.** Edit mode for the mapping and the `ArtelInput` reads; play mode for the
      dispatch, which needs a live `EventSystem` and a collider under the pointer:
      - `key_down Mouse0` then `Input.GetMouseButton(0)` and `GetMouseButtonDown(0)`
      - `mouse_down 0` then `Input.GetKey(KeyCode.Mouse0)` and `GetKeyDown(KeyCode.Mouse0)`
      - `key_up Mouse0` then `Input.GetKeyUp(KeyCode.Mouse0)` and `GetMouseButtonUp(0)`
      - `key_down Mouse0` fires `IPointerDownHandler`; `key_up` fires `IPointerUpHandler` and
        `IPointerClickHandler`
      - `key_down Mouse0` fires `OnMouseDown` after a `move_mouse`; `key_up` fires `OnMouseUp` and
        `OnMouseUpAsButton`
      - `key_click Mouse0` releases after the duration, with both edges dispatched
      - `key_click Mouse0` releases on a game with `Time.timeScale` at 0
      - `mouse_down 0` followed by `key_down Mouse0` dispatches the handler once, not twice, and
        `GetMouseButtonDown(0)` does not go true a second time
      - `key_click` with a non-mouse keycode still returns immediately and expires by duration
      - `KeyCode.Mouse1` maps to button 1 for `Input` polling and uGUI
      - a non-mouse keycode still goes to `VirtualKeyboardState` and touches no mouse state

- [x] **Step 6: Rollout / Rollback** — revert the branch. No configuration, no migration, no flag.

## Validation

- **Commands to run:**
  ```bash
  .github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test
  ```
  then Unity 2022.3.34f1 `-runTests -testPlatform EditMode` and `-testPlatform PlayMode` against
  that project, summarized with `.github/scripts/summarize-test-results.py`.
- **Result:** EditMode 320 passed, 0 failed. PlayMode 32 passed, 0 failed. Unity 2022.3.34f1.
  The 7 new edit-mode cases and the 10 new play-mode cases are included in those totals.
- **Red check:** the play-mode suite was run once more with the `ActionExecutor` routing stripped
  out and everything else in place. 5 of the 10 new cases failed —
  `KeyDownMouse0_PressesTheButtonAndFiresThePointerHandlers`,
  `KeyDownMouse0_ReachesTheOnMouseHandlersUnderTheCursor`,
  `KeyDownMouse0_WithoutAMoveSaysNothingToAnyone`,
  `KeyClickMouse0_LetsGoAtTheEndOfTheDurationWithBothEdgesDispatched`, and
  `KeyDownMouse1_DrivesTheRightButtonAndLeavesTheLeftAlone`. The other five do not discriminate
  against that particular strip: two guard the reverse direction and the control case, and two
  guard failure modes the strip does not induce (the double-press guard, and the unscaled wait).
  Worth saying plainly rather than claiming all ten are red-provable.
- **Not run:** driving a real game through `samples/WordVenture`. The play-mode tests build their
  own `EventSystem`, canvas, and collider, which proves the dispatch reaches a handler — it does not
  prove that a shipped game's own input code sees the click. Say so in the PR rather than letting
  the suite read as end-to-end evidence.

## Risks & Rollback

- **Risks:** a game that polls both `Input.GetKey(KeyCode.Mouse0)` and `Input.GetMouseButton(0)` and
  counts the two as separate inputs would now see both fire. That game was already broken for a
  physical click, where the engine reports both, so this makes the SDK match the engine rather than
  introducing a new failure.
- **Second risk:** `key_click` with a mouse keycode now blocks for its duration instead of returning
  immediately. Callers that sequence actions see the same ordering; a caller that measured the
  round-trip sees it grow by the duration.
- **Rollback steps:** revert the commits on this branch. Nothing outside the SDK changes.

## Rejected feedback

- **Warn when a mouse keycode is pressed with no pointer position.** Considered and dropped. The
  press is still delivered to `Input` polling, which is a legitimate use on its own, so the warning
  would fire on every correct poll-only click. `PointerEventDispatcher.Press` already logs one line
  per press carrying the raycast hit count, and a `0 hits` line is the diagnosis this warning would
  have been reaching for.
- **Drive `PointerEventDispatcher` from state edges in `AdvanceFrame`.** The better architecture, and
  the reason it is not done here is written out under Non-goals rather than left implied.

## Open Questions

- None.
