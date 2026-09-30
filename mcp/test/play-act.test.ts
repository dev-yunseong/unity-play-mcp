import assert from "node:assert/strict";
import test from "node:test";

import { actAndObserve, withFrameGaps, type ActInput, type ActResponse, type Step } from "../src/play-act.js";
import { PlayClient } from "../src/play-client.js";
import { InputGate, OperationLedger } from "../src/play-operations.js";
import { PlayObserver } from "../src/play-observe.js";
import { baseUnity, entity, FakeClock, FakeUnity, SESSION, stampOf, UnityFailure, type Handler } from "./play-fake.js";

const ref = { sessionId: SESSION, id: 10, generation: 1 };
const refKey = `ref:${SESSION}:10:1`;

interface World {
  scene: string;
  hp: number;
  revision: number;
  interactable: boolean;
  lifecycle: string;
}

function makeWorld(): World {
  return { scene: "Main", hp: 10, revision: 1, interactable: true, lifecycle: "present" };
}

/// 대상 하나(`ref`)와 `Health.hp` 멤버가 있는 가상의 Unity.
function unityFor(world: World, extra: Record<string, Handler> = {}): FakeUnity {
  return baseUnity({
    play_observe: () => ({ stamp: stampOf({ scene: world.scene }), entities: [entity(10)], coherent: true, inputRevision: world.revision }),
    play_begin: () => ({ status: "acquired", sessionId: SESSION, scene: world.scene, inputRevision: world.revision }),
    play_end: () => ({}),
    play_checkpoint: (params) => ({ sceneChanged: world.scene !== params[0] }),
    wait_frames: () => ({ frame: 1 }),
    play_query_space: () => ({ ok: true }),
    play_inspect: () => ({
      entity: ref, availability: "available", recipe: [{ method: "pointerClick", x: 50, y: 60 }],
      preconditions: [], outcomePredicates: [],
    }),
    play_sample: (params) => {
      const request = params[0] as { targets: Array<{ key: string }>; members: Array<{ target: string; component: string; member: string }> };
      return {
        stamp: stampOf({ scene: world.scene }),
        targets: Object.fromEntries(request.targets.map((target) => [target.key, { status: "one", count: 1, ref, lifecycle: world.lifecycle, active: true, interactable: world.interactable }])),
        members: request.members.map((member) => ({ ...member, status: "known", value: world.hp, valueType: "number", unit: "hp" })),
        facts: [],
      };
    },
    move_mouse: () => ({}), mouse_down: () => { world.revision++; return { frame: 5 }; },
    key_down: () => ({}), key_up: () => ({}), mouse_up: () => ({}), set_axis: () => ({}), set_button: () => ({}),
    ...extra,
  });
}

async function harness(world: World, unity: FakeUnity, clock = new FakeClock()) {
  const client = new PlayClient(unity);
  const observer = new PlayObserver(client, { mono: clock.now });
  const base = await observer.observe({});
  assert.ok(base.ok);
  const gate = new InputGate();
  const ledger = new OperationLedger<ActResponse>(clock.now);
  const deps = { client, observer, gate, ledger, sleep: (ms: number) => clock.sleep(ms), now: clock.now };
  const act = (input: Partial<ActInput>, signal = new AbortController().signal) => actAndObserve(deps, {
    operationId: `op-${Math.random()}`,
    basedOnObservationId: base.body.observationId as string,
    ...input,
  } as ActInput, signal);
  return { act, gate, ledger, clock, client, observer, base, world };
}

const clickSteps: Step[] = [{ method: "pointerClick", x: 50, y: 60 }];
const hpDropped = [{ member: { target: { ref }, component: "Health", name: "hp", op: "lt", value: 10 } }];

function body(response: ActResponse) {
  return response.body as {
    execution: { status: string; steps: Array<{ method: string; status: string }> };
    outcome: { status: string; evidence: unknown[]; unmet: string[] };
    termination: string;
    before?: unknown;
    after?: unknown;
    observation?: Record<string, unknown>;
    timings: Record<string, number>;
    error?: { code: string };
    replayed?: boolean;
  };
}

test("accepted input alone is never confirmed: no expect means unknown", async () => {
  const world = makeWorld();
  const { act } = await harness(world, unityFor(world));
  const result = body(await act({ steps: clickSteps }));
  assert.equal(result.execution.status, "completed");
  assert.equal(result.outcome.status, "unknown");
  assert.equal(result.termination, "finished");
  assert.ok(result.observation !== undefined, "one final observation is returned");
});

