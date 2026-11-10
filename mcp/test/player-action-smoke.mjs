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
  console.log("IL2CPP player listed and executed the private game action through MCP.");
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

  const nonce = randomUUID();
  const result = await client.callTool({
    name: "perform_actions",
    arguments: { actions: [{ method: "unity_action", name: action.name, params: [nonce] }] },
  }, undefined, { timeout: 5_000, signal: startup.signal });
  assert.ok(!result.isError, JSON.stringify(result));
  const [content] = result.content;
  assert.equal(content?.type, "text");
  const [echo] = JSON.parse(content.text.slice(content.text.indexOf("\n") + 1));
  assert.equal(echo.success, true);
  assert.equal(echo.returnValue, "player:" + nonce);
}
