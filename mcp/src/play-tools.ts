import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import type { UnityConnection } from "./connection.js";
import type { PulseStore } from "./pulse.js";
import { actAndObserve, actInputSchema, defaultSleep, type ActInput, type ActResponse } from "./play-act.js";
import { PlayCallError, PlayClient, type PlayTransport } from "./play-client.js";
import { EventLog } from "./play-events.js";
import { defaultInputGate, InputGate, OperationLedger } from "./play-operations.js";
import { failure, PlayObserver, type ObserveResult } from "./play-observe.js";
import { parsePredicates, PredicateError } from "./play-predicate.js";
import { watchEvents } from "./play-watch.js";
import {
  PLAY_LIMITS,
  PLAY_PROTOCOL_VERSION,
  PLAY_SCHEMA_VERSION,
  entityRefSchema,
  envelope,
  errorBody,
  isRecord,
  observeOptionsSchema,
  redactPayload,
  type PlayScope,
} from "./play-types.js";

type Content =
  | { type: "text"; text: string }
  | { type: "image"; data: string; mimeType: string };

interface PlayToolResponse {
  [key: string]: unknown;
  content: Content[];
  structuredContent?: Record<string, unknown>;
  isError?: boolean;
}

/// 구조화 결과는 structuredContent 와 같은 compact JSON text 로 함께 준다. 이미지는 별도 block 이다.
function respond(body: Record<string, unknown>, isError: boolean, image?: { mimeType: string; data: string }): PlayToolResponse {
  const safe = redactPayload(body) as Record<string, unknown>;
  const content: Content[] = [{ type: "text", text: JSON.stringify(safe) }];
  if (image !== undefined) content.push({ type: "image", data: image.data, mimeType: image.mimeType });
  return { content, structuredContent: safe, ...(isError ? { isError: true } : {}) };
}

const scopeField = () => z.enum(["player", "debug"]).optional();

const spacePointSchema = () => z.object({
  space: z.enum(["screen", "image", "world2d", "world3d"]),
  x: z.number().finite(),
  y: z.number().finite(),
  z: z.number().finite().optional(),
  /// `space: "image"` 일 때 필수. 이미지 좌표를 화면 픽셀로 바꾸는 capture 메타데이터를 명시한다.
  capture: z.object({
    observationId: z.string().min(1),
    region: z.object({ x: z.number(), y: z.number(), width: z.number(), height: z.number() }).strict(),
    scale: z.object({ x: z.number().positive(), y: z.number().positive() }).strict(),
  }).strict().optional(),
}).strict();

const vec3 = () => z.object({ x: z.number().finite(), y: z.number().finite(), z: z.number().finite().optional() }).strict();

const querySchema = () => z.object({
  kind: z.enum(["hit_test", "entities_in_region", "project_world_to_screen", "project_screen_to_world", "line_test"]),
  point: spacePointSchema().optional(),
  region: z.object({
    shape: z.enum(["screen_rect", "world_sphere", "world_box"]),
    x: z.number().optional(), y: z.number().optional(), width: z.number().optional(), height: z.number().optional(),
    center: vec3().optional(),
    radius: z.number().positive().optional(),
    size: vec3().optional(),
    dimension: z.enum(["2d", "3d"]).optional(),
  }).strict().optional(),
  camera: entityRefSchema().optional(),
  /// 화면 점을 world 로 옮길 명시적 평면. 없으면 추측하지 않고 실패한다.
  plane: z.object({ point: vec3(), normal: vec3() }).strict().optional(),
  colliderRef: entityRefSchema().optional(),
  from: z.union([entityRefSchema(), spacePointSchema()]).optional(),
  to: z.union([entityRefSchema(), spacePointSchema()]).optional(),
  dimension: z.enum(["2d", "3d"]).optional(),
  layerMask: z.number().int().optional(),
  includeTriggers: z.boolean().optional(),
  limit: z.number().int().positive().max(PLAY_LIMITS.observe.entities.max).optional(),
}).strict();

