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
  members: PulseMember[];
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
function sameValue(left: JsonValue, right: JsonValue): boolean {
  return JSON.stringify(left ?? null) === JSON.stringify(right ?? null);
}

function mergeMembers(
  previous: readonly PulseMember[],
  incoming: readonly PulseMember[],
  recorder: Recorder,
  on: string,
): PulseMember[] {
  const merged = new Map(previous.map((member) => [memberKey(member), member]));
  for (const member of incoming) {
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

function foldInternal(
  previous: InternalPulseState | undefined,
  pulse: PulseFrame,
): InternalPulseState | undefined {
  if (previous !== undefined && pulse.reading <= previous.publicState.reading) {
    return previous;
  }
  const sceneChanged = previous !== undefined && pulse.scene !== previous.publicState.scene;
  const replace = pulse.whole || sceneChanged;
  const { objects, tombstones, history } = indexObjects(pulse, replace, sceneChanged, previous);
  return { publicState: toPublicState(pulse, objects, tombstones), objects, tombstones, history };
}

export class PulseStore {
  private pulseState?: InternalPulseState;
  private diagnostics: PulseDiagnostics = {};

  fold(frame: GamePush): boolean {
    if (frame.type === "PULSE") {
      const previous = this.pulseState;
      this.pulseState = foldInternal(previous, frame);
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
}
