import { z } from "zod";

import { PlayCallError, type PlayClient } from "./play-client.js";
import { InputGate, canonicalHash, isMutatingMethod, OperationLedger, busyText } from "./play-operations.js";
import type { PlayObserver } from "./play-observe.js";
import {
  collectNeeds,
  evaluateAll,
  hasNeeds,
  parsePredicate,
  parsePredicates,
  PredicateError,
  type Evidence,
  type PlayEvent,
  type PlaySample,
  type Predicate,
  type SampleNeeds,
} from "./play-predicate.js";
import {
  PLAY_LIMITS,
  entityRefSchema,
  envelope,
  errorBody,
  isEntityRef,
  isRecord,
  observeOptionsSchema,
  screenPointSchema,
  worldPointSchema,
  type EntityRef,
  type PlayScope,
} from "./play-types.js";

const positiveInt = () => z.number().int().positive();

/// 새 scheduler 가 받는 step. 기존 입력 경로만 쓰고, 무한 반복이나 사용자 코드는 받지 않는다.
///
/// `waitCondition.until` 은 recursive 라 입력 schema 에서는 느슨한 object 로 받고 `parsePredicate` 가 검증한다.
export const stepSchema = () => z.discriminatedUnion("method", [
  z.object({ method: z.literal("button_click"), targetId: z.number().int() }).strict(),
  z.object({ method: z.literal("pointer_click"), targetId: z.number().int() }).strict(),
  z.object({ method: z.literal("pointer_drag"), sourceId: z.number().int(), targetId: z.number().int() }).strict(),
  z.object({ method: z.literal("pointer_hover"), targetId: z.number().int() }).strict(),
  z.object({ method: z.literal("enter_text"), targetId: z.number().int(), text: z.string() }).strict(),
  z.object({ method: z.literal("move_mouse"), x: z.number(), y: z.number() }).strict(),
  z.object({ method: z.literal("mouse_down"), button: z.number().int().min(0).max(2) }).strict(),
  z.object({ method: z.literal("mouse_up"), button: z.number().int().min(0).max(2) }).strict(),
  z.object({ method: z.literal("key_click"), key: z.string().min(1), seconds: z.number().positive() }).strict(),
  z.object({ method: z.literal("key_down"), key: z.string().min(1) }).strict(),
  z.object({ method: z.literal("key_up"), key: z.string().min(1) }).strict(),
  z.object({ method: z.literal("set_axis"), name: z.string().min(1), value: z.number() }).strict(),
  z.object({ method: z.literal("set_button"), name: z.string().min(1), pressed: z.boolean() }).strict(),
  z.object({ method: z.literal("waitFrames"), frames: positiveInt().max(PLAY_LIMITS.act.maxWaitFrames) }).strict(),
  z.object({
    method: z.literal("waitCondition"),
    until: z.record(z.unknown()),
    timeoutMs: positiveInt().max(PLAY_LIMITS.act.timeoutMs.max).optional(),
  }).strict(),
]);

export type Step = z.infer<ReturnType<typeof stepSchema>>;

export const actionSchema = () => z.object({
  actionRef: z.string().min(1),
  target: z.union([entityRefSchema(), screenPointSchema(), worldPointSchema()]).optional(),
}).strict();

export const actInputSchema = () => ({
  operationId: z.string().min(1).max(128),
  basedOnObservationId: z.string().min(1),
  maxObservationAgeMs: z.number().int().nonnegative().optional(),
  action: actionSchema().optional(),
  steps: z.array(stepSchema()).min(1).max(PLAY_LIMITS.act.maxSteps).optional(),
  preconditions: z.array(z.record(z.unknown())).optional(),
  expect: z.array(z.record(z.unknown())).optional(),
  observation: observeOptionsSchema().optional(),
  timeoutMs: positiveInt().max(PLAY_LIMITS.act.timeoutMs.max).optional(),
  scope: z.enum(["player", "debug"]).optional(),
});

export interface ActInput {
  operationId: string;
  basedOnObservationId: string;
  maxObservationAgeMs?: number | undefined;
  action?: z.infer<ReturnType<typeof actionSchema>> | undefined;
  steps?: Step[] | undefined;
  preconditions?: Record<string, unknown>[] | undefined;
  expect?: Record<string, unknown>[] | undefined;
  observation?: z.infer<ReturnType<typeof observeOptionsSchema>> | undefined;
  timeoutMs?: number | undefined;
  scope?: PlayScope | undefined;
}

export type StepStatus = "completed" | "failed" | "skipped";

export interface StepResult {
  index: number;
  method: string;
  status: StepStatus;
  error?: string;
  frame?: number;
  elapsedMs: number;
}

