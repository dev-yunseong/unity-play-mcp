import { z } from "zod";

import type { JsonValue } from "./pulse.js";
import { nameLooksSecret, redactSecrets } from "./secrets.js";

/// 새 observe/act 도구 묶음이 Unity 와 주고받는 공통 계약.
///
/// 게임 종류를 가정하지 않는다. `card`, `mana` 같은 낱말이나 특정 씬·클래스 이름은 여기에 없어야 한다.

/// 이 server 가 이해하는 wire protocol 버전. Unity 가 같은 값을 보고해야 새 도구를 쓴다.
export const PLAY_PROTOCOL_VERSION = 1;

/// 응답 schema 버전. 필드를 빼거나 뜻을 바꿀 때만 올린다.
export const PLAY_SCHEMA_VERSION = 1;

export const PLAY_LIMITS = {
  observe: {
    timeoutMs: { default: 5_000, max: 30_000 },
    entities: { default: 50, max: 200 },
    factsPerEntity: { default: 20, max: 100 },
    events: { default: 100, max: 500 },
    textBytes: { default: 32_768, max: 131_072 },
  },
  act: {
    timeoutMs: { default: 5_000, max: 30_000 },
    maxObservationAgeMs: 2_000,
    maxSteps: 32,
    maxWaitFrames: 600,
  },
  operations: { keep: 256, keepMs: 5 * 60_000 },
  events: { capacity: 2_048, bytes: 2 * 1024 * 1024 },
  continuationTtlMs: 30_000,
} as const;

export type PlayScope = "player" | "debug";

export interface EntityRef {
  sessionId: string;
  id: number;
  generation: number;
}

export interface Stamp {
  /// Play session 과 assembly reload 경계. reading 재시작과 다르다.
  sessionId: string;
  run?: string;
  reading?: number;
  scene: string;
  frame: number;
  /// wall-clock timeout 용 단조 시계. 서버가 받은 시각과 다르다.
  sampledAtMonotonicMs: number;
  /// scaled game time. pause 중에는 멈출 수 있다.
  gameTimeSeconds: number;
}

export type FactStatus = "known" | "unknown" | "unsupported";
export type FactSource = "runtime" | "analysis" | "provider";

export interface Fact {
  name?: string;
  value?: unknown;
  unit?: string;
  status: FactStatus;
  source: FactSource;
  evidence: { entity?: EntityRef; component?: string; member?: string; providerId?: string };
  reason?: string;
}

/// Unity 가 돌려주는 엔터티 하나. `Fact` 로 근거를 붙인다.
export interface PlayEntity {
  ref: EntityRef;
  label: string;
  path?: string;
  types: string[];
  active: boolean;
  transform?: unknown;
  bounds?: unknown;
  screenRect?: { x: number; y: number; width: number; height: number };
  state?: Record<string, unknown>;
  facts: Fact[];
  actions: PlayActionSummary[];
  [key: string]: unknown;
}

export interface PlayActionSummary {
  actionRef: string;
  kind: string;
  label?: string;
  availability?: Availability;
  [key: string]: unknown;
}

export type Availability = "available" | "unavailable" | "unknown";

export interface ErrorBody {
  code: string;
  message: string;
  [key: string]: unknown;
}

/// zod 는 draft-07 로 변환되므로 tool 입력에 recursive schema 를 쓰지 않는다(`schema.test.ts`).
/// 입력 schema 는 느슨한 object 로 받고 아래 파서가 정확히 검증한다.
const finiteNumber = () => z.number().finite();

export const entityRefSchema = () => z.object({
  sessionId: z.string().min(1),
  id: z.number().int(),
  generation: z.number().int().nonnegative(),
}).strict();

export const screenPointSchema = () => z.object({
  space: z.literal("screen"),
  x: finiteNumber(),
  y: finiteNumber(),
}).strict();

export const worldPointSchema = () => z.object({
  space: z.enum(["world2d", "world3d"]),
  x: finiteNumber(),
  y: finiteNumber(),
  z: finiteNumber().optional(),
}).strict();

export type ScreenPoint = z.infer<ReturnType<typeof screenPointSchema>>;
export type WorldPoint = z.infer<ReturnType<typeof worldPointSchema>>;

export const filterSchema = () => z.object({
  name: z.string().min(1).optional(),
  component: z.string().min(1).optional(),
  selector: z.string().min(1).optional(),
}).strict();

export type PlayFilter = z.infer<ReturnType<typeof filterSchema>>;

export const includeSchema = () => z.array(z.enum(["entities", "actions", "facts", "changes", "image", "transform"])).min(1);

export type PlayInclude = "entities" | "actions" | "facts" | "changes" | "image";

export const limitsSchema = () => z.object({
  entities: z.number().int().positive().max(PLAY_LIMITS.observe.entities.max).optional(),
  factsPerEntity: z.number().int().positive().max(PLAY_LIMITS.observe.factsPerEntity.max).optional(),
  events: z.number().int().positive().max(PLAY_LIMITS.observe.events.max).optional(),
  textBytes: z.number().int().positive().max(PLAY_LIMITS.observe.textBytes.max).optional(),
}).strict();

