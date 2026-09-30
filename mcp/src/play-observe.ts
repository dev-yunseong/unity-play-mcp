import { createHash, randomUUID } from "node:crypto";

import { redactSecrets } from "./secrets.js";
import type { JsonValue } from "./pulse.js";
import { PlayCallError, type PlayClient } from "./play-client.js";
import {
  PLAY_LIMITS,
  entityKey,
  envelope,
  errorBody,
  isRecord,
  resolveLimits,
  utf8Bytes,
  type Envelope,
  type EntityRef,
  type ObserveOptions,
  type PlayEntity,
  type PlayScope,
  type ResolvedLimits,
  type Stamp,
} from "./play-types.js";

export interface CapturedView {
  mimeType: string;
  data: string;
  width: number;
  height: number;
  screen?: unknown;
  region?: unknown;
  scale?: unknown;
  frame?: number;
  scene?: string;
  toScreen?: string;
}

export interface ObservationSnapshot {
  observationId: string;
  scope: PlayScope;
  filterKey: string;
  stamp: Stamp;
  entities: PlayEntity[];
  /// 이 snapshot 을 받은 시각(TS 단조 시계). Unity 의 `sampledAtMonotonicMs` 와 다른 시계다.
  receivedAtMs: number;
  lastChangeFrame: number;
  /// Unity 가 입력을 처리할 때마다 올리는 번호. 관찰 뒤 다른 입력이 있었는지 알아내는 데 쓴다.
  inputRevision?: number;
  /// 이 관찰 시점의 event log 위치. `watch_events` 의 `afterCursor` 로 쓴다.
  eventCursor?: string;
}

export interface ObserveResult {
  ok: true;
  body: Record<string, unknown>;
  image?: CapturedView;
}

export interface ObserveFailure {
  ok: false;
  body: Record<string, unknown>;
}

export interface ObserverClock {
  /// 단조 시계(ms). test 가 주입한다.
  mono(): number;
}

interface Continuation {
  snapshot: ObservationSnapshot;
  offset: number;
  limits: ResolvedLimits;
  include: Set<string>;
  expiresAtMs: number;
}

const KEEP_SNAPSHOTS = 16;

/// `observe` 를 구현한다. Unity 가 새로 샘플링한 snapshot 을 받아 비밀을 가리고, 예산을 적용하고,
/// 이전 관찰과의 차이를 계산한다.
export class PlayObserver {
  private readonly snapshots = new Map<string, ObservationSnapshot>();
  private readonly continuations = new Map<string, Continuation>();
  private readonly lastFingerprint = new Map<string, { fingerprint: string; frame: number }>();
  private counter = 0;

  constructor(
    private readonly client: PlayClient,
    private readonly clock: ObserverClock = { mono: () => performance.now() },
    private readonly eventHead?: () => string,
  ) {}

  /// `act_and_observe` 가 기준 관찰의 나이와 scene 을 확인할 때 쓴다.
  getSnapshot(observationId: string): ObservationSnapshot | undefined {
    return this.snapshots.get(observationId);
  }

  nowMs(): number {
    return this.clock.mono();
  }