export type Termination =
  | "finished" | "timeout" | "cancelled" | "disconnected" | "scene_changed" | "precondition_failed" | "busy";

export interface ActDeps {
  client: PlayClient;
  observer: PlayObserver;
  gate: InputGate;
  ledger: OperationLedger<ActResponse>;
  /// 대기. 취소되면 즉시 끝난다.
  sleep: (ms: number, signal?: AbortSignal) => Promise<void>;
  /// 이벤트 log 의 event 를 조건 평가에 넘긴다.
  eventsSince?: (cursor: string | undefined) => PlayEvent[];
  eventsHead?: () => string | undefined;
  now?: () => number;
}

export interface ActResponse {
  body: Record<string, unknown>;
  image?: { mimeType: string; data: string } | undefined;
  isError: boolean;
}

const POLL_INTERVAL_MS = 100;
const RELEASE_TIMEOUT_MS = 1_500;

export function defaultSleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal?.aborted) return resolve();
    const timer = setTimeout(done, ms);
    function done() {
      clearTimeout(timer);
      signal?.removeEventListener("abort", done);
      resolve();
    }
    signal?.addEventListener("abort", done, { once: true });
  });
}

/// 입력 검증 실패. Unity 에 아무것도 보내기 전에 낸다.
class InvalidRequest extends Error {
  constructor(message: string, readonly path?: string) {
    super(message);
  }
}

interface HeldState {
  keys: Set<string>;
  buttons: Set<number>;
  axes: Set<string>;
  named: Set<string>;
}

interface Recipe {
  steps: Record<string, unknown>[];
  availability?: string;
  preconditions: Array<{ predicate: unknown; reason?: string }>;
  outcomePredicates: unknown[];
  entity?: EntityRef;
  acceptsTarget?: string;
}

/// `act_and_observe`: 유한한 입력을 실행하고 그 뒤 관찰 가능한 결과를 함께 돌려준다.
///
/// 입력 접수(`execution`)와 게임 안의 결과(`outcome`)를 분리한다. 입력이 성공했다는 사실만으로
/// `confirmed` 를 만들지 않는다.
export async function actAndObserve(
  deps: ActDeps,
  input: ActInput,
  signal: AbortSignal,
): Promise<ActResponse> {
  const scope = input.scope ?? "player";

  let prepared: Prepared;
  try {
    prepared = prepare(input);
  } catch (error) {
    if (error instanceof InvalidRequest || error instanceof PredicateError) {
      return rejected(scope, input.operationId, "invalid_request", error.message);
    }
    throw error;
  }

  const hash = canonicalHash({ ...input, scope });
  const begin = deps.ledger.begin(input.operationId, hash);
  if (begin.kind === "conflict") {
    return rejected(scope, input.operationId, "operation_conflict",
      "This operationId was already used with a different request. Use a new operationId for a different request.");
  }
  if (begin.kind === "replay") {
    const earlier = await begin.promise;
    return { ...earlier, body: { ...earlier.body, replayed: true } };
  }
  return begin.finish(() => run(deps, input, prepared, scope, hash, signal));
}

interface Prepared {
  preconditions: Predicate[];
  expect: Predicate[];
  timeoutMs: number;
  explicitSteps?: Step[];
}

function prepare(input: ActInput): Prepared {
  if ((input.action === undefined) === (input.steps === undefined)) {
    throw new InvalidRequest("Give exactly one of action or steps.");
  }
  const steps = input.steps;
  if (steps !== undefined) {
    if (steps.length > PLAY_LIMITS.act.maxSteps) {
      throw new InvalidRequest(`At most ${PLAY_LIMITS.act.maxSteps} steps are allowed.`);
    }
    steps.forEach((step, index) => {
      if (step.method === "waitCondition") parsePredicate(step.until, `steps[${index}].until`);
    });
  }
  return {
    preconditions: parsePredicates(input.preconditions, "preconditions"),
    expect: parsePredicates(input.expect, "expect"),
    timeoutMs: input.timeoutMs ?? PLAY_LIMITS.act.timeoutMs.default,
    ...(steps === undefined ? {} : { explicitSteps: steps }),
  };
}

function rejected(scope: PlayScope, operationId: string, code: string, message: string, extra: Record<string, unknown> = {}): ActResponse {
  return {
    isError: true,
    body: {
      ...envelope(scope),
      operationId,
      execution: { status: "not_started", steps: [] },
      outcome: { status: "unknown", evidence: [], unmet: [message] },
      termination: code === "busy" ? "busy" : "precondition_failed",
      error: errorBody(code, message, extra),
    },
  };
}

