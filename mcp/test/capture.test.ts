import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { ActionRequest, ActionResult, UnityConnection } from "../src/connection.js";
import { PulseStore, type PulseFrame } from "../src/pulse.js";
import { describeCapture, registerTools } from "../src/tools.js";

/// #71: 스크린샷 픽셀을 입력 좌표로 되돌릴 수 있는지, 그리고 그 스크린샷이 든 reading 과 같은 장면인지.

const IMAGE = "AQIDBA==";

function fullScreen(overrides: Record<string, unknown> = {}): Parameters<typeof describeCapture>[0] {
  return {
    mimeType: "image/jpeg" as const, width: 1024, height: 576, clipped: false,
    screen: { width: 1920, height: 1080 },
    region: { x: 0, y: 0, width: 1920, height: 1080 },
    scale: { x: 1024 / 1920, y: 576 / 1080 },
    frame: 5_000, scene: "GameScene", data: IMAGE, ...overrides,
  };
}

function toScreen(
  description: ReturnType<typeof describeCapture>,
  imageX: number,
  imageY: number,
): [number, number] {
  assert.ok(description.region !== undefined && description.scale !== undefined);
  return [
    description.region.x + imageX / description.scale.x,
    description.region.y + imageY / description.scale.y,
  ];
}

type CallToolHandler = (
  request: { method: string; params: { name: string; arguments: Record<string, unknown> } },
  extra: { signal: AbortSignal },
) => Promise<{ content: { type: string; text?: string; data?: string }[]; isError?: boolean }>;

async function captureThroughTool(returnValue: unknown, store = new PulseStore()) {
  const connection = {
    endpoint: "ws://127.0.0.1:17311/ws",
    isConnected: () => true,
    async ensureConnected(): Promise<void> {},
    async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
      return actions.map((action) => ({ id: action.id, success: true, returnValue: returnValue as never }));
    },
  } as unknown as UnityConnection;
  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  registerTools(server, connection, store);
  const handler = (server.server as unknown as {
    _requestHandlers?: Map<string, CallToolHandler>;
  })._requestHandlers?.get("tools/call");
  assert.ok(handler !== undefined, "server.server._requestHandlers no longer carries tools/call");
  return handler(
    { method: "tools/call", params: { name: "capture_screen", arguments: {} } },
    { signal: new AbortController().signal },
  );
}

function reading(scene: string, readingNumber: number, frame: number): PulseFrame {
  return {
    type: "PULSE", id: readingNumber, schema: 2, reading: readingNumber, frame, scene,
    statics: [], active: [], deactive: [], whole: true, watching: 1,
    unresolved: 0, unwatchable: 0, changed: [],
  };
}

test("a 1920x1080 screen captured at 1024x576 maps image pixels back to screen pixels", () => {
  const description = describeCapture(fullScreen(), undefined);

  assert.deepEqual(description.screen, { width: 1920, height: 1080 });
  assert.deepEqual(description.image, { width: 1024, height: 576, mimeType: "image/jpeg" });
  // 한가운데와 오른쪽 아래 구석.
  assert.deepEqual(toScreen(description, 512, 288).map(Math.round), [960, 540]);
  assert.deepEqual(toScreen(description, 1024, 576).map(Math.round), [1920, 1080]);
  assert.match(description.toScreen ?? "", /region\.x \+ imageX \/ scale\.x/);
});

test("a target crop carries its own origin, and a clipped crop says what the image leaves out", () => {
  const description = describeCapture({
    mimeType: "image/png", width: 60, height: 40, targetId: 7, clipped: true,
    screen: { width: 1920, height: 1080 },
    region: { x: 0, y: 500, width: 60, height: 40 },
    requestedRegion: { x: -20, y: 500, width: 80, height: 40 },
    scale: { x: 1, y: 1 }, frame: 10, scene: "GameScene", data: IMAGE,
  }, undefined);

  assert.deepEqual(toScreen(description, 0, 0), [0, 500]);
  assert.deepEqual(toScreen(description, 30, 20), [30, 520]);
  assert.deepEqual(description.requestedRegion, { x: -20, y: 500, width: 80, height: 40 });
  assert.match(description.toScreen ?? "", /only region, the on-screen part of requestedRegion/);
});

