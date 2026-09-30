import { redactSecrets } from "./secrets.js";
import type { JsonValue } from "./pulse.js";
import { PlayCallError, type PlayClient } from "./play-client.js";
import { EventLog, type WatchFilter } from "./play-events.js";
import { collectNeeds, evaluateAll, type PlayEvent, type Predicate } from "./play-predicate.js";
import { defaultSleep } from "./play-act.js";
import { PLAY_LIMITS, envelope, errorBody, isRecord, type PlayScope } from "./play-types.js";

export interface WatchInput {
  afterCursor?: string | undefined;
  filter?: WatchFilter | undefined;
  predicates?: Predicate[] | undefined;
  limit?: number | undefined;
  timeoutMs?: number | undefined;
  scope?: PlayScope | undefined;
}

export interface WatchDeps {
  client: PlayClient;
  log: EventLog;
  timers?: {
    setTimeout(callback: () => void, ms: number): ReturnType<typeof setTimeout>;
    clearTimeout(timer: ReturnType<typeof setTimeout>): void;
  };
  now?: () => number;
}

const PROVIDER_POLL_MS = 250;
const PREDICATE_POLL_MS = 200;

/// `watch_events`: 중요한 변화가 생기거나 timeout 이 될 때까지 한 번 기다린다.
///
/// 연속 polling 을 대신한다. 기다리는 동안 구독과 타이머를 만들고, 끝나면 모두 해제한다.
export async function watchEvents(
  deps: WatchDeps,
  input: WatchInput,
  signal: AbortSignal,
): Promise<{ body: Record<string, unknown>; isError: boolean }> {
  const scope = input.scope ?? "player";
  const timers = deps.timers ?? { setTimeout, clearTimeout };
  const now = deps.now ?? (() => performance.now());
  const timeoutMs = input.timeoutMs ?? PLAY_LIMITS.observe.timeoutMs.default;
  const limit = input.limit ?? PLAY_LIMITS.observe.events.default;
  const started = now();
  const deadline = started + timeoutMs;

  let capabilities;
  try {
    capabilities = await deps.client.capabilities();
  } catch (error) {
    const code = error instanceof PlayCallError ? error.code : "unavailable";
    const message = error instanceof Error ? error.message : String(error);
    return { isError: true, body: { ...envelope(scope), error: errorBody(code, message) } };
  }
  deps.log.changeSession(capabilities.sessionId);

  const hasProviders = capabilities.providers.some((provider) => provider.status === "active");
  let providerCursor: number | undefined;
  const pullProviderEvents = async (): Promise<void> => {
    if (!hasProviders) return;
    try {
      const raw = await deps.client.call("play_events", [providerCursor ?? 0, limit], 2_000);
      if (!isRecord(raw)) return;
      if (typeof raw.next === "number") providerCursor = raw.next;
      for (const item of Array.isArray(raw.events) ? raw.events : []) {
        if (!isRecord(item) || typeof item.kind !== "string" || typeof item.providerId !== "string") continue;
        deps.log.appendProvider({
          kind: item.kind,
          providerId: item.providerId,
          ...(typeof item.name === "string" ? { name: item.name } : {}),
          ...(isRecord(item.entity) ? { entity: item.entity as unknown as PlayEvent["entity"] } : {}),
          ...(item.data === undefined ? {} : { data: redactSecrets("data", item.data as JsonValue) }),
          ...(isRecord(item.stamp) ? { stamp: item.stamp as PlayEvent["stamp"] } : {}),
          playerVisible: item.playerVisible === true,
        });
      }
    } catch {
      // provider event 를 못 읽어도 기본 event 는 계속 돌려준다.
    }
  };

  // 기준 cursor: 안 주면 지금 이후만 받는다.
  const after = input.afterCursor ?? deps.log.head();
  const predicates = input.predicates ?? [];
  const needs = collectNeeds(predicates);

  let waiting: { wake: () => void } | undefined;
  const unsubscribe = deps.log.onAppend(() => waiting?.wake());
  const wait = (ms: number): Promise<void> => new Promise((resolve) => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const done = () => {
      if (timer !== undefined) timers.clearTimeout(timer);
      signal.removeEventListener("abort", done);
      waiting = undefined;
      resolve();
    };
    waiting = { wake: done };
    timer = timers.setTimeout(done, ms);
    signal.addEventListener("abort", done, { once: true });
  });

  let predicatesMet = false;
  let cancelled = false;
  try {
    for (;;) {
      await pullProviderEvents();
      const read = deps.log.read(after, scope, input.filter ?? {}, limit);

      if (predicates.length > 0) {
        // 대상이 없는 조건(sceneIs, eventMatches)도 현재 stamp 가 필요하므로 항상 샘플링한다.
        try {
          const sample = await deps.client.sample(needs, scope, Math.max(1, deadline - now()));
          predicatesMet = evaluateAll(predicates, { after: sample, events: read.events }).result === "true";
        } catch {
          predicatesMet = false;
        }
      }

      if (read.events.length > 0 || read.gap || read.baselineRequired || predicatesMet) {
        return { isError: false, body: shape(deps, scope, read, predicatesMet, false, redactEvents(read.events)) };
      }
      if (signal.aborted) {
        cancelled = true;
        break;
      }
      const remaining = deadline - now();
      if (remaining <= 0) break;
      const poll = predicates.length > 0 ? PREDICATE_POLL_MS : hasProviders ? PROVIDER_POLL_MS : remaining;
      await wait(Math.min(remaining, poll));
    }

    // timeout 이나 취소. 이벤트가 없다는 것은 연결이 끊겼다는 뜻이 아니다.
    const read = deps.log.read(after, scope, input.filter ?? {}, limit);
    const body = shape(deps, scope, read, false, true, []);
    if (cancelled) body.cancelled = true;
    return { isError: false, body };
  } finally {
    unsubscribe();
    waiting = undefined;
  }
}

function redactEvents(events: PlayEvent[]): PlayEvent[] {
  return events.map((event) => event.data === undefined
    ? event
    : { ...event, data: redactSecrets("data", event.data as JsonValue) });
}

function shape(
  deps: WatchDeps,
  scope: PlayScope,
  read: ReturnType<EventLog["read"]>,
  predicatesMet: boolean,
  timedOut: boolean,
  events: PlayEvent[],
): Record<string, unknown> {
  const body: Record<string, unknown> = {
    ...envelope(scope),
    cursor: read.cursor,
    events,
    timedOut,
    predicatesMet,
    gap: read.gap,
    baselineRequired: read.baselineRequired,
    truncated: read.truncated,
    stamp: { sessionId: deps.log.currentEpoch },
    samplingNote: "Default events are differences between scene readings; changes that start and end between two readings are not recorded. "
      + "Provider events carry provider provenance and the frame they were emitted on.",
  };
  if (read.oldestCursor !== undefined) body.oldestCursor = read.oldestCursor;
  if (read.gap) {
    body.partial = true;
    (body.warnings as string[]).push("Events were lost (buffer overflow or an old cursor). Call observe for a new baseline.");
  }
  return body;
}

export { defaultSleep };
