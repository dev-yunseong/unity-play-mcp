import { redactSecrets } from "./secrets.js";

export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

export interface PulseMember {
  member: string;
  among?: number;
  value: JsonValue;
  [key: string]: JsonValue | undefined;
}

export interface PulseComponent {
  on: string;
  /// 이 component 의 멤버들. 게임은 `m` 으로 보내고 `readComponents` 가 여기로 옮긴다.
  ///
  /// 값이 socket 에서 오므로 optional 이다. 필수로 두면 게임이 키를 바꿀 때
  /// `for ... of undefined` 가 reading 전체를 버린다 (#19).
  members?: PulseMember[];
  [key: string]: JsonValue | PulseMember[] | undefined;
}

/// `Button.onClick` 같은 UnityEvent 에 걸린 persistent call 하나.
///
/// 이 배열이 비어 있으면 `click` 을 보내도 아무 일도 일어나지 않을 수 있다.
export interface OfferedClick {
  event: string;
  method: string;
  on: string;
}

/// 이 객체가 살아 있는 동안 게임 코드가 반응하는 키 하나.
///
/// `does` 가 없으면 분석이 동작을 읽지 못한 것이지 아무 일도 안 한다는 뜻이 아니다. 그래서
/// 빈 배열로 채우지 않는다.
export interface OfferedKey {
  key: string;
  does?: string[];
}

/// 이 객체가 답하는 조작들. 게임의 실제 `wiring` 에서 나온 값이다.
///
/// `offers` 가 있으면 최소 하나는 있지만 어느 것인지 모르므로 셋 다 optional 이다.
export interface PulseOffers {
  clicks?: OfferedClick[];
  keys?: OfferedKey[];
  pointers?: string[];
}

export interface PulseObject {
  id: number;
  path: string;
  selector: string;
  scene?: string;
  tag?: string;
  where?: { [key: string]: JsonValue };
  offers?: PulseOffers;
  by?: PulseComponent[];
  [key: string]: JsonValue | PulseComponent[] | PulseOffers | undefined;
}

export interface PulseStatic {
  declaring: string;
  member: string;
  type: string;
  value: JsonValue;
  [key: string]: JsonValue;
}

export interface PulseFrame {
  type: "PULSE";
  id: number;
  schema: number;
  /// reading 번호가 속한 run. `Pulse.Begin` 마다 새로 정해진다.
  ///
  /// `reading` 은 run 마다 1부터 다시 세므로 run 이 다르면 번호를 비교할 수 없다. 이 값이 없는
  /// package(0.2.x 이하)는 번호가 뒤로 간 것으로 새 run 을 판단한다.
  run?: string;
  reading: number;
  frame: number;
  scene: string;
  statics: PulseStatic[];
  active: PulseObject[];
  deactive: PulseObject[];
  whole: boolean;
  watching: number;
  unresolved: number;
  unwatchable: number;
  gone?: string[];
  changed: string[];
}

export interface DiagnosticFrame {
  type: "PERFORMANCE" | "DEVICE_CONTEXT";
  id: number;
  [key: string]: JsonValue;
}

export interface ErrorFrame {
  type: "ERROR";
  id: number;
  message: string;
}

export type GamePush = PulseFrame | DiagnosticFrame | ErrorFrame;

/// 멤버가 가졌던 값 하나와 그 값이 된 `reading`.
export interface MemberReading {
  value: JsonValue;
  reading: number;
  frame: number;
}

/// 파괴되었거나 화면을 떠난 객체와 그 마지막 모습.
export interface GoneObject {
  object: PulseObject;
  goneAtReading: number;
}

export interface FoldedPulseState {
  run?: string;
  reading: number;
  frame: number;
  scene: string;
  schema: number;
  statics: PulseStatic[];
  active: PulseObject[];
  deactive: PulseObject[];
  watching: number;
  unresolved: number;
  unwatchable: number;
  changed: string[];
  gone: GoneObject[];
}

export interface PulseDiagnostics {
  performance?: DiagnosticFrame;
  deviceContext?: DiagnosticFrame;
}

