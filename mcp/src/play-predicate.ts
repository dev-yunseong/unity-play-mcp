import { holdsRedaction, redactSecrets } from "./secrets.js";
import type { JsonValue } from "./pulse.js";
import { entityKey, isEntityRef, isRecord, type EntityRef, type Stamp } from "./play-types.js";

/// Predicate v1.
///
/// 임의 표현식이나 eval 은 받지 않는다. 결과는 세 값(`true`/`false`/`unknown`)이다. 대상이 없거나
/// 자료형이 맞지 않으면 `unknown` 이고, `unknown` 은 조건을 통과한 것이 아니다.

export type Primitive = string | number | boolean | null;
export type Tri = "true" | "false" | "unknown";

export type EntityTarget =
  | { ref: EntityRef }
  | { name: string; component?: string };

export type CompareOp = "eq" | "ne" | "lt" | "lte" | "gt" | "gte" | "changed";

export interface MemberTerm {
  target: EntityTarget;
  component: string;
  name: string;
  op: CompareOp;
  value?: Primitive;
  /// 값의 단위를 요구할 때만 준다. 관찰한 단위와 다르면 `unknown` 이다.
  unit?: string;
}

export interface FactTerm {
  providerId: string;
  name: string;
  target?: EntityTarget;
  op: CompareOp;
  value?: Primitive;
  unit?: string;
}

export interface EventTerm {
  kind?: string;
  providerId?: string;
  name?: string;
  entity?: EntityRef;
  component?: string;
  member?: string;
}

export type Predicate =
  | { all: Predicate[] }
  | { any: Predicate[] }
  | { not: Predicate }
  | { entityExists: EntityTarget }
  | { entityAbsent: EntityTarget }
  | { active: EntityTarget }
  | { interactable: EntityTarget }
  | { member: MemberTerm }
  | { fact: FactTerm }
  | { sceneIs: string }
  | { eventMatches: EventTerm };

const MAX_DEPTH = 8;
const MAX_NODES = 64;

/// 파싱 실패. 입력 검증 오류이므로 Unity 에 아무것도 보내기 전에 낸다.
export class PredicateError extends Error {
  constructor(message: string, readonly path: string) {
    super(`${path}: ${message}`);
    this.name = "PredicateError";
  }
}

function isPrimitive(value: unknown): value is Primitive {
  return value === null || typeof value === "string" || typeof value === "boolean"
    || (typeof value === "number" && Number.isFinite(value));
}

function parseTarget(value: unknown, path: string): EntityTarget {
  if (!isRecord(value)) throw new PredicateError("expected an entity target object", path);
  if ("ref" in value) {
    if (!isEntityRef(value.ref)) throw new PredicateError("ref must be {sessionId,id,generation}", path);
    return { ref: value.ref };
  }
  if (typeof value.name === "string" && value.name.length > 0) {
    if (value.component !== undefined && typeof value.component !== "string") {
      throw new PredicateError("component must be a string", path);
    }
    return { name: value.name, ...(value.component === undefined ? {} : { component: value.component }) };
  }
  throw new PredicateError("an entity target needs ref or name", path);
}

const OPS = new Set<CompareOp>(["eq", "ne", "lt", "lte", "gt", "gte", "changed"]);

function parseOp(record: Record<string, unknown>, path: string): { op: CompareOp; value?: Primitive } {
  if (typeof record.op !== "string" || !OPS.has(record.op as CompareOp)) {
    throw new PredicateError(`op must be one of ${[...OPS].join(", ")}`, path);
  }
  const op = record.op as CompareOp;
  if (op === "changed") return { op };
  if (!isPrimitive(record.value)) throw new PredicateError("value must be a string, finite number, boolean, or null", path);
  if (["lt", "lte", "gt", "gte"].includes(op) && typeof record.value !== "number") {
    throw new PredicateError(`${op} compares numbers only`, path);
  }
  return { op, value: record.value };
}

