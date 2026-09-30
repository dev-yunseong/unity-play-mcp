import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import { UnityConnection } from "../src/connection.js";
import { PulseStore, type PulseFrame } from "../src/pulse.js";
import { describeStatus, registerTools } from "../src/tools.js";
import { waitForCondition } from "../src/wait.js";

/// #69 의 회귀 test.
///
/// Unity 쪽 `Pulse` 는 `Begin` 마다 `reading` 을 1부터 다시 센다 (`Pulse._reading` 은 인스턴스
/// 필드다). `stop_readings`→`start_readings`, Play Mode 재진입, reload 뒤 재시작이 모두 그렇다.
/// 그 run 의 첫 reading 과 장면이 바뀐 reading 은 `whole` 이다. 이 파일의 가짜 게임은 그 규칙을
/// 그대로 따르고, 실제 `UnityConnection`·`PulseStore`·tool handler 를 거쳐 agent 가 보는 답을 본다.

class FakeSocket extends EventEmitter {
  readyState = 0;

  open(): void {
    this.readyState = 1;
    this.emit("open");
  }

  send(data: string): void {
    const frame = JSON.parse(data) as { id: number; actions: { id: number; method: string }[] };
    this.emit("sent", frame);
  }

  push(frame: unknown): void {
    this.emit("message", JSON.stringify(frame));
  }

  close(): void {
    this.readyState = 3;
    this.emit("close");
  }
}

interface GameRun {
  id?: string;
  reading: number;
  scene?: string;
}

/// package 의 번호 규칙을 흉내 내는 게임. `runIds` 가 참이면 0.3.0 package 처럼 run 을 싣는다.
class FakeGame {
  readonly sockets: FakeSocket[] = [];
  private run?: GameRun;
  private runCount = 0;
  private frameCount = 146_000;
  private messageId = 1;

  constructor(private readonly runIds: boolean) {}

  get socket(): FakeSocket {
    const socket = this.sockets.at(-1);
    assert.ok(socket !== undefined);
    return socket;
  }

  connect(): FakeSocket {
    const socket = new FakeSocket();
    this.sockets.push(socket);
    socket.on("sent", (frame: { id: number; actions: { id: number; method: string }[] }) => {
      for (const action of frame.actions) {
        if (action.method === "start_readings" && this.run === undefined) this.begin();
        if (action.method === "stop_readings") this.run = undefined;
      }
      queueMicrotask(() => socket.push({
        type: "ACTION_RESULT", id: this.messageId++, requestId: frame.id, frame: this.frameCount,
        results: frame.actions.map((action) => ({ id: action.id, success: true })),
      }));
    });
    return socket;
  }

  /// Play Mode 를 나갔다 들어온 것처럼 reading 을 멈춘다. 다음 `start_readings` 가 새 run 을 연다.
  endRun(): void {
    this.run = undefined;
  }

  take(scene: string, overrides: Partial<PulseFrame> = {}): void {
    const run = this.run;
    assert.ok(run !== undefined, "readings are not running");
    run.reading += 1;
    this.frameCount += 60;
    const whole = run.scene !== scene;
    run.scene = scene;
    this.socket.push({
      type: "PULSE", id: this.messageId++, schema: 2,
      ...(run.id === undefined ? {} : { run: run.id }),
      reading: run.reading, frame: this.frameCount, scene,
      statics: [], deactive: [], whole, watching: 1, unresolved: 0, unwatchable: 0, changed: [],
      active: [{
        id: 1000 + scene.length, path: "Canvas/Label", selector: "Canvas[0]/Label[0]", scene,
        by: [{ on: "UnityEngine.UI.Text", m: [{ member: "text", value: `${scene} label` }] }],
      }],
      ...overrides,
    });
  }

  private begin(): void {
    this.runCount += 1;
    this.run = { reading: 0, ...(this.runIds ? { id: `run-${this.runCount}` } : {}) };
  }
}

type CallToolHandler = (
  request: { method: string; params: { name: string; arguments: Record<string, unknown> } },
  extra: { signal: AbortSignal },
) => Promise<{ content: { type: string; text?: string }[]; isError?: boolean }>;

function harness(runIds: boolean) {
  const game = new FakeGame(runIds);
  let now = Date.parse("2026-09-29T07:00:00Z");
  const clock = () => now;
  const store = new PulseStore(clock);
  const reconnects: (() => void)[] = [];
  const connection = new UnityConnection({
    url: "ws://127.0.0.1:17311/ws",
    pulseStore: store,
    createWebSocket: () => {
      const socket = game.connect();
      queueMicrotask(() => socket.open());
      return socket;
    },
    timers: {
      setTimeout: (callback: () => void) => {
        reconnects.push(callback);
        return reconnects.length as unknown as ReturnType<typeof setTimeout>;
      },
      clearTimeout: () => undefined,
    },
  });
  const server = new McpServer({ name: "unity-play-mcp-test", version: "0" });
  registerTools(server, connection, store);
  const handler = (server.server as unknown as {
    _requestHandlers?: Map<string, CallToolHandler>;
  })._requestHandlers?.get("tools/call");
  assert.ok(handler !== undefined, "server.server._requestHandlers no longer carries tools/call");

  const call = async (name: string, args: Record<string, unknown> = {}) => {
    const result = await handler(
      { method: "tools/call", params: { name, arguments: args } },
      { signal: new AbortController().signal },
    );
    return result.content.map((item) => item.text ?? "").join("\n");
  };
  const sceneState = async () => JSON.parse(await call("get_scene_state")) as {
    stale?: { reason: string; message: string; lastReadingAt?: string; since?: string };
    reading: number;
    scene: string;
    active: { by: { members: { value: string }[] }[] }[];
  };
  const advance = (milliseconds: number) => { now += milliseconds; };
  const reconnect = () => reconnects.shift()?.();
  return { game, store, connection, call, sceneState, advance, reconnect };
}