/// 멤버 하나가 보관하는 값의 개수.
///
/// 값이 바뀐 `reading` 만 기록하므로 시간이 아니라 개수로 제한한다. 매 프레임 바뀌는 값은
/// 약 1초치, 느리게 바뀌는 값은 몇 분치가 남는다.
const HISTORY_DEPTH = 10;

/// 동시에 보관하는 파괴된 객체의 수. 객체가 계속 파괴되는 게임에서 메모리를 제한한다.
const TOMBSTONE_LIMIT = 50;

type Activity = "active" | "deactive";

interface HeldObject {
  activity: Activity;
  object: PulseObject;
}

interface InternalPulseState {
  publicState: FoldedPulseState;
  objects: Map<string, HeldObject>;
  tombstones: Map<string, GoneObject>;
  /// `objectKey \0 component.on \0 memberKey` 별 값 이력.
  ///
  /// `whole` reading 이 객체 map 을 새로 만들므로 `HeldObject` 밖에 둔다. 안에 두면 재연결로
  /// `whole` reading 이 올 때마다 이력이 사라진다.
  history: Map<string, MemberReading[]>;
}

interface Recorder {
  history: Map<string, MemberReading[]>;
  reading: number;
  frame: number;
  objectKey: string;
}

export function objectKey(object: PulseObject, readingScene: string): string {
  return `${object.scene ?? readingScene}/${object.selector}`;
}

function memberKey(member: PulseMember): string {
  return member.among === undefined
    ? member.member
    : `${member.member}\u0000${member.among}`;
}

/// 값 하나를 그 멤버의 이력에 추가한다. 직전 값과 같으면 추가하지 않는다.
///
/// 처음 보는 멤버는 항상 기록한다. 그러지 않으면 "바뀐 적 없다" 와 "추적된 적 없다" 를
/// 구별할 수 없다.
function record(recorder: Recorder, on: string, member: PulseMember): void {
  const path = `${recorder.objectKey}\u0000${on}\u0000${memberKey(member)}`;
  const series = recorder.history.get(path);
  const entry: MemberReading = {
    value: member.value,
    reading: recorder.reading,
    frame: recorder.frame,
  };

  if (series === undefined) {
    recorder.history.set(path, [entry]);
    return;
  }

  const last = series[series.length - 1];
  if (last !== undefined && sameValue(last.value, member.value)) {
    return;
  }

  // 배열을 제자리에서 고치지 않고 교체한다. 그래서 이전 상태를 건드리지 않고 map 을 얕게
  // 복사할 수 있다.
  const grown = [...series, entry];
  recorder.history.set(path, grown.slice(Math.max(0, grown.length - HISTORY_DEPTH)));
}

/// `LiveState` 가 멤버를 고정된 키 순서로 직렬화하므로 문자열 비교로 충분하다. 키 순서가
/// 바뀌면 같은 값이 변경으로 잡힌다.
///
/// `wait.ts` 의 `memberEquals` 도 같은 규칙을 쓰도록 export 한다.
export function sameValue(left: JsonValue, right: JsonValue): boolean {
  return JSON.stringify(left ?? null) === JSON.stringify(right ?? null);
}

/// 두 멤버 목록을 멤버 키로 합친다.
///
/// 멤버 목록이 없는 component 는 이번 reading 에 변화가 없는 것이므로 이전 멤버를 유지한다.
function mergeMembers(
  previous: readonly PulseMember[] | undefined,
  incoming: readonly PulseMember[] | undefined,
  recorder: Recorder,
  on: string,
): PulseMember[] {
  const merged = new Map((previous ?? []).map((member) => [memberKey(member), member]));
  for (const member of incoming ?? []) {
    record(recorder, on, member);
    merged.set(memberKey(member), member);
  }
  return [...merged.values()];
}