export function parsePredicate(value: unknown, path = "predicate", state = { nodes: 0 }, depth = 0): Predicate {
  if (depth > MAX_DEPTH) throw new PredicateError(`nesting is deeper than ${MAX_DEPTH}`, path);
  if (++state.nodes > MAX_NODES) throw new PredicateError(`more than ${MAX_NODES} predicate nodes`, path);
  if (!isRecord(value)) throw new PredicateError("expected an object", path);
  const keys = Object.keys(value);
  if (keys.length !== 1) throw new PredicateError("exactly one predicate key is required", path);
  const key = keys[0] as string;
  const body = value[key];

  switch (key) {
    case "all":
    case "any": {
      if (!Array.isArray(body) || body.length === 0) throw new PredicateError(`${key} needs a non-empty array`, path);
      const items = body.map((item, index) => parsePredicate(item, `${path}.${key}[${index}]`, state, depth + 1));
      return key === "all" ? { all: items } : { any: items };
    }
    case "not":
      return { not: parsePredicate(body, `${path}.not`, state, depth + 1) };
    case "entityExists":
    case "entityAbsent":
    case "active":
    case "interactable":
      return { [key]: parseTarget(body, `${path}.${key}`) } as Predicate;
    case "sceneIs":
      if (typeof body !== "string" || body.length === 0) throw new PredicateError("sceneIs needs a scene name", path);
      return { sceneIs: body };
    case "member": {
      if (!isRecord(body)) throw new PredicateError("member needs an object", path);
      if (typeof body.component !== "string" || typeof body.name !== "string") {
        throw new PredicateError("member needs component and name", `${path}.member`);
      }
      const parsed = parseOp(body, `${path}.member`);
      return {
        member: {
          target: parseTarget(body.target, `${path}.member.target`),
          component: body.component,
          name: body.name,
          ...parsed,
          ...(typeof body.unit === "string" ? { unit: body.unit } : {}),
        },
      };
    }
    case "fact": {
      if (!isRecord(body)) throw new PredicateError("fact needs an object", path);
      if (typeof body.providerId !== "string" || typeof body.name !== "string") {
        throw new PredicateError("fact needs providerId and name", `${path}.fact`);
      }
      const parsed = parseOp(body, `${path}.fact`);
      return {
        fact: {
          providerId: body.providerId,
          name: body.name,
          ...(body.target === undefined ? {} : { target: parseTarget(body.target, `${path}.fact.target`) }),
          ...parsed,
          ...(typeof body.unit === "string" ? { unit: body.unit } : {}),
        },
      };
    }
    case "eventMatches": {
      if (!isRecord(body)) throw new PredicateError("eventMatches needs an object", path);
      const term: EventTerm = {};
      for (const field of ["kind", "providerId", "name", "component", "member"] as const) {
        const raw = body[field];
        if (raw === undefined) continue;
        if (typeof raw !== "string") throw new PredicateError(`${field} must be a string`, `${path}.eventMatches`);
        term[field] = raw;
      }
      if (body.entity !== undefined) {
        if (!isEntityRef(body.entity)) throw new PredicateError("entity must be an entity ref", `${path}.eventMatches`);
        term.entity = body.entity;
      }
      if (Object.keys(term).length === 0) throw new PredicateError("eventMatches needs at least one field", path);
      return { eventMatches: term };
    }
    default:
      throw new PredicateError(`unknown predicate "${key}"`, path);
  }
}

export function parsePredicates(values: readonly unknown[] | undefined, label: string): Predicate[] {
  return (values ?? []).map((value, index) => parsePredicate(value, `${label}[${index}]`));
}

// ---------------------------------------------------------------------------------------------
// 무엇을 샘플링해야 하는지

export interface SampleTarget {
  key: string;
  ref?: EntityRef;
  name?: string;
  component?: string;
}

export interface SampleMember {
  target: string;
  component: string;
  member: string;
}

export interface SampleFact {
  target?: string;
  providerId: string;
  name: string;
}

export interface SampleNeeds {
  targets: SampleTarget[];
  members: SampleMember[];
  facts: SampleFact[];
}

export function targetKey(target: EntityTarget): string {
  return "ref" in target ? `ref:${entityKey(target.ref)}` : `name:${target.name}|${target.component ?? ""}`;
}

