import assert from "node:assert/strict";
import test from "node:test";

import { PlayClient } from "../src/play-client.js";
import { EventLog } from "../src/play-events.js";
import { watchEvents } from "../src/play-watch.js";
import { parsePredicate } from "../src/play-predicate.js";
import { baseUnity, capabilitiesPayload, FakeUnity, SESSION, stampOf } from "./play-fake.js";

class FakeTimers {
  private next = 1;
  readonly pending = new Map<number, { callback: () => void; ms: number }>();
  setTimeout(callback: () => void, ms: number): ReturnType<typeof setTimeout> {
    const id = this.next++;
    this.pending.set(id, { callback, ms });
    return id as unknown as ReturnType<typeof setTimeout>;
  }
  clearTimeout(timer: ReturnType<typeof setTimeout>): void {
    this.pending.delete(timer as unknown as number);
  }
  /// 가장 최근 타이머를 실행한다.
  fireLast(): void {
    const [id, entry] = [...this.pending.entries()].at(-1) as [number, { callback: () => void }];
    this.pending.delete(id);
    entry.callback();
  }
}

async function until(condition: () => boolean): Promise<void> {
  for (let i = 0; i < 200 && !condition(); i++) await new Promise((resolve) => setImmediate(resolve));
  assert.ok(condition(), "condition was not reached");
}

function setup(unity: FakeUnity = baseUnity()) {
  const client = new PlayClient(unity);
  const log = new EventLog();
  const timers = new FakeTimers();
  const clock = { t: 0 };
  return { unity, client, log, timers, clock, deps: { client, log, timers, now: () => clock.t } };
}

test("with no event the wait ends at the timeout, returns a cursor and releases everything", async () => {
  const { deps, log, timers, clock } = setup();
  const waiting = watchEvents(deps, { timeoutMs: 1_000 }, new AbortController().signal);
  await until(() => timers.pending.size === 1);
  clock.t = 1_000;
  timers.fireLast();
  const { body, isError } = await waiting;
  assert.equal(isError, false);
  assert.equal(body.timedOut, true);
  assert.equal(body.events instanceof Array && body.events.length, 0);
  assert.equal(typeof body.cursor, "string");
  assert.equal(timers.pending.size, 0, "the timer is cleared");
  assert.equal(log.listenerCount, 0, "the subscription is released");
});

test("an event that arrives wakes the wait immediately", async () => {
  const { deps, log, timers } = setup();
  const waiting = watchEvents(deps, { timeoutMs: 10_000 }, new AbortController().signal);
  await until(() => timers.pending.size === 1);
  log.appendProvider({ kind: "goal_reached", providerId: "p", playerVisible: true });
  const { body } = await waiting;
  assert.equal(body.timedOut, false);
  assert.equal((body.events as Array<{ kind: string }>)[0]?.kind, "goal_reached");
  assert.equal(timers.pending.size, 0);
  assert.equal(log.listenerCount, 0);
});

test("events after a given cursor are returned at once", async () => {
  const { deps, log } = setup();
  await deps.client.capabilities();
  log.changeSession(SESSION);
  const start = log.head();
  log.appendProvider({ kind: "one", providerId: "p", playerVisible: true });
  log.appendProvider({ kind: "two", providerId: "p", playerVisible: true });
  const { body } = await watchEvents(deps, { afterCursor: start, limit: 1 }, new AbortController().signal);
  assert.equal((body.events as unknown[]).length, 1);
  assert.equal(body.truncated, true);
});

test("an overflowed cursor reports the gap instead of pretending nothing was lost", async () => {
  const unity = baseUnity();
  const client = new PlayClient(unity);
  const log = new EventLog("unbound", { capacity: 2 });
  await client.capabilities();
  log.changeSession(SESSION);
  const start = log.head();
  for (let i = 0; i < 5; i++) log.appendProvider({ kind: "e", providerId: "p", playerVisible: true });
  const { body } = await watchEvents({ client, log }, { afterCursor: start }, new AbortController().signal);
  assert.equal(body.gap, true);
  assert.equal(body.baselineRequired, true);
  assert.equal(body.partial, true);
  assert.ok(typeof body.oldestCursor === "string");
});

test("a cursor from before a session change is a gap with a new baseline", async () => {
  const { deps, log } = setup();
  await deps.client.capabilities();
  log.changeSession("older-session");
  const old = log.head();
  const { body } = await watchEvents(deps, { afterCursor: old }, new AbortController().signal);
  assert.equal(body.gap, true);
  assert.equal(body.baselineRequired, true);
});

test("cancellation ends the wait and cleans up", async () => {
  const { deps, log, timers } = setup();
  const controller = new AbortController();
  const waiting = watchEvents(deps, { timeoutMs: 10_000 }, controller.signal);
  await until(() => timers.pending.size === 1);
  controller.abort();
  const { body } = await waiting;
  assert.equal(body.cancelled, true);
  assert.equal(timers.pending.size, 0);
  assert.equal(log.listenerCount, 0);
});

test("provider events are pulled from Unity with provenance and hidden ones stay out of player scope", async () => {
  let served = false;
  const unity = baseUnity({
    play_capabilities: () => capabilitiesPayload({ providers: [{ id: "prov", status: "active" }] }),
    play_events: () => {
      if (served) return { events: [], next: 3 };
      served = true;
      return {
        next: 3,
        events: [
          { kind: "goal_reached", providerId: "prov", name: "goal", playerVisible: true, stamp: { frame: 77, scene: "Main" } },
          { kind: "secret_event", providerId: "prov", playerVisible: false },
        ],
      };
    },
  });
  const { deps } = setup(unity);
  const { body } = await watchEvents(deps, { scope: "player", timeoutMs: 1_000 }, new AbortController().signal);
  const events = body.events as Array<{ kind: string; provenance: string; stamp: { frame: number } }>;
  assert.deepEqual(events.map((event) => event.kind), ["goal_reached"]);
  assert.equal(events[0]?.provenance, "provider");
  assert.equal(events[0]?.stamp.frame, 77);
});

test("credential-like provider event data is redacted", async () => {
  const unity = baseUnity({
    play_capabilities: () => capabilitiesPayload({ providers: [{ id: "prov", status: "active" }] }),
    play_events: () => ({ next: 2, events: [{ kind: "login", providerId: "prov", playerVisible: true, data: { accessToken: "abc" } }] }),
  });
  const { deps } = setup(unity);
  const { body } = await watchEvents(deps, { timeoutMs: 1_000 }, new AbortController().signal);
  const text = JSON.stringify(body);
  assert.doesNotMatch(text, /"abc"/);
  assert.match(text, /redacted/);
});

test("predicates end the wait early when they become true", async () => {
  const unity = baseUnity({
    play_sample: () => ({ stamp: stampOf({ scene: "Next" }), targets: {}, members: [], facts: [] }),
  });
  const { deps } = setup(unity);
  const { body } = await watchEvents(
    deps, { predicates: [parsePredicate({ sceneIs: "Next" })], timeoutMs: 5_000 }, new AbortController().signal);
  assert.equal(body.predicatesMet, true);
  assert.equal(body.timedOut, false);
});

test("an unsupported Unity fails with unsupported_capability", async () => {
  const unity = new FakeUnity();
  const { body, isError } = await watchEvents(
    { client: new PlayClient(unity), log: new EventLog() }, {}, new AbortController().signal);
  assert.equal(isError, true);
  assert.equal((body.error as { code: string }).code, "unsupported_capability");
});
