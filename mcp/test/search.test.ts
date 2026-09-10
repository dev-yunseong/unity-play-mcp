import assert from "node:assert/strict";
import test from "node:test";

import type { FoldedPulseState, JsonValue, PulseComponent, PulseObject, PulseOffers } from "../src/pulse.js";
import { searchTargets } from "../src/search.js";

/// `visible.test.ts`와 같은 이유로 `PulseStore.fold`를 태우지 않고 `FoldedPulseState` literal
/// 에 대고 돈다 — 여기서 검증하려는 것은 접힌 상태에서 무엇을 고르느냐이지 무엇이 접히느냐가
/// 아니다.
function component(on: string, values: Record<string, JsonValue>): PulseComponent {
  return {
    on,
    members: Object.entries(values).map(([member, value]) => ({ member, value })),
  };
}

function object(
  selector: string,
  by: PulseComponent[] = [],
  extra: { scene?: string; offers?: PulseOffers } = {},
): PulseObject {
  return {
    id: selector.length,
    path: `Canvas/${selector}`,
    selector,
    by,
    ...extra,
  };
}

function state(active: PulseObject[], deactive: PulseObject[] = []): FoldedPulseState {
  return {
    reading: 7,
    frame: 700,
    scene: "Battle",
    schema: 1,
    statics: [],
    active,
    deactive,
    watching: 0,
    unresolved: 0,
    unwatchable: 0,
    changed: [],
    gone: [],
  };
}

test("필터가 없으면 active 전부를 낸다", () => {
  const result = searchTargets(state([object("Score"), object("Timer")]));
  assert.deepEqual(result.candidates.map((c) => c.selector), ["Score", "Timer"]);
  assert.equal(result.total, 2);
  assert.equal(result.truncated, false);
  assert.deepEqual(result.duplicateNames, []);
});

test("name 은 형제 순번을 뗀 leaf 이름과 견준다", () => {
  const result = searchTargets(state([
    object("Canvas[0]/Card(Clone)[3]"),
    object("Canvas[0]/Timer[0]"),
  ]), { name: "Card(Clone)" });
  assert.deepEqual(result.candidates.map((c) => c.selector), ["Canvas[0]/Card(Clone)[3]"]);
});

test("기본은 contains 이고 대소문자를 가린다", () => {
  const result = searchTargets(state([object("continue")]), { name: "Continue" });
  assert.equal(result.candidates.length, 1);
});

test("caseSensitive 가 서면 대소문자가 달라진 이름은 걸러진다", () => {
  const result = searchTargets(state([object("continue")]), { name: "Continue", caseSensitive: true });
  assert.equal(result.candidates.length, 0);
});

test("exact 가 서면 부분 일치로는 못 들어온다", () => {
  const result = searchTargets(state([object("ScoreLabel")]), { name: "Score", exact: true });
  assert.equal(result.candidates.length, 0);
  const whole = searchTargets(state([object("ScoreLabel")]), { name: "ScoreLabel", exact: true });
  assert.equal(whole.candidates.length, 1);
});

test("exact 와 caseSensitive 는 독립이다 — exact 이면서 대소문자는 무시할 수 있다", () => {
  const result = searchTargets(state([object("Button")]), { name: "BUTTON", exact: true });
  assert.equal(result.candidates.length, 1);
});

test("displayedText 는 화면에 보이는 문자열과 견주고, 그런 문자열이 없으면 탈락한다", () => {
  const result = searchTargets(state([
    object("Label", [component("UnityEngine.UI.Text", { text: "Ready" })]),
    object("Bar", [component("UnityEngine.UI.Image", { fillAmount: 0.5 })]),
  ]), { displayedText: "Ready" });
  assert.deepEqual(result.candidates.map((c) => c.selector), ["Label"]);
});

test("component 는 object.by[].on 중 하나를 contains 로 찾는다", () => {
  const result = searchTargets(state([
    object("continue", [component("MyGame.Button", {})]),
    object("Score", [component("UnityEngine.UI.Text", { text: "0" })]),
  ]), { component: "Button" });
  assert.deepEqual(result.candidates.map((c) => c.selector), ["continue"]);
});