export interface PlayToolsOptions {
  client?: PlayClient;
  observer?: PlayObserver;
  log?: EventLog;
  gate?: InputGate;
  sleep?: (ms: number, signal?: AbortSignal) => Promise<void>;
}

/// 새 관찰·행동 도구 여섯 개를 등록한다. 기존 도구는 건드리지 않는다.
export function registerPlayTools(
  server: McpServer,
  connection: UnityConnection,
  store: PulseStore,
  options: PlayToolsOptions = {},
): { client: PlayClient; observer: PlayObserver; log: EventLog } {
  const client = options.client ?? new PlayClient(connection as unknown as PlayTransport);
  const log = options.log ?? new EventLog();
  const observer = options.observer ?? new PlayObserver(client, undefined, () => log.head());
  const gate = options.gate ?? defaultInputGate;
  const ledger = new OperationLedger<ActResponse>();
  const sleep = options.sleep ?? defaultSleep;

  // 기본 event 는 reading 사이의 차이에서 만든다. 별도 polling 은 하지 않는다.
  store.onReading((state) => log.observeReading(state));
  connection.onDisconnect?.(() => log.noteDisconnected());
  client.onSessionChange((sessionId) => log.changeSession(sessionId));

  server.registerTool("get_play_capabilities", {
    description: "Report what the connected Unity Play MCP runtime supports: protocol/runtime/server versions, the new tools, input paths, 2D/3D physics and UI support, registered semantic providers, allowed observation scopes, and limits. "
      + "Fails with unsupported_capability when the Unity package is older than this server; the existing tools keep working.",
    inputSchema: {},
  }, async () => {
    try {
      const capabilities = await client.capabilities();
      return respond({
        ...envelope("player"),
        server: {
          protocolVersion: PLAY_PROTOCOL_VERSION,
          schemaVersion: PLAY_SCHEMA_VERSION,
          tools: ["get_play_capabilities", "observe", "inspect_action", "query_space", "act_and_observe", "watch_events"],
        },
        unity: capabilities,
        limits: PLAY_LIMITS,
      }, false);
    } catch (error) {
      return respond({ ...envelope("player"), error: describeError(error), server: { protocolVersion: PLAY_PROTOCOL_VERSION } }, true);
    }
  });

  server.registerTool("observe", {
    description: "Take one coherent snapshot to decide the next move: a fresh Unity sample (even when nothing changed, with a new stamp) of entities with labels, types, transforms, bounds, states and typed facts (known/unknown/unsupported with source and evidence), available action summaries, changes since a previous observation, and optionally a screenshot from the same end-of-frame barrier. "
      + "scope 'player' (default) reports visible UI/renderers and public facts; renderer visibility is not a fog-of-war guarantee (see policy.visibilityGuarantee). 'debug' widens introspection; credentials are always redacted. "
      + "If the image and state could not be taken in one frame, coherent is false with both stamps and frameDelta. Use sinceObservationId for a diff (a stale cursor returns baseline_required with a new baseline). Large results are truncated with omittedCounts and a continuation cursor valid for 30 seconds. "
      + "Returns eventCursor to pass to watch_events, and entity refs whose generation protects against reused instance ids.",
    inputSchema: observeOptionsSchema().shape,
  }, async (args) => {
    const result = await observer.observe(args);
    if (!result.ok) return respond(result.body, true);
    return respond(result.body, false, (result as ObserveResult).image);
  });

  server.registerTool("inspect_action", {
    description: "Explain one action from observe: its normal-input recipe (a short list of typed steps), availability (available/unavailable/unknown), preconditions with evidence, target constraints, cost/cooldown/expected-effect facts, and outcome predicates you can pass to act_and_observe. "
      + "expectedEffects are declared or inferred; observedEffects only appear after acting. Static-analysis hints are not guarantees, and unknown costs are never treated as zero.",
    inputSchema: { actionRef: z.string().min(1), scope: scopeField() },
  }, async ({ actionRef, scope }) => {
    const effective: PlayScope = scope ?? "player";
    try {
      await client.capabilities();
      const raw = await client.call("play_inspect", [{ actionRef, scope: effective }], 5_000);
      return respond({ ...envelope(effective), ...(isRecord(raw) ? raw : { result: raw }) }, false);
    } catch (error) {
      return respond({ ...envelope(effective), error: describeError(error) }, true);
    }
  });

  server.registerTool("query_space", {
    description: "Ask a geometry question with an explicit coordinate space: hit_test (which UI/2D/3D object would receive input at a screen point, and what blocks it), entities_in_region (observable entities in a screen rect, world sphere or world box, with distances), project_world_to_screen / project_screen_to_world (camera and units stated; a screen point needs an explicit plane or collider to reach the world), or line_test (physics occlusion between two points/entities; not a game attack rule). "
      + "Screen points are top-left-origin Unity Screen pixels; to use image pixels pass space 'image' with the observation's capture region and scale. 2D is XY, 3D is XYZ; no floor normal is assumed. Several cameras without a chosen one return ambiguous_camera. Walkability, target validity and best placement are unknown unless a provider says otherwise.",
    inputSchema: { query: querySchema(), scope: scopeField() },
  }, async ({ query, scope }) => {
    const effective: PlayScope = scope ?? "player";
    let prepared;
    try {
      prepared = prepareQuery(query);
    } catch (error) {
      return respond({ ...envelope(effective), error: errorBody("invalid_request", error instanceof Error ? error.message : String(error)) }, true);
    }
    try {
      await client.capabilities();
      const raw = await client.call("play_query_space", [{ ...prepared, scope: effective }], 5_000);
      return respond({ ...envelope(effective), result: raw }, false);
    } catch (error) {
      return respond({ ...envelope(effective), error: describeError(error) }, true);
    }
  });

  server.registerTool("act_and_observe", {
    description: "Run one finite input and report what happened, in separate fields: execution (which steps Unity accepted), outcome (confirmed / not_observed / unknown, judged only by your expect predicates or the action's provider outcome predicates), termination, and one final observation. "
      + "An accepted input never means in-game success; with no expect the outcome is unknown. Observed changes are temporal associations, not proof of causation. "
      + "Give operationId (retry-safe: the same id with the same request returns the earlier result without sending input again; a different request with the same id is a conflict), basedOnObservationId, and either action {actionRef, target?} or explicit steps (max 32: buttons, pointer, keys, axes, waitFrames, waitCondition; never loops or code). "
      + "Down and up are delivered on different frames. Preconditions and the target's active/interactable/blocker state are re-checked just before input. Only one operation owns the input at a time; another client gets busy. On timeout, cancel, disconnect or scene change, only keys/buttons/axes this operation pressed are released and remaining steps are not run. Predicates: all/any/not, entityExists, entityAbsent, active, interactable, member (eq/ne/lt/lte/gt/gte/changed), fact, sceneIs, eventMatches; missing or mistyped values are unknown, never a pass.",
    inputSchema: actInputSchema(),
  }, async (args, extra) => {
    const response = await actAndObserve(
      { client, observer, gate, ledger, sleep, eventsSince: (cursor) => log.read(cursor, "debug").events, eventsHead: () => log.head() },
      args as ActInput,
      extra.signal,
    );
    return respond(response.body, response.isError, response.image);
  });

  server.registerTool("watch_events", {
    description: "Wait, once and bounded, for something worth reacting to instead of polling: entity spawn/despawn/activation, property changes, scene/session changes, and events a game provider emitted (with provider provenance and the frame they occurred on). "
      + "Pass afterCursor from observe.eventCursor or a previous watch_events; a lost or old cursor returns gap/baselineRequired. Default events are differences between scene readings, so changes that start and end between readings are not recorded. "
      + "Always returns within timeoutMs (default 5000, max 30000) with the current cursor; no event does not mean the connection dropped. Player scope never reports hidden entities or values. Optional predicates end the wait early when true.",
    inputSchema: {
      afterCursor: z.string().min(1).optional(),
      filter: z.object({
        entityId: z.number().int().optional(),
        component: z.string().min(1).optional(),
        member: z.string().min(1).optional(),
        providerId: z.string().min(1).optional(),
        kinds: z.array(z.string().min(1)).optional(),
        severity: z.string().min(1).optional(),
      }).strict().optional(),
      predicates: z.array(z.record(z.unknown())).optional(),
      limit: z.number().int().positive().max(PLAY_LIMITS.observe.events.max).optional(),
      timeoutMs: z.number().int().positive().max(PLAY_LIMITS.observe.timeoutMs.max).optional(),
      scope: scopeField(),
    },
  }, async (args, extra) => {
    const scope: PlayScope = args.scope ?? "player";
    let predicates;
    try {
      predicates = parsePredicates(args.predicates, "predicates");
    } catch (error) {
      if (error instanceof PredicateError) {
        return respond({ ...envelope(scope), error: errorBody("invalid_request", error.message) }, true);
      }
      throw error;
    }
    const result = await watchEvents({ client, log }, { ...args, predicates }, extra.signal);
    return respond(result.body, result.isError);
  });

  return { client, observer, log };
}