  async observe(options: ObserveOptions): Promise<ObserveResult | ObserveFailure> {
    const scope: PlayScope = options.scope ?? "player";
    const limits = resolveLimits(options.limits);
    const include = new Set<string>(options.include ?? ["entities", "actions", "facts", "changes"]);
    const timeoutMs = options.timeoutMs ?? PLAY_LIMITS.observe.timeoutMs.default;
    const started = this.clock.mono();

    if (options.continuation !== undefined) {
      return this.page(options.continuation, scope);
    }

    try {
      await this.client.capabilities();
    } catch (error) {
      return failure(scope, error);
    }

    const filterKey = createHash("sha1").update(JSON.stringify([scope, options.filter ?? {}])).digest("hex").slice(0, 12);

    // cached: 충분히 새로운 관찰이 있으면 Unity 에 묻지 않는다. 그 사실과 나이를 그대로 알린다.
    if (options.freshness === "cached") {
      const cached = this.newestFor(filterKey);
      const maxAge = options.maxAgeMs ?? PLAY_LIMITS.act.maxObservationAgeMs;
      if (cached !== undefined && this.clock.mono() - cached.receivedAtMs <= maxAge) {
        return this.respond(cached, { scope, limits, include, since: undefined, sampled: false, timings: { sampleMs: 0, totalMs: this.clock.mono() - started } });
      }
    }

    const since = options.sinceObservationId === undefined ? undefined : this.snapshots.get(options.sinceObservationId);
    const usableSince = since !== undefined && since.filterKey === filterKey ? since : undefined;

    const request = {
      scope,
      filter: options.filter ?? {},
      includeImage: include.has("image"),
      ...(options.imageMaxEdge === undefined ? {} : { imageMaxEdge: options.imageMaxEdge }),
      maxEntities: limits.entities,
      factsPerEntity: limits.factsPerEntity,
      includeActions: include.has("actions"),
      includeFacts: include.has("facts"),
      track: usableSince === undefined ? [] : usableSince.entities.slice(0, PLAY_LIMITS.observe.entities.max).map((entity) => entity.ref),
    };

    let raw: unknown;
    const sampleStart = this.clock.mono();
    try {
      raw = await this.client.call("play_observe", [request], timeoutMs);
    } catch (error) {
      return failure(scope, error);
    }
    const sampleMs = this.clock.mono() - sampleStart;

    const parsed = parseObservation(raw);
    if (parsed === undefined) {
      return failure(scope, new PlayCallError("protocol_error", "play_observe returned an invalid payload"));
    }
    this.client.noteSession(parsed.stamp.sessionId);

    const received = this.clock.mono();
    const redacted = parsed.entities.map(redactEntity);
    const entities = capFacts(redacted, limits.factsPerEntity);
    const fingerprint = fingerprintOf(entities);
    const previous = this.lastFingerprint.get(filterKey);
    const lastChangeFrame = previous !== undefined && previous.fingerprint === fingerprint
      && previous.frame <= parsed.stamp.frame ? previous.frame : parsed.stamp.frame;
    this.lastFingerprint.set(filterKey, { fingerprint, frame: lastChangeFrame });

    const snapshot: ObservationSnapshot = {
      ...(parsed.inputRevision === undefined ? {} : { inputRevision: parsed.inputRevision }),
      ...(this.eventHead === undefined ? {} : { eventCursor: this.eventHead() }),
      observationId: `obs-${randomUUID().slice(0, 8)}-${++this.counter}`,
      scope,
      filterKey,
      stamp: parsed.stamp,
      entities,
      receivedAtMs: received,
      lastChangeFrame,
    };
    this.remember(snapshot);

    return this.respond(snapshot, {
      scope, limits, include,
      since: options.sinceObservationId === undefined ? undefined : { requested: options.sinceObservationId, found: usableSince, lifecycles: parsed.lifecycles },
      sampled: true,
      parsed,
      timings: { sampleMs, encodeMs: parsed.encodeMs, totalMs: this.clock.mono() - started },
    });
  }

  private newestFor(filterKey: string): ObservationSnapshot | undefined {
    let found: ObservationSnapshot | undefined;
    for (const snapshot of this.snapshots.values()) {
      if (snapshot.filterKey === filterKey) found = snapshot;
    }
    return found;
  }

  private remember(snapshot: ObservationSnapshot): void {
    this.snapshots.set(snapshot.observationId, snapshot);
    while (this.snapshots.size > KEEP_SNAPSHOTS) {
      const oldest = this.snapshots.keys().next();
      if (oldest.done === true) break;
      this.snapshots.delete(oldest.value);
    }
  }

