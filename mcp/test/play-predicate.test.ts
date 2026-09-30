import assert from "node:assert/strict";
import test from "node:test";

import {
  collectNeeds, evaluateAll, evaluatePredicate, parsePredicate, PredicateError,
  type PlaySample, type Predicate,
} from "../src/play-predicate.js";
import { SESSION, stampOf } from "./play-fake.js";

const ref = { sessionId: SESSION, id: 7, generation: 1 };
const byRef = { ref };
const refKey = `ref:${SESSION}:7:1`;

function sample(over: Partial<PlaySample> = {}): PlaySample {
  return {
    stamp: stampOf(), scope: "debug",
    targets: { [refKey]: { status: "one", count: 1, ref, lifecycle: "present", active: true, interactable: true } },
    members: [], facts: [], ...over,
  };
}

function member(value: unknown, extra: Record<string, unknown> = {}) {
  return { target: refKey, component: "Health", member: "hp", status: "known", value, valueType: "number", unit: "hp", ...extra } as PlaySample["members"][number];
}

test("parsePredicate rejects unknown keys, empty groups and eval-like input", () => {
  assert.throws(() => parsePredicate({ eval: "1+1" }), PredicateError);
  assert.throws(() => parsePredicate({ all: [] }), PredicateError);
  assert.throws(() => parsePredicate({ all: [{ sceneIs: "A" }], any: [] }), /exactly one/);
  assert.throws(() => parsePredicate({ member: { target: byRef, component: "C", name: "n", op: "lt", value: "x" } }), /compares numbers only/);
  assert.throws(() => parsePredicate({ member: { target: { name: "" }, component: "C", name: "n", op: "eq", value: 1 } }), PredicateError);
});

test("nesting deeper than the limit is refused", () => {
  let nested: unknown = { sceneIs: "A" };
  for (let i = 0; i < 12; i++) nested = { not: nested };
  assert.throws(() => parsePredicate(nested), /deeper/);
});

test("member comparisons use numbers and the same unit", () => {
  const p = (op: string, value: number, unit?: string) => parsePredicate({ member: { target: byRef, component: "Health", name: "hp", op, value, ...(unit ? { unit } : {}) } });
  const context = { after: sample({ members: [member(10)] }) };
  assert.equal(evaluatePredicate(p("gt", 5, "hp"), context).result, "true");
  assert.equal(evaluatePredicate(p("lte", 5, "hp"), context).result, "false");
  assert.equal(evaluatePredicate(p("gt", 5, "seconds"), context).result, "unknown", "a different unit is not comparable");
});

test("a missing, mistyped or redacted member is unknown, never a pass", () => {
  const p = parsePredicate({ member: { target: byRef, component: "Health", name: "hp", op: "eq", value: 10 } });
  assert.equal(evaluatePredicate(p, { after: sample() }).result, "unknown");
  assert.equal(evaluatePredicate(p, { after: sample({ members: [member("ten", { valueType: "string" })] }) }).result, "unknown");
  assert.equal(evaluatePredicate(p, { after: sample({ members: [member({ $redacted: true, because: "name", length: 3 })] }) }).result, "unknown");
  assert.equal(evaluatePredicate(p, { after: sample({ members: [member(undefined, { status: "unknown", reason: "no such member" })] }) }).result, "unknown");
});

test("ordering comparisons never apply to strings", () => {
  const p = parsePredicate({ member: { target: byRef, component: "Health", name: "hp", op: "eq", value: 1 } });
  assert.equal(evaluatePredicate(p, { after: sample({ members: [member("1")] }) }).result, "unknown");
});

test("changed compares only the same entity, unit and type across the same session and scene", () => {
  const changed = parsePredicate({ member: { target: byRef, component: "Health", name: "hp", op: "changed" } });
  const before = sample({ members: [member(10)] });
  assert.equal(evaluatePredicate(changed, { before, after: sample({ members: [member(7)] }) }).result, "true");
  assert.equal(evaluatePredicate(changed, { before, after: sample({ members: [member(10)] }) }).result, "false");
  assert.equal(evaluatePredicate(changed, { after: sample({ members: [member(7)] }) }).result, "unknown", "no before sample");
  assert.equal(
    evaluatePredicate(changed, { before, after: sample({ stamp: stampOf({ sessionId: "other" }), members: [member(7)] }) }).result,
    "unknown", "a different session is not a comparable baseline");
  assert.equal(
    evaluatePredicate(changed, { before, after: sample({ stamp: stampOf({ scene: "Other" }), members: [member(7)] }) }).result,
    "unknown");
  assert.equal(
    evaluatePredicate(changed, { before, after: sample({ members: [member(7, { unit: "mp" })] }) }).result,
    "unknown", "a changed unit must not read as a change");
  assert.equal(
    evaluatePredicate(changed, { before, after: sample({ members: [member(7, { valueType: "string" })] }) }).result,
    "unknown");
});