for (const runIds of [false, true]) {
  const flavor = runIds ? "a package that sends run ids" : "a 0.2.x package without run ids";

  test(`scene changes inside one run follow the screen (${flavor})`, async () => {
    const { game, call, sceneState, connection } = harness(runIds);
    await call("start_readings");
    for (let reading = 0; reading < 203; reading++) game.take("LobbyScene");

    for (const scene of ["GameScene", "ResultScene", "LobbyScene"]) {
      game.take(scene);
      const state = await sceneState();
      assert.equal(state.scene, scene);
      assert.equal(state.active[0]?.by[0]?.members[0]?.value, `${scene} label`);
      assert.equal(state.stale, undefined);
    }
    connection.close();
  });

  test(`stop_readings then start_readings recovers the current scene (${flavor})`, async () => {
    const { game, call, sceneState, connection, store } = harness(runIds);
    await call("start_readings");
    for (let reading = 0; reading < 203; reading++) game.take("LobbyScene");

    await call("stop_readings");
    const stopped = await sceneState();
    assert.equal(stopped.stale?.reason, "stopped");
    assert.match(await call("get_unity_status"), /Readings are stopped/);

    await call("start_readings");
    game.take("GameScene");
    const state = await sceneState();
    assert.equal(state.reading, 1);
    assert.equal(state.scene, "GameScene");
    assert.equal(state.active[0]?.by[0]?.members[0]?.value, "GameScene label");
    assert.equal(state.stale, undefined);
    // 이전 run 의 이력은 새 run 의 번호와 섞이지 않는다.
    assert.deepEqual(
      [...store.getObjectHistory("LobbyScene/Canvas[0]/Label[0]").keys()],
      [],
    );
    connection.close();
  });

  test(`a reconnect into a new run recovers the current scene (${flavor})`, async () => {
    const { game, call, sceneState, connection, reconnect } = harness(runIds);
    await call("start_readings");
    for (let reading = 0; reading < 203; reading++) game.take("LobbyScene");

    // Play Mode 재진입: 소켓이 끊기고, Unity 는 reading 을 멈춘 채 다시 뜬다.
    game.socket.close();
    game.endRun();
    const dropped = await sceneState();
    assert.equal(dropped.stale?.reason, "disconnected");
    assert.equal(dropped.scene, "LobbyScene");

    reconnect();
    await new Promise((resolve) => setImmediate(resolve));
    await call("start_readings");
    game.take("ResultScene");
    const state = await sceneState();
    assert.equal(state.scene, "ResultScene");
    assert.equal(state.reading, 1);
    assert.equal(state.stale, undefined);
    connection.close();
  });
}

test("a new run's delta cannot be folded onto the previous run and marks the state stale", async () => {
  const { game, call, sceneState, connection, advance } = harness(true);
  await call("start_readings");
  for (let reading = 0; reading < 5; reading++) game.take("LobbyScene");
  advance(4_000);

  await call("stop_readings");
  await call("start_readings");
  advance(1_000);
  // 전량 reading 이 오지 않고 차이만 온 경우. 그 위에 얹으면 이전 장면의 객체가 새 run 의 것처럼 남는다.
  game.take("GameScene", { whole: false });

  const state = await sceneState();
  assert.equal(state.scene, "LobbyScene");
  assert.equal(state.stale?.reason, "restarted");
  assert.equal(state.stale?.lastReadingAt, "2026-09-29T07:00:00.000Z");
  assert.match(state.stale?.message ?? "", /previous run/);
  assert.match(await call("get_visible_elements"), /"stale"/);
  assert.match(await call("search_targets", { name: "Label" }), /"stale"/);
  connection.close();
});

test("a reading the store does not apply leaves the arrival time alone", () => {
  let now = 1_000;
  const store = new PulseStore(() => now);
  const frame = (reading: number, scene: string): PulseFrame => ({
    type: "PULSE", id: reading, schema: 2, reading, frame: reading * 10, scene,
    statics: [], active: [], deactive: [], whole: reading === 1, watching: 1,
    unresolved: 0, unwatchable: 0, changed: [],
  });
  store.fold({ ...frame(1, "Lobby"), run: "a" });
  store.fold({ ...frame(2, "Lobby"), run: "a" });

  now = 9_000;
  // 같은 run 에서 이미 지나간 번호.
  assert.equal(store.fold({ ...frame(2, "Game"), run: "a" }), false);
  // 다른 run 의 차이.
  assert.equal(store.fold({ ...frame(7, "Game"), whole: false, run: "b" }), false);

  assert.equal(store.getState()?.reading, 2);
  assert.equal(store.getLastReadingAt(), 1_000);
  const status = describeStatus({
    connected: true, endpoint: "ws://127.0.0.1:17311/ws", reading: 2, frame: 20, scene: "Lobby",
    lastReadingAt: store.getLastReadingAt(), staleness: store.getStaleness(), now,
  });
  assert.match(status, /reading 2 on frame 20 arrived 8s ago/);
  assert.match(status, /stale \(restarted just now\)/);
});

