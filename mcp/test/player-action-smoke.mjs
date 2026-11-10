import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { once } from "node:events";
import { createServer } from "node:net";
import { resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const [executable, logPath] = process.argv.slice(2);
assert.ok(executable && logPath, "Usage: node test/player-action-smoke.mjs <player> <player-log>");

const host = "127.0.0.1";
const port = 17311;
const listener = createServer();
await once(listener.listen(port, host), "listening");
await once(listener.close(), "close");

const player = spawn(resolve(executable), ["-batchmode", "-nographics", "-logFile", resolve(logPath)], {
  stdio: "ignore",
  windowsHide: true,
});
const exited = once(player, "exit");
const prematureExit = exited.then(([code, signal]) => {
  throw new Error(`Unity player exited before verification finished (${code ?? signal}). See ${logPath}.`);
});
const transport = new StdioClientTransport({
  command: process.execPath,
  args: [fileURLToPath(new URL("../dist/index.js", import.meta.url))],
  env: {
    UNITY_PLAY_MCP_URL: `ws://${host}:${port}/ws`,
    UNITY_PLAY_MCP_TIMEOUT_MS: "3000",
  },
});
const client = new Client({ name: "player-action-smoke", version: "1.0.0" });
const startup = new AbortController();

try {
  await Promise.race([verifyPlayer(), prematureExit]);
  console.log("IL2CPP player verified project actions and set_button and capture_screen overrides through MCP.");
} finally {
  startup.abort();
  try {
    await transport.close();
  } finally {
    player.kill();
    if (await Promise.race([exited.then(() => false), delay(5_000, true, { ref: false })])) {
      player.kill("SIGKILL");
      await exited;
    }
  }
}

async function verifyPlayer() {
  await client.connect(transport, { timeout: 10_000 });
  const deadline = Date.now() + 45_000;
  let capabilities;
  do {
    capabilities = await client.callTool({ name: "get_play_capabilities", arguments: {} }, undefined,
      { timeout: 5_000, signal: startup.signal });
    if (!capabilities.isError) break;
    await delay(250, undefined, { signal: startup.signal });
  } while (Date.now() < deadline);
  assert.ok(!capabilities.isError, JSON.stringify(capabilities));

  const action = capabilities.structuredContent?.unity?.registeredActions?.find(({ name }) => name === "smoke_echo");
  assert.deepEqual(action, {
    name: "smoke_echo",
    description: "Echo a value from the game assembly.",
    parameters: "[value: string]",
  });

  assert.ok(!capabilities.structuredContent.unity.registeredActions.some(({ name }) => name === "set_button"));

  const nonce = randomUUID();
  const [press] = actionResults(await callTool("set_button", { name: nonce, pressed: true }));
  assert.equal(press.returnValue, "press:" + nonce);

  const directCapture = await callTool("capture_screen", {});
  const [image] = directCapture.content;
  assert.deepEqual(image, {
    type: "image",
    mimeType: "image/png",
    data: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGOQLDkEAAH5AVAz2BXaAAAAAElFTkSuQmCC",
  });

  const batch = await callTool("perform_actions", { actions: [
    { method: "unity_action", name: action.name, params: [nonce] },
    { method: "set_button", name: nonce, pressed: false },
    { method: "capture_screen" },
  ] });
  const [echo, release, capture] = actionResults(batch);
  assert.equal(echo.returnValue, "player:" + nonce);
  assert.equal(release.returnValue, "release:" + nonce);
  assert.equal(capture.returnValue.imageBlock, 1);
  assert.deepEqual(batch.content[1], image);
}

async function callTool(name, args) {
  const result = await client.callTool({ name, arguments: args }, undefined,
    { timeout: 5_000, signal: startup.signal });
  assert.ok(!result.isError, JSON.stringify(result));
  return result;
}

function actionResults(result) {
  const [content] = result.content;
  assert.equal(content?.type, "text");
  const actions = JSON.parse(content.text.slice(content.text.indexOf("\n") + 1));
  assert.ok(actions.every(({ success }) => success));
  return actions;
}