test("an expect that becomes true is confirmed with evidence", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { mouse_down: () => { world.hp = 6; world.revision++; return { frame: 5 }; } });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps, expect: hpDropped }));
  assert.equal(result.outcome.status, "confirmed");
  assert.ok(result.outcome.evidence.length > 0);
  assert.equal(result.execution.status, "completed");
});

test("input accepted but the effect never appears is not_observed, without re-sending input", async () => {
  const world = makeWorld();
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps, expect: hpDropped, timeoutMs: 1_000 }));
  assert.equal(result.execution.status, "completed");
  assert.equal(result.outcome.status, "not_observed");
  assert.equal(result.termination, "timeout");
  assert.equal(unity.count("mouse_down"), 1, "a late effect is never answered with automatic re-input");
});

test("a late effect arriving during the wait is still confirmed", async () => {
  const world = makeWorld();
  const clock = new FakeClock();
  const unity = unityFor(world);
  const { act } = await harness(world, unity, clock);
  const originalSleep = clock.sleep;
  let sleeps = 0;
  clock.sleep = async (ms) => { sleeps++; if (sleeps === 3) world.hp = 4; await originalSleep(ms); };
  const result = body(await act({ steps: clickSteps, expect: hpDropped, timeoutMs: 5_000 }));
  assert.equal(result.outcome.status, "confirmed");
});

test("a failed precondition sends no input at all", async () => {
  const world = makeWorld();
  world.interactable = false;
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  const result = body(await act({
    steps: clickSteps,
    preconditions: [{ interactable: { ref } }],
  }));
  assert.equal(result.execution.status, "not_started");
  assert.equal(result.termination, "precondition_failed");
  assert.equal(unity.count("mouse_down"), 0);
  assert.ok(result.outcome.unmet.length > 0);
});

test("an unknown precondition (missing data) counts as failed, not passed", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_sample: () => ({ stamp: stampOf(), targets: {}, members: [], facts: [] }) });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps, preconditions: [{ active: { ref } }] }));
  assert.equal(result.termination, "precondition_failed");
  assert.equal(unity.count("mouse_down"), 0);
});

test("a blocker found just before input stops the operation", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_query_space: () => ({ ok: false, reason: "blocked by Overlay/Popup" }) });
  const { act } = await harness(world, unity);
  const result = body(await act({ action: { actionRef: "act:x" } }));
  assert.equal(result.termination, "precondition_failed");
  assert.match(result.outcome.unmet.join(" "), /blocked by Overlay\/Popup/);
  assert.equal(unity.count("mouse_down"), 0);
});

test("a stale entity ref is refused rather than replaced by another object", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_inspect: () => { throw new UnityFailure("stale_ref: id 10 now belongs to a different object"); } });
  const { act } = await harness(world, unity);
  const result = body(await act({ action: { actionRef: "act:old" } }));
  assert.equal(result.termination, "precondition_failed");
  assert.match(result.outcome.unmet.join(" "), /different object/);
  assert.equal(unity.count("mouse_down"), 0);
});

test("invalid requests are refused before Unity sees anything", async () => {
  const world = makeWorld();
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  const both = await act({ steps: clickSteps, action: { actionRef: "a" } });
  assert.equal(body(both).error?.code, "invalid_request");
  const neither = await act({});
  assert.equal(body(neither).error?.code, "invalid_request");
  const badPredicate = await act({ steps: clickSteps, expect: [{ eval: "1" }] });
  assert.equal(body(badPredicate).error?.code, "invalid_request");
  assert.equal(unity.count("play_begin"), 0);
  assert.equal(unity.count("mouse_down"), 0);
});

test("a retry with the same operationId and request returns the earlier result without input", async () => {
  const world = makeWorld();
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  const first = await act({ operationId: "same", steps: clickSteps });
  const again = await act({ operationId: "same", steps: clickSteps });
  assert.equal(unity.count("mouse_down"), 1);
  assert.equal(body(again).replayed, true);
  assert.equal(body(again).execution.status, body(first).execution.status);
});

