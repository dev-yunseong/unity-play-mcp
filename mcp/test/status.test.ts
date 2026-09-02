import assert from "node:assert/strict";
import test from "node:test";

import { UnityUnreachableError } from "../src/connection.js";
import { PulseStore, type PulseFrame } from "../src/pulse.js";
import { describeStatus, failureText } from "../src/tools.js";

const ENDPOINT = "ws://127.0.0.1:17311/ws";

/// 이 두 문장이 갈리는 것이 이 기능 전체다. agent 는 문장만 읽고 재시도할지 사용자에게 말할지를
/// 정하므로, 둘이 같은 말로 수렴하면 기능이 없는 것과 같다.
test("a connection failure says the game is not running, not what was attempted", () => {
  const message = failureText("Unity action failed", new UnityUnreachableError(ENDPOINT));

  assert.match(message, /Unity is not running/);
  assert.match(message, /Play Mode/);
  assert.doesNotMatch(message, /Unity action failed/);
});

test("a failure after reaching Unity keeps what was attempted", () => {
  const message = failureText("Screenshot failed", new Error("the target has no renderer"));

  assert.match(message, /Screenshot failed: the target has no renderer/);
  assert.doesNotMatch(message, /Unity is not running/);
});

test("a non-Error failure still reads as text", () => {
  assert.match(failureText("Unity action failed", "socket hung up"), /socket hung up/);
});

test("status says the game is not running when nothing is connected", () => {
  const described = describeStatus({ connected: false, endpoint: ENDPOINT, now: 1_000 });

  assert.match(described, /Unity is not running/);
  assert.match(described, /Play Mode/);
  assert.ok(described.includes(ENDPOINT));
});

test("status separates connected-but-no-readings from connected-and-reading", () => {
  const idle = describeStatus({ connected: true, endpoint: ENDPOINT, now: 1_000 });

  assert.match(idle, /Unity is running/);
  assert.match(idle, /No scene reading has arrived/);
  assert.match(idle, /start_readings/);
});

test("status reports the latest reading and how long ago it arrived", () => {
  const described = describeStatus({
    connected: true,
    endpoint: ENDPOINT,
    reading: 42,
    frame: 900,
    scene: "Main",
    lastReadingAt: 10_000,
    now: 13_000,
  });

  assert.match(described, /reading 42 on frame 900 arrived 3s ago/);
  assert.match(described, /Scene: Main/);
});

test("a reading that just arrived does not read as zero seconds ago", () => {
  const described = describeStatus({
    connected: true,
    endpoint: ENDPOINT,
    reading: 1,
    frame: 2,
    scene: "Main",
    lastReadingAt: 10_000,
    now: 10_100,
  });

  assert.match(described, /arrived just now/);
});

test("the store remembers when a reading arrived, even one that changed nothing", () => {
  let clock = 5_000;
  const store = new PulseStore(() => clock);

  assert.equal(store.getLastReadingAt(), undefined);

  const frame: PulseFrame = {
    type: "PULSE",
    id: 1,
    schema: 2,
    reading: 1,
    frame: 10,
    scene: "Main",
    whole: true,
    active: [],
    deactive: [],
    statics: [],
    gone: [],
  } as unknown as PulseFrame;

  store.fold(frame);
  assert.equal(store.getLastReadingAt(), 5_000);

  // 같은 판독 번호가 다시 와도 상태는 안 바뀌지만, 도착했다는 사실은 게임이 살아 있다는 증거다.
  clock = 6_000;
  store.fold(frame);
  assert.equal(store.getLastReadingAt(), 6_000);
});
