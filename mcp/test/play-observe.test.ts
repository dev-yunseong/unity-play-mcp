import assert from "node:assert/strict";
import test from "node:test";

import { PlayClient } from "../src/play-client.js";
import { PlayObserver } from "../src/play-observe.js";
import { baseUnity, capabilitiesPayload, entity, FakeUnity, SESSION, stampOf } from "./play-fake.js";

function observation(overrides: Record<string, unknown> = {}) {
  return { stamp: stampOf(), entities: [entity(1), entity(2)], coherent: true, inputRevision: 1, ...overrides };
}

function setup(handler: (params: unknown[]) => unknown, mono = { t: 0 }) {
  const unity = baseUnity({ play_observe: handler });
  const client = new PlayClient(unity);
  const observer = new PlayObserver(client, { mono: () => mono.t }, () => "cursor-head");
  return { unity, client, observer, mono };
}

test("every current observe samples Unity again and returns a fresh stamp even when nothing changed", async () => {
  let frame = 100;
  const { unity, observer } = setup(() => observation({ stamp: stampOf({ frame: frame += 5 }) }));
  const first = await observer.observe({});
  const second = await observer.observe({});
  assert.equal(unity.count("play_observe"), 2);
  assert.ok(first.ok && second.ok);
  const [a, b] = [first.body.stamp as { frame: number }, second.body.stamp as { frame: number }];
  assert.ok(b.frame > a.frame, "a new sample stamp is returned");
  const freshness = second.body.freshness as { current: boolean; lastChangeFrame: number };
  assert.equal(freshness.current, true);
  assert.equal(freshness.lastChangeFrame, a.frame, "the scene did not change, so lastChangeFrame stays");
});

test("cached freshness serves an earlier observation only within maxAgeMs and says so", async () => {
  const mono = { t: 0 };
  const { unity, observer } = setup(() => observation(), mono);
  await observer.observe({});
  mono.t = 500;
  const cached = await observer.observe({ freshness: "cached", maxAgeMs: 1000 });
  assert.equal(unity.count("play_observe"), 1);
  assert.equal((cached.body.freshness as { current: boolean }).current, false);
  mono.t = 5_000;
  await observer.observe({ freshness: "cached", maxAgeMs: 1000 });
  assert.equal(unity.count("play_observe"), 2, "too old: sampled again");
});

test("an unsupported Unity package fails clearly and leaves the caller able to use other tools", async () => {
  const unity = new FakeUnity();
  const observer = new PlayObserver(new PlayClient(unity));
  const result = await observer.observe({});
  assert.equal(result.ok, false);
  assert.equal((result.body.error as { code: string }).code, "unsupported_capability");
});

test("a protocol version mismatch is unsupported_capability with both versions", async () => {
  const unity = new FakeUnity({ play_capabilities: () => capabilitiesPayload({ protocolVersion: 99 }) });
  const result = await new PlayObserver(new PlayClient(unity)).observe({});
  assert.equal(result.ok, false);
  const error = result.body.error as { code: string; unityProtocolVersion?: number };
  assert.equal(error.code, "unsupported_capability");
  assert.equal(error.unityProtocolVersion, 99);
});

test("a Unity timeout is reported as a missing answer, not as no change", async () => {
  const { observer } = setup(() => { throw new Error("ACTION 5 timed out after 100ms"); });
  const result = await observer.observe({ timeoutMs: 100 });
  assert.equal(result.ok, false);
  const error = result.body.error as { code: string; message: string };
  assert.equal(error.code, "timeout");
  assert.match(error.message, /not evidence that nothing changed/);
});

test("the per-call timeout reaches the transport", async () => {
  const { unity, observer } = setup(() => observation());
  await observer.observe({ timeoutMs: 1234 });
  assert.equal(unity.calls.find((call) => call.method === "play_observe")?.timeoutMilliseconds, 1234);
});