  private respond(snapshot: ObservationSnapshot, context: {
    scope: PlayScope;
    limits: ResolvedLimits;
    include: Set<string>;
    since?: { requested: string; found?: ObservationSnapshot; lifecycles: Record<string, string> } | undefined;
    sampled: boolean;
    parsed?: ParsedObservation;
    timings: Record<string, number>;
  }): ObserveResult {
    const { scope, limits, include, parsed } = context;
    const body: Record<string, unknown> = { ...envelope(scope, parsed?.policy) };
    const warnings = body.warnings as string[];
    warnings.push(...(parsed?.warnings ?? []));

    const age = Math.max(0, this.clock.mono() - snapshot.receivedAtMs);
    body.observationId = snapshot.observationId;
    body.stamp = snapshot.stamp;
    if (snapshot.eventCursor !== undefined) body.eventCursor = snapshot.eventCursor;
    body.freshness = {
      current: context.sampled,
      ageMs: Math.round(age),
      sampledAtMonotonicMs: snapshot.stamp.sampledAtMonotonicMs,
      lastChangeFrame: snapshot.lastChangeFrame,
      note: context.sampled
        ? "Sampled from Unity for this request. An unchanged scene still returns a new stamp; lastChangeFrame says when it last changed."
        : "Served from an earlier observation, not a new sample.",
    };

    if (parsed !== undefined) {
      body.coherent = parsed.coherent;
      if (!parsed.coherent) {
        body.imageStamp = parsed.imageStamp;
        body.frameDelta = parsed.frameDelta;
        warnings.push("The image and the structured state were not collected in the same frame; do not treat them as one moment.");
      }
    }

    // 변화는 요청한 경우에만 계산한다.
    if (include.has("changes")) {
      body.changes = this.changesFor(snapshot, context.since);
    }

    if (include.has("entities")) {
      const page = this.paginate(snapshot, 0, limits, include);
      body.entities = page.entities;
      if (page.next !== undefined) {
        body.partial = true;
        body.continuation = page.next;
        body.continuationTtlMs = PLAY_LIMITS.continuationTtlMs;
      }
      (body.omittedCounts as Record<string, number>).entities = page.omitted;
      if (parsed !== undefined) {
        const unityOmitted = parsed.omittedEntities;
        if (unityOmitted > 0) {
          body.partial = true;
          (body.omittedCounts as Record<string, number>).entitiesBeyondLimit = unityOmitted;
        }
      }
    }
    body.timings = context.timings;
    if (parsed?.analysis !== undefined) body.analysis = parsed.analysis;

    if (parsed?.image !== undefined) {
      // 이미지는 별도 content block 이다. 좌표 변환 메타데이터는 구조화 결과에 남겨 픽셀 좌표를 해석할 수 있게 한다.
      const { data: _data, ...meta } = parsed.image;
      body.image = { ...meta, observationId: snapshot.observationId };
    }

    return { ok: true, body, ...(parsed?.image === undefined ? {} : { image: parsed.image }) };
  }

  private paginate(snapshot: ObservationSnapshot, offset: number, limits: ResolvedLimits, include: Set<string>) {
    const out: PlayEntity[] = [];
    let used = 0;
    let index = offset;
    for (; index < snapshot.entities.length && out.length < limits.entities; index++) {
      const entity = shape(snapshot.entities[index] as PlayEntity, include);
      const size = utf8Bytes(entity);
      // 첫 엔터티는 항상 싣는다. 하나도 못 싣는 응답은 진행을 막는다.
      if (out.length > 0 && used + size > limits.textBytes) break;
      out.push(entity);
      used += size;
    }
    const omitted = snapshot.entities.length - index;
    if (omitted <= 0) return { entities: out, omitted: 0, next: undefined as string | undefined };

    const token = randomUUID();
    this.continuations.set(token, {
      snapshot, offset: index, limits, include, expiresAtMs: this.clock.mono() + PLAY_LIMITS.continuationTtlMs,
    });
    return { entities: out, omitted, next: token };
  }

  private page(token: string, scope: PlayScope): ObserveResult | ObserveFailure {
    const held = this.continuations.get(token);
    if (held === undefined || held.expiresAtMs < this.clock.mono()) {
      this.continuations.delete(token);
      return {
        ok: false,
        body: {
          ...envelope(scope),
          error: errorBody("continuation_expired",
            "The continuation is unknown or older than 30 seconds. Call observe again for a fresh snapshot."),
        },
      };
    }
    this.continuations.delete(token);
    const page = this.paginate(held.snapshot, held.offset, held.limits, held.include);
    const body: Record<string, unknown> = {
      ...envelope(held.snapshot.scope),
      observationId: held.snapshot.observationId,
      stamp: held.snapshot.stamp,
      continued: true,
      entities: page.entities,
      partial: page.next !== undefined,
      ...(page.next === undefined ? {} : { continuation: page.next, continuationTtlMs: PLAY_LIMITS.continuationTtlMs }),
    };
    (body.omittedCounts as Record<string, number>).entities = page.omitted;
    return { ok: true, body };
  }