function mergeComponents(
  previous: readonly PulseComponent[],
  incoming: readonly PulseComponent[],
  recorder: Recorder,
): PulseComponent[] {
  const merged = new Map(previous.map((component) => [component.on, component]));
  for (const component of incoming) {
    const oldComponent = merged.get(component.on);
    merged.set(
      component.on,
      oldComponent === undefined
        ? { ...component, members: mergeMembers([], component.members, recorder, component.on) }
        : {
            ...oldComponent,
            ...component,
            members: mergeMembers(
              oldComponent.members, component.members, recorder, component.on),
          },
    );
  }
  return [...merged.values()];
}

function mergeObject(
  previous: PulseObject | undefined,
  incoming: PulseObject,
  recorder: Recorder,
): PulseObject {
  const merged = { ...(previous ?? {}), ...incoming } as PulseObject;
  if (previous?.by === undefined && incoming.by === undefined) {
    return merged;
  }
  merged.by = mergeComponents(previous?.by ?? [], incoming.by ?? [], recorder);
  return merged;
}

function forgetHistory(history: Map<string, MemberReading[]>, objectKey: string): void {
  const prefix = `${objectKey}\u0000`;
  for (const path of history.keys()) {
    if (path.startsWith(prefix)) {
      history.delete(path);
    }
  }
}

function indexObjects(pulse: PulseFrame, replace: boolean, sceneChanged: boolean,
  previous?: InternalPulseState) {
  const objects = replace || previous === undefined
    ? new Map<string, HeldObject>()
    : new Map(previous.objects);

  // 씬이 바뀔 때만 이력과 tombstone 을 비운다. 첫 reading 이나 유실 복구로 온 `whole` 은 값을
  // 다시 보낼 뿐 지우라는 뜻이 아니다.
  const tombstones = sceneChanged || previous === undefined
    ? new Map<string, GoneObject>()
    : new Map(previous.tombstones);
  // 얕게 복사한다. `record` 가 배열을 교체하므로 바뀌지 않은 멤버의 배열은 이전 상태와 공유한다.
  // 감시 멤버가 많으면 초당 열 번 도는 경로라 CPU 비용이 크다.
  const history = sceneChanged || previous === undefined
    ? new Map<string, MemberReading[]>()
    : new Map(previous.history);

  for (const key of pulse.gone ?? []) {
    const held = objects.get(key);
    objects.delete(key);
    forgetHistory(history, key);
    if (held === undefined) {
      continue;
    }
    // 같은 키가 다시 파괴되면 맨 뒤로 보낸다. 삽입 순서가 버릴 순서다.
    tombstones.delete(key);
    tombstones.set(key, { object: held.object, goneAtReading: pulse.reading });
  }

  const accept = (activity: Activity, incoming: PulseObject) => {
    const key = objectKey(incoming, pulse.scene);
    const oldObject = replace ? undefined : objects.get(key)?.object;
    tombstones.delete(key);
    const recorder: Recorder = {
      history, reading: pulse.reading, frame: pulse.frame, objectKey: key,
    };
    objects.set(key, { activity, object: mergeObject(oldObject, incoming, recorder) });
  };
  pulse.active.forEach((object) => accept("active", object));
  pulse.deactive.forEach((object) => accept("deactive", object));

  // `whole` reading 은 현재 객체 전부를 담으므로 거기 없는 객체의 이력은 버린다.
  if (replace) {
    for (const path of history.keys()) {
      if (!objects.has(path.slice(0, path.indexOf("\u0000")))) {
        history.delete(path);
      }
    }
  }

  while (tombstones.size > TOMBSTONE_LIMIT) {
    const oldest = tombstones.keys().next();
    if (oldest.done === true) break;
    tombstones.delete(oldest.value);
  }

  return { objects, tombstones, history };
}

function toPublicState(
  pulse: PulseFrame,
  objects: Map<string, HeldObject>,
  tombstones: Map<string, GoneObject>,
): FoldedPulseState {
  const active: PulseObject[] = [];
  const deactive: PulseObject[] = [];
  for (const held of objects.values()) {
    (held.activity === "active" ? active : deactive).push(held.object);
  }
  return {
    ...(pulse.run === undefined ? {} : { run: pulse.run }),
    reading: pulse.reading,
    frame: pulse.frame,
    scene: pulse.scene,
    schema: pulse.schema,
    statics: pulse.statics,
    active,
    deactive,
    watching: pulse.watching,
    unresolved: pulse.unresolved,
    unwatchable: pulse.unwatchable,
    changed: pulse.changed,
    gone: [...tombstones.values()],
  };
}