test("the same image size after a resolution change maps with a different scale", () => {
  const description = describeCapture(fullScreen({
    screen: { width: 1280, height: 720 },
    region: { x: 0, y: 0, width: 1280, height: 720 },
    scale: { x: 0.8, y: 0.8 },
  }), undefined);

  assert.deepEqual(toScreen(description, 512, 288).map(Math.round), [640, 360]);
});

test("a held reading from another scene is flagged instead of being attached as this screen's state", () => {
  const store = new PulseStore();
  store.fold(reading("LobbyScene", 203, 4_000));

  const description = describeCapture(fullScreen(), store.getState());

  assert.equal(description.reading?.sameScene, false);
  assert.match(description.reading?.relation ?? "", /"LobbyScene", but this image shows "GameScene"/);
  assert.match(description.reading?.relation ?? "", /Do not use that reading's ids/);
});

test("a held reading from the same scene says how many frames it trails the image", () => {
  const store = new PulseStore();
  store.fold(reading("GameScene", 12, 4_940));

  const description = describeCapture(fullScreen(), store.getState());

  assert.equal(description.reading?.sameScene, true);
  assert.equal(
    description.reading?.relation,
    "The held reading 12 was taken 60 frames before this image. Its rects and values describe frame 4940.",
  );
});

test("a same-scene reading far from the image is not offered as this image's layout", () => {
  const store = new PulseStore();
  store.fold(reading("GameScene", 3, 900));

  const description = describeCapture(fullScreen(), store.getState());

  assert.equal(description.reading?.sameScene, true);
  assert.match(description.reading?.relation ?? "", /4100 frames before this image/);
  assert.match(description.reading?.relation ?? "", /read the scene again before aiming/);
});

test("a reading whose frame is ahead of the image, as after a Play Mode restart, is flagged", () => {
  const store = new PulseStore();
  store.fold(reading("GameScene", 203, 146_536));

  const description = describeCapture(fullScreen(), store.getState());

  assert.match(description.reading?.relation ?? "", /frames after this image/);
  assert.match(description.reading?.relation ?? "", /read the scene again before aiming/);
});

test("capture_screen keeps the image block and adds the description after the old first line", async () => {
  const store = new PulseStore();
  store.fold(reading("GameScene", 12, 4_940));

  const result = await captureThroughTool(fullScreen(), store);

  assert.equal(result.isError, undefined);
  assert.equal(result.content[0]?.type, "image");
  assert.equal(result.content[0]?.data, IMAGE);
  const [firstLine, ...rest] = (result.content[1]?.text ?? "").split("\n");
  assert.equal(firstLine, "1024x576; clipped=false");
  const parsed = JSON.parse(rest.join("\n")) as ReturnType<typeof describeCapture>;
  assert.equal(parsed.frame, 5_000);
  assert.equal(parsed.scene, "GameScene");
  assert.equal(parsed.reading?.reading, 12);
});

test("a 0.2.x package capture without metadata still returns the image and says what is missing", async () => {
  const result = await captureThroughTool({
    mimeType: "image/jpeg", width: 1024, height: 576, clipped: false, data: IMAGE,
  });

  assert.equal(result.isError, undefined);
  assert.equal(result.content[0]?.type, "image");
  assert.match(result.content[1]?.text ?? "", /does not report the capture's screen size/);
});

test("a stale held reading carries its stale marker next to the capture", async () => {
  const store = new PulseStore();
  store.fold(reading("GameScene", 12, 4_940));
  store.markInterrupted("disconnected");

  const result = await captureThroughTool(fullScreen(), store);
  const parsed = JSON.parse((result.content[1]?.text ?? "").split("\n").slice(1).join("\n")) as {
    reading?: { stale?: { reason: string } };
  };

  assert.equal(parsed.reading?.stale?.reason, "disconnected");
});