test("actionable 은 offers 로 조작 가능한 것만, 또는 없는 것만 남긴다", () => {
  const clickable = object("Confirm", [], { offers: { clicks: [{ event: "onClick", method: "Ok", on: "Confirm" }] } });
  const inert = object("Backdrop");
  const onlyActionable = searchTargets(state([clickable, inert]), { actionable: true });
  assert.deepEqual(onlyActionable.candidates.map((c) => c.selector), ["Confirm"]);
  const onlyInert = searchTargets(state([clickable, inert]), { actionable: false });
  assert.deepEqual(onlyInert.candidates.map((c) => c.selector), ["Backdrop"]);
});

test("actions 는 offers 의 clicks/keys/pointers 를 그대로 옮긴다", () => {
  const offers: PulseOffers = {
    clicks: [{ event: "onClick", method: "Fire", on: "Card" }],
    keys: [{ key: "Return", does: ["submit"] }, { key: "Escape" }],
    pointers: ["OnPointerDown"],
  };
  const result = searchTargets(state([object("Card", [], { offers })]));
  assert.deepEqual(result.candidates[0]?.actions, ["click", "key:Return", "key:Escape", "pointer:OnPointerDown"]);
});

test("offers 가 없는 객체는 actions 가 빈 배열이다", () => {
  const result = searchTargets(state([object("Backdrop")]));
  assert.deepEqual(result.candidates[0]?.actions, []);
});

test("includeInactive 가 없으면 deactive 는 검색되지 않는다", () => {
  const result = searchTargets(state([object("Score")], [object("Menu")]));
  assert.deepEqual(result.candidates.map((c) => c.selector), ["Score"]);
});

test("includeInactive 가 서면 deactive 도 검색되고 active 가 false 로 실린다", () => {
  const result = searchTargets(state([object("Score")], [object("Menu")]), { includeInactive: true });
  assert.deepEqual(result.candidates.map((c) => [c.selector, c.active]), [["Score", true], ["Menu", false]]);
});

test("scene 은 객체 자신의 scene, 없으면 reading 의 scene 을 낸다", () => {
  const result = searchTargets(state([object("Local"), object("Persistent", [], { scene: "DontDestroyOnLoad" })]));
  assert.deepEqual(result.candidates.map((c) => c.scene), ["Battle", "DontDestroyOnLoad"]);
});

test("limit 을 넘으면 truncated 가 서고 total 은 잘리기 전 개수를 낸다", () => {
  const result = searchTargets(state([object("A"), object("B"), object("C")]), { limit: 2 });
  assert.deepEqual(result.candidates.map((c) => c.selector), ["A", "B"]);
  assert.equal(result.total, 3);
  assert.equal(result.truncated, true);
});

test("0건이면 후보가 비고 total 도 0, truncated 는 false 다", () => {
  const result = searchTargets(state([object("Score")]), { name: "NoSuchThing" });
  assert.deepEqual(result.candidates, []);
  assert.equal(result.total, 0);
  assert.equal(result.truncated, false);
});

test("duplicateNames 는 leaf 이름이 겹치는 서로 다른 인스턴스를 잡는다", () => {
  const result = searchTargets(state([
    object("Canvas[0]/Card(Clone)[0]"),
    object("Canvas[0]/Card(Clone)[1]"),
    object("Canvas[0]/Timer[0]"),
  ]));
  assert.deepEqual(result.duplicateNames, ["Card(Clone)"]);
});

test("duplicateNames 는 limit 으로 안 보이는 후보의 겹침도 잡는다", () => {
  const result = searchTargets(state([
    object("Canvas[0]/Card(Clone)[0]"),
    object("Canvas[0]/Card(Clone)[1]"),
  ]), { limit: 1 });
  assert.deepEqual(result.candidates.map((c) => c.selector), ["Canvas[0]/Card(Clone)[0]"]);
  assert.deepEqual(result.duplicateNames, ["Card(Clone)"]);
});
