import assert from "node:assert/strict";
import test from "node:test";

import { PulseStore, type PulseFrame, type PulseObject } from "../src/pulse.js";
import {
  describeWaitOutcome,
  unmetReasons,
  waitForCondition,
  type WaitCondition,
  type WaitConnectionLike,
  type WaitTimerApi,
} from "../src/wait.js";

/// `PulseStore.fold` 는 게임이 실제로 보내는 wire 모양(`m`)만 읽는다 — `readComponent` 가
/// `members` 는 어긋난 키로 본다. `pulse.test.ts` 의 같은 이름 helper 와 같은 이유다.
function object(id: number, selector: string, value: number): PulseObject {
  return {
    id,
    path: `Canvas/${selector}`,
    selector,
    by: [{ on: "Widget", m: [{ member: "value", value }] }],
  };
}

function pulse(overrides: Partial<PulseFrame> = {}): PulseFrame {
  return {
    type: "PULSE", id: 1, schema: 2, reading: 1, frame: 10, scene: "Main",
    statics: [], active: [], deactive: [], whole: false, watching: 1,
    unresolved: 0, unwatchable: 0, gone: [], changed: [], ...overrides,
  };
}

function condition(overrides: Partial<WaitCondition> = {}): WaitCondition {
  return { timeoutMilliseconds: 1_000, ...overrides };
}

/// `waitForCondition` 이 거는 timeout 하나를 손으로 쥐고 있다가 원할 때 터뜨리는 가짜 시계.
/// `connection.test.ts` 의 `FakeTimers` 와 같은 모양이지만, 여기서는 `wait.ts` 가 거는 timer
/// 하나만 있으면 되므로 실제 시간을 전혀 재우지 않는다.
class FakeTimers implements WaitTimerApi {
  private nextId = 1;
  private readonly callbacks = new Map<number, () => void>();

  setTimeout(callback: () => void, _delayMilliseconds: number): ReturnType<typeof setTimeout> {
    const id = this.nextId++;
    this.callbacks.set(id, callback);
    return id as unknown as ReturnType<typeof setTimeout>;
  }

  clearTimeout(timer: ReturnType<typeof setTimeout>): void {
    this.callbacks.delete(timer as unknown as number);
  }

  fire(): void {
    const entry = this.callbacks.entries().next().value as [number, () => void] | undefined;
    assert.ok(entry, "expected a pending timeout");
    this.callbacks.delete(entry[0]);
    entry[1]();
  }

  get pendingCount(): number {
    return this.callbacks.size;
  }
}

class FakeConnection implements WaitConnectionLike {
  private readonly listeners = new Set<() => void>();

  onDisconnect(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  disconnect(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }

  get listenerCount(): number {
    return this.listeners.size;
  }
}

test("a condition already met resolves immediately without a subscription or a timer", async () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true }));
  const connection = new FakeConnection();
  const timers = new FakeTimers();

  const outcome = await waitForCondition(
    store, connection, condition({ sinceReading: 0 }), new AbortController().signal, timers,
  );

  assert.equal(outcome.kind, "met");
  assert.equal(timers.pendingCount, 0);
  assert.equal(connection.listenerCount, 0);
});

test("a reading that satisfies the condition resolves met before the timeout", async () => {
  const store = new PulseStore();
  const connection = new FakeConnection();
  const timers = new FakeTimers();

  const waiting = waitForCondition(store, connection, condition({ scene: "Story" }), new AbortController().signal, timers);
  store.fold(pulse({ whole: true, scene: "Main" }));
  store.fold(pulse({ reading: 2, scene: "Story" }));
  const outcome = await waiting;

  assert.equal(outcome.kind, "met");
  assert.equal(outcome.state?.scene, "Story");
  assert.equal(timers.pendingCount, 0, "the timeout must be cleared once met");
});

test("no pulse at all still ends the wait once the timeout fires", async () => {
  const store = new PulseStore();
  const connection = new FakeConnection();
  const timers = new FakeTimers();

  const waiting = waitForCondition(store, connection, condition({ sinceReading: 5 }), new AbortController().signal, timers);
  timers.fire();
  const outcome = await waiting;

  assert.equal(outcome.kind, "timeout");
  assert.deepEqual(outcome.kind === "timeout" ? outcome.unmet : [], [
    "no scene reading has arrived yet; call start_readings",
  ]);
  assert.equal(connection.listenerCount, 0, "the disconnect subscription must be cleared on timeout too");
});

test("a disconnect ends the wait as disconnected, distinct from a plain timeout", async () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, scene: "Main" }));
  const connection = new FakeConnection();
  const timers = new FakeTimers();

  const waiting = waitForCondition(store, connection, condition({ scene: "Story" }), new AbortController().signal, timers);
  connection.disconnect();
  const outcome = await waiting;

  assert.equal(outcome.kind, "disconnected");
  assert.deepEqual(outcome.kind === "disconnected" ? outcome.unmet : [], ['scene is "Main", not "Story"']);
  assert.equal(timers.pendingCount, 0, "the timeout must be cleared on disconnect");
});