function describeError(error: unknown): Record<string, unknown> {
  if (error instanceof PlayCallError) return errorBody(error.code, error.message, error.detail);
  return failure("player", error).body.error as Record<string, unknown>;
}

/// image 좌표를 화면 픽셀로 바꾸고 kind 별 필수 값을 확인한다.
function prepareQuery(query: z.infer<ReturnType<typeof querySchema>>): Record<string, unknown> {
  const withScreen = (point: z.infer<ReturnType<typeof spacePointSchema>> | undefined) => {
    if (point === undefined) throw new Error(`${query.kind} needs a point.`);
    if (point.space !== "image") {
      const { capture: _capture, ...rest } = point;
      return rest;
    }
    if (point.capture === undefined) {
      throw new Error("An image point needs capture {observationId, region, scale} from the observation that produced the image.");
    }
    const { region, scale } = point.capture;
    return {
      space: "screen",
      x: region.x + point.x / scale.x,
      y: region.y + point.y / scale.y,
      convertedFrom: { space: "image", observationId: point.capture.observationId },
    };
  };

  switch (query.kind) {
    case "hit_test": {
      const point = withScreen(query.point);
      if (point.space !== "screen") throw new Error("hit_test takes a screen (or image) point.");
      return { kind: query.kind, point };
    }
    case "entities_in_region":
      if (query.region === undefined) throw new Error("entities_in_region needs a region.");
      return { kind: query.kind, region: query.region, ...(query.limit === undefined ? {} : { limit: query.limit }) };
    case "project_world_to_screen": {
      const point = query.point;
      if (point === undefined || (point.space !== "world2d" && point.space !== "world3d")) {
        throw new Error("project_world_to_screen needs a world2d or world3d point.");
      }
      const { capture: _capture, ...rest } = point;
      return { kind: query.kind, point: rest, ...(query.camera === undefined ? {} : { camera: query.camera }) };
    }
    case "project_screen_to_world": {
      const point = withScreen(query.point);
      if (query.plane === undefined && query.colliderRef === undefined) {
        throw new Error("project_screen_to_world needs an explicit plane or colliderRef; no ground is assumed.");
      }
      return {
        kind: query.kind, point,
        ...(query.plane === undefined ? {} : { plane: query.plane }),
        ...(query.colliderRef === undefined ? {} : { colliderRef: query.colliderRef }),
        ...(query.camera === undefined ? {} : { camera: query.camera }),
      };
    }
    case "line_test":
      if (query.from === undefined || query.to === undefined || query.dimension === undefined) {
        throw new Error("line_test needs from, to and an explicit dimension (2d or 3d).");
      }
      return {
        kind: query.kind, from: query.from, to: query.to, dimension: query.dimension,
        ...(query.layerMask === undefined ? {} : { layerMask: query.layerMask }),
        includeTriggers: query.includeTriggers ?? false,
      };
  }
}