/// 조건을 평가하는 데 필요한 대상만 모은다. 프레임마다 장면 전체를 직렬화하지 않기 위해서다.
export function collectNeeds(predicates: readonly Predicate[]): SampleNeeds {
  const targets = new Map<string, SampleTarget>();
  const members = new Map<string, SampleMember>();
  const facts = new Map<string, SampleFact>();

  const addTarget = (target: EntityTarget): string => {
    const key = targetKey(target);
    if (!targets.has(key)) {
      targets.set(key, "ref" in target
        ? { key, ref: target.ref }
        : { key, name: target.name, ...(target.component === undefined ? {} : { component: target.component }) });
    }
    return key;
  };

  const visit = (predicate: Predicate): void => {
    if ("all" in predicate) return void predicate.all.forEach(visit);
    if ("any" in predicate) return void predicate.any.forEach(visit);
    if ("not" in predicate) return visit(predicate.not);
    if ("entityExists" in predicate) return void addTarget(predicate.entityExists);
    if ("entityAbsent" in predicate) return void addTarget(predicate.entityAbsent);
    if ("active" in predicate) return void addTarget(predicate.active);
    if ("interactable" in predicate) return void addTarget(predicate.interactable);
    if ("member" in predicate) {
      const target = addTarget(predicate.member.target);
      members.set(`${target}|${predicate.member.component}|${predicate.member.name}`, {
        target, component: predicate.member.component, member: predicate.member.name,
      });
      return;
    }
    if ("fact" in predicate) {
      const target = predicate.fact.target === undefined ? undefined : addTarget(predicate.fact.target);
      facts.set(`${target ?? ""}|${predicate.fact.providerId}|${predicate.fact.name}`, {
        ...(target === undefined ? {} : { target }),
        providerId: predicate.fact.providerId,
        name: predicate.fact.name,
      });
    }
  };
  predicates.forEach(visit);
  return { targets: [...targets.values()], members: [...members.values()], facts: [...facts.values()] };
}

export function hasNeeds(needs: SampleNeeds): boolean {
  return needs.targets.length + needs.members.length + needs.facts.length > 0;
}

// ---------------------------------------------------------------------------------------------
// 샘플 (Unity `play_sample` 응답을 TS 쪽에서 읽은 모양)

export type Lifecycle = "present" | "inactive" | "destroyed" | "unobserved" | "out_of_scope";

export interface TargetSample {
  status: "one" | "none" | "ambiguous";
  count: number;
  ref?: EntityRef;
  lifecycle?: Lifecycle;
  active?: boolean;
  interactable?: boolean;
}

export interface MemberSample {
  target: string;
  component: string;
  member: string;
  status: "known" | "unknown" | "unsupported";
  value?: JsonValue;
  valueType?: string;
  unit?: string;
  reason?: string;
}

export interface FactSample {
  target?: string;
  providerId: string;
  name: string;
  status: "known" | "unknown" | "unsupported";
  value?: JsonValue;
  unit?: string;
  reason?: string;
}

export interface PlaySample {
  stamp: Stamp;
  scope: "player" | "debug";
  targets: Record<string, TargetSample>;
  members: MemberSample[];
  facts: FactSample[];
}

export interface PlayEvent {
  cursor: string;
  sequence: number;
  kind: string;
  stamp: { frame?: number; scene?: string; gameTimeSeconds?: number };
  /// 게임이 준 entity handle. sample 로 만든 event 는 generation 을 모르므로 `entityId` 만 싣는다.
  entity?: EntityRef;
  entityId?: number;
  component?: string;
  member?: string;
  providerId?: string;
  name?: string;
  data?: unknown;
  provenance: "sample" | "provider";
}

export interface EvalContext {
  after: PlaySample;
  /// `changed` 의 기준이 되는 실행 전 샘플.
  before?: PlaySample;
  events?: readonly PlayEvent[];
}

export interface Evidence {
  predicate: string;
  result: Tri;
  detail: string;
  stamp?: Pick<Stamp, "frame" | "scene" | "sessionId">;
}

export interface Evaluation {
  result: Tri;
  evidence: Evidence[];
}

