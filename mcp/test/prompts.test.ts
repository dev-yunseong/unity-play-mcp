import assert from "node:assert/strict";
import test from "node:test";

import { registerPrompts, unityPrompts } from "../src/prompts.js";

/// prompt 의 이름과 설명은 사용자가 보는 slash command 목록이고, render 결과는 agent 가 받는
/// 지시이므로 둘 다 계약이다.

function named(name: string) {
  const found = unityPrompts.find((prompt) => prompt.name === name);
  assert.ok(found !== undefined, `${name} must be registered`);
  return found;
}

test("every prompt carries a name, a title, and a description", () => {
  assert.ok(unityPrompts.length > 0);
  for (const prompt of unityPrompts) {
    assert.match(prompt.name, /^[a-z][a-z0-9_]*$/, `${prompt.name} must be snake_case`);
    assert.ok(prompt.title.length > 0, `${prompt.name} needs a title`);
    assert.ok(prompt.description.length > 0, `${prompt.name} needs a description`);
  }
});

test("prompt names are unique", () => {
  const names = unityPrompts.map((prompt) => prompt.name);
  assert.equal(new Set(names).size, names.length);
});

test("a prompt without its optional argument still renders", () => {
  const rendered = named("inspect_scene").render({});

  assert.match(rendered, /start_readings/);
  assert.match(rendered, /get_scene_state/);
  // 인자가 없으면 selector 를 지어내지 않고 좁히는 방법만 알려 준다.
  assert.doesNotMatch(rendered, /selector "/);
});

test("an optional argument reaches the rendered message", () => {
  const rendered = named("inspect_scene").render({ selector: "Canvas/StartButton" });

  assert.match(rendered, /selector "Canvas\/StartButton"/);
});

test("run_steps carries the steps it was given and tells the agent to read ids first", () => {
  const rendered = named("run_steps").render({ steps: "1. Press Start\n2. Type a word" });

  assert.match(rendered, /Press Start/);
  assert.match(rendered, /Type a word/);
  assert.match(rendered, /Never guess an id/);
});

test("run_steps appends the expectation only when one was given", () => {
  const withoutExpectation = named("run_steps").render({ steps: "1. Press Start" });
  const withExpectation = named("run_steps").render({
    steps: "1. Press Start",
    expectation: "the game board appears",
  });

  assert.doesNotMatch(withoutExpectation, /should end with/);
  assert.match(withExpectation, /should end with: the game board appears/);
});

test("track_value asks for history and names the object it was given", () => {
  const rendered = named("track_value").render({ selector: "Score" });

  assert.match(rendered, /"Score"/);
  assert.match(rendered, /includeHistory/);
});

test("track_value folds an action into the steps when one was given", () => {
  const rendered = named("track_value").render({ selector: "Score", action: "click Start" });

  assert.match(rendered, /Do this: click Start/);
});

test("review_screen captures before it judges", () => {
  const rendered = named("review_screen").render({});

  assert.ok(rendered.indexOf("capture_screen") < rendered.indexOf("Report each finding"));
});

test("every prompt renders a non-empty message through the server registration", () => {
  const registered: Array<{ name: string; render: (args: Record<string, string>) => unknown }> = [];
  const server = {
    registerPrompt(
      name: string,
      _config: unknown,
      callback: (args: Record<string, string>) => unknown,
    ) {
      registered.push({ name, render: callback });
    },
  };

  registerPrompts(server as never);

  assert.equal(registered.length, unityPrompts.length);
  for (const entry of registered) {
    const result = entry.render({ steps: "a step", selector: "an object" }) as {
      messages: Array<{ role: string; content: { type: string; text: string } }>;
    };

    assert.equal(result.messages.length, 1);
    assert.equal(result.messages[0].role, "user");
    assert.equal(result.messages[0].content.type, "text");
    assert.ok(result.messages[0].content.text.length > 0, `${entry.name} rendered nothing`);
  }
});

test("run_steps uses current observations, explicit effects and retry-safe inputs", () => {
  const rendered = named("run_steps").render({ steps: "Play a card" });

  assert.match(rendered, /freshness: "current"/);
  assert.ok(rendered.indexOf("observe with") < rendered.indexOf("Perform one step with act_and_observe"));
  assert.match(rendered, /basedOnObservationId/);
  assert.match(rendered, /expect predicates/);
  assert.match(rendered, /baseline/);
  assert.match(rendered, /identical arguments and operationId/);
  assert.match(rendered, /new id for a new input/);
});

test("run_steps stops on unconfirmed effects and retains a checked legacy fallback", () => {
  const rendered = named("run_steps").render({ steps: "Press Start" });

  assert.match(rendered, /unknown or not_observed/);
  assert.match(rendered, /do not automatically send the input again/);
  assert.match(rendered, /Stop at the first mismatch or unconfirmed result/);
  assert.match(rendered, /unsupported_capability/);
  assert.match(rendered, /wait_for_condition/);
  assert.match(rendered, /reading number alone is not success/);
  assert.match(rendered, /capture_screen/);
});
