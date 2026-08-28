import assert from "node:assert/strict";
import test from "node:test";

import { PulseStore, type PulseFrame, type PulseObject } from "../src/pulse.js";

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

test("whole reading replaces all held objects", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Old", 1)] }));
  store.fold(pulse({ reading: 2, whole: true, active: [object(2, "New", 2)] }));
  assert.deepEqual(store.getState()?.active.map(({ selector }) => selector), ["New"]);
});

test("delta merges members by member and optional among", () => {
  const store = new PulseStore();
  const original = object(1, "Card", 1);
  original.by = [{ on: "Widget", members: [
    { member: "label", value: "kept" },
    { member: "slot", among: 0, value: "zero" },
    { member: "slot", among: 1, value: "one" },
  ] }];
  const update = object(1, "Card", 2);
  update.by = [{ on: "Widget", members: [
    { member: "slot", among: 1, value: "changed" },
  ] }];
  store.fold(pulse({ whole: true, active: [original] }));
  store.fold(pulse({ reading: 2, active: [update] }));
  assert.deepEqual(store.getState()?.active[0]?.by?.[0]?.members, [
    { member: "label", value: "kept" },
    { member: "slot", among: 0, value: "zero" },
    { member: "slot", among: 1, value: "changed" },
  ]);
});

test("object-level scene override participates in identity", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1), object(2, "Card", 2, "HUD")] }));
  store.fold(pulse({ reading: 2, active: [object(2, "Card", 3, "HUD")] }));
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [1, 2]);
  assert.equal(store.getState()?.active[1]?.by?.[0]?.members[0]?.value, 3);
});

test("delta moves bins and retains untouched objects", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "A", 1), object(2, "B", 2)] }));
  store.fold(pulse({ reading: 2, deactive: [object(1, "A", 3)] }));
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [2]);
  assert.deepEqual(store.getState()?.deactive.map(({ id }) => id), [1]);
});

test("gone removes scene and selector key", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "A", 1)] }));
  store.fold(pulse({ reading: 2, gone: ["Main/A"] }));
  assert.deepEqual(store.getState()?.active, []);
});

test("scene change resets objects even if malformed as delta", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "A", 1)] }));
  store.fold(pulse({ reading: 2, scene: "Next", active: [object(2, "B", 2)] }));
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [2]);
});

test("out-of-order reading changes neither objects nor metadata", () => {
  const store = new PulseStore();
  store.fold(pulse({ reading: 4, whole: true, frame: 40, changed: ["new"], active: [object(1, "A", 1)] }));
  assert.equal(store.fold(pulse({ reading: 3, frame: 30, changed: ["old"], active: [object(2, "B", 2)] })), false);
  assert.equal(store.getState()?.frame, 40);
  assert.deepEqual(store.getState()?.changed, ["new"]);
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [1]);
});

test("accepted reading replaces statics and changed metadata", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, statics: [{ declaring: "Game", member: "stage", type: "int", value: 1 }], changed: ["first"] }));
  store.fold(pulse({ reading: 2, statics: [], changed: ["second"] }));
  assert.deepEqual(store.getState()?.statics, []);
  assert.deepEqual(store.getState()?.changed, ["second"]);
});

test("diagnostics retain the latest performance and device context independently", () => {
  const store = new PulseStore();
  store.fold({ type: "PERFORMANCE", id: 1, fps: 30 });
  store.fold({ type: "DEVICE_CONTEXT", id: 2, platform: "Linux" });
  store.fold({ type: "PERFORMANCE", id: 3, fps: 60 });
  store.fold({ type: "ERROR", id: 4, message: "ignored by fold store" });
  assert.equal(store.getDiagnostics().performance?.id, 3);
  assert.equal(store.getDiagnostics().deviceContext?.id, 2);
});
