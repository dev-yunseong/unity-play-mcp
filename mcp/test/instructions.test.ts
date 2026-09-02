import assert from "node:assert/strict";
import test from "node:test";

import { serverInstructions } from "../src/instructions.js";

/// 이 안내가 답해야 하는 물음은 세 가지다: 이 server 를 언제 쓰는가, 무엇이 먼저 있어야 하는가,
/// tool 을 어떤 순서로 부르는가. 문장을 그대로 고정하면 문장을 다듬을 때마다 test 가 깨지므로,
/// 그 세 가지가 실제로 적혀 있는지만 확인한다.
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
  // 안내가 부르라고 말하는 tool 이름이 tools.ts 의 등록 이름과 어긋나면, agent 는 없는 tool 을 찾는다.
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
    "pause_game",
    "resume_game",
    "reset_game",
  ]) {
    assert.ok(serverInstructions.includes(toolName), `${toolName} must be mentioned`);
  }
});

test("instructions stay short enough to sit in every context", () => {
  // 모든 대화의 system prompt 에 들어간다. 길어지면 tool 설명을 옮겨 적고 있다는 뜻이다.
  assert.ok(
    serverInstructions.length < 2000,
    `instructions are ${serverInstructions.length} characters; keep them under 2000`,
  );
});
