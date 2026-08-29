import assert from "node:assert/strict";
import test from "node:test";

import { PulseStore, type PulseFrame, type PulseObject } from "../src/pulse.js";

/// 이력 map 의 키는 `component.on` 과 멤버 키를 NUL 로 이은 것이다.
const at = (on: string, member: string, among?: number): string =>
  among === undefined
    ? `${on}\u0000${member}`
    : `${on}\u0000${member}\u0000${among}`;

function object(id: number, selector: string, value: number, scene?: string): PulseObject {
  return {
    id,
    path: `Canvas/${selector}`,
    selector,
    ...(scene === undefined ? {} : { scene }),
    by: [{ on: "Widget", members: [{ member: "value", value }] }],
  };
}

function pulse(overrides: Partial<PulseFrame> = {}): PulseFrame {
  return {
    type: "PULSE", id: 1, schema: 2, reading: 1, frame: 10, scene: "Main",
    statics: [], active: [], deactive: [], whole: false, watching: 1,
    unresolved: 0, unwatchable: 0, gone: [], changed: [], ...overrides,
  };
}

const valuesAt = (store: PulseStore, key: string, path: string) =>
  store.getObjectHistory(key).get(path)?.map(({ value }) => value);

test("a moved value lands in that member's history", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  store.fold(pulse({ reading: 2, frame: 20, active: [object(1, "Card", 2)] }));
  assert.deepEqual(store.getObjectHistory("Main/Card").get(at("Widget", "value")), [
    { value: 1, reading: 1, frame: 10 },
    { value: 2, reading: 2, frame: 20 },
  ]);
});

test("a value that arrives unchanged does not spend a slot", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 7)] }));
  store.fold(pulse({ reading: 2, active: [object(1, "Card", 7)] }));
  store.fold(pulse({ reading: 3, active: [object(1, "Card", 7)] }));
  assert.equal(store.getObjectHistory("Main/Card").get(at("Widget", "value"))?.length, 1);
});

test("the eleventh change pushes the oldest one out", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 0)] }));
  for (let value = 1; value <= 11; value++) {
    store.fold(pulse({ reading: value + 1, active: [object(1, "Card", value)] }));
  }
  const series = store.getObjectHistory("Main/Card").get(at("Widget", "value")) ?? [];
  assert.equal(series.length, 10);
  assert.deepEqual(series.map(({ value }) => value), [2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
});

test("a member seen for the first time gets its opening slot", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 5)] }));
  assert.deepEqual(store.getObjectHistory("Main/Card").get(at("Widget", "value")), [
    { value: 5, reading: 1, frame: 10 },
  ]);
});

test("a whole reading keeps the history of the keys that survive it", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  store.fold(pulse({ reading: 2, active: [object(1, "Card", 2)] }));
  // 전달이 유실된 뒤의 복구. 씬은 그대로다.
  store.fold(pulse({ reading: 3, whole: true, active: [object(1, "Card", 2)] }));
  assert.deepEqual(valuesAt(store, "Main/Card", at("Widget", "value")), [1, 2]);
});

test("a whole reading that carries a different value records the move", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  store.fold(pulse({ reading: 5, whole: true, active: [object(1, "Card", 9)] }));
  assert.deepEqual(valuesAt(store, "Main/Card", at("Widget", "value")), [1, 9]);
});

test("a whole reading drops the history of keys it no longer names", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1), object(2, "Panel", 1)] }));
  store.fold(pulse({ reading: 2, whole: true, active: [object(1, "Card", 1)] }));
  assert.equal(store.getObjectHistory("Main/Panel").size, 0);
  assert.equal(store.getObjectHistory("Main/Card").size, 1);
});

test("a scene change empties both the history and the tombstones", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1), object(2, "Panel", 1)] }));
  store.fold(pulse({ reading: 2, gone: ["Main/Panel"] }));
  assert.equal(store.getState()?.gone.length, 1);
  store.fold(pulse({ reading: 3, scene: "Battle", active: [object(1, "Card", 1)] }));
  assert.equal(store.getState()?.gone.length, 0);
  assert.equal(store.getObjectHistory("Main/Card").size, 0);
});

test("a gone key becomes a tombstone instead of vanishing", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 3)] }));
  store.fold(pulse({ reading: 4, gone: ["Main/Card"] }));
  const state = store.getState();
  assert.deepEqual(state?.active, []);
  assert.equal(state?.gone.length, 1);
  assert.equal(state?.gone[0]?.goneAtReading, 4);
  assert.equal(state?.gone[0]?.object.selector, "Card");
  // tombstone 은 마지막 모습을 들되 이력은 함께 버린다.
  assert.equal(store.getObjectHistory("Main/Card").size, 0);
});

test("tombstones past the limit are dropped oldest first", () => {
  const store = new PulseStore();
  const many = Array.from({ length: 60 }, (_, index) => object(index + 1, `Enemy${index}`, 1));
  store.fold(pulse({ whole: true, active: many }));
  store.fold(pulse({ reading: 2, gone: many.map(({ selector }) => `Main/${selector}`) }));
  const gone = store.getState()?.gone ?? [];
  assert.equal(gone.length, 50);
  assert.equal(gone[0]?.object.selector, "Enemy10");
  assert.equal(gone[49]?.object.selector, "Enemy59");
});

test("a key that comes back leaves the tombstones", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  store.fold(pulse({ reading: 2, gone: ["Main/Card"] }));
  assert.equal(store.getState()?.gone.length, 1);
  store.fold(pulse({ reading: 3, active: [object(1, "Card", 8)] }));
  assert.equal(store.getState()?.gone.length, 0);
  assert.equal(store.getState()?.active.length, 1);
});

test("members that differ only by among keep separate histories", () => {
  const store = new PulseStore();
  const first = object(1, "Card", 0);
  first.by = [{ on: "Widget", members: [
    { member: "slot", among: 0, value: "zero" },
    { member: "slot", among: 1, value: "one" },
  ] }];
  const second = object(1, "Card", 0);
  second.by = [{ on: "Widget", members: [{ member: "slot", among: 1, value: "changed" }] }];
  store.fold(pulse({ whole: true, active: [first] }));
  store.fold(pulse({ reading: 2, active: [second] }));
  assert.deepEqual(valuesAt(store, "Main/Card", at("Widget", "slot", 0)), ["zero"]);
  assert.deepEqual(valuesAt(store, "Main/Card", at("Widget", "slot", 1)), ["one", "changed"]);
});

test("folding again leaves a series a caller already read untouched", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  const held = store.getObjectHistory("Main/Card").get(at("Widget", "value"));
  store.fold(pulse({ reading: 2, active: [object(1, "Card", 2)] }));
  assert.deepEqual(held?.map(({ value }) => value), [1]);
  assert.deepEqual(
    valuesAt(store, "Main/Card", at("Widget", "value")), [1, 2]);
});
