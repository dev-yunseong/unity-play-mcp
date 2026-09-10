import assert from "node:assert/strict";
import test from "node:test";

import { PulseStore, type PulseFrame, type PulseObject } from "../src/pulse.js";

/// 게임이 실제로 내는 모양. `LiveState.cs:568`~`669` 이 component 를
/// `{"on":<타입>,"m":[...]}` 로 쓰고, 멤버는 `member` 와 값을 들되 `among` 은 같은 타입의
/// 둘째 component 부터, `asked` 는 `false` 일 때만 싣는다.
function object(id: number, selector: string, value: number, scene?: string): PulseObject {
  return {
    id,
    path: `Canvas/${selector}`,
    selector,
    ...(scene === undefined ? {} : { scene }),
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

test("whole reading replaces all held objects", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Old", 1)] }));
  store.fold(pulse({ reading: 2, whole: true, active: [object(2, "New", 2)] }));
  assert.deepEqual(store.getState()?.active.map(({ selector }) => selector), ["New"]);
});

test("delta merges members by member and optional among", () => {
  const store = new PulseStore();
  const original = object(1, "Card", 1);
  original.by = [{ on: "Widget", m: [
    { member: "label", asked: false, value: "kept" },
    { member: "slot", value: "zero" },
    { member: "slot", among: 1, value: "one" },
  ] }];
  const update = object(1, "Card", 2);
  update.by = [{ on: "Widget", m: [
    { member: "slot", among: 1, value: "changed" },
  ] }];
  store.fold(pulse({ whole: true, active: [original] }));
  store.fold(pulse({ reading: 2, active: [update] }));
  assert.deepEqual(store.getState()?.active[0]?.by?.[0]?.members, [
    { member: "label", asked: false, value: "kept" },
    { member: "slot", value: "zero" },
    { member: "slot", among: 1, value: "changed" },
  ]);
});

test("object-level scene override participates in identity", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1), object(2, "Card", 2, "HUD")] }));
  store.fold(pulse({ reading: 2, active: [object(2, "Card", 3, "HUD")] }));
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [1, 2]);
  assert.equal(store.getState()?.active[1]?.by?.[0]?.members?.[0]?.value, 3);
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

test("a delta without gone retains existing objects", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "A", 1)] }));
  store.fold(pulse({ reading: 2, gone: undefined }));
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [1]);
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

/// #19 이 고치는 것 자체. 게임이 `m` 에 실어 보낸 값이 접힌 상태의 `members` 에 도착해야 한다.
test("member values arrive under members even though the game sends m", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 7)] }));

  const component = store.getState()?.active[0]?.by?.[0];
  assert.deepEqual(component?.members, [{ member: "value", value: 7 }]);
  // 옮기고 나면 원래 키는 남지 않는다. 남으면 같은 목록이 두 벌 실려 나간다.
  assert.equal(component?.m, undefined);
});

/// 게임은 멤버가 하나도 없는 component 를 내지 않는다 — `LiveState.cs` 가 `count > 0` 일 때만
/// 쓴다. 그래도 그것이 도착했을 때 reading 전체를 버리지는 않는다. component 하나가 아무 말도
/// 안 한 것과 구별할 수 없는 모양이기 때문이다.
test("a component that carries no members keeps the members already held", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [object(1, "Card", 3)] }));

  const silent: PulseObject = { id: 1, path: "Canvas/Card", selector: "Card", by: [{ on: "Widget" }] };
  assert.equal(store.fold(pulse({ reading: 2, active: [silent] })), true);

  assert.equal(store.getLastUnreadableFrame(), undefined);
  assert.deepEqual(store.getState()?.active[0]?.by?.[0]?.members, [{ member: "value", value: 3 }]);
});

/// 키가 어긋난 것을 "멤버가 없다" 와 같은 말로 뭉뚱그리지 않는다. reading 을 못 읽었다고 말하되,
/// 몇 개가 어떤 키를 들고 왔는지 이름을 댄다.
test("a component whose members sit under another key names that key", () => {
  const store = new PulseStore();
  assert.equal(
    store.fold(pulse({ whole: true, active: [componentWithUnexpectedKey(1, "Card")] })),
    false,
  );

  const reason = store.getLastUnreadableFrame()?.reason ?? "";
  assert.match(reason, /1 of 1 PULSE components carried no "m"/);
  assert.match(reason, /first: Widget on Canvas\/Card/);
  assert.match(reason, /may be under: members/);
});