async function run(
  deps: ActDeps,
  input: ActInput,
  prepared: Prepared,
  scope: PlayScope,
  hash: string,
  outerSignal: AbortSignal,
): Promise<ActResponse> {
  const now = deps.now ?? (() => performance.now());
  const started = now();
  const deadline = started + prepared.timeoutMs;
  const timings = { queueMs: 0, dispatchMs: 0, waitMs: 0, sampleMs: 0, encodeMs: 0, totalMs: 0 };
  const steps: StepResult[] = [];
  const held: HeldState = { keys: new Set(), buttons: new Set(), axes: new Set(), named: new Set() };
  let termination: Termination = "finished";
  let executionStatus: "not_started" | "completed" | "partial" | "failed" = "not_started";
  let before: PlaySample | undefined;
  let after: PlaySample | undefined;
  const unmet: string[] = [];
  const evidence: Evidence[] = [];
  let holdsLock = false;
  let beganOnUnity = false;

  // 취소는 호출한 쪽의 signal 과 wall-clock deadline 중 먼저 오는 쪽이다.
  const controller = new AbortController();
  const onOuterAbort = () => controller.abort();
  outerSignal.addEventListener("abort", onOuterAbort, { once: true });
  const remaining = () => Math.max(1, deadline - now());

  const finish = async (): Promise<ActResponse> => {
    // 이 operation 이 누른 것만 놓는다. 다른 client 나 사용자의 입력은 건드리지 않는다.
    await releaseOwned(deps.client, held);
    if (beganOnUnity) {
      try {
        await deps.client.call("play_end", [input.operationId, { termination, executionStatus }], RELEASE_TIMEOUT_MS);
      } catch {
        // Unity 쪽 lock 은 TTL 로 풀린다.
      }
    }
    if (holdsLock) deps.gate.release(input.operationId);
    outerSignal.removeEventListener("abort", onOuterAbort);
    controller.abort();

    let observation: Record<string, unknown> | undefined;
    let image: ActResponse["image"];
    if (executionStatus !== "not_started" || termination === "finished") {
      const observeStart = now();
      try {
        const result = await deps.observer.observe({
          ...(input.observation ?? {}),
          scope,
          freshness: "current",
          timeoutMs: Math.min(PLAY_LIMITS.observe.timeoutMs.max, Math.max(1_000, deadline - now() + 1_000)),
        });
        observation = result.body;
        if (result.ok && result.image !== undefined) image = { mimeType: result.image.mimeType, data: result.image.data };
        if (result.ok && typeof result.body.timings === "object" && result.body.timings !== null) {
          timings.encodeMs = Number((result.body.timings as Record<string, number>).encodeMs ?? 0);
        }
      } catch (error) {
        observation = { error: errorBody("observation_failed", error instanceof Error ? error.message : String(error)) };
      }
      timings.sampleMs += Math.round(now() - observeStart);
    }

    timings.totalMs = Math.round(now() - started);
    const outcome = outcomeOf(prepared, input, after, evidence, unmet, executionStatus);
    const body: Record<string, unknown> = {
      ...envelope(scope),
      operationId: input.operationId,
      before: before?.stamp,
      ...(after === undefined ? {} : { after: after.stamp }),
      execution: { status: executionStatus, steps },
      outcome,
      termination,
      note: "execution says what input Unity accepted; outcome says what was observed in the game. "
        + "Observed changes are temporal associations, not proof that this input caused them.",
      ...(observation === undefined ? {} : { observation }),
      timings,
    };
    return { body, image, isError: termination === "busy" || termination === "precondition_failed" };
  };

  try {
    // 1. 기준 관찰과 capability.
    try {
      await deps.client.capabilities();
    } catch (error) {
      return earlyFailure(scope, input.operationId, error);
    }

    const base = deps.observer.getSnapshot(input.basedOnObservationId);
    if (base === undefined) {
      termination = "precondition_failed";
      unmet.push("basedOnObservationId is unknown or expired; call observe again.");
      return await finish();
    }

    // 2. 같은 프로세스의 다른 입력과 섞이지 않게 gate 를 잡는다.
    const lockStart = now();
    if (!deps.gate.acquire(input.operationId)) {
      termination = "busy";
      unmet.push(busyText(deps.gate.activeOperationId ?? "unknown"));
      return await finish();
    }
    holdsLock = true;

    // 3. Unity 쪽 lock 과 operation 기록. 다른 client 가 점유 중이면 busy 다.
    const beginResult = await beginOnUnity(deps, input.operationId, hash, Math.min(remaining() + 5_000, 60_000));
    timings.queueMs = Math.round(now() - lockStart);
    if (beginResult.kind === "busy") {
      termination = "busy";
      unmet.push(`another operation (${beginResult.operationId ?? "unknown"}) holds the input; nothing was sent.`);
      return await finish();
    }
    if (beginResult.kind === "unknown_operation") {
      termination = "precondition_failed";
      unmet.push("Unity has seen this operationId before but this server has no result for it "
        + "(for example after a restart). The input was not re-sent; its outcome is unknown. Observe and decide again.");
      return await finish();
    }
    beganOnUnity = true;

    // 4. 기준 관찰이 아직 유효한지: 세션/scene 이 같고, 오래됐다면 그 사이 다른 입력이 없었는지.
    const maxAge = input.maxObservationAgeMs ?? PLAY_LIMITS.act.maxObservationAgeMs;
    const age = deps.observer.nowMs() - base.receivedAtMs;
    if (beginResult.sessionId !== undefined && beginResult.sessionId !== base.stamp.sessionId) {
      termination = "precondition_failed";
      unmet.push("session_changed: the Play session changed since the observation.");
      return await finish();
    }
    if (beginResult.scene !== undefined && beginResult.scene !== base.stamp.scene) {
      termination = "scene_changed";
      unmet.push(`scene_changed: the scene is "${beginResult.scene}", the observation was of "${base.stamp.scene}".`);
      return await finish();
    }
    if (age > maxAge && beginResult.inputRevision !== undefined && base.inputRevision !== undefined
      && beginResult.inputRevision !== base.inputRevision) {
      termination = "precondition_failed";
      unmet.push(`stale_observation: other input ran after the observation, which is ${Math.round(age)}ms old (limit ${maxAge}ms). Observe again.`);
      return await finish();
    }

    // 5. recipe 를 만들고 step 을 검증한다. 입력 전에 전부 끝낸다.
    let plan: Step[];
    let recipe: Recipe | undefined;
    try {
      if (input.action !== undefined) {
        recipe = await resolveRecipe(deps.client, input.action, remaining());
        if (recipe.availability === "unavailable") {
          termination = "precondition_failed";
          unmet.push("The action is unavailable.");
          return await finish();
        }
        plan = await planFromRecipe(deps.client, recipe, input.action.target, remaining());
      } else {
        plan = prepared.explicitSteps as Step[];
      }
    } catch (error) {
      termination = "precondition_failed";
      unmet.push(error instanceof PlayCallError || error instanceof InvalidRequest ? error.message
        : `Could not prepare the action: ${error instanceof Error ? error.message : String(error)}`);
      return await finish();
    }
    plan = withFrameGaps(plan);
    if (plan.length > PLAY_LIMITS.act.maxSteps * 2) {
      termination = "precondition_failed";
      unmet.push("The plan is longer than allowed.");
      return await finish();
    }

    // 6. 실행 직전 live 확인: 명시한 precondition, 그리고 대상의 active/interactable/blocker.
    const expect = [...prepared.expect, ...recipePredicates(recipe)];
    const recipePre = recipe?.preconditions.map((item) => item.predicate).filter((item) => item !== undefined) ?? [];
    let recipePreconditions: Predicate[] = [];
    try {
      recipePreconditions = parsePredicates(recipePre as unknown[], "recipe.preconditions");
    } catch {
      unmet.push("Some action preconditions from Unity could not be read and were ignored.");
    }
    const preconditions = [...prepared.preconditions, ...recipePreconditions];
    const needs = mergeNeeds(collectNeeds(preconditions), collectNeeds(expect));
    if (hasNeeds(needs)) {
      const sampleStart = now();
      before = await deps.client.sample(needs, scope, remaining());
      timings.sampleMs += Math.round(now() - sampleStart);
    } else {
      before = undefined;
    }
    if (preconditions.length > 0 && before !== undefined) {
      const check = evaluateAll(preconditions, { after: before });
      evidence.push(...check.evidence);
      if (check.result !== "true") {
        termination = "precondition_failed";
        unmet.push(...check.evidence.filter((item) => item.result !== "true").map((item) => item.detail));
        return await finish();
      }
    }
    const blocker = await checkTarget(deps.client, recipe, input.action?.target, remaining());
    if (blocker !== undefined) {
      termination = "precondition_failed";
      unmet.push(blocker);
      return await finish();
    }
    // 조건 평가에 쓸 기준 stamp. 필요한 대상이 없어도 before stamp 는 남긴다.
    if (before === undefined) {
      before = await deps.client.sample({ targets: [], members: [], facts: [] }, scope, remaining());
    }

    // 7. step 실행.
    const eventCursor = deps.eventsHead?.();
    executionStatus = "partial";
    let failedStep = false;
    for (let index = 0; index < plan.length; index++) {
      const step = plan[index] as Step;
      if (controller.signal.aborted || now() >= deadline) {
        termination = outerSignal.aborted ? "cancelled" : "timeout";
        skipRest(plan, steps, index);
        break;
      }
      const stepStart = now();
      const result = await executeStep(deps, step, index, held, remaining, controller.signal, scope);
      const elapsed = Math.round(now() - stepStart);
      result.elapsedMs = elapsed;
      steps.push(result);
      if (step.method === "waitFrames" || step.method === "waitCondition") timings.waitMs += elapsed;
      else timings.dispatchMs += elapsed;

      if (result.status === "failed") {
        failedStep = true;
        termination = classifyStepFailure(result, outerSignal, now() >= deadline);
        skipRest(plan, steps, index + 1);
        break;
      }
      // scene 이 바뀌면 남은 step 은 옛 entity 를 가리키므로 실행하지 않는다.
      if (index < plan.length - 1) {
        const guard = await sceneGuard(deps.client, base.stamp.scene, remaining());
        if (guard === "scene_changed") {
          termination = "scene_changed";
          skipRest(plan, steps, index + 1);
          break;
        }
      }
    }
    executionStatus = failedStep || steps.some((item) => item.status === "skipped")
      ? (steps.some((item) => item.status === "completed") ? "partial" : "failed")
      : "completed";

    // 8. 결과 확인: 조건이 참이 될 때까지 남은 시간 안에서 기다린다. 못 봤다고 실패로 단정하지 않는다.
    if (expect.length > 0 && !controller.signal.aborted && (termination === "finished" || termination === "timeout")) {
      const needsAfter = mergeNeeds(collectNeeds(expect), { targets: [], members: [], facts: [] });
      const waitStart = now();
      for (;;) {
        const sampleStart = now();
        try {
          after = await deps.client.sample(needsAfter, scope, remaining());
        } catch (error) {
          unmet.push(error instanceof Error ? error.message : String(error));
          if (error instanceof PlayCallError && error.code === "unsupported_capability") break;
          if (now() >= deadline) break;
        }
        timings.sampleMs += Math.round(now() - sampleStart);
        if (after !== undefined) {
          const events = deps.eventsSince?.(eventCursor) ?? [];
          const check = evaluateAll(expect, { after, ...(before === undefined ? {} : { before }), events });
          if (check.result === "true") {
            evidence.length = 0;
            evidence.push(...check.evidence);
            break;
          }
          evidence.length = 0;
          evidence.push(...check.evidence);
          if (after.stamp.scene !== base.stamp.scene && !expect.some(mentionsScene)) {
            termination = "scene_changed";
            break;
          }
        }
        if (now() + POLL_INTERVAL_MS >= deadline || controller.signal.aborted) {
          if (termination === "finished" && !controller.signal.aborted) termination = "timeout";
          break;
        }
        await deps.sleep(POLL_INTERVAL_MS, controller.signal);
      }
      timings.waitMs += Math.round(now() - waitStart);
      if (outerSignal.aborted) termination = "cancelled";
    } else if (expect.length === 0) {
      after = await deps.client.sample({ targets: [], members: [], facts: [] }, scope, remaining()).catch(() => undefined);
    }
    return await finish();
  } catch (error) {
    if (error instanceof PlayCallError && error.code === "unsupported_capability") {
      return earlyFailure(scope, input.operationId, error);
    }
    termination = isDisconnect(error) ? "disconnected" : "timeout";
    if (executionStatus === "not_started") executionStatus = "failed";
    unmet.push(error instanceof Error ? error.message : String(error));
    return await finish();
  } finally {
    outerSignal.removeEventListener("abort", onOuterAbort);
  }
}

