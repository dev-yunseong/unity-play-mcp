import assert from "node:assert/strict";
import test from "node:test";

import type { ActionRequest, ActionResult } from "../src/connection.js";
import { dispatchActions } from "../src/tools.js";

test("dispatchActions assigns ids and preserves successful results", async () => {
  let received: ActionRequest[] = [];
  const connection = {
    async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
      received = actions;
      return [
        { id: actions[0].id, success: true, returnValue: { first: true } },
        { id: actions[1].id, success: true, returnValue: { second: true } },
      ];
    },
  };

  const response = await dispatchActions(connection, [
    { method: "mouse_down", params: [0] },
    { method: "mouse_up", params: [0] },
  ]);

  assert.equal(received.length, 2);
  assert.ok(received[1].id > received[0].id);
  assert.equal(response.isError, undefined);
  assert.match(response.content[0].type === "text" ? response.content[0].text : "", /first/);
  assert.match(response.content[0].type === "text" ? response.content[0].text : "", /second/);
});

test("dispatchActions marks partial failure without discarding results", async () => {
  const connection = {
    async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
      return [
        { id: actions[0].id, success: true, returnValue: "kept" },
        { id: actions[1].id, success: false, error: "rejected" },
      ];
    },
  };

  const response = await dispatchActions(connection, [
    { method: "key_down", params: ["Space"] },
    { method: "key_up", params: ["Space"] },
  ]);

  assert.equal(response.isError, true);
  const body = response.content[0].type === "text" ? response.content[0].text : "";
  assert.match(body, /kept/);
  assert.match(body, /rejected/);
});

test("dispatchActions converts connection rejection to a tool error", async () => {
  const connection = {
    async sendActions(): Promise<ActionResult[]> {
      throw new Error("game is offline");
    },
  };

  const response = await dispatchActions(connection, [{ method: "pause_time", params: [] }]);
  assert.equal(response.isError, true);
  assert.match(response.content[0].type === "text" ? response.content[0].text : "", /game is offline/);
});
