import assert from "node:assert/strict";
import test from "node:test";

import { EventLog, formatCursor, parseCursor } from "../src/play-events.js";
import type { FoldedPulseState, PulseObject } from "../src/pulse.js";
import { entityRefFor } from "./play-watch-helpers.js";

function obj(id: number, selector: string, value: number, extra: Record<string, unknown> = {}): PulseObject {
  return { id, path: `Canvas/${selector}`, selector, by: [{ on: "Widget", members: [{ member: "value", value }] }], ...extra } as PulseObject;
}

function state(reading: number, active: PulseObject[], over: Partial<FoldedPulseState> = {}): FoldedPulseState {
  return {
    run: "r1", reading, frame: reading * 10, scene: "Main", schema: 2, statics: [], active, deactive: [],
    watching: 1, unresolved: 0, unwatchable: 0, changed: [], gone: [], ...over,
  };
}

test("cursor format round-trips and rejects garbage", () => {
  assert.deepEqual(parseCursor(formatCursor({ epoch: "a:b", sequence: 4 })), { epoch: "a:b", sequence: 4 });
  assert.equal(parseCursor("nonsense"), undefined);
  assert.equal(parseCursor("x:-1"), undefined);
});

test("readings produce spawn, despawn and property-change events after the baseline", () => {
  const log = new EventLog("s1");
  log.observeReading(state(1, [obj(1, "A", 1)]));
  assert.equal(log.size, 0, "the first reading is only a baseline");

  log.observeReading(state(2, [obj(1, "A", 2), obj(2, "B", 0)]));
  log.observeReading(state(3, [obj(2, "B", 0)]));
  const kinds = log.read(undefined, "debug").events.map((event) => `${event.kind}:${event.entityId}`);
  assert.deepEqual(kinds, ["member_changed:1", "entity_spawned:2", "entity_despawned:1"]);
});

test("a scene change or a new run resets the baseline instead of diffing unrelated objects", () => {
  const log = new EventLog("s1");
  log.observeReading(state(1, [obj(1, "A", 1)]));
  log.observeReading(state(2, [obj(9, "Z", 1)], { scene: "Other" }));
  log.observeReading(state(1, [obj(9, "Z", 5)], { scene: "Other", run: "r2" }));
  const kinds = log.read(undefined, "debug").events.map((event) => event.kind);
  assert.deepEqual(kinds, ["scene_changed", "baseline_reset"]);
});

test("player scope drops events about hidden or covered objects", () => {
  const log = new EventLog("s1");
  log.observeReading(state(1, [obj(1, "A", 1, { covered: true }), obj(2, "B", 1)]));
  log.observeReading(state(2, [obj(1, "A", 2, { covered: true }), obj(2, "B", 2)]));
  assert.deepEqual(log.read(undefined, "player").events.map((event) => event.entityId), [2]);
  assert.deepEqual(log.read(undefined, "debug").events.map((event) => event.entityId), [1, 2]);
});

test("overflow drops the oldest events and reports a gap with the oldest cursor", () => {
  const log = new EventLog("s1", { capacity: 3 });
  const first = log.head();
  for (let i = 0; i < 5; i++) log.appendProvider({ kind: "tick", providerId: "p", playerVisible: true, data: { i } });
  assert.equal(log.size, 3);
  const read = log.read(first, "debug");
  assert.equal(read.gap, true);
  assert.equal(read.baselineRequired, true);
  assert.equal(read.events.length, 3);
  assert.ok(read.oldestCursor !== undefined);
});

test("the byte budget also bounds the buffer", () => {
  const log = new EventLog("s1", { maxBytes: 2_000 });
  for (let i = 0; i < 50; i++) log.appendProvider({ kind: "big", providerId: "p", playerVisible: true, data: { text: "x".repeat(200) } });
  assert.ok(log.size < 50);
  assert.ok(log.size >= 1);
});

test("a cursor from another session is a gap, never silently continued", () => {
  const log = new EventLog("s1");
  log.appendProvider({ kind: "a", providerId: "p", playerVisible: true });
  const stale = log.head();
  log.changeSession("s2");
  const read = log.read(stale, "debug");
  assert.equal(read.gap, true);
  assert.equal(read.baselineRequired, true);
  assert.deepEqual(read.events.map((event) => event.kind), ["session_changed"]);
});

test("provider events keep their provenance and emit stamp", () => {
  const log = new EventLog("s1");
  log.appendProvider({ kind: "goal_reached", providerId: "prov", playerVisible: true, stamp: { frame: 42, scene: "Main" }, entity: entityRefFor(3) });
  const [event] = log.read(undefined, "player").events;
  assert.equal(event?.provenance, "provider");
  assert.equal(event?.stamp.frame, 42);
  assert.equal(event?.entityId, 3);
});

test("a provider event the player scope may not see is absent from player reads", () => {
  const log = new EventLog("s1");
  log.appendProvider({ kind: "secret", providerId: "p", playerVisible: false });
  assert.equal(log.read(undefined, "player").events.length, 0);
  assert.equal(log.read(undefined, "debug").events.length, 1);
});

test("truncation by limit keeps the cursor at the last delivered event", () => {
  const log = new EventLog("s1");
  for (let i = 0; i < 5; i++) log.appendProvider({ kind: "e", providerId: "p", playerVisible: true });
  const first = log.read(undefined, "debug", {}, 2);
  assert.equal(first.events.length, 2);
  assert.equal(first.truncated, true);
  const second = log.read(first.cursor, "debug", {}, 10);
  assert.equal(second.events.length, 3);
});

test("a disconnect is recorded and forgets the reading baseline", () => {
  const log = new EventLog("s1");
  log.observeReading(state(1, [obj(1, "A", 1)]));
  log.noteDisconnected();
  log.observeReading(state(2, [obj(1, "A", 2)]));
  assert.deepEqual(log.read(undefined, "debug").events.map((event) => event.kind), ["disconnected"]);
});

test("redaction: values that look like credentials never enter change events", () => {
  const log = new EventLog("s1");
  const secret = (value: string) => ({ id: 1, path: "A", selector: "A", by: [{ on: "Auth", members: [{ member: "AccessToken", value }] }] }) as PulseObject;
  log.observeReading(state(1, [secret("aaa")]));
  log.observeReading(state(2, [secret("bbb")]));
  const [event] = log.read(undefined, "debug").events;
  assert.match(JSON.stringify(event?.data), /redacted/);
  assert.doesNotMatch(JSON.stringify(event?.data), /bbb/);
});