function earlyFailure(scope: PlayScope, operationId: string, error: unknown): ActResponse {
  const body = error instanceof PlayCallError
    ? errorBody(error.code, error.message, error.detail)
    : errorBody("unavailable", error instanceof Error ? error.message : String(error));
  return {
    isError: true,
    body: {
      ...envelope(scope),
      operationId,
      execution: { status: "not_started", steps: [] },
      outcome: { status: "unknown", evidence: [], unmet: [body.message] },
      termination: "precondition_failed",
      error: body,
    },
  };
}

function isDisconnect(error: unknown): boolean {
  return error instanceof Error && /closed|disconnect|not running|Nothing is listening/i.test(error.message);
}

function mentionsScene(predicate: Predicate): boolean {
  if ("sceneIs" in predicate) return true;
  if ("all" in predicate) return predicate.all.some(mentionsScene);
  if ("any" in predicate) return predicate.any.some(mentionsScene);
  if ("not" in predicate) return mentionsScene(predicate.not);
  return false;
}

function mergeNeeds(left: SampleNeeds, right: SampleNeeds): SampleNeeds {
  const targets = new Map(left.targets.map((item) => [item.key, item]));
  right.targets.forEach((item) => targets.set(item.key, item));
  const members = new Map(left.members.map((item) => [`${item.target}|${item.component}|${item.member}`, item]));
  right.members.forEach((item) => members.set(`${item.target}|${item.component}|${item.member}`, item));
  const facts = new Map(left.facts.map((item) => [`${item.target ?? ""}|${item.providerId}|${item.name}`, item]));
  right.facts.forEach((item) => facts.set(`${item.target ?? ""}|${item.providerId}|${item.name}`, item));
  return { targets: [...targets.values()], members: [...members.values()], facts: [...facts.values()] };
}