test("the same operationId with a different request is a conflict and sends nothing", async () => {
  const world = makeWorld();
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  await act({ operationId: "same", steps: clickSteps });
  const clicks = unity.count("mouse_down");
  const conflict = body(await act({ operationId: "same", steps: [{ method: "pointerClick", x: 11, y: 11 }] }));
  assert.equal(conflict.error?.code, "operation_conflict");
  assert.equal(unity.count("mouse_down"), clicks);
});

test("after a server restart Unity's record prevents re-execution and reports the outcome as unknown", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_begin: () => ({ status: "already_started", state: "finished" }) });
  const { act } = await harness(world, unity);
  const result = body(await act({ operationId: "restarted", steps: clickSteps }));
  assert.equal(result.termination, "precondition_failed");
  assert.match(result.outcome.unmet.join(" "), /not re-sent/);
  assert.equal(unity.count("mouse_down"), 0);
});

test("another client holding the input yields busy and no input", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_begin: () => { throw new UnityFailure("busy:other-op another operation owns the input"); } });
  const { act, gate } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps }));
  assert.equal(result.termination, "busy");
  assert.match(result.outcome.unmet.join(" "), /other-op/);
  assert.equal(unity.count("mouse_down"), 0);
  assert.equal(gate.activeOperationId, undefined, "the local gate is released");
});

test("two operations in one process do not interleave", async () => {
  const world = makeWorld();
  let release!: () => void;
  const hold = new Promise<void>((resolve) => { release = resolve; });
  const unity = unityFor(world, { mouse_down: async () => { await hold; return { frame: 1 }; } });
  const { act } = await harness(world, unity);
  const first = act({ operationId: "one", steps: clickSteps });
  await new Promise((resolve) => setImmediate(resolve));
  const second = body(await act({ operationId: "two", steps: clickSteps }));
  assert.equal(second.termination, "busy");
  release();
  assert.equal(body(await first).execution.status, "completed");
  assert.equal(unity.count("mouse_down"), 1);
});

test("a stale observation is rejected only when other input ran after it and it is old", async () => {
  const world = makeWorld();
  const clock = new FakeClock();
  const unity = unityFor(world);
  const { act } = await harness(world, unity, clock);
  // 시간만 흐른 경우: 다른 입력이 없으므로 거부하지 않는다.
  clock.time = 60_000;
  assert.equal(body(await act({ steps: clickSteps })).termination, "finished");
  // 그 사이 다른 입력이 있었다면 오래된 관찰은 거부한다.
  world.revision += 5;
  clock.time = 120_000;
  const stale = body(await act({ steps: clickSteps }));
  assert.equal(stale.termination, "precondition_failed");
  assert.match(stale.outcome.unmet.join(" "), /stale_observation/);
  // 관찰이 아직 신선하면 revision 이 달라도 허용한다.
  const fresh = await harness(world, unityFor(world), clock);
  world.revision += 1;
  assert.equal(body(await fresh.act({ steps: clickSteps })).termination, "finished");
});

test("a different Play session or scene since the observation stops the operation", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { play_begin: () => ({ status: "acquired", sessionId: "another-session", scene: "Main", inputRevision: 1 }) });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps }));
  assert.match(result.outcome.unmet.join(" "), /session_changed/);
  assert.equal(unity.count("mouse_down"), 0);

  const world2 = makeWorld();
  const { act: act2, world: w } = await harness(world2, unityFor(world2));
  w.scene = "Other";
  const moved = body(await act2({ steps: clickSteps }));
  assert.equal(moved.termination, "scene_changed");
});

test("a scene change mid-plan skips the remaining steps and never touches old entities", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { mouse_down: () => { world.scene = "Next"; return { frame: 3 }; } });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: [
    { method: "pointerClick", x: 10, y: 10 },
    { method: "pointerClick", x: 11, y: 11 },
    { method: "pointerClick", x: 12, y: 12 },
  ] }));
  assert.equal(result.termination, "scene_changed");
  assert.equal(unity.count("mouse_down"), 1);
  const statuses = result.execution.steps.map((step) => step.status);
  assert.equal(statuses.at(-1), "skipped");
  assert.ok(!unity.calls.some((call) => call.method === "move_mouse" && call.params[0] === 11), "no input for the old entities");
  assert.equal(result.execution.status, "partial");
});

