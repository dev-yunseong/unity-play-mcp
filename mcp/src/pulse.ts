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
  /// 이 component 가 지닌 멤버들.
  ///
  /// 게임은 이것을 `m` 에 실어 보내고(`LiveState.cs:669`), `readComponents` 가 frame 이 store
  /// 로 들어오는 자리에서 여기로 옮긴다. 그 아래는 이 이름만 안다.
  ///
  /// optional 인 이유는 값이 socket 에서 오기 때문이다. 필수라고 선언해 두면 게임이 키를 또
  /// 바꿀 때 `for ... of undefined` 하나가 reading 전체를 지운다 — #19 가 그것이었다.
  members?: PulseMember[];
  [key: string]: JsonValue | PulseMember[] | undefined;
}

export interface PulseObject {
  id: number;
  path: string;
  selector: string;
  scene?: string;
  tag?: string;
  where?: { [key: string]: JsonValue };
  offers?: JsonValue[];
  by?: PulseComponent[];
  [key: string]: JsonValue | PulseComponent[] | undefined;
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

/// 한 멤버가 지녔던 값 하나와, 그것이 그 값이 된 `reading`.
export interface MemberReading {
  value: JsonValue;
  reading: number;
  frame: number;
}

/// 파괴되었거나 화면을 떠난 객체. 마지막으로 알던 모습을 그대로 든다.
export interface GoneObject {
  object: PulseObject;
  goneAtReading: number;
}

export interface FoldedPulseState {
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

/// 멤버 하나가 드는 값의 개수.
///
/// 게임은 초당 열 번 읽고, 아무것도 안 움직인 `reading` 은 아예 보내지 않는다. 그래서 이것은
/// 시계가 아니라 그 값이 실제로 움직인 마지막 열 번이다 — 매 프레임 흔들리는 값에는 1초치
/// 이고, 천천히 변하는 값에는 몇 분치다. 후자가 이 상한을 개수로 둔 이유다.
const HISTORY_DEPTH = 10;

/// 동시에 드는 파괴된 객체의 수. 적이 계속 죽는 게임에서 메모리를 유계로 만든다.
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
  /// `objectKey \0 component.on \0 memberKey` 를 그 멤버가 지나온 값들에 건다.
  ///
  /// `HeldObject` 안이 아니라 여기 있는 이유는 `whole` 인 `reading` 이 객체 map 을 통째로
  /// 새로 만들기 때문이다. 안에 두면 소켓이 잠깐 끊겨 게임이 그것을 다시 보낼 때마다 멀쩡한
  /// 이력이 함께 사라진다.
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

/// 값 하나를 그 멤버의 이력에 얹는다. 값이 그대로면 얹지 않는다.
///
/// 처음 보는 멤버는 첫 칸을 얻는다. 그러지 않으면 "한 번도 안 움직였다" 와 "추적된 적이
/// 없다" 가 똑같이 빈 이력으로 보인다.
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

  // 제자리에서 밀지 않고 갈아 끼운다. 그래야 접기가 이전 상태를 건드리지 않으면서도 map 을
  // 얕게만 복사할 수 있다 — 움직인 멤버의 배열만 새로 만들면 된다.
  const grown = [...series, entry];
  recorder.history.set(path, grown.slice(Math.max(0, grown.length - HISTORY_DEPTH)));
}

/// 두 값이 같은 값인지.
///
/// `LiveState` 가 멤버를 고정된 키 순서로 직렬화하므로 문자열 비교로 충분하다. 게임이 같은
/// 멤버를 `reading` 마다 다른 키 순서로 쓰기 시작하면 안 움직인 값이 변경으로 잡힌다.
///
/// `wait.ts` 가 `memberEquals` 조건을 검사할 때 같은 비교를 그대로 쓴다 — 값 비교 규칙은
/// 하나만 있어야 하므로 export 한다.
export function sameValue(left: JsonValue, right: JsonValue): boolean {
  return JSON.stringify(left ?? null) === JSON.stringify(right ?? null);
}

/// 두 멤버 목록을 멤버 키로 합친다.
///
/// 둘 다 없어도 된다. 멤버 목록이 없는 component 는 이번 reading 에 아무 말도 안 한
/// component 이므로, 이전에 들고 있던 멤버를 지우지 않고 그대로 돌려준다.
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

/// 이 객체의 멤버 이력을 전부 버린다.
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