/// 게임이 component 의 멤버 목록을 싣는 키(`members` 의 약어).
///
/// reading 크기를 줄이려고 게임은 짧은 키를 쓰고, frame 이 store 로 들어올 때 한 번 옮긴다.
const WIRE_MEMBERS = "m";

/// 멤버를 하나도 읽지 못한 component 와, 그것을 실은 객체의 `path`.
interface UnreadComponent {
  on: string;
  path: string;
  /// `on` 외의 키들. 비어 있으면 멤버가 없는 component 다.
  otherKeys: string[];
}

function readComponent(
  component: PulseComponent,
  path: string,
  unread: UnreadComponent[],
): PulseComponent {
  const wire = component[WIRE_MEMBERS];
  if (!Array.isArray(wire)) {
    // `m` 이 있는데 배열이 아니면 키가 어긋난 것으로 본다. 없는 것으로 다루면 오류가 조용히
    // 묻힌다.
    const otherKeys = Object.keys(component)
      .filter((key) => key !== "on" && (key !== WIRE_MEMBERS || wire !== undefined));
    unread.push({ on: component.on, path, otherKeys });
    return component;
  }
  // 멤버 모양은 확인하지 않는다. `isGamePushFrame` 도 top-level 만 보고, `record` 는 `value` 가
  // 없어도 던지지 않는다.
  //
  // 비밀처럼 보이는 값은 여기서 가린다. 이후의 상태, 이력, tree, 검색, 대기는 가린 값만 본다 (#72).
  const members = (wire as PulseMember[]).map((member) => ({
    ...member,
    // 멤버 이름만 본다. 타입 namespace(`Game.TokenShop`)까지 보면 그 타입의 모든 문자열이 가려진다.
    value: redactSecrets(member.member, member.value),
  }));
  // `m` 은 뗀다. 남기면 같은 멤버 목록이 두 번 저장되고 `get_scene_state` 응답에도 실린다.
  const { [WIRE_MEMBERS]: dropped, ...rest } = component;
  return { ...rest, members };
}

function readObject(object: PulseObject, unread: UnreadComponent[]): PulseObject {
  if (object.by === undefined) {
    return object;
  }
  const path = typeof object.path === "string" ? object.path : object.selector;
  return { ...object, by: object.by.map((component) => readComponent(component, path, unread)) };
}

/// 게임이 보낸 frame 을 store 형식으로 옮긴다.
///
/// `m` 도 다른 키도 없는 component 는 이번 reading 에 변화가 없는 것으로 보고
/// `mergeMembers` 가 이전 멤버를 유지한다.
///
/// `m` 없이 다른 키를 가진 component 가 있으면 던진다. 게임이 키를 바꾸면 모든 component 가
/// 그렇게 되므로 값 없는 상태를 조용히 받는 것보다 낫다. 메시지는
/// `lastUnreadableFrame.reason` 을 거쳐 `get_unity_status` 에 보인다.
function readComponents(pulse: PulseFrame): PulseFrame {
  const unread: UnreadComponent[] = [];
  const active = pulse.active.map((object) => readObject(object, unread));
  const deactive = pulse.deactive.map((object) => readObject(object, unread));

  const mismatched = unread.filter(({ otherKeys }) => otherKeys.length > 0);
  if (mismatched.length > 0) {
    throw new Error(describeMismatch(mismatched, countComponents(pulse)));
  }
  // static 은 객체에 속하지 않으므로 선언 타입의 단순 이름과 멤버 이름(`SceneContext.JwtToken`)으로
  // 판정한다. 모양이 어긋난 항목은 그대로 넘긴다. 하나 때문에 reading 전체를 버리지 않는다.
  const statics = pulse.statics.map((declared) => (typeof declared === "object" && declared !== null
    ? {
        ...declared,
        value: redactSecrets(
          `${String(declared.declaring ?? "").split(".").at(-1) ?? ""}.${declared.member}`, declared.value),
      }
    : declared));
  return { ...pulse, statics, active, deactive };
}