test("down and up are separated by an explicit frame wait", () => {
  const plan = withFrameGaps([{ method: "mouse_down", button: 0 }, { method: "mouse_up", button: 0 }]);
  assert.deepEqual(plan.map((step) => step.method), ["mouse_down", "waitFrames", "mouse_up"]);
  const already = withFrameGaps([{ method: "key_down", key: "W" }, { method: "waitFrames", frames: 5 }, { method: "key_up", key: "W" }]);
  assert.equal(already.length, 3, "an explicit wait is not doubled");
  assert.equal(withFrameGaps([{ method: "set_axis", name: "X", value: 0 }, { method: "key_down", key: "A" }]).length, 2, "releasing an axis needs no gap");
});

test("cancel mid-hold releases exactly what this operation pressed", async () => {
  const world = makeWorld();
  const controller = new AbortController();
  let waits = 0;
  const unity = unityFor(world, { wait_frames: () => { if (++waits === 3) controller.abort(); return {}; } });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: [
    { method: "key_down", key: "W" },
    { method: "mouse_down", button: 0 },
    { method: "set_axis", name: "Horizontal", value: 1 },
    { method: "waitFrames", frames: 30 },
    { method: "key_up", key: "W" },
  ] }, controller.signal));
  assert.equal(result.termination, "cancelled");
  const sent = unity.methods();
  assert.ok(sent.includes("key_up"), "the held key is released");
  assert.ok(sent.includes("mouse_up"), "the held mouse button is released");
  const axisRelease = unity.calls.filter((call) => call.method === "set_axis").at(-1)?.params;
  assert.deepEqual(axisRelease, ["Horizontal", 0]);
  assert.equal(unity.count("play_end"), 1, "Unity is told the operation ended");
});

test("a step failure stops the plan, reports partial, and still releases held input", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { mouse_down: () => { throw new UnityFailure("Target is covered by Popup"); } });
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: [
    { method: "key_down", key: "Space" },
    { method: "pointerClick", x: 10, y: 10 },
    { method: "key_up", key: "Space" },
  ] }));
  assert.equal(result.execution.status, "partial");
  const statuses = result.execution.steps.map((step) => step.status);
  assert.ok(statuses.includes("failed"));
  assert.equal(statuses.at(-1), "skipped");
  const releases = unity.calls.filter((call) => call.method === "key_up");
  assert.equal(releases.length, 1, "the key is released once after the failure");
});

test("a disconnect during a step is reported and cleanup does not hang", async () => {
  const world = makeWorld();
  const unity = unityFor(world, { mouse_down: () => { throw new Error("Unity WebSocket closed"); } });
  const { act, gate } = await harness(world, unity);
  const result = body(await act({ steps: clickSteps }));
  assert.equal(result.termination, "disconnected");
  assert.equal(gate.activeOperationId, undefined);
});

test("waitCondition is bounded: it fails the step instead of waiting forever", async () => {
  const world = makeWorld();
  const unity = unityFor(world);
  const { act } = await harness(world, unity);
  const result = body(await act({ steps: [
    { method: "waitCondition", until: { sceneIs: "Never" }, timeoutMs: 500 },
    { method: "pointerClick", x: 10, y: 10 },
  ], timeoutMs: 5_000 }));
  assert.equal(result.execution.steps[0]?.status, "failed");
  assert.equal(result.execution.steps[1]?.status, "skipped");
  assert.equal(unity.count("mouse_down"), 0);
});

test("waitCondition lets an animation finish before the next step", async () => {
  const world = makeWorld();
  const clock = new FakeClock();
  const unity = unityFor(world);
  const { act } = await harness(world, unity, clock);
  const original = clock.sleep;
  let n = 0;
  clock.sleep = async (ms) => { if (++n === 2) world.interactable = true; await original(ms); };
  world.interactable = false;
  const result = body(await act({ steps: [
    { method: "waitCondition", until: { interactable: { ref } }, timeoutMs: 2_000 },
    { method: "pointerClick", x: 10, y: 10 },
  ] }));
  assert.equal(result.execution.status, "completed");
});

test("the whole operation respects a wall-clock deadline even while the game is paused", async () => {
  const world = makeWorld();
  const clock = new FakeClock();
  // 게임이 멈춰 있어 조건이 영영 참이 되지 않는다.
  const unity = unityFor(world);
  const { act } = await harness(world, unity, clock);
  const result = body(await act({ steps: clickSteps, expect: hpDropped, timeoutMs: 800 }));
  assert.equal(result.termination, "timeout");
  assert.ok(clock.time <= 2_000, "the wait ended by the deadline, not by the game");
});