test("cancelling the signal ends the wait as cancelled and cleans up every subscription", async () => {
  const store = new PulseStore();
  const connection = new FakeConnection();
  const timers = new FakeTimers();
  const controller = new AbortController();

  const waiting = waitForCondition(store, connection, condition({ scene: "Story" }), controller.signal, timers);
  controller.abort();
  const outcome = await waiting;

  assert.equal(outcome.kind, "cancelled");
  assert.equal(timers.pendingCount, 0);
  assert.equal(connection.listenerCount, 0);

  // 취소 뒤에 온 신호는 이미 끝난 대기를 다시 resolve 하지 않는다. reading 이나 timeout 이
  // 더 온다 해도 조용히 무시되어야 한다 — 구독을 이미 뗐으므로 store.fold 는 아무도 못 듣는다.
  store.fold(pulse({ whole: true, scene: "Story" }));
  assert.equal((await waiting).kind, "cancelled");
});

test("an already-aborted signal resolves cancelled without touching the store or the connection", async () => {
  const store = new PulseStore();
  const connection = new FakeConnection();
  const timers = new FakeTimers();
  const controller = new AbortController();
  controller.abort();

  const outcome = await waitForCondition(store, connection, condition({ scene: "Story" }), controller.signal, timers);

  assert.equal(outcome.kind, "cancelled");
  assert.equal(timers.pendingCount, 0);
  assert.equal(connection.listenerCount, 0);
});

test("unmetReasons reports that no reading has arrived yet, distinct from an unmet value", () => {
  assert.deepEqual(unmetReasons(undefined, condition({ sinceReading: 1 })), [
    "no scene reading has arrived yet; call start_readings",
  ]);
});

test("unmetReasons ANDs sinceReading, sinceFrame, and scene", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, reading: 5, frame: 50, scene: "Main" }));
  const state = store.getState();

  assert.deepEqual(unmetReasons(state, condition({ sinceReading: 5 })), [
    "reading is 5, not newer than 5",
  ]);
  assert.deepEqual(unmetReasons(state, condition({ sinceFrame: 50 })), [
    "frame is 50, not newer than 50",
  ]);
  assert.deepEqual(unmetReasons(state, condition({ sinceReading: 4, sinceFrame: 49, scene: "Story" })), [
    'scene is "Main", not "Story"',
  ]);
  assert.deepEqual(unmetReasons(state, condition({ sinceReading: 4, sinceFrame: 49, scene: "Main" })), []);
});

test("unmetReasons matches memberEquals by exact selector, on, member, and among", () => {
  const store = new PulseStore();
  const dialogue: PulseObject = {
    id: 1, path: "Canvas/Dialogue", selector: "Dialogue",
    by: [{ on: "DialogueBox", m: [
      { member: "IsStreaming", value: true },
      { member: "slot", among: 1, value: "one" },
    ] }],
  };
  store.fold(pulse({ whole: true, active: [dialogue] }));
  const state = store.getState();

  assert.deepEqual(unmetReasons(state, condition({
    memberEquals: [{ selector: "Dialogue", on: "DialogueBox", member: "IsStreaming", equals: false }],
  })), ["Dialogue.DialogueBox.IsStreaming is true, expected false"]);

  assert.deepEqual(unmetReasons(state, condition({
    memberEquals: [{ selector: "Dialogue", on: "DialogueBox", member: "IsStreaming", equals: true }],
  })), []);

  // `among` 을 안 주면 `among` 이 없는 멤버에만 맞는다 — among:1 짜리 "slot" 은 못 찾는다.
  assert.deepEqual(unmetReasons(state, condition({
    memberEquals: [{ selector: "Dialogue", on: "DialogueBox", member: "slot", equals: "one" }],
  })), ["Dialogue.DialogueBox.slot was not found in the current reading"]);

  assert.deepEqual(unmetReasons(state, condition({
    memberEquals: [{ selector: "Dialogue", on: "DialogueBox", member: "slot", among: 1, equals: "one" }],
  })), []);
});

test("unmetReasons finds a member on a deactive object too", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, deactive: [object(1, "Card", 3)] }));
  const state = store.getState();

  assert.deepEqual(unmetReasons(state, condition({
    memberEquals: [{ selector: "Card", on: "Widget", member: "value", equals: 3 }],
  })), []);
});

test("describeWaitOutcome marks only disconnected as an error, and carries reading/frame/scene when present", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, reading: 1, frame: 10, scene: "Main" }));
  const state = store.getState();

  const met = describeWaitOutcome({ kind: "met", state: state! }, undefined);
  assert.equal(met.isError, false);
  assert.deepEqual(met.payload, { met: true, disconnected: false, cancelled: false, unmet: [], reading: 1, frame: 10, scene: "Main" });

  const timeout = describeWaitOutcome({ kind: "timeout", state, unmet: ["x"] }, undefined);
  assert.equal(timeout.isError, false);
  assert.equal(timeout.payload.met, false);
  assert.deepEqual(timeout.payload.unmet, ["x"]);

  const disconnected = describeWaitOutcome({ kind: "disconnected", state, unmet: ["x"] }, undefined);
  assert.equal(disconnected.isError, true);
  assert.equal(disconnected.payload.disconnected, true);

  const cancelled = describeWaitOutcome({ kind: "cancelled", state: undefined }, undefined);
  assert.equal(cancelled.isError, false);
  assert.equal(cancelled.payload.cancelled, true);
  assert.equal(cancelled.payload.reading, undefined);
});

test("describeWaitOutcome surfaces a stalled unreadable frame alongside timeout or disconnected", () => {
  const unreadable = { at: 900, reason: "incoming is not iterable" };
  const { payload } = describeWaitOutcome({ kind: "timeout", state: undefined, unmet: ["x"] }, unreadable);
  assert.deepEqual(payload.lastUnreadableFrame, unreadable);
});
