import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";

import { UnityConnection } from "../src/connection.js";

class FakeSocket extends EventEmitter {
  readyState = 0;
  readonly sent: string[] = [];

  open(): void {
    this.readyState = 1;
    this.emit("open");
  }

  message(frame: unknown): void {
    this.emit("message", JSON.stringify(frame));
  }

  send(data: string): void {
    if (this.readyState !== 1) {
      throw new Error("socket is not open");
    }
    this.sent.push(data);
  }

  close(): void {
    this.readyState = 3;
    this.emit("close");
  }
}

class FakeTimers {
  private nextId = 1;
  private readonly callbacks = new Map<number, () => void>();
  readonly delays: number[] = [];

  setTimeout(callback: () => void, delayMilliseconds: number): ReturnType<typeof setTimeout> {
    const id = this.nextId++;
    this.callbacks.set(id, callback);
    this.delays.push(delayMilliseconds);
    return id as unknown as ReturnType<typeof setTimeout>;
  }

  clearTimeout(timer: ReturnType<typeof setTimeout>): void {
    this.callbacks.delete(timer as unknown as number);
  }

  runNext(): void {
    const entry = this.callbacks.entries().next().value as [number, () => void] | undefined;
    assert.ok(entry);
    this.callbacks.delete(entry[0]);
    entry[1]();
  }

  get size(): number {
    return this.callbacks.size;
  }
}

function createFixture() {
  const sockets: FakeSocket[] = [];
  const timers = new FakeTimers();
  const folded: unknown[] = [];
  const reports: string[] = [];
  const connection = new UnityConnection({
    pulseStore: { fold: (frame: unknown) => { folded.push(frame); } } as never,
    createWebSocket: () => {
      const socket = new FakeSocket();
      sockets.push(socket);
      return socket;
    },
    timers,
    timeoutMilliseconds: 1_000,
    reconnectBaseMilliseconds: 10,
    report: (message) => reports.push(message),
  });
  return { connection, folded, reports, sockets, timers };
}

test("connects lazily once and correlates only by requestId", async () => {
  const fixture = createFixture();
  assert.equal(fixture.sockets.length, 0);

  const first = fixture.connection.sendActions([{ id: 7, method: "button_click", params: [12] }]);
  const second = fixture.connection.sendActions([{ id: 8, method: "enter_text", params: [13, "hello"] }]);
  assert.equal(fixture.sockets.length, 1);
  fixture.sockets[0].open();
  await Promise.resolve();

  const firstWire = JSON.parse(fixture.sockets[0].sent[0]);
  const secondWire = JSON.parse(fixture.sockets[0].sent[1]);
  assert.deepEqual(firstWire, {
    type: "ACTION", id: 1, actions: [{ id: 7, method: "button_click", params: [12] }],
  });
  fixture.sockets[0].message({
    type: "ACTION_RESULT", id: firstWire.id, requestId: secondWire.id, frame: 8, results: [],
  });
  assert.deepEqual(await second, []);
  fixture.sockets[0].message({
    type: "ACTION_RESULT", id: 999, requestId: firstWire.id, frame: 9, results: [],
  });
  assert.deepEqual(await first, []);
  fixture.connection.close();
});

test("routes push frames and safely ignores malformed, unmatched, and duplicate frames", async () => {
  const fixture = createFixture();
  const result = fixture.connection.sendActions([{ id: 1, method: "pause_time", params: [] }]);
  fixture.sockets[0].open();
  await Promise.resolve();

  fixture.sockets[0].emit("message", "{");
  fixture.sockets[0].message({ type: "PULSE", reading: 1 });
  fixture.sockets[0].message({ type: "PERFORMANCE", id: 2, frame: 1 });
  fixture.sockets[0].message({ type: "DEVICE_CONTEXT", id: 3, platform: "Editor" });
  fixture.sockets[0].message({ type: "ACTION_RESULT", id: 1, requestId: 42, frame: 1, results: [] });
  fixture.sockets[0].message({ type: "ACTION_RESULT", id: 2, requestId: 1, frame: 2, results: [] });
  fixture.sockets[0].message({ type: "ACTION_RESULT", id: 3, requestId: 1, frame: 3, results: [] });

  await result;
  assert.deepEqual(fixture.folded.map((frame) => (frame as { type: string }).type), [
    "PERFORMANCE", "DEVICE_CONTEXT",
  ]);
  assert.equal(fixture.reports.length, 4);
  fixture.connection.close();
});

test("cleans up timeout and rejects pending actions on disconnect without resending", async () => {
  const fixture = createFixture();
  const pending = fixture.connection.sendActions([{ id: 1, method: "resume_time", params: [] }]);
  fixture.sockets[0].open();
  await Promise.resolve();
  assert.equal(fixture.timers.size, 1);

  fixture.sockets[0].close();
  await assert.rejects(pending, /closed/);
  assert.equal(fixture.timers.size, 1);

  fixture.timers.runNext();
  assert.equal(fixture.sockets.length, 2);
  fixture.sockets[1].open();
  assert.equal(fixture.sockets[1].sent.length, 0);
  fixture.connection.close();
});

test("rejects a timed out action and removes its pending correlation", async () => {
  const fixture = createFixture();
  const pending = fixture.connection.sendActions([{ id: 1, method: "stop_readings", params: [] }]);
  fixture.sockets[0].open();
  await Promise.resolve();

  fixture.timers.runNext();
  await assert.rejects(pending, /timed out/);
  fixture.sockets[0].message({ type: "ACTION_RESULT", id: 4, requestId: 1, frame: 5, results: [] });
  assert.match(fixture.reports.at(-1) ?? "", /unmatched or duplicate/);
  fixture.connection.close();
});

test("uses single-flight exponential reconnect and resets backoff after success", async () => {
  const fixture = createFixture();
  const pending = fixture.connection.sendActions([{ id: 1, method: "pause_time", params: [] }]);
  fixture.sockets[0].open();
  await Promise.resolve();
  fixture.sockets[0].close();
  await assert.rejects(pending);
  assert.equal(fixture.timers.delays.at(-1), 10);

  fixture.timers.runNext();
  fixture.sockets[1].emit("error", new Error("offline"));
  await Promise.resolve();
  assert.equal(fixture.timers.delays.at(-1), 20);

  fixture.timers.runNext();
  fixture.sockets[2].open();
  fixture.sockets[2].close();
  assert.equal(fixture.timers.delays.at(-1), 10);
  fixture.connection.close();
});