test("timings and both stamps are reported", async () => {
  const world = makeWorld();
  const { act } = await harness(world, unityFor(world));
  const result = body(await act({ steps: clickSteps, expect: [{ sceneIs: "Main" }] }));
  assert.ok(result.before && result.after);
  for (const key of ["queueMs", "dispatchMs", "waitMs", "sampleMs", "encodeMs", "totalMs"]) {
    assert.ok(key in result.timings, key);
  }
});

test("provider outcome predicates from the recipe make an outcome judgeable", async () => {
  const world = makeWorld();
  const unity = unityFor(world, {
    play_inspect: () => ({
      entity: ref, availability: "available", recipe: [{ method: "pointerClick", x: 50, y: 60 }],
      preconditions: [], outcomePredicates: [{ sceneIs: "Main" }],
    }),
  });
  const { act } = await harness(world, unity);
  const result = body(await act({ action: { actionRef: "act:x" } }));
  assert.equal(result.outcome.status, "confirmed");
});

test("recipe targets substitute an entity id or a point and reject a mismatched target", async () => {
  const world = makeWorld();
  const unity = unityFor(world, {
    play_inspect: () => ({ entity: ref, availability: "available", recipe: [
      { method: "move_mouse", x: "$target.x", y: "$target.y" },
      { method: "pointerClick", x: 10, y: 10 },
    ], preconditions: [], outcomePredicates: [], acceptsTarget: "screen" }),
    move_mouse: () => ({}),
  });
  const { act } = await harness(world, unity);
  const missing = body(await act({ action: { actionRef: "a" } }));
  assert.match(missing.outcome.unmet.join(" "), /needs a target/);
  const done = body(await act({ action: { actionRef: "a", target: { space: "screen", x: 12, y: 34 } } }));
  assert.equal(done.execution.status, "completed");
  assert.deepEqual(unity.calls.find((call) => call.method === "move_mouse")?.params, [12, 34]);
});

test("a recipe step outside the whitelist is refused", async () => {
  const world = makeWorld();
  const unity = unityFor(world, {
    play_inspect: () => ({ entity: ref, availability: "available", recipe: [{ method: "reset_game", clearPlayerPrefs: true }], preconditions: [], outcomePredicates: [] }),
  });
  const { act } = await harness(world, unity);
  const result = body(await act({ action: { actionRef: "a" } }));
  assert.equal(result.termination, "precondition_failed");
  assert.equal(unity.count("reset_game"), 0);
});

test("world point targets are projected through query_space, never guessed", async () => {
  const world = makeWorld();
  const unity = unityFor(world, {
    play_inspect: () => ({ entity: ref, availability: "available", recipe: [{ method: "move_mouse", x: "$target.x", y: "$target.y" }], preconditions: [], outcomePredicates: [] }),
    play_query_space: (params) => {
      const query = params[0] as { kind: string };
      return query.kind === "project_world_to_screen" ? { screen: { x: 100, y: 200 } } : { ok: true };
    },
    move_mouse: () => ({}),
  });
  const { act } = await harness(world, unity);
  await act({ action: { actionRef: "a", target: { space: "world3d", x: 1, y: 2, z: 3 } } });
  assert.deepEqual(unity.calls.find((call) => call.method === "move_mouse")?.params, [100, 200]);
});

test("an old Unity without the new protocol fails as unsupported_capability", async () => {
  const unity = new FakeUnity();
  const client = new PlayClient(unity);
  const observer = new PlayObserver(client);
  const response = await actAndObserve(
    { client, observer, gate: new InputGate(), ledger: new OperationLedger(), sleep: async () => undefined },
    { operationId: "x", basedOnObservationId: "y", steps: clickSteps } as ActInput,
    new AbortController().signal,
  );
  assert.equal(body(response).error?.code, "unsupported_capability");
  assert.equal(unity.count("mouse_down"), 0);
});

test("the abort listener and the gate are cleaned up after every run", async () => {
  const world = makeWorld();
  const { act, gate } = await harness(world, unityFor(world));
  const controller = new AbortController();
  await act({ steps: clickSteps }, controller.signal);
  assert.equal(gate.activeOperationId, undefined);
  controller.abort(); // 이미 끝난 operation 에 영향이 없어야 한다.
});