function describeTarget(target: EntityTarget): string {
  return "ref" in target ? `entity ${target.ref.id}#${target.ref.generation}` : `entity named "${target.name}"`;
}

function and(values: Tri[]): Tri {
  if (values.includes("false")) return "false";
  return values.includes("unknown") ? "unknown" : "true";
}

function or(values: Tri[]): Tri {
  if (values.includes("true")) return "true";
  return values.includes("unknown") ? "unknown" : "false";
}

function negate(value: Tri): Tri {
  return value === "true" ? "false" : value === "false" ? "true" : "unknown";
}

function comparable(left: unknown, right: unknown): boolean {
  return typeof left === typeof right && (typeof left === "number" || typeof left === "string" || typeof left === "boolean");
}

function compare(op: Exclude<CompareOp, "changed">, actual: JsonValue, expected: Primitive): Tri {
  if (expected === null || actual === null) {
    if (op === "eq") return actual === expected ? "true" : "false";
    if (op === "ne") return actual === expected ? "false" : "true";
    return "unknown";
  }
  if (!comparable(actual, expected)) return "unknown";
  switch (op) {
    case "eq": return actual === expected ? "true" : "false";
    case "ne": return actual !== expected ? "true" : "false";
    default:
      // 순서 비교는 숫자에만 허용한다. 문자열 사전순은 게임 값의 크기가 아니다.
      if (typeof actual !== "number" || typeof expected !== "number") return "unknown";
      if (op === "lt") return actual < expected ? "true" : "false";
      if (op === "lte") return actual <= expected ? "true" : "false";
      if (op === "gt") return actual > expected ? "true" : "false";
      return actual >= expected ? "true" : "false";
  }
}

function stampBrief(sample: PlaySample) {
  return { frame: sample.stamp.frame, scene: sample.stamp.scene, sessionId: sample.stamp.sessionId };
}

function sameLineage(before: PlaySample, after: PlaySample): boolean {
  return before.stamp.sessionId === after.stamp.sessionId && before.stamp.scene === after.stamp.scene;
}

/// 조건 하나를 평가한다. 근거는 `Evidence` 로 남긴다.
export function evaluatePredicate(predicate: Predicate, context: EvalContext): Evaluation {
  const evidence: Evidence[] = [];
  const result = evaluateInto(predicate, context, evidence);
  return { result, evidence };
}

function evaluateInto(predicate: Predicate, context: EvalContext, out: Evidence[]): Tri {
  const { after } = context;
  const note = (name: string, result: Tri, detail: string): Tri => {
    out.push({ predicate: name, result, detail, stamp: stampBrief(after) });
    return result;
  };

  if ("all" in predicate) return and(predicate.all.map((item) => evaluateInto(item, context, out)));
  if ("any" in predicate) return or(predicate.any.map((item) => evaluateInto(item, context, out)));
  if ("not" in predicate) return negate(evaluateInto(predicate.not, context, out));

  if ("sceneIs" in predicate) {
    const result: Tri = after.stamp.scene === predicate.sceneIs ? "true" : "false";
    return note("sceneIs", result, `scene is "${after.stamp.scene}"`);
  }

  if ("eventMatches" in predicate) {
    const term = predicate.eventMatches;
    const matched = (context.events ?? []).some((event) =>
      (term.kind === undefined || event.kind === term.kind)
      && (term.providerId === undefined || event.providerId === term.providerId)
      && (term.name === undefined || event.name === term.name)
      && (term.component === undefined || event.component === term.component)
      && (term.member === undefined || event.member === term.member)
      && (term.entity === undefined || (event.entity !== undefined
        ? entityKey(event.entity) === entityKey(term.entity)
        : event.entityId === term.entity.id)));
    // 샘플 사이의 짧은 변화는 놓칠 수 있으므로 없다는 것은 `false` 이지 "일어나지 않았다" 의 증명이 아니다.
    return note("eventMatches", matched ? "true" : "false",
      matched ? "a matching event was recorded" : "no matching event was recorded (sampled events can miss short changes)");
  }

  if ("entityExists" in predicate || "entityAbsent" in predicate || "active" in predicate || "interactable" in predicate) {
    const name = Object.keys(predicate)[0] as "entityExists" | "entityAbsent" | "active" | "interactable";
    const target = (predicate as Record<string, EntityTarget>)[name] as EntityTarget;
    return lifecycleResult(name, target, context, note);
  }

  if ("member" in predicate) return memberResult(predicate.member, context, note);
  return factResult((predicate as { fact: FactTerm }).fact, context, note);
}

