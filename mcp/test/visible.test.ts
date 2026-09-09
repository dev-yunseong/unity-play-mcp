import assert from "node:assert/strict";
import test from "node:test";

import type { FoldedPulseState, JsonValue, PulseComponent, PulseObject } from "../src/pulse.js";
import { visibleElements } from "../src/visible.js";

/// `PulseStore.fold` 를 태우지 않고 `FoldedPulseState` literal 에 대고 돌린다.
///
/// fold 를 태우면 #19 가 고치는 중인 member key 결함에 묶인다. Unity 는 `"m"` 을 보내고
/// `mergeMembers` 는 `component.members` 를 읽으므로, 지금 그 길로 넣은 frame 은 fold 자체가
/// 실패한다. 이 tool 이 검증하려는 것은 접힌 상태에서 무엇을 고르느냐이지 무엇이 접히느냐가
/// 아니다.
function component(on: string, values: Record<string, JsonValue>): PulseComponent {
  return {
    on,
    members: Object.entries(values).map(([member, value]) => ({ member, value, asked: false })),
  };
}

function object(
  selector: string,
  by: PulseComponent[],
  extra: Record<string, JsonValue> = {},
): PulseObject {
  return {
    id: selector.length,
    path: `Canvas/${selector}`,
    selector,
    rect: { x: 10, y: 20, w: 100, h: 40 },
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

const seen = { onScreen: true, covered: false };

test("화면에 무언가를 그리는 컴포넌트만 낸다", () => {
  const found = visibleElements(state([
    object("Score", [component("UnityEngine.UI.Text", { text: "12" })], seen),
    object("Player", [component("MyGame.PlayerController", { hp: 3 })], seen),
  ]));
  assert.deepEqual(found.map((element) => element.on), ["UnityEngine.UI.Text"]);
  assert.deepEqual(found[0]?.shows, { text: "12" });
});

test("멤버를 하나도 안 든 UI 컴포넌트는 요소가 아니다", () => {
  // SDK 는 읽어 낸 멤버가 없는 컴포넌트를 `by` 에 안 쓴다. 그래도 delta 에서는 빈 항목이
  // 도착할 수 있고, 그것은 "이 타입이 여기 있다" 이지 "이것이 무언가를 보이고 있다" 가 아니다.
  const found = visibleElements(state([
    object("Layout", [component("UnityEngine.UI.LayoutElement", {})], seen),
  ]));
  assert.deepEqual(found, []);
});

test("글자, 채움 비율, 값을 그대로 낸다", () => {
  const found = visibleElements(state([
    object("Label", [component("TMPro.TextMeshProUGUI", { text: "Ready" })], seen),
    object("Health", [component("UnityEngine.UI.Image", { fillAmount: 0.35 })], seen),
    object("Volume", [component("UnityEngine.UI.Slider", { value: 0.8 })], seen),
    object("Mute", [component("UnityEngine.UI.Toggle", { isOn: true })], seen),
  ]));
  assert.deepEqual(found.map((element) => element.shows), [
    { text: "Ready" },
    { fillAmount: 0.35 },
    { value: 0.8 },
    { isOn: true },
  ]);
});

test("가려진 요소는 기본으로 빠지고 includeHidden 이 도로 낸다", () => {
  const behind = state([
    object("Hidden", [component("UnityEngine.UI.Text", { text: "behind" })],
      { onScreen: true, covered: true }),
  ]);
  assert.deepEqual(visibleElements(behind), []);
  assert.deepEqual(visibleElements(behind, { includeHidden: true }).map((e) => e.covered), [true]);
});

test("화면 밖 요소는 기본으로 빠진다", () => {
  const off = state([
    object("Offscreen", [component("UnityEngine.UI.Text", { text: "away" })],
      { onScreen: false, covered: false }),
  ]);
  assert.deepEqual(visibleElements(off), []);
  assert.deepEqual(visibleElements(off, { includeHidden: true }).map((e) => e.onScreen), [false]);
});

test("꺼진 요소는 기본으로 빠지고 includeHidden 이 active false 로 낸다", () => {
  const closed = state(
    [object("Score", [component("UnityEngine.UI.Text", { text: "12" })], seen)],
    [object("Menu", [component("UnityEngine.UI.Text", { text: "Resume" })], seen)],
  );
  assert.deepEqual(visibleElements(closed).map((e) => e.selector), ["Score"]);

  const all = visibleElements(closed, { includeHidden: true });
  assert.deepEqual(all.map((e) => e.selector), ["Score", "Menu"]);
  assert.deepEqual(all.map((e) => e.active), [true, false]);
});

test("selector 가 부분 문자열로 좁힌다", () => {
  const found = visibleElements(state([
    object("ScoreLabel", [component("UnityEngine.UI.Text", { text: "12" })], seen),
    object("TimerLabel", [component("UnityEngine.UI.Text", { text: "60" })], seen),
  ]), { selector: "Score" });
  assert.deepEqual(found.map((element) => element.selector), ["ScoreLabel"]);
});

test("한 객체가 보이는 컴포넌트를 둘 나르면 항목도 둘이다", () => {
  const found = visibleElements(state([
    object("Volume", [
      component("UnityEngine.UI.Slider", { value: 0.8, interactable: true }),
      component("UnityEngine.UI.Image", { fillAmount: 1 }),
    ], seen),
  ]));
  assert.deepEqual(found.map((element) => element.on), [
    "UnityEngine.UI.Slider", "UnityEngine.UI.Image",
  ]);
  assert.deepEqual(found.map((element) => element.id), [6, 6]);
});

test("자리를 그대로 옮겨 싣는다", () => {
  const found = visibleElements(state([
    object("Score", [component("UnityEngine.UI.Text", { text: "12" })], seen),
  ]));
  assert.deepEqual(found[0]?.rect, { x: 10, y: 20, w: 100, h: 40 });
  assert.equal(found[0]?.path, "Canvas/Score");
});

test("아직 아무 판단도 안 실린 요소는 빼지 않는다", () => {
  // `onScreen` 과 `covered` 가 없는 것은 "안 보인다" 가 아니라 whole pulse 를 아직 못 받은
  // 것이다. 없음을 아니오로 읽으면 첫 delta 에서 화면 전체가 사라진다.
  const found = visibleElements(state([
    object("Score", [component("UnityEngine.UI.Text", { text: "12" })]),
  ]));
  assert.deepEqual(found.map((element) => element.selector), ["Score"]);
  assert.equal(found[0]?.onScreen, undefined);
});