export interface ResolvedLimits {
  entities: number;
  factsPerEntity: number;
  events: number;
  textBytes: number;
}

export function resolveLimits(limits: z.infer<ReturnType<typeof limitsSchema>> | undefined): ResolvedLimits {
  return {
    entities: limits?.entities ?? PLAY_LIMITS.observe.entities.default,
    factsPerEntity: limits?.factsPerEntity ?? PLAY_LIMITS.observe.factsPerEntity.default,
    events: limits?.events ?? PLAY_LIMITS.observe.events.default,
    textBytes: limits?.textBytes ?? PLAY_LIMITS.observe.textBytes.default,
  };
}

export const observeOptionsSchema = () => z.object({
  scope: z.enum(["player", "debug"]).optional(),
  filter: filterSchema().optional(),
  include: includeSchema().optional(),
  sinceObservationId: z.string().min(1).optional(),
  freshness: z.enum(["current", "cached"]).optional(),
  maxAgeMs: z.number().int().nonnegative().optional(),
  timeoutMs: z.number().int().positive().max(PLAY_LIMITS.observe.timeoutMs.max).optional(),
  limits: limitsSchema().optional(),
  continuation: z.string().min(1).optional(),
  imageMaxEdge: z.number().int().positive().optional(),
}).strict();

export type ObserveOptions = z.infer<ReturnType<typeof observeOptionsSchema>>;

/// 응답 공통 envelope 의 필드. 모든 새 도구가 싣는다.
export interface Envelope {
  schemaVersion: number;
  protocolVersion: number;
  scope: PlayScope;
  policy: {
    scope: PlayScope;
    visibilityBasis: string;
    visibilityGuarantee: "none" | "provider";
    secretsRedacted: true;
  };
  partial: boolean;
  omittedCounts: Record<string, number>;
  warnings: string[];
}

export function envelope(
  scope: PlayScope,
  policy: Partial<Envelope["policy"]> = {},
): Envelope {
  return {
    schemaVersion: PLAY_SCHEMA_VERSION,
    protocolVersion: PLAY_PROTOCOL_VERSION,
    scope,
    policy: {
      scope,
      visibilityBasis: scope === "player" ? "renderer-or-ui-screen-rect" : "none",
      visibilityGuarantee: "none",
      secretsRedacted: true,
      ...policy,
    },
    partial: false,
    omittedCounts: {},
    warnings: [],
  };
}

/// 오류를 코드가 있는 구조로 돌려준다. 문자열 비교 없이 agent 가 분기하도록 한다.
export function errorBody(code: string, message: string, extra: Record<string, unknown> = {}): ErrorBody {
  return { code, message, ...extra };
}

export function utf8Bytes(value: unknown): number {
  return Buffer.byteLength(typeof value === "string" ? value : JSON.stringify(value) ?? "", "utf8");
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function entityKey(ref: EntityRef): string {
  return `${ref.sessionId}:${ref.id}:${ref.generation}`;
}

export function isEntityRef(value: unknown): value is EntityRef {
  return isRecord(value)
    && typeof value.sessionId === "string"
    && Number.isInteger(value.id)
    && Number.isInteger(value.generation);
}

/// 식별자나 cursor 처럼 값이 비밀이 아니라 구조인 키. 이름이 `sessionId` 처럼 비밀 이름과 겹쳐도 가리지 않는다.
const STRUCTURAL_KEYS = new Set([
  "sessionId", "observationId", "operationId", "cursor", "afterCursor", "eventCursor", "oldestCursor",
  "continuation", "actionRef", "run", "providerId", "id", "generation", "ref", "entity", "kind", "code",
]);

/// Unity 가 준 payload 에 기존 비밀 제거 규칙(#72)을 적용한다.
///
/// 새 도구의 모든 출력(사실, 상태, 이벤트, 조건 진단, 공간 질의)은 이 함수나 `redactSecrets` 를 거친다.
/// 구조 키(`sessionId`, `cursor` 등)는 값의 모양이 아니라 식별자이므로 그대로 둔다.
export function redactPayload(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(redactPayload);
  if (value === null || typeof value !== "object") return value;
  const out: Record<string, unknown> = {};
  for (const [key, inner] of Object.entries(value as Record<string, unknown>)) {
    if (STRUCTURAL_KEYS.has(key) && (typeof inner !== "object" || inner === null)) {
      out[key] = inner;
    } else if (STRUCTURAL_KEYS.has(key)) {
      out[key] = redactPayload(inner);
    } else if (typeof inner === "string" || (typeof inner === "object" && inner !== null && !Array.isArray(inner))
      || Array.isArray(inner)) {
      out[key] = typeof inner === "string"
        ? redactSecrets(key, inner)
        : redactNested(key, inner);
    } else {
      out[key] = inner;
    }
  }
  return out;
}

function redactNested(key: string, value: unknown): unknown {
  // 이름이 비밀처럼 보이는 키 아래는 통째로 가리고, 아니면 안쪽 키를 다시 본다.
  if (nameLooksSecret(key)) return redactSecrets(key, value as JsonValue);
  return redactPayload(value);
}