type Note = (name: string, result: Tri, detail: string) => Tri;

function lookup(sample: PlaySample, target: EntityTarget): TargetSample | undefined {
  return sample.targets[targetKey(target)];
}

function lifecycleResult(
  name: "entityExists" | "entityAbsent" | "active" | "interactable",
  target: EntityTarget,
  context: EvalContext,
  note: Note,
): Tri {
  const label = describeTarget(target);
  const sampled = lookup(context.after, target);
  if (sampled === undefined) return note(name, "unknown", `${label} was not sampled`);
  if (sampled.status === "ambiguous") {
    return note(name, "unknown", `${label} is ambiguous: ${sampled.count} entities match; use an entity ref`);
  }

  const lifecycle: Lifecycle | undefined = sampled.status === "none"
    ? ("ref" in target ? "destroyed" : undefined)
    : sampled.lifecycle;

  if (sampled.status === "none" && lifecycle === undefined) {
    // 이름으로 찾은 대상이 없다. player scope 는 가려진 대상을 뺐을 수 있으므로 없다고 단정하지 않는다.
    if (name === "entityAbsent" && context.after.scope === "debug") return note(name, "true", `no ${label} exists`);
    if (name === "entityExists" && context.after.scope === "debug") return note(name, "false", `no ${label} exists`);
    return note(name, "unknown", `no ${label} is observable in ${context.after.scope} scope; it may be hidden, not destroyed`);
  }

  if (lifecycle === "unobserved" || lifecycle === "out_of_scope") {
    return note(name, "unknown", `${label} is ${lifecycle}; that does not mean it was destroyed`);
  }
  switch (name) {
    case "entityExists":
      return note(name, lifecycle === "destroyed" ? "false" : "true", `${label} is ${lifecycle}`);
    case "entityAbsent":
      return note(name, lifecycle === "destroyed" ? "true" : "false", `${label} is ${lifecycle}`);
    case "active":
      if (lifecycle === "destroyed") return note(name, "false", `${label} is destroyed`);
      return note(name, sampled.active === true ? "true" : "false", `${label} active=${String(sampled.active)}`);
    case "interactable":
      if (lifecycle === "destroyed") return note(name, "false", `${label} is destroyed`);
      if (sampled.interactable === undefined) return note(name, "unknown", `${label} has no interactable state`);
      return note(name, sampled.interactable ? "true" : "false", `${label} interactable=${String(sampled.interactable)}`);
  }
}

function findMember(sample: PlaySample, targetKeyValue: string, component: string, member: string): MemberSample | undefined {
  return sample.members.find((item) => item.target === targetKeyValue && item.component === component && item.member === member);
}

