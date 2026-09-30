import assert from "node:assert/strict";
import test from "node:test";

import { canonicalHash, InputGate, isMutatingMethod, OperationLedger } from "../src/play-operations.js";
import { dispatchActions } from "../src/tools.js";
import { defaultInputGate } from "../src/play-operations.js";

test("canonicalHash ignores key order and undefined fields", () => {
  assert.equal(canonicalHash({ a: 1, b: { c: 2, d: [1, 2] } }), canonicalHash({ b: { d: [1, 2], c: 2 }, a: 1, e: undefined }));
  assert.notEqual(canonicalHash({ a: 1 }), canonicalHash({ a: 2 }));
});

test("the ledger replays the same request without running it again", async () => {
  const ledger = new OperationLedger<string>();
  let runs = 0;
  const first = ledger.begin("op1", "h");
  assert.equal(first.kind, "new");
  const done = first.kind === "new" ? first.finish(async () => { runs++; return "result"; }) : undefined;
  const again = ledger.begin("op1", "h");
  assert.equal(again.kind, "replay", "a retry while running joins the same run");
  assert.equal(await done, "result");
  const later = ledger.begin("op1", "h");
  assert.equal(later.kind, "replay");
  assert.equal(later.kind === "replay" ? await later.promise : undefined, "result");
  assert.equal(runs, 1);
});

test("the same operationId with a different request is a conflict", async () => {
  const ledger = new OperationLedger<string>();
  const first = ledger.begin("op1", "h1");
  if (first.kind === "new") await first.finish(async () => "x");
  assert.equal(ledger.begin("op1", "h2").kind, "conflict");
});

test("a failed run is remembered so it is not sent again", async () => {
  const ledger = new OperationLedger<string>();
  const first = ledger.begin("op1", "h");
  if (first.kind === "new") await first.finish(async () => { throw new Error("boom"); }).catch(() => undefined);
  assert.equal(ledger.begin("op1", "h").kind, "replay");
});

test("eviction keeps at least the newest `keep` entries and the last `keepMs`", async () => {
  let clock = 0;
  const ledger = new OperationLedger<number>(() => clock, 3, 1_000);
  for (let i = 0; i < 6; i++) {
    const begin = ledger.begin(`op${i}`, "h");
    if (begin.kind === "new") await begin.finish(async () => i);
  }
  ledger.begin("probe", "h");
  assert.equal(ledger.size, 6, "recent entries are kept even beyond the count");
  clock = 5_000;
  ledger.begin("probe2", "h");
  assert.ok(ledger.size <= 3, "old entries beyond the count are evicted");
  assert.equal(ledger.begin("op0", "h").kind, "new", "an evicted id is treated as new");
});

test("an unfinished operation is never evicted", () => {
  let clock = 0;
  const ledger = new OperationLedger<number>(() => clock, 1, 10);
  const running = ledger.begin("run", "h");
  if (running.kind === "new") void running.finish(() => new Promise<number>(() => undefined));
  clock = 1_000_000;
  ledger.begin("other", "h");
  assert.equal(ledger.begin("run", "h").kind, "replay");
});

test("the input gate serialises operations and reopens on release", () => {
  const gate = new InputGate();
  assert.equal(gate.acquire("a"), true);
  assert.equal(gate.acquire("a"), true, "re-entrant for the owner");
  assert.equal(gate.acquire("b"), false);
  gate.release("b");
  assert.equal(gate.activeOperationId, "a");
  gate.release("a");
  assert.equal(gate.acquire("b"), true);
});

test("existing input tools refuse to interleave with a running operation, but capture and readings pass", async () => {
  const sent: string[] = [];
  const connection = { sendActions: async (actions: Array<{ method: string; id: number }>) => { sent.push(...actions.map((a) => a.method)); return actions.map((a) => ({ id: a.id, success: true })); } };
  defaultInputGate.acquire("op-busy");
  try {
    const blocked = await dispatchActions(connection, [{ method: "pointer_click", params: [1] }]);
    assert.equal(blocked.isError, true);
    assert.match(blocked.content[0]?.type === "text" ? blocked.content[0].text : "", /busy: operation "op-busy"/);
    assert.deepEqual(sent, [], "nothing was sent to Unity");
    const readings = await dispatchActions(connection, [{ method: "start_readings", params: [] }]);
    assert.equal(readings.isError, undefined);
  } finally {
    defaultInputGate.release("op-busy");
  }
  const free = await dispatchActions(connection, [{ method: "pointer_click", params: [1] }]);
  assert.equal(free.isError, undefined);
});

test("which wire methods are input", () => {
  for (const method of ["pointer_click", "key_down", "set_axis", "pause_time", "reset_game"]) assert.equal(isMutatingMethod(method), true, method);
  for (const method of ["capture_screen", "start_readings", "stop_readings", "wait_frames"]) assert.equal(isMutatingMethod(method), false, method);
});