  private changesFor(
    snapshot: ObservationSnapshot,
    since: { requested: string; found?: ObservationSnapshot; lifecycles: Record<string, string> } | undefined,
  ): Record<string, unknown> {
    if (since === undefined) return { baseline: true, reason: "first observation for this filter" };
    const previous = since.found;
    if (previous === undefined) {
      return {
        baseline: true,
        baselineRequired: true,
        reason: "baseline_required",
        message: "sinceObservationId is unknown, expired, or was taken with a different scope or filter; this response is a new baseline.",
      };
    }
    if (previous.stamp.sessionId !== snapshot.stamp.sessionId) {
      return { baseline: true, baselineRequired: true, reason: "session_changed", message: "The Play session changed; this response is a new baseline." };
    }
    if (previous.stamp.scene !== snapshot.stamp.scene) {
      return {
        baseline: true, baselineRequired: true, reason: "scene_changed",
        message: `The scene changed from "${previous.stamp.scene}" to "${snapshot.stamp.scene}"; entities of the two scenes are not compared.`,
      };
    }

    const before = new Map(previous.entities.map((entity) => [entityKey(entity.ref), entity]));
    const now = new Map(snapshot.entities.map((entity) => [entityKey(entity.ref), entity]));
    const added: EntityRef[] = [];
    const changed: Array<{ ref: EntityRef; fields: string[] }> = [];
    for (const [key, entity] of now) {
      const old = before.get(key);
      if (old === undefined) {
        added.push(entity.ref);
        continue;
      }
      const fields = differingFields(old, entity);
      if (fields.length > 0) changed.push({ ref: entity.ref, fields });
    }

    // 사라진 것은 이유를 구분한다. 필터에서 빠진 것을 파괴로 간주하지 않는다.
    const gone: Array<{ ref: EntityRef; lifecycle: string }> = [];
    for (const [key, entity] of before) {
      if (now.has(key)) continue;
      gone.push({ ref: entity.ref, lifecycle: since.lifecycles[key] ?? "unobserved" });
    }
    return {
      baseline: false,
      since: since.requested,
      frameDelta: snapshot.stamp.frame - previous.stamp.frame,
      added,
      changed,
      gone,
      goneNote: "destroyed/inactive/unobserved/out_of_scope are distinct; a missing entity is destroyed only when lifecycle says so.",
    };
  }
}

interface ParsedObservation {
  inputRevision?: number;
  stamp: Stamp;
  entities: PlayEntity[];
  coherent: boolean;
  imageStamp?: unknown;
  frameDelta?: number;
  image?: CapturedView;
  lifecycles: Record<string, string>;
  warnings: string[];
  policy?: Partial<Envelope["policy"]>;
  omittedEntities: number;
  encodeMs: number;
  analysis?: unknown;
}