function outcomeOf(
  prepared: Prepared,
  input: ActInput,
  after: PlaySample | undefined,
  evidence: Evidence[],
  unmet: string[],
  executionStatus: string,
): Record<string, unknown> {
  const hasExpectation = (prepared.expect.length > 0) || evidence.length > 0;
  if (!hasExpectation || after === undefined) {
    return {
      status: "unknown",
      evidence,
      unmet: [...unmet, ...(hasExpectation ? [] : ["No expect or provider outcome predicate was given, so success cannot be judged."])],
    };
  }
  const all = evidence.length > 0 && evidence.every((item) => item.result === "true");
  const failed = evidence.filter((item) => item.result !== "true");
  return {
    status: all ? "confirmed" : evidence.some((item) => item.result === "unknown") && !evidence.some((item) => item.result === "false")
      ? "unknown" : "not_observed",
    evidence,
    unmet: [...unmet, ...failed.map((item) => item.detail)],
    ...(executionStatus === "completed" && !all
      ? { note: "The input was executed but the expected effect was not observed within the time limit. It may still happen later." }
      : {}),
  };
}

function recipePredicates(recipe: Recipe | undefined): Predicate[] {
  const out: Predicate[] = [];
  for (const item of recipe?.outcomePredicates ?? []) {
    try {
      out.push(parsePredicate(item, "recipe.outcomePredicates"));
    } catch {
      // 읽지 못한 provider 조건은 무시한다. 위험을 막는 쪽이 아니라 확인을 더하는 쪽이다.
    }
  }
  return out;
}