/// `m` 이 있는데 배열이 아닌 경우. 없는 것과 같이 다루면 멤버가 실려 왔는데도 "아무 말도 안 한
/// component" 로 조용히 넘어간다.
test("a component whose m is not an array is not read as an empty component", () => {
  const store = new PulseStore();
  const broken = {
    id: 1, path: "Canvas/Card", selector: "Card",
    by: [{ on: "Widget", m: { member: "value", value: 1 } }],
  } as unknown as PulseObject;

  assert.equal(store.fold(pulse({ whole: true, active: [broken] })), false);
  assert.match(store.getLastUnreadableFrame()?.reason ?? "", /may be under: m/);
});

/// 멤버 목록이 `m` 이 아니라 다른 키에 실려 온 component.
///
/// 그 다른 키를 `members` 로 잡은 것은 의도한 것이다. #19 가 실제로 만든 모양이고, `m` 이
/// 있는지가 아니라 `members` 가 있는지로 판정하는 순진한 수정이 들어오면 이 fixture 가 잡는다.
function componentWithUnexpectedKey(id: number, selector: string): PulseObject {
  return {
    id,
    path: `Canvas/${selector}`,
    selector,
    by: [{ on: "Widget", members: [{ member: "value", value: 1 }] }],
  };
}

test("a frame that cannot be folded is not counted as an arrived reading", () => {
  let clock = 1_000;
  const store = new PulseStore(() => clock);

  // 먼저 정상 reading 하나를 성공시킨다. "성공 하나 뒤에 실패 하나" 가 #48 이 고치는 실제
  // 장면이다 — reading 이 잘 들어오다 멈추는 순간이다.
  store.fold(pulse({ whole: true, active: [object(1, "Card", 1)] }));
  assert.equal(store.getLastReadingAt(), 1_000);
  assert.equal(store.getLastUnreadableFrame(), undefined);

  clock = 2_000;
  assert.equal(
    store.fold(pulse({ reading: 2, active: [componentWithUnexpectedKey(1, "Card")] })),
    false,
  );

  // 실패한 frame 은 도착한 reading 으로 세지 않는다 — `get_unity_status` 와
  // `get_scene_state` 가 같은 답을 하려면 이전 성공 상태 그대로 남아야 한다.
  assert.equal(store.getLastReadingAt(), 1_000);
  assert.deepEqual(store.getState()?.active.map(({ id }) => id), [1]);

  const unreadable = store.getLastUnreadableFrame();
  assert.equal(unreadable?.at, 2_000);
  assert.ok((unreadable?.reason.length ?? 0) > 0);
});

test("a successful reading clears a prior unreadable-frame mark", () => {
  const store = new PulseStore();
  store.fold(pulse({ whole: true, active: [componentWithUnexpectedKey(1, "Card")] }));
  assert.notEqual(store.getLastUnreadableFrame(), undefined);

  store.fold(pulse({ reading: 2, active: [object(1, "Card", 1)] }));
  assert.equal(store.getLastUnreadableFrame(), undefined);
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

/// `wait.ts` 는 값이 하나도 안 바뀐 pulse 에도 새 `reading` 이 왔다는 신호가 필요하다 —
/// `sinceReading` 조건은 값이 아니라 번호만 본다.
test("onReading fires on every successful fold, even one that changes nothing", () => {
  const store = new PulseStore();
  const seen: number[] = [];
  store.onReading((state) => seen.push(state.reading));

  store.fold(pulse({ whole: true, active: [object(1, "A", 1)] }));
  store.fold(pulse({ reading: 2 }));
  assert.deepEqual(seen, [1, 2]);
});

test("onReading does not fire for a frame that could not be folded", () => {
  const store = new PulseStore();
  const seen: number[] = [];
  store.onReading((state) => seen.push(state.reading));

  assert.equal(
    store.fold(pulse({ whole: true, active: [componentWithUnexpectedKey(1, "Card")] })),
    false,
  );
  assert.deepEqual(seen, []);
});

test("onReading stops firing once unsubscribed", () => {
  const store = new PulseStore();
  const seen: number[] = [];
  const unsubscribe = store.onReading((state) => seen.push(state.reading));

  store.fold(pulse({ whole: true, active: [object(1, "A", 1)] }));
  unsubscribe();
  store.fold(pulse({ reading: 2 }));
  assert.deepEqual(seen, [1]);
});