function parseObservation(raw: unknown): ParsedObservation | undefined {
  if (!isRecord(raw) || !isRecord(raw.stamp) || typeof raw.stamp.sessionId !== "string"
    || typeof raw.stamp.scene !== "string" || typeof raw.stamp.frame !== "number") {
    return undefined;
  }
  const entities = Array.isArray(raw.entities)
    ? raw.entities.filter((item): item is PlayEntity => isRecord(item) && isRecord(item.ref) && typeof item.ref.id === "number")
    : [];
  const image = isRecord(raw.image) && typeof raw.image.data === "string"
    ? (raw.image as unknown as CapturedView) : undefined;
  return {
    ...(typeof raw.inputRevision === "number" ? { inputRevision: raw.inputRevision } : {}),
    stamp: raw.stamp as unknown as Stamp,
    entities: entities.map((entity) => ({
      ...entity,
      facts: Array.isArray(entity.facts) ? entity.facts : [],
      actions: Array.isArray(entity.actions) ? entity.actions : [],
      types: Array.isArray(entity.types) ? entity.types : [],
    })),
    coherent: raw.coherent !== false,
    ...(raw.imageStamp === undefined ? {} : { imageStamp: raw.imageStamp }),
    ...(typeof raw.frameDelta === "number" ? { frameDelta: raw.frameDelta } : {}),
    ...(image === undefined ? {} : { image }),
    lifecycles: isRecord(raw.lifecycles) ? (raw.lifecycles as Record<string, string>) : {},
    warnings: Array.isArray(raw.warnings) ? raw.warnings.filter((item): item is string => typeof item === "string") : [],
    ...(isRecord(raw.policy) ? { policy: raw.policy as Partial<Envelope["policy"]> } : {}),
    omittedEntities: typeof raw.omittedEntities === "number" ? raw.omittedEntities : 0,
    encodeMs: typeof raw.encodeMs === "number" ? raw.encodeMs : 0,
    ...(raw.analysis === undefined ? {} : { analysis: raw.analysis }),
  };
}

/// entity 안의 모든 텍스트와 값에 기존 비밀 제거 규칙을 적용한다. 새 출력 경로마다 반드시 거친다.
export function redactEntity(entity: PlayEntity): PlayEntity {
  const facts = entity.facts.map((fact) => fact.value === undefined
    ? fact
    : { ...fact, value: redactSecrets(fact.name ?? "", fact.value as JsonValue) });
  const state = entity.state === undefined ? undefined : redactSecrets("state", entity.state as JsonValue) as Record<string, unknown>;
  const label = typeof entity.label === "string" ? String(redactSecrets("label", entity.label)) : entity.label;
  return { ...entity, label, facts: facts ?? [], actions: entity.actions ?? [], ...(state === undefined ? {} : { state }) };
}

function capFacts(entities: PlayEntity[], limit: number): PlayEntity[] {
  return entities.map((entity) => entity.facts.length <= limit ? entity : { ...entity, facts: entity.facts.slice(0, limit), factsOmitted: entity.facts.length - limit });
}

function shape(entity: PlayEntity, include: Set<string>): PlayEntity {
  const shaped: PlayEntity = { ...entity };
  if (!include.has("facts")) shaped.facts = [];
  if (!include.has("actions")) shaped.actions = [];
  return shaped;
}

function fingerprintOf(entities: PlayEntity[]): string {
  return createHash("sha1").update(JSON.stringify(entities.map((entity) => [entityKey(entity.ref), entity.active, entity.transform, entity.state, entity.facts]))).digest("hex");
}

function differingFields(before: PlayEntity, after: PlayEntity): string[] {
  const fields: string[] = [];
  if (before.active !== after.active) fields.push("active");
  if (JSON.stringify(before.transform) !== JSON.stringify(after.transform)) fields.push("transform");
  if (JSON.stringify(before.state) !== JSON.stringify(after.state)) fields.push("state");
  if (JSON.stringify(before.screenRect) !== JSON.stringify(after.screenRect)) fields.push("screenRect");
  const beforeFacts = new Map(before.facts.map((fact) => [fact.name ?? "", JSON.stringify(fact.value)]));
  for (const fact of after.facts) {
    if (beforeFacts.get(fact.name ?? "") !== JSON.stringify(fact.value)) fields.push(`fact:${fact.name ?? ""}`);
  }
  return fields;
}

/// 실패를 코드가 있는 응답으로 바꾼다.
export function failure(scope: PlayScope, error: unknown): ObserveFailure {
  const body = error instanceof PlayCallError
    ? errorBody(error.code, error.message, error.detail)
    : /timed out/i.test(String((error as Error)?.message))
      ? errorBody("timeout", `${(error as Error).message}. This is a missing answer from Unity, not evidence that nothing changed.`)
      : errorBody("unavailable", error instanceof Error ? error.message : String(error));
  return { ok: false, body: { ...envelope(scope), error: body } };
}