async function beginOnUnity(
  deps: ActDeps,
  operationId: string,
  hash: string,
  ttlMs: number,
): Promise<
  | { kind: "ok"; sessionId?: string; scene?: string; inputRevision?: number }
  | { kind: "busy"; operationId?: string }
  | { kind: "unknown_operation" }
> {
  try {
    const raw = await deps.client.call("play_begin", [operationId, hash, ttlMs], 5_000);
    if (isRecord(raw) && raw.status === "already_started") return { kind: "unknown_operation" };
    return {
      kind: "ok",
      ...(isRecord(raw) && typeof raw.sessionId === "string" ? { sessionId: raw.sessionId } : {}),
      ...(isRecord(raw) && typeof raw.scene === "string" ? { scene: raw.scene } : {}),
      ...(isRecord(raw) && typeof raw.inputRevision === "number" ? { inputRevision: raw.inputRevision } : {}),
    };
  } catch (error) {
    if (error instanceof PlayCallError && error.code === "busy") {
      const holder = error.detail.operationId;
      return { kind: "busy", ...(typeof holder === "string" ? { operationId: holder } : {}) };
    }
    throw error;
  }
}

async function resolveRecipe(client: PlayClient, action: NonNullable<ActInput["action"]>, budgetMs: number): Promise<Recipe> {
  const raw = await client.call("play_inspect", [{ actionRef: action.actionRef }], budgetMs);
  if (!isRecord(raw) || !Array.isArray(raw.recipe)) {
    throw new PlayCallError("protocol_error", "play_inspect returned no recipe");
  }
  return {
    steps: raw.recipe.filter(isRecord),
    ...(typeof raw.availability === "string" ? { availability: raw.availability } : {}),
    preconditions: Array.isArray(raw.preconditions)
      ? raw.preconditions.filter(isRecord).map((item) => ({ predicate: item.predicate, ...(typeof item.reason === "string" ? { reason: item.reason } : {}) }))
      : [],
    outcomePredicates: Array.isArray(raw.outcomePredicates) ? raw.outcomePredicates : [],
    ...(isEntityRef(raw.entity) ? { entity: raw.entity } : {}),
    ...(typeof raw.acceptsTarget === "string" ? { acceptsTarget: raw.acceptsTarget } : {}),
  };
}