function countComponents(pulse: PulseFrame): number {
  return [...pulse.active, ...pulse.deactive]
    .reduce((total, object) => total + (object.by?.length ?? 0), 0);
}

function describeMismatch(mismatched: readonly UnreadComponent[], total: number): string {
  const first = mismatched[0];
  const keys = [...new Set(mismatched.flatMap(({ otherKeys }) => otherKeys))].sort();
  return `${mismatched.length} of ${total} PULSE components carried no "${WIRE_MEMBERS}" `
    + `(first: ${first?.on ?? "?"} on ${first?.path ?? "?"}); `
    + `their members may be under: ${keys.join(", ")}`;
}

function foldInternal(
  previous: InternalPulseState | undefined,
  pulse: PulseFrame,
): InternalPulseState {
  const readable = readComponents(pulse);
  const sceneChanged = previous !== undefined && readable.scene !== previous.publicState.scene;
  const replace = readable.whole || sceneChanged;
  const { objects, tombstones, history } = indexObjects(readable, replace, sceneChanged, previous);
  return {
    publicState: toPublicState(readable, objects, tombstones), objects, tombstones, history,
  };
}

/// 어느 쪽이든 run 이 있으면 run 으로 비교한다. 둘 다 없으면(0.2.x package) 번호가 늘지 않은
/// 것을 새 run 으로 본다. 한 run 안에서 `reading` 은 단조 증가하고 socket 은 순서를 바꾸지 않는다.
function startsNewRun(held: FoldedPulseState, pulse: PulseFrame): boolean {
  if (held.run !== undefined || pulse.run !== undefined) {
    return held.run !== pulse.run;
  }
  return pulse.reading <= held.reading;
}

/// 도착했지만 fold 하지 못한 frame. `get_unity_status` 가 이유를 보여 준다.
export interface UnreadableFrame {
  at: number;
  reason: string;
}

/// 현재 상태가 게임을 따라가지 못하게 된 이유.
///
/// - `restarted`: Unity 가 새 run 을 시작했지만 그 run 의 `whole` reading 이 아직 오지 않았다.
/// - `disconnected`: 연결이 끊겨 그 사이의 변화를 놓쳤을 수 있다.
/// - `stopped`: `stop_readings` 가 성공해 더 이상 reading 이 오지 않는다.
export type ReadingInterruption = "restarted" | "disconnected" | "stopped";

export interface Staleness {
  reason: ReadingInterruption;
  at: number;
  /// 현재 상태를 만든 마지막 reading 을 fold 한 시각.
  lastReadingAt?: number;
}