  // 씬이 바뀌면 이력도 tombstone 도 다른 씬의 이야기다. 그 밖의 `whole` — 첫 `reading` 과,
  // 전달이 유실된 뒤의 복구 — 은 값을 다시 말하는 것일 뿐 아무것도 지우라는 말이 아니다.
  const tombstones = sceneChanged || previous === undefined
    ? new Map<string, GoneObject>()
    : new Map(previous.tombstones);
  // 얕게만 복사한다. `record` 가 배열을 제자리에서 고치지 않고 갈아 끼우므로, 움직이지 않은
  // 멤버의 배열은 이전 상태와 그대로 나눠 쓴다. 감시 멤버가 수천 개인 게임에서 초당 열 번
  // 도는 자리라 이 차이가 그대로 CPU 다.
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
    // 같은 키가 또 죽으면 맨 뒤로 보낸다. 삽입 순서가 곧 버릴 순서다.
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

  // `whole` 인 `reading` 은 지금 있는 것 전부를 말한다. 거기 없는 키는 더 말할 것이 없는 키다.
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

/// 게임이 component 의 멤버 목록을 싣는 키.
///
/// `members` 를 줄인 이름이다. component 하나에 6 B 이고 한 문서에 component 가 316개,
/// reading 은 초당 열 번 — `fd16e70` 이 그만큼을 줄이려고 고른 것이라 게임 쪽은 그대로 둔다.
/// 대신 frame 이 store 로 들어오는 자리에서 한 번 옮긴다.
const WIRE_MEMBERS = "m";

/// 멤버를 하나도 읽지 못한 component 와, 그것을 실은 객체의 `path`.
interface UnreadComponent {
  on: string;
  path: string;
  /// 그 component 가 `on` 말고 들고 온 키들. 비어 있으면 멤버가 없는 component 다.
  otherKeys: string[];
}

function readComponent(
  component: PulseComponent,
  path: string,
  unread: UnreadComponent[],
): PulseComponent {
  const wire = component[WIRE_MEMBERS];
  if (!Array.isArray(wire)) {
    // `m` 이 있는데 배열이 아니면 그 자체가 어긋난 키다. 없는 것과 같이 두면 "아무 말도 안 한
    // component" 로 조용히 넘어가는데, 그 조용함이 이 issue 가 없애려는 것이다.
    const otherKeys = Object.keys(component)
      .filter((key) => key !== "on" && (key !== WIRE_MEMBERS || wire !== undefined));
    unread.push({ on: component.on, path, otherKeys });
    return component;
  }
  // 멤버 하나하나의 모양은 확인하지 않는다. `isGamePushFrame` 이 top-level 만 보는 것과 같은
  // 선이고, `record` 는 `value` 가 없어도 던지지 않는다.
  const members = wire as PulseMember[];
  // `m` 은 떼고 내보낸다. 두면 같은 멤버 목록이 `members` 와 나란히 두 벌로 접힌 상태에 앉고,
  // 그대로 `get_scene_state` 응답에 실린다.
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

/// 게임이 보낸 frame 을 store 가 아는 모양으로 옮긴다.
///
/// `m` 도 없고 다른 키도 없는 component 는 실패가 아니다. "이번 reading 에 이 component 는
/// 아무 말도 안 했다" 와 구별할 수 없는 모양이고, 실패로 다루면 그 하나가 나머지 315개를
/// 함께 버린다. `mergeMembers` 가 이전 멤버를 그대로 든다.
///
/// 반대로 `m` 없이 다른 키를 들고 온 component 는 던진다. 게임이 키를 바꾸면 하나가 아니라
/// 316개 전부가 그렇게 되므로, 값 없는 상태를 조용히 받아 두는 것보다 이 reading 을 못 읽었다고
/// 말하는 편이 낫다. `fold` 가 그 message 를 `lastUnreadableFrame.reason` 에 넣고
/// `get_unity_status` 가 그것을 사람에게 그대로 보여 준다.
function readComponents(pulse: PulseFrame): PulseFrame {
  const unread: UnreadComponent[] = [];
  const active = pulse.active.map((object) => readObject(object, unread));
  const deactive = pulse.deactive.map((object) => readObject(object, unread));

  const mismatched = unread.filter(({ otherKeys }) => otherKeys.length > 0);
  if (mismatched.length > 0) {
    throw new Error(describeMismatch(mismatched, countComponents(pulse)));
  }
  return { ...pulse, active, deactive };
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
): InternalPulseState | undefined {
  if (previous !== undefined && pulse.reading <= previous.publicState.reading) {
    return previous;
  }
  // reading 번호를 확인한 뒤에 옮긴다. 이미 지나간 reading 은 복사할 이유가 없다.
  const readable = readComponents(pulse);
  const sceneChanged = previous !== undefined && readable.scene !== previous.publicState.scene;
  const replace = readable.whole || sceneChanged;
  const { objects, tombstones, history } = indexObjects(readable, replace, sceneChanged, previous);
  return {
    publicState: toPublicState(readable, objects, tombstones), objects, tombstones, history,
  };
}

/// frame 은 도착했지만 접을 수 없었다는 것. `get_unity_status` 가 이걸로 무엇을 못 읽었는지
/// 말한다.
export interface UnreadableFrame {
  at: number;
  reason: string;
}

function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export class PulseStore {
  private pulseState?: InternalPulseState;
  private diagnostics: PulseDiagnostics = {};
  private lastReadingAt?: number;
  private lastUnreadableFrame?: UnreadableFrame;
  /// `wait.ts` 가 새 reading 을 기다릴 때 건다. pulse 가 아예 안 오면 여기가 안 불리므로,
  /// 부르는 쪽이 timeout 을 별도로 걸어야 무한히 기다리지 않는다.
  private readonly readingListeners = new Set<(state: FoldedPulseState) => void>();

  /// 시계를 밖에서 받는 이유는 test 가 "pulse 가 얼마나 오래되었는지" 를 실제 시간을 기다리지 않고
  /// 확인하기 위해서다.
  constructor(private readonly now: () => number = Date.now) {}

  fold(frame: GamePush): boolean {
    if (frame.type === "PULSE") {
      const previous = this.pulseState;
      let folded: InternalPulseState | undefined;
      try {
        folded = foldInternal(previous, frame);
      } catch (error) {
        // 이 frame 은 읽지 못했다. 도착한 reading 으로 세면 `get_unity_status` 와
        // `get_scene_state` 가 서로 다른 말을 하게 되므로 `lastReadingAt` 은 건드리지 않는다.
        this.lastUnreadableFrame = { at: this.now(), reason: reasonOf(error) };
        return false;
      }
      // 도착했다는 사실 자체가 게임이 돌고 있다는 증거다. 값이 하나도 안 바뀐 pulse 도 마찬가지다.
      this.lastReadingAt = this.now();
      // 이번 fold 가 성공했으니 지난 실패는 더 이상 최신 사건이 아니다.
      this.lastUnreadableFrame = undefined;
      this.pulseState = folded;
      // `foldInternal` 은 실제로는 항상 정의된 상태를 돌려준다(`previous` 를 그대로 돌려주는
      // 가지도 `previous !== undefined` 를 먼저 확인한 뒤에만 탄다) — 그래도 signature 가
      // `| undefined` 라 여기서 좁혀 준다.
      if (folded !== undefined) {
        for (const listener of this.readingListeners) {
          listener(folded.publicState);
        }
      }
      return this.pulseState !== previous;
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

  /// 한 객체의 멤버들이 지나온 값을, `component.on` 과 멤버 키를 이은 이름에 걸어 낸다.
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

  /// 마지막 pulse 가 도착한 시각. 아직 하나도 안 왔으면 `undefined`.
  getLastReadingAt(): number | undefined {
    return this.lastReadingAt;
  }

  /// 마지막으로 시도한 fold 가 실패했고, 그 뒤로 아무 reading 도 성공하지 않았을 때만 값을
  /// 돌려준다. 성공한 fold 가 하나라도 오면 지워진다.
  getLastUnreadableFrame(): UnreadableFrame | undefined {
    return this.lastUnreadableFrame;
  }

  /// `PULSE` frame 이 성공적으로 접힐 때마다(값이 하나도 안 바뀌어도) 새 상태로 부른다.
  /// 반환값은 구독을 끊는 함수다.
  onReading(listener: (state: FoldedPulseState) => void): () => void {
    this.readingListeners.add(listener);
    return () => this.readingListeners.delete(listener);
  }
}