/// recipe 의 `$target` 자리를 채우고 step whitelist 로 검증한다.
async function planFromRecipe(
  client: PlayClient,
  recipe: Recipe,
  target: NonNullable<ActInput["action"]>["target"],
  budgetMs: number,
): Promise<Step[]> {
  const needsTarget = JSON.stringify(recipe.steps).includes("$target");
  if (needsTarget && target === undefined) {
    throw new InvalidRequest(`This action needs a target (${recipe.acceptsTarget ?? "entity, screen or world point"}).`);
  }
  if (!needsTarget && target !== undefined) {
    throw new InvalidRequest("This action does not take a target.");
  }

  let point: { x: number; y: number } | undefined;
  let targetId: number | undefined;
  if (target !== undefined) {
    if ("space" in target) {
      if (target.space === "screen") point = { x: target.x, y: target.y };
      else {
        const projected = await client.call("play_query_space", [{
          kind: "project_world_to_screen",
          point: target,
        }], budgetMs);
        if (!isRecord(projected) || !isRecord(projected.screen)
          || typeof projected.screen.x !== "number" || typeof projected.screen.y !== "number") {
          throw new InvalidRequest("The world point could not be projected onto the screen.");
        }
        point = { x: projected.screen.x, y: projected.screen.y };
      }
    } else {
      targetId = target.id;
    }
  }

  const substitute = (value: unknown): unknown => {
    if (typeof value === "string" && value.startsWith("$target")) {
      if (value === "$target.id" || value === "$target") {
        if (targetId === undefined) throw new InvalidRequest("This recipe needs an entity target.");
        return targetId;
      }
      if (value === "$target.x" || value === "$target.y") {
        if (point === undefined) throw new InvalidRequest("This recipe needs a screen or world point target.");
        return value === "$target.x" ? point.x : point.y;
      }
      throw new InvalidRequest(`Unknown placeholder ${value}`);
    }
    return value;
  };

  const schema = stepSchema();
  return recipe.steps.map((raw, index) => {
    const filled = Object.fromEntries(Object.entries(raw).map(([key, value]) => [key, substitute(value)]));
    const parsed = schema.safeParse(filled);
    if (!parsed.success) {
      throw new InvalidRequest(`Recipe step ${index} is not an allowed input step: ${parsed.error.issues[0]?.message ?? "invalid"}`);
    }
    return parsed.data;
  });
}

/// key/button 을 누른 뒤 다음 입력 전에 한 프레임을 둔다. down 과 up 이 같은 프레임에 들어가지 않게 한다.
export function withFrameGaps(plan: Step[]): Step[] {
  const out: Step[] = [];
  plan.forEach((step, index) => {
    out.push(step);
    const next = plan[index + 1];
    if (next === undefined) return;
    const pressed = step.method === "mouse_down" || step.method === "key_down"
      || (step.method === "set_button" && step.pressed) || (step.method === "set_axis" && step.value !== 0);
    const nextIsInput = next.method !== "waitFrames" && next.method !== "waitCondition";
    if (pressed && nextIsInput) out.push({ method: "waitFrames", frames: 1 });
  });
  return out;
}

async function checkTarget(
  client: PlayClient,
  recipe: Recipe | undefined,
  target: NonNullable<ActInput["action"]>["target"],
  budgetMs: number,
): Promise<string | undefined> {
  const entity = recipe?.entity ?? (target !== undefined && isEntityRef(target) ? target : undefined);
  if (entity === undefined) return undefined;
  try {
    const raw = await client.call("play_query_space", [{ kind: "entity_input_check", ref: entity }], budgetMs);
    if (isRecord(raw) && raw.ok === false) {
      return typeof raw.reason === "string" ? raw.reason : "The target cannot receive input right now.";
    }
    return undefined;
  } catch (error) {
    if (error instanceof PlayCallError && (error.code === "stale_ref" || error.code === "unknown_entity")) return error.message;
    if (error instanceof PlayCallError && error.code === "unsupported_capability") return undefined;
    throw error;
  }
}

function skipRest(plan: Step[], steps: StepResult[], from: number): void {
  for (let index = from; index < plan.length; index++) {
    steps.push({ index, method: (plan[index] as Step).method, status: "skipped", elapsedMs: 0 });
  }
}

function classifyStepFailure(result: StepResult, outer: AbortSignal, pastDeadline: boolean): Termination {
  if (outer.aborted) return "cancelled";
  if (result.error !== undefined && /closed|disconnect|Nothing is listening/i.test(result.error)) return "disconnected";
  if (result.error !== undefined && /^busy:/i.test(result.error)) return "busy";
  if (pastDeadline || (result.error !== undefined && /timed out|timeout/i.test(result.error))) return "timeout";
  return "finished";
}

