import assert from "node:assert/strict";
import test from "node:test";

import { performActionSchema, toWireAction } from "../src/tools.js";

/// agent 가 보내는 이름 있는 action 과, Unity 가 받아야 하는 `params` 배열의 짝.
///
/// Unity 쪽 protocol 은 이 issue 에서 건드리지 않았다. 입력 모양이 tuple 에서 이름 있는 field 로
/// 바뀌어도 배열의 순서와 값은 그대로여야 하므로, 16 개 method 를 전부 여기에 못 박는다.
const wireCases: ReadonlyArray<{
  name: string;
  action: unknown;
  params: unknown[];
}> = [
  { name: "button_click", action: { method: "button_click", targetId: 42 }, params: [42] },
  {
    name: "enter_text",
    action: { method: "enter_text", targetId: 7, text: "hello" },
    params: [7, "hello"],
  },
  { name: "move_mouse", action: { method: "move_mouse", x: 12.5, y: 30 }, params: [12.5, 30] },
  { name: "mouse_down", action: { method: "mouse_down", button: 0 }, params: [0] },
  { name: "mouse_up", action: { method: "mouse_up", button: 2 }, params: [2] },
  {
    name: "key_click",
    action: { method: "key_click", key: "Space", seconds: 0.05 },
    params: ["Space", 0.05],
  },
  { name: "key_down", action: { method: "key_down", key: "W" }, params: ["W"] },
  { name: "key_up", action: { method: "key_up", key: "W" }, params: ["W"] },
  {
    name: "set_axis",
    action: { method: "set_axis", name: "Horizontal", value: -1 },
    params: ["Horizontal", -1],
  },
  {
    name: "set_button",
    action: { method: "set_button", name: "Jump", pressed: true },
    params: ["Jump", true],
  },
  { name: "pause_time", action: { method: "pause_time" }, params: [] },
  { name: "resume_time", action: { method: "resume_time" }, params: [] },
  {
    name: "reset_game",
    action: { method: "reset_game", clearPlayerPrefs: true },
    params: [{ clearPlayerPrefs: true }],
  },
  { name: "start_readings", action: { method: "start_readings" }, params: [] },
  { name: "stop_readings", action: { method: "stop_readings" }, params: [] },
  { name: "capture_screen for the whole screen", action: { method: "capture_screen" }, params: [] },
  {
    name: "capture_screen for one target",
    action: { method: "capture_screen", targetId: 11 },
    params: [11],
  },
  {
    name: "capture_screen with options",
    action: { method: "capture_screen", targetId: 11, maxEdge: 512, padding: 8 },
    params: [11, { maxEdge: 512, padding: 8 }],
  },
  {
    name: "capture_screen with only maxEdge",
    action: { method: "capture_screen", targetId: 11, maxEdge: 512 },
    params: [11, { maxEdge: 512 }],
  },
  {
    name: "capture_screen with only padding",
    action: { method: "capture_screen", targetId: 11, padding: 8 },
    params: [11, { padding: 8 }],
  },
];

for (const { name, action, params } of wireCases) {
  test(`${name} keeps the params Unity expects`, () => {
    const parsed = performActionSchema.safeParse(action);
    assert.ok(parsed.success, `schema rejected ${JSON.stringify(action)}`);

    const wire = toWireAction(parsed.data);
    assert.equal(wire.method, (action as { method: string }).method);
    assert.deepEqual(wire.params, params);
  });
}

test("every method the schema accepts is covered by a wire case", () => {
  const covered = new Set(wireCases.map(({ action }) => (action as { method: string }).method));
  assert.equal(covered.size, 16);
});

const rejectedCases: ReadonlyArray<{ name: string; action: unknown }> = [
  { name: "an unknown method", action: { method: "quit_game" } },
  { name: "a missing targetId", action: { method: "button_click" } },
  { name: "a fractional targetId", action: { method: "button_click", targetId: 1.5 } },
  { name: "a mouse button above 2", action: { method: "mouse_down", button: 3 } },
  { name: "a negative mouse button", action: { method: "mouse_up", button: -1 } },
  { name: "an empty key", action: { method: "key_down", key: "" } },
  { name: "a negative key_click duration", action: { method: "key_click", key: "Space", seconds: -1 } },
  { name: "a zero key_click duration", action: { method: "key_click", key: "Space", seconds: 0 } },
  { name: "a key_click without a duration", action: { method: "key_click", key: "Space" } },
  { name: "an empty axis name", action: { method: "set_axis", name: "", value: 1 } },
  { name: "an empty button name", action: { method: "set_button", name: "", pressed: true } },
  { name: "a reset_game without clearPlayerPrefs", action: { method: "reset_game" } },
  { name: "a field the method does not take", action: { method: "pause_time", seconds: 1 } },
  { name: "maxEdge without a targetId", action: { method: "capture_screen", maxEdge: 512 } },
  { name: "padding without a targetId", action: { method: "capture_screen", padding: 8 } },
  { name: "a zero maxEdge", action: { method: "capture_screen", targetId: 3, maxEdge: 0 } },
  { name: "a negative padding", action: { method: "capture_screen", targetId: 3, padding: -1 } },
  { name: "the old positional params shape", action: { method: "button_click", params: [42] } },
];

for (const { name, action } of rejectedCases) {
  test(`the schema rejects ${name}`, () => {
    assert.equal(performActionSchema.safeParse(action).success, false);
  });
}
