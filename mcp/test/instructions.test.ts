import assert from "node:assert/strict";
import test from "node:test";

import { serverInstructions } from "../src/instructions.js";

/// 안내가 언제 쓰는지, 무엇이 먼저 필요한지, tool 을 어떤 순서로 부르는지를 담는지만 확인한다.
/// 문장 자체를 고정하면 문구를 다듬을 때마다 test 가 깨진다.
test("instructions state the Play Mode precondition", () => {
  assert.match(serverInstructions, /Play Mode/);
  assert.match(serverInstructions, /Unity editor/);
});

test("instructions put start_readings before get_scene_state", () => {
  const startsAt = serverInstructions.indexOf("start_readings");
  const readsAt = serverInstructions.indexOf("get_scene_state");

  assert.ok(startsAt >= 0, "start_readings must be mentioned");
  assert.ok(readsAt >= 0, "get_scene_state must be mentioned");
  assert.ok(startsAt < readsAt, "the call order must read start_readings first");
});

test("instructions say where an instance id comes from", () => {
  assert.match(serverInstructions, /instance id/);
  assert.match(serverInstructions, /never guess an id/);
});

test("instructions name every tool they refer to", () => {
  // 안내의 tool 이름이 tools.ts 의 등록 이름과 다르면 agent 가 없는 tool 을 찾는다.
  for (const toolName of [
    "get_unity_status",
    "start_readings",
    "stop_readings",
    "get_scene_state",
    "capture_screen",
    "click",
    "enter_text",
    "move_mouse",
    "mouse_button",
    "press_key",
    "set_axis",
    "set_button",
    "perform_actions",
    "wait_for_condition",
    "pause_game",
    "resume_game",
    "reset_game",
  ]) {
    assert.ok(serverInstructions.includes(toolName), `${toolName} must be mentioned`);
  }
});

test("instructions stay short enough to sit in every context", () => {
  // 모든 대화의 system prompt 에 들어가므로 짧게 유지한다. 길어지면 tool 설명을 반복하고 있다는 뜻이다.
  assert.ok(
    serverInstructions.length < 2000,
    `instructions are ${serverInstructions.length} characters; keep them under 2000`,
  );
});