function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export class PulseStore {
  private pulseState?: InternalPulseState;
  private diagnostics: PulseDiagnostics = {};
  private lastReadingAt?: number;
  private lastUnreadableFrame?: UnreadableFrame;
  private interruption?: { reason: ReadingInterruption; at: number };
  /// 이전 상태 없이 새로 만든 횟수. run 이 바뀌면 reading 번호를 비교할 수 없으므로 `wait.ts` 가
  /// 대기 중 run 이 바뀌었는지 이것으로 판단한다.
  private generation = 0;
  /// `wait.ts` 가 새 reading 을 기다릴 때 등록한다. pulse 가 오지 않으면 호출되지 않으므로
  /// 호출하는 쪽이 timeout 을 걸어야 한다.
  private readonly readingListeners = new Set<(state: FoldedPulseState) => void>();

  /// test 가 실제 시간을 기다리지 않도록 시계를 주입받는다.
  constructor(private readonly now: () => number = Date.now) {}

  fold(frame: GamePush): boolean {
    if (frame.type === "PULSE") {
      const previous = this.pulseState;
      let base = previous;
      if (previous !== undefined && startsNewRun(previous.publicState, frame)) {
        // 새 run 의 차이 frame 은 이전 run 상태 위에 얹을 수 없다. `whole` reading 이 올 때까지
        // 상태를 낡았다고 표시한다.
        if (!frame.whole) {
          this.interruption = { reason: "restarted", at: this.now() };
          return false;
        }
        base = undefined;
      } else if (previous !== undefined && frame.reading <= previous.publicState.reading) {
        // 같은 run 의 지나간 reading 이다. 시각을 갱신하면 `get_unity_status` 가 낡은 reading 을
        // 최신으로 보고한다 (#69).
        return false;
      }
      let folded: InternalPulseState;
      try {
        folded = foldInternal(base, frame);
      } catch (error) {
        // 읽지 못한 frame 은 `lastReadingAt` 을 갱신하지 않는다. 갱신하면 `get_unity_status` 와
        // `get_scene_state` 가 어긋난다.
        this.lastUnreadableFrame = { at: this.now(), reason: reasonOf(error) };
        return false;
      }
      if (base === undefined) {
        this.generation += 1;
      }
      this.lastReadingAt = this.now();
      this.lastUnreadableFrame = undefined;
      // 중단 표시는 `whole` reading 만 지운다. 재연결 직후 먼저 도착하는 밀린 차이 frame 은 놓친
      // 변화를 채우지 못한다.
      if (frame.whole) {
        this.interruption = undefined;
      }
      this.pulseState = folded;
      for (const listener of this.readingListeners) {
        listener(folded.publicState);
      }
      return true;
    }
    if (frame.type === "PERFORMANCE") {
      this.diagnostics = { ...this.diagnostics, performance: frame };
      return true;
    }
    if (frame.type === "DEVICE_CONTEXT") {
      this.diagnostics = { ...this.diagnostics, deviceContext: frame };
      return true;
    }
    return false;
  }

  getState(): FoldedPulseState | undefined {
    return this.pulseState?.publicState;
  }

  /// 한 객체의 멤버 이력을 `component.on` 과 멤버 키를 이은 이름별로 돌려준다.
  getObjectHistory(key: string): Map<string, readonly MemberReading[]> {
    const prefix = `${key}\u0000`;
    const found = new Map<string, readonly MemberReading[]>();
    for (const [path, series] of this.pulseState?.history ?? []) {
      if (path.startsWith(prefix)) {
        found.set(path.slice(prefix.length), series);
      }
    }
    return found;
  }

  getDiagnostics(): PulseDiagnostics {
    return this.diagnostics;
  }

  /// 현재 상태를 만든 마지막 reading 을 fold 한 시각. 적용하지 않은 frame 은 반영하지 않는다.
  getLastReadingAt(): number | undefined {
    return this.lastReadingAt;
  }

  getGeneration(): number {
    return this.generation;
  }

  /// 상태가 낡았음을 기록한다. 상태가 없으면 무시하고, 다음 `whole` reading 이 적용되면 지워진다.
  markInterrupted(reason: ReadingInterruption): void {
    if (this.pulseState === undefined) {
      return;
    }
    this.interruption = { reason, at: this.now() };
  }

  /// 상태가 낡았으면 그 이유와 시각을 돌려준다.
  getStaleness(): Staleness | undefined {
    if (this.interruption === undefined) {
      return undefined;
    }
    return { ...this.interruption, lastReadingAt: this.lastReadingAt };
  }

  /// 마지막 fold 가 실패했고 그 뒤 성공한 fold 가 없을 때만 값을 돌려준다.
  getLastUnreadableFrame(): UnreadableFrame | undefined {
    return this.lastUnreadableFrame;
  }

  /// `PULSE` frame 을 fold 할 때마다(값 변화가 없어도) 새 상태로 호출한다. 구독 해제 함수를
  /// 돌려준다.
  onReading(listener: (state: FoldedPulseState) => void): () => void {
    this.readingListeners.add(listener);
    return () => this.readingListeners.delete(listener);
  }
}