function memberResult(term: MemberTerm, context: EvalContext, note: Note): Tri {
  const key = targetKey(term.target);
  const label = `${describeTarget(term.target)} ${term.component}.${term.name}`;
  const targetSample = lookup(context.after, term.target);
  if (targetSample === undefined || targetSample.status === "none") {
    return note("member", "unknown", `${label}: target is not present`);
  }
  if (targetSample.status === "ambiguous") {
    return note("member", "unknown", `${label}: ${targetSample.count} entities match; use an entity ref`);
  }
  if (targetSample.lifecycle === "destroyed" || targetSample.lifecycle === "unobserved" || targetSample.lifecycle === "out_of_scope") {
    return note("member", "unknown", `${label}: target is ${targetSample.lifecycle}`);
  }

  const now = findMember(context.after, key, term.component, term.name);
  if (now === undefined || now.status !== "known") {
    return note("member", "unknown", `${label}: ${now?.reason ?? "member was not observed"} (${now?.status ?? "missing"})`);
  }
  if (holdsRedaction(now.value)) return note("member", "unknown", `${label}: the value is redacted`);
  if (term.unit !== undefined && term.unit !== now.unit) {
    return note("member", "unknown", `${label}: unit is ${now.unit ?? "not declared"}, not ${term.unit}`);
  }

  if (term.op === "changed") {
    const before = context.before === undefined ? undefined : findMember(context.before, key, term.component, term.name);
    const beforeTarget = context.before === undefined ? undefined : lookup(context.before, term.target);
    if (context.before === undefined || before === undefined || before.status !== "known" || beforeTarget?.status !== "one") {
      return note("member", "unknown", `${label}: no comparable value before the action`);
    }
    // 같은 entity, 같은 단위, 같은 자료형이고 session/scene 이 이어질 때만 비교한다.
    const sameEntity = targetSample.ref !== undefined && beforeTarget.ref !== undefined
      && entityKey(targetSample.ref) === entityKey(beforeTarget.ref);
    if (!sameEntity || !sameLineage(context.before, context.after)
      || before.unit !== now.unit || before.valueType !== now.valueType) {
      return note("member", "unknown", `${label}: the entity, unit, type, session, or scene differs from before`);
    }
    const changed = JSON.stringify(before.value ?? null) !== JSON.stringify(now.value ?? null);
    return note("member", changed ? "true" : "false", `${label} was ${JSON.stringify(before.value)}, now ${JSON.stringify(now.value)}`);
  }

  const result = compare(term.op, now.value ?? null, term.value ?? null);
  return note("member", result, `${label} is ${JSON.stringify(now.value)} (${term.op} ${JSON.stringify(term.value)})`);
}

function factResult(term: FactTerm, context: EvalContext, note: Note): Tri {
  const key = term.target === undefined ? undefined : targetKey(term.target);
  const label = `fact ${term.providerId}/${term.name}`;
  const match = (sample: PlaySample) => sample.facts.find((item) =>
    item.providerId === term.providerId && item.name === term.name && item.target === key);
  const now = match(context.after);
  if (now === undefined || now.status !== "known") {
    return note("fact", "unknown", `${label}: ${now?.reason ?? "the provider did not report it"} (${now?.status ?? "missing"})`);
  }
  if (holdsRedaction(now.value)) return note("fact", "unknown", `${label}: the value is redacted`);
  if (term.unit !== undefined && term.unit !== now.unit) {
    return note("fact", "unknown", `${label}: unit is ${now.unit ?? "not declared"}, not ${term.unit}`);
  }
  if (term.op === "changed") {
    const before = context.before === undefined ? undefined : match(context.before);
    if (context.before === undefined || before === undefined || before.status !== "known"
      || !sameLineage(context.before, context.after) || before.unit !== now.unit) {
      return note("fact", "unknown", `${label}: no comparable value before the action`);
    }
    const changed = JSON.stringify(before.value ?? null) !== JSON.stringify(now.value ?? null);
    return note("fact", changed ? "true" : "false", `${label} was ${JSON.stringify(before.value)}, now ${JSON.stringify(now.value)}`);
  }
  return note("fact", compare(term.op, now.value ?? null, term.value ?? null), `${label} is ${JSON.stringify(now.value)}`);
}

/// 여러 조건을 AND 로 평가한다. 조건이 없으면 결과는 `unknown` 이다.
export function evaluateAll(predicates: readonly Predicate[], context: EvalContext): Evaluation {
  if (predicates.length === 0) return { result: "unknown", evidence: [] };
  const evidence: Evidence[] = [];
  const results = predicates.map((predicate) => {
    const evaluation = evaluatePredicate(predicate, context);
    evidence.push(...evaluation.evidence);
    return evaluation.result;
  });
  return { result: and(results), evidence };
}

/// 샘플 값 중 비밀처럼 보이는 것을 가린다. Unity 가 준 샘플은 항상 이것을 거친다.
export function redactSample(sample: PlaySample): PlaySample {
  return {
    ...sample,
    members: sample.members.map((item) => item.value === undefined
      ? item : { ...item, value: redactSecrets(item.member, item.value) }),
    facts: sample.facts.map((item) => item.value === undefined
      ? item : { ...item, value: redactSecrets(item.name, item.value) }),
  };
}