test("since diff reports added, changed and gone with distinct lifecycles", async () => {
  let call = 0;
  const { observer } = setup((params) => {
    call++;
    if (call === 1) return observation({ entities: [entity(1), entity(2)] });
    const track = (params[0] as { track: unknown[] }).track;
    assert.equal(track.length, 2, "the previous refs are tracked so gone entities can be classified");
    return observation({
      stamp: stampOf({ frame: 110 }),
      entities: [entity(1, { state: { interactable: false } }), entity(3)],
      lifecycles: { [`${SESSION}:2:1`]: "out_of_scope" },
    });
  });
  const first = await observer.observe({});
  assert.ok(first.ok);
  const second = await observer.observe({ sinceObservationId: first.body.observationId as string });
  const changes = second.body.changes as { added: unknown[]; changed: Array<{ fields: string[] }>; gone: Array<{ lifecycle: string }>; baseline: boolean };
  assert.equal(changes.baseline, false);
  assert.equal(changes.added.length, 1);
  assert.deepEqual(changes.changed[0]?.fields, ["state"]);
  assert.equal(changes.gone[0]?.lifecycle, "out_of_scope", "leaving the filter is not destruction");
});

test("an unknown since cursor returns baseline_required with a fresh baseline", async () => {
  const { observer } = setup(() => observation());
  const result = await observer.observe({ sinceObservationId: "obs-nope" });
  const changes = result.body.changes as { baselineRequired: boolean; baseline: boolean };
  assert.equal(changes.baselineRequired, true);
  assert.ok(Array.isArray(result.body.entities));
});

test("a scene change between observations is a new baseline, not a cross-scene diff", async () => {
  let call = 0;
  const { observer } = setup(() => {
    call++;
    return observation({ stamp: stampOf({ scene: call === 1 ? "A" : "B" }) });
  });
  const first = await observer.observe({});
  const second = await observer.observe({ sinceObservationId: first.body.observationId as string });
  const changes = second.body.changes as { reason: string };
  assert.equal(changes.reason, "scene_changed");
});

test("a changed Play session is a new baseline", async () => {
  let call = 0;
  const { observer } = setup(() => {
    call++;
    return observation({ stamp: stampOf({ sessionId: call === 1 ? "s1" : "s2" }) });
  });
  const first = await observer.observe({});
  const second = await observer.observe({ sinceObservationId: first.body.observationId as string });
  assert.equal((second.body.changes as { reason: string }).reason, "session_changed");
});

test("entity limits paginate against the same snapshot and the continuation expires after 30s", async () => {
  const mono = { t: 0 };
  let calls = 0;
  const { observer } = setup(() => { calls++; return observation({ entities: [1, 2, 3, 4, 5].map((id) => entity(id)) }); }, mono);
  const first = await observer.observe({ limits: { entities: 2 } });
  assert.ok(first.ok);
  assert.equal((first.body.entities as unknown[]).length, 2);
  assert.equal(first.body.partial, true);
  assert.equal((first.body.omittedCounts as { entities: number }).entities, 3);
  const token = first.body.continuation as string;

  const second = await observer.observe({ continuation: token });
  assert.ok(second.ok);
  assert.equal(second.body.observationId, first.body.observationId, "the same snapshot is read");
  assert.equal((second.body.entities as unknown[]).length, 2);
  assert.equal(calls, 1, "pagination never re-samples");

  const third = await observer.observe({ continuation: second.body.continuation as string });
  assert.equal((third.body.entities as unknown[]).length, 1);
  assert.equal(third.body.partial, false);

  const again = await observer.observe({ limits: { entities: 1 } });
  mono.t = 31_000;
  const expired = await observer.observe({ continuation: again.body.continuation as string });
  assert.equal(expired.ok, false);
  assert.equal((expired.body.error as { code: string }).code, "continuation_expired");
});

test("the text byte budget truncates and says so, always keeping the first entity", async () => {
  const big = (id: number) => entity(id, { label: "x".repeat(2_000) });
  const { observer } = setup(() => observation({ entities: [big(1), big(2), big(3)] }));
  const result = await observer.observe({ limits: { textBytes: 3_000 } });
  assert.ok(result.ok);
  assert.equal((result.body.entities as unknown[]).length, 1);
  assert.equal(result.body.partial, true);
});

test("factsPerEntity is enforced even if Unity sent more", async () => {
  const facts = Array.from({ length: 10 }, (_, index) => ({ name: `f${index}`, value: index, status: "known", source: "runtime", evidence: {} }));
  const { observer } = setup(() => observation({ entities: [entity(1, { facts })] }));
  const result = await observer.observe({ limits: { factsPerEntity: 3 } });
  assert.ok(result.ok);
  const first = (result.body.entities as Array<{ facts: unknown[]; factsOmitted: number }>)[0];
  assert.equal(first?.facts.length, 3);
  assert.equal(first?.factsOmitted, 7);
});