async function executeStep(
  deps: ActDeps,
  step: Step,
  index: number,
  held: HeldState,
  remaining: () => number,
  signal: AbortSignal,
  scope: PlayScope,
): Promise<StepResult> {
  const result: StepResult = { index, method: step.method, status: "completed", elapsedMs: 0 };

  try {
    if (step.method === "waitFrames") {
      await deps.client.call("wait_frames", [step.frames], remaining());
      return result;
    }
    if (step.method === "waitCondition") {
      const predicate = parsePredicate(step.until, `steps[${index}].until`);
      const needs = collectNeeds([predicate]);
      const limit = Math.min(remaining(), step.timeoutMs ?? 2_000);
      const stepStart = (deps.now ?? (() => performance.now()))();
      for (;;) {
        const sample = await deps.client.sample(needs, scope, Math.max(1, remaining()));
        const check = evaluateAll([predicate], { after: sample });
        if (check.result === "true") return result;
        const elapsed = (deps.now ?? (() => performance.now()))() - stepStart;
        if (elapsed + POLL_INTERVAL_MS >= limit || signal.aborted) {
          return { ...result, status: "failed", error: `waitCondition timed out: ${check.evidence.map((item) => item.detail).join("; ")}` };
        }
        await deps.sleep(POLL_INTERVAL_MS, signal);
      }
    }

    const wire = toWire(step);
    if (!isMutatingMethod(wire.method)) throw new InvalidRequest(`${wire.method} is not an input step`);
    const returned = await deps.client.call(wire.method, wire.params, remaining());
    trackHolds(held, step);
    if (isRecord(returned) && typeof returned.frame === "number") result.frame = returned.frame;
    return result;
  } catch (error) {
    // 실패했어도 보냈을 수 있으므로 press 계열은 놓을 대상으로 남긴다.
    trackHolds(held, step);
    return { ...result, status: "failed", error: error instanceof Error ? error.message : String(error) };
  }
}

function trackHolds(held: HeldState, step: Step): void {
  switch (step.method) {
    case "key_down": held.keys.add(step.key); break;
    case "key_up": held.keys.delete(step.key); break;
    case "mouse_down": held.buttons.add(step.button); break;
    case "mouse_up": held.buttons.delete(step.button); break;
    case "set_axis": if (step.value === 0) held.axes.delete(step.name); else held.axes.add(step.name); break;
    case "set_button": if (step.pressed) held.named.add(step.name); else held.named.delete(step.name); break;
    default: break;
  }
}

async function sceneGuard(client: PlayClient, expectedScene: string, budgetMs: number): Promise<"same" | "scene_changed" | "unknown"> {
  try {
    const raw = await client.call("play_checkpoint", [expectedScene], Math.min(budgetMs, 2_000));
    if (isRecord(raw) && raw.sceneChanged === true) return "scene_changed";
    return "same";
  } catch {
    return "unknown";
  }
}

/// 이 operation 이 누른 key, 버튼, 축만 놓는다. 실패해도 다음 release 를 계속한다.
async function releaseOwned(client: PlayClient, held: HeldState): Promise<void> {
  const releases: Array<{ method: string; params: unknown[] }> = [];
  for (const key of held.keys) releases.push({ method: "key_up", params: [key] });
  for (const button of held.buttons) releases.push({ method: "mouse_up", params: [button] });
  for (const axis of held.axes) releases.push({ method: "set_axis", params: [axis, 0] });
  for (const name of held.named) releases.push({ method: "set_button", params: [name, false] });
  for (const release of releases) {
    try {
      await client.call(release.method, release.params, RELEASE_TIMEOUT_MS);
    } catch {
      // 연결이 끊겼으면 Unity 가 세션 종료 때 놓는다. 나머지 release 는 계속 시도한다.
    }
  }
  held.keys.clear();
  held.buttons.clear();
  held.axes.clear();
  held.named.clear();
}

/// step 을 Unity wire method 와 위치 인자로 바꾼다. 인자 순서는 Unity 쪽 protocol 이다.
function toWire(step: Step): { method: string; params: unknown[] } {
  switch (step.method) {
    case "button_click":
    case "pointer_click":
    case "pointer_hover":
      return { method: step.method, params: [step.targetId] };
    case "pointer_drag":
      return { method: step.method, params: [step.sourceId, step.targetId] };
    case "enter_text":
      return { method: step.method, params: [step.targetId, step.text] };
    case "move_mouse":
      return { method: step.method, params: [step.x, step.y] };
    case "mouse_down":
    case "mouse_up":
      return { method: step.method, params: [step.button] };
    case "key_click":
      return { method: step.method, params: [step.key, step.seconds] };
    case "key_down":
    case "key_up":
      return { method: step.method, params: [step.key] };
    case "set_axis":
      return { method: step.method, params: [step.name, step.value] };
    case "set_button":
      return { method: step.method, params: [step.name, step.pressed] };
    case "waitFrames":
    case "waitCondition":
      throw new InvalidRequest(`${step.method} is not an input step`);
  }
}