test("wait_for_condition does not accept a stale state and treats a new run as newer", async () => {
  const store = new PulseStore();
  const connection = { onDisconnect: () => () => undefined };
  const frame = (reading: number, scene: string, run: string): PulseFrame => ({
    type: "PULSE", id: reading, schema: 2, run, reading, frame: reading * 10, scene,
    statics: [], active: [], deactive: [], whole: true, watching: 1,
    unresolved: 0, unwatchable: 0, changed: [],
  });
  store.fold(frame(203, "LobbyScene", "a"));
  store.markInterrupted("disconnected");

  const waiting = waitForCondition(
    store, connection, { sinceReading: 203, scene: "GameScene", timeoutMilliseconds: 1_000 },
    new AbortController().signal,
  );
  store.fold(frame(1, "GameScene", "b"));
  const outcome = await waiting;
  assert.equal(outcome.kind, "met");

  store.markInterrupted("disconnected");
  const stale = await waitForCondition(
    store, connection, { scene: "GameScene", timeoutMilliseconds: 1 },
    new AbortController().signal,
  );
  assert.equal(stale.kind, "timeout");
  assert.deepEqual(stale.kind === "timeout" ? stale.unmet : [], [
    "the held reading is stale; no fresh reading arrived before the timeout",
  ]);
});

/// 재연결 뒤에도 Unity 가 끊긴 동안 쌓아 둔 차이가 전량 reading 보다 먼저 온다. 그 차이는 놓친 차이를 채우지
/// 못하므로 상태를 새것으로 되돌리지 않는다.
test("only a whole reading clears an interruption; a leftover delta keeps the state stale", () => {
  const store = new PulseStore();
  const frame = (reading: number, whole: boolean): PulseFrame => ({
    type: "PULSE", id: reading, schema: 2, run: "a", reading, frame: reading * 10, scene: "Lobby",
    statics: [], active: [], deactive: [], whole, watching: 1,
    unresolved: 0, unwatchable: 0, changed: [],
  });
  store.fold(frame(1, true));
  store.markInterrupted("disconnected");

  assert.equal(store.fold(frame(9, false)), true);
  assert.equal(store.getState()?.reading, 9);
  assert.equal(store.getStaleness()?.reason, "disconnected");

  store.fold(frame(10, true));
  assert.equal(store.getStaleness(), undefined);
});

test("a reconnect into a run that kept going stays stale until the requested whole reading", async () => {
  const { game, call, sceneState, connection, reconnect } = harness(true);
  await call("start_readings");
  game.take("LobbyScene");
  game.take("LobbyScene");

  game.socket.close();
  reconnect();
  await new Promise((resolve) => setImmediate(resolve));

  // 끊긴 동안 쌓인 차이가 먼저 온다.
  game.take("LobbyScene");
  const leftover = await sceneState();
  assert.equal(leftover.reading, 3);
  assert.equal(leftover.stale?.reason, "disconnected");

  // 새 client 를 본 package 가 청한 전량 reading.
  game.take("LobbyScene", { whole: true });
  const recovered = await sceneState();
  assert.equal(recovered.reading, 4);
  assert.equal(recovered.stale, undefined);
  connection.close();
});

test("read tools name the run their reading numbers belong to", async () => {
  const { game, call, connection } = harness(true);
  await call("start_readings");
  game.take("LobbyScene");

  assert.equal((JSON.parse(await call("get_scene_state")) as { run?: string }).run, "run-1");
  assert.equal((JSON.parse(await call("get_scene_state", { depth: 1 })) as { run?: string }).run, "run-1");
  connection.close();
});

test("wait_for_condition treats a reading of a different run than sinceRun as newer", async () => {
  const store = new PulseStore();
  const connection = { onDisconnect: () => () => undefined };
  store.fold({
    type: "PULSE", id: 1, schema: 2, run: "b", reading: 5, frame: 50, scene: "GameScene",
    statics: [], active: [], deactive: [], whole: true, watching: 1,
    unresolved: 0, unwatchable: 0, changed: [],
  });

  const outcome = await waitForCondition(
    store, connection, { sinceRun: "a", sinceReading: 203, timeoutMilliseconds: 1 },
    new AbortController().signal,
  );
  assert.equal(outcome.kind, "met");

  const sameRun = await waitForCondition(
    store, connection, { sinceRun: "b", sinceReading: 203, timeoutMilliseconds: 1 },
    new AbortController().signal,
  );
  assert.equal(sameRun.kind, "timeout");
});