test("credential-like fact values are redacted and unknown facts stay unknown", async () => {
  const facts = [
    { name: "AccessToken", value: "abcdef", status: "known", source: "runtime", evidence: {} },
    { name: "cost", status: "unknown", source: "analysis", evidence: {}, reason: "no evidence" },
  ];
  const { observer } = setup(() => observation({ entities: [entity(1, { facts })] }));
  const result = await observer.observe({});
  assert.ok(result.ok);
  const [first] = result.body.entities as Array<{ facts: Array<{ value?: unknown; status: string }> }>;
  assert.deepEqual(first?.facts[0]?.value, { $redacted: true, because: "name", length: 6 });
  assert.equal(first?.facts[1]?.status, "unknown");
  assert.equal("value" in (first?.facts[1] ?? {}), false, "null is not turned into 0");
});

test("an incoherent image is reported with both stamps and never claimed as one moment", async () => {
  const { observer } = setup(() => observation({
    coherent: false, imageStamp: { frame: 90, scene: "Main" }, frameDelta: 10,
    image: { mimeType: "image/jpeg", data: "AAAA", width: 2, height: 2, frame: 90 },
  }));
  const result = await observer.observe({ include: ["entities", "image"] });
  assert.ok(result.ok);
  assert.equal(result.body.coherent, false);
  assert.equal(result.body.frameDelta, 10);
  assert.deepEqual(result.body.imageStamp, { frame: 90, scene: "Main" });
  assert.ok((result.body.warnings as string[]).some((warning) => /same frame/.test(warning)));
  assert.equal(result.ok && result.image?.data, "AAAA");
  const meta = result.body.image as Record<string, unknown>;
  assert.equal(meta.width, 2);
  assert.equal("data" in meta, false, "the base64 stays out of the structured body");
  assert.equal(meta.observationId, result.body.observationId);
});

test("the envelope carries schema, policy, partial, omittedCounts and warnings", async () => {
  const { observer } = setup(() => observation({ policy: { visibilityBasis: "renderer.isVisible" }, warnings: ["editor scene view counts as a camera"] }));
  const result = await observer.observe({ scope: "debug" });
  assert.ok(result.ok);
  assert.equal(result.body.schemaVersion, 1);
  const policy = result.body.policy as { scope: string; visibilityGuarantee: string; visibilityBasis: string; secretsRedacted: boolean };
  assert.equal(policy.scope, "debug");
  assert.equal(policy.visibilityGuarantee, "none");
  assert.equal(policy.visibilityBasis, "renderer.isVisible");
  assert.equal(policy.secretsRedacted, true);
  assert.deepEqual(result.body.warnings, ["editor scene view counts as a camera"]);
  assert.equal(result.body.partial, false);
  assert.equal(result.body.eventCursor, "cursor-head");
});

test("player scope is the default and is sent to Unity", async () => {
  const { unity, observer } = setup(() => observation());
  await observer.observe({});
  const request = unity.calls.find((call) => call.method === "play_observe")?.params[0] as { scope: string };
  assert.equal(request.scope, "player");
});

const MOVED = { position: { x: 1, y: 2, z: 3 }, scale: { x: 1, y: 1, z: 1 }, eulerAngles: { x: 0, y: 0, z: 0 }, layer: 5 };

test("an entity's transform is left out unless include asks for it, but changes still see it", async () => {
  let call = 0;
  const { observer } = setup(() => {
    call++;
    return observation({ entities: [entity(1, { transform: call === 1 ? MOVED : { ...MOVED, position: { x: 9, y: 2, z: 3 } } })] });
  });

  const first = await observer.observe({});
  assert.ok(first.ok);
  assert.equal((first.body.entities as Array<{ transform?: unknown }>)[0]?.transform, undefined);

  const second = await observer.observe({ sinceObservationId: first.body.observationId as string });
  const changes = second.body.changes as { changed: Array<{ fields: string[] }> };
  assert.deepEqual(changes.changed[0]?.fields, ["transform"], "the difference is reported though transform is not shown");

  const shown = await observer.observe({ include: ["entities", "transform"] });
  assert.deepEqual((shown.body.entities as Array<{ transform?: unknown }>)[0]?.transform, { ...MOVED, position: { x: 9, y: 2, z: 3 } });
});