test("a duplicate name selector is an ambiguity, not a guess", () => {
  const named = parsePredicate({ entityExists: { name: "Card" } });
  const key = "name:Card|";
  const context = { after: sample({ targets: { [key]: { status: "ambiguous", count: 2 } } }) };
  const result = evaluatePredicate(named, context);
  assert.equal(result.result, "unknown");
  assert.match(result.evidence[0]?.detail ?? "", /ambiguous/);
});

test("destroyed, inactive and unobserved are distinguished", () => {
  const exists = parsePredicate({ entityExists: byRef });
  const absent = parsePredicate({ entityAbsent: byRef });
  const withLifecycle = (lifecycle: string, extra: Record<string, unknown> = {}) =>
    sample({ targets: { [refKey]: { status: "one", count: 1, ref, lifecycle: lifecycle as never, ...extra } } });
  assert.equal(evaluatePredicate(absent, { after: withLifecycle("destroyed") }).result, "true");
  assert.equal(evaluatePredicate(absent, { after: withLifecycle("inactive", { active: false }) }).result, "false");
  assert.equal(evaluatePredicate(exists, { after: withLifecycle("inactive", { active: false }) }).result, "true");
  for (const lifecycle of ["unobserved", "out_of_scope"]) {
    assert.equal(evaluatePredicate(absent, { after: withLifecycle(lifecycle) }).result, "unknown", `${lifecycle} is not destroyed`);
  }
});

test("a name that matches nothing is absent only in debug scope", () => {
  const absent = parsePredicate({ entityAbsent: { name: "Ghost" } });
  const none = { "name:Ghost|": { status: "none" as const, count: 0 } };
  assert.equal(evaluatePredicate(absent, { after: sample({ scope: "debug", targets: none }) }).result, "true");
  assert.equal(evaluatePredicate(absent, { after: sample({ scope: "player", targets: none }) }).result, "unknown");
});

test("all/any/not follow three-valued logic", () => {
  const t = { sceneIs: "Main" }, f = { sceneIs: "Nope" };
  const u = { active: { name: "Missing" } };
  const run = (predicate: unknown) => evaluatePredicate(parsePredicate(predicate), { after: sample({ targets: { "name:Missing|": { status: "ambiguous", count: 2 } } }) }).result;
  assert.equal(run({ all: [t, u] }), "unknown");
  assert.equal(run({ all: [f, u] }), "false");
  assert.equal(run({ any: [t, u] }), "true");
  assert.equal(run({ any: [f, u] }), "unknown");
  assert.equal(run({ not: u }), "unknown");
  assert.equal(run({ not: f }), "true");
});

test("evaluateAll with no predicates is unknown", () => {
  assert.equal(evaluateAll([], { after: sample() }).result, "unknown");
});

test("eventMatches reads recorded events and says sampled events can miss changes", () => {
  const p = parsePredicate({ eventMatches: { kind: "goal_reached", providerId: "p1" } });
  const event = { cursor: "s:1", sequence: 1, kind: "goal_reached", providerId: "p1", stamp: {}, provenance: "provider" as const };
  assert.equal(evaluatePredicate(p, { after: sample(), events: [event] }).result, "true");
  const none = evaluatePredicate(p, { after: sample(), events: [] });
  assert.equal(none.result, "false");
  assert.match(none.evidence[0]?.detail ?? "", /can miss/);
});

test("collectNeeds gathers only the targets the predicates touch", () => {
  const predicates: Predicate[] = [
    parsePredicate({ member: { target: byRef, component: "Health", name: "hp", op: "changed" } }),
    parsePredicate({ interactable: { name: "Go" } }),
    parsePredicate({ sceneIs: "Main" }),
  ];
  const needs = collectNeeds(predicates);
  assert.equal(needs.targets.length, 2);
  assert.equal(needs.members.length, 1);
  assert.equal(collectNeeds([parsePredicate({ sceneIs: "Main" })]).targets.length, 0);
});

test("fact predicates need the provider fact and honour units", () => {
  const p = parsePredicate({ fact: { providerId: "prov", name: "energy", op: "gte", value: 3, unit: "energy" } });
  const fact = (value: unknown, unit = "energy") => ({ providerId: "prov", name: "energy", status: "known" as const, value: value as never, unit });
  assert.equal(evaluatePredicate(p, { after: sample({ facts: [fact(4)] }) }).result, "true");
  assert.equal(evaluatePredicate(p, { after: sample({ facts: [fact(4, "gold")] }) }).result, "unknown");
  assert.equal(evaluatePredicate(p, { after: sample() }).result, "unknown");
});
