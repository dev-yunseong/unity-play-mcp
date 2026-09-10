import type { PulseStore, UnreadableFrame } from "./pulse.js";
import { sameValue, type FoldedPulseState, type JsonValue, type PulseObject } from "./pulse.js";

/// 대기 조건이 겨눈 멤버 하나. `selector` 는 `PulseObject.selector` 와 정확히 같은 문자열일
/// 때만 맞는다 — `get_scene_state` 의 부분 일치 검색과 다르다. `among` 은 `PulseMember.among`
/// 과 `===` 로 비교한다: 안 주면 `among` 이 없는 멤버에만 맞는다. 이것은 `pulse.ts` 의
/// `memberKey` 가 멤버를 구별하는 규칙과 같다.
export interface MemberTarget {
  selector: string;
  on: string;
  member: string;
  among?: number;
}

/// 멤버 하나가 특정 값과 같기를 바라는 조건.
export interface MemberCondition extends MemberTarget {
  equals: JsonValue;
}

/// 기다릴 조건. 주어진 field 는 전부 동시에 만족해야 한다(AND) — 안 준 field 는 조건이
/// 아니다. `sinceReading`/`sinceFrame` 은 "그 값보다 큰" 이다. `timeoutMilliseconds` 는
/// pulse 가 하나도 안 와도 반드시 끝을 내는 상한이다.
export interface WaitCondition {
  sinceReading?: number;
  sinceFrame?: number;
  scene?: string;
  memberEquals?: MemberCondition[];
  timeoutMilliseconds: number;
}

export type WaitOutcome =
  | { kind: "met"; state: FoldedPulseState }
  | { kind: "timeout"; state: FoldedPulseState | undefined; unmet: string[] }
  | { kind: "disconnected"; state: FoldedPulseState | undefined; unmet: string[] }
  | { kind: "cancelled"; state: FoldedPulseState | undefined };

function memberLabel(target: MemberTarget): string {
  const among = target.among === undefined ? "" : `#${target.among}`;
  return `${target.selector}.${target.on}.${target.member}${among}`;
}

function findMemberValue(
  state: FoldedPulseState,
  target: MemberTarget,
): { found: true; value: JsonValue } | { found: false } {
  for (const object of [...state.active, ...state.deactive] as PulseObject[]) {
    if (object.selector !== target.selector) continue;
    for (const component of object.by ?? []) {
      if (component.on !== target.on) continue;
      for (const member of component.members ?? []) {
        if (member.member !== target.member) continue;
        if (member.among !== target.among) continue;
        return { found: true, value: member.value };
      }
    }
  }
  return { found: false };
}

/// 지금 상태가 이 조건을 만족하지 못하는 이유들. 빈 배열이면 전부 충족한 것이다.
///
/// `state` 가 `undefined` 면(pulse 가 한 번도 안 왔으면) 이유 하나만 돌려준다 — "조건이 안
/// 맞았다" 와 "애초에 읽을 상태가 없다" 를 응답에서 갈라야 하기 때문이다.
export function unmetReasons(state: FoldedPulseState | undefined, condition: WaitCondition): string[] {
  if (state === undefined) {
    return ["no scene reading has arrived yet; call start_readings"];
  }

  const reasons: string[] = [];
  if (condition.sinceReading !== undefined && state.reading <= condition.sinceReading) {
    reasons.push(`reading is ${state.reading}, not newer than ${condition.sinceReading}`);
  }
  if (condition.sinceFrame !== undefined && state.frame <= condition.sinceFrame) {
    reasons.push(`frame is ${state.frame}, not newer than ${condition.sinceFrame}`);
  }
  if (condition.scene !== undefined && state.scene !== condition.scene) {
    reasons.push(`scene is "${state.scene}", not "${condition.scene}"`);
  }
  for (const target of condition.memberEquals ?? []) {
    const label = memberLabel(target);
    const found = findMemberValue(state, target);
    if (!found.found) {
      reasons.push(`${label} was not found in the current reading`);
      continue;
    }
    if (!sameValue(found.value, target.equals)) {
      reasons.push(`${label} is ${JSON.stringify(found.value)}, expected ${JSON.stringify(target.equals)}`);
    }
  }
  return reasons;
}

export interface WaitTimerApi {
  setTimeout(callback: () => void, delayMilliseconds: number): ReturnType<typeof setTimeout>;
  clearTimeout(timer: ReturnType<typeof setTimeout>): void;
}

/// `waitForCondition` 이 필요로 하는 것만 든 최소 의존성. `UnityConnection` 전체가 아니라
/// `onDisconnect` 만 받는 이유는 이 함수가 연결을 만들거나 끊을 일이 없기 때문이다.
export interface WaitConnectionLike {
  onDisconnect(listener: () => void): () => void;
}

/// 조건이 맞거나, timeout 이 되거나, 연결이 끊기거나, 호출이 취소될 때까지 기다린다.
///
/// 변화가 없으면 게임은 pulse 를 아예 보내지 않는다(`PulseStore` 의 기존 정책) — 그래서
/// `onReading` 구독만으로는 끝을 낼 수 없고, timeout 타이머가 반드시 함께 걸린다. 넷 중
/// 무엇이 먼저 오든 나머지 구독과 타이머를 전부 정리하고 다시는 resolve 하지 않는다.
export function waitForCondition(
  store: PulseStore,
  connection: WaitConnectionLike,
  condition: WaitCondition,
  signal: AbortSignal,
  timers: WaitTimerApi = { setTimeout, clearTimeout },
): Promise<WaitOutcome> {
  return new Promise((resolve) => {
    const initialState = store.getState();
    const initialUnmet = unmetReasons(initialState, condition);
    if (initialUnmet.length === 0) {
      resolve({ kind: "met", state: initialState as FoldedPulseState });
      return;
    }
    if (signal.aborted) {
      resolve({ kind: "cancelled", state: initialState });
      return;
    }

    let settled = false;
    const cleanup = () => {
      unsubscribeReading();
      unsubscribeDisconnect();
      timers.clearTimeout(timeoutTimer);
      signal.removeEventListener("abort", onAbort);
    };
    const settle = (outcome: WaitOutcome) => {
      if (settled) return;
      settled = true;
      cleanup();
      resolve(outcome);
    };

    const onAbort = () => settle({ kind: "cancelled", state: store.getState() });
    const unsubscribeReading = store.onReading((state) => {
      const unmet = unmetReasons(state, condition);
      if (unmet.length === 0) {
        settle({ kind: "met", state });
      }
    });
    const unsubscribeDisconnect = connection.onDisconnect(() => {
      const state = store.getState();
      settle({ kind: "disconnected", state, unmet: unmetReasons(state, condition) });
    });
    const timeoutTimer = timers.setTimeout(() => {
      const state = store.getState();
      settle({ kind: "timeout", state, unmet: unmetReasons(state, condition) });
    }, condition.timeoutMilliseconds);

    signal.addEventListener("abort", onAbort);
  });
}

/// `wait_for_condition` tool 이 그대로 JSON 으로 직렬화해 돌려주는 모양.
export interface WaitResponsePayload {
  met: boolean;
  disconnected: boolean;
  cancelled: boolean;
  reading?: number;
  frame?: number;
  scene?: string;
  unmet: string[];
  lastUnreadableFrame?: UnreadableFrame;
}

/// `WaitOutcome` 을 tool 응답 payload 로 바꾼다. `disconnected` 만 `isError` 다 — timeout 과
/// cancelled 는 정상적으로 있을 수 있는 결과이지 tool 실행 자체의 실패가 아니다.
export function describeWaitOutcome(
  outcome: WaitOutcome,
  lastUnreadableFrame: UnreadableFrame | undefined,
): { payload: WaitResponsePayload; isError: boolean } {
  const state = outcome.state;
  const base = {
    ...(state === undefined ? {} : { reading: state.reading, frame: state.frame, scene: state.scene }),
    ...(lastUnreadableFrame === undefined ? {} : { lastUnreadableFrame }),
  };

  if (outcome.kind === "met") {
    return {
      payload: { met: true, disconnected: false, cancelled: false, unmet: [], ...base },
      isError: false,
    };
  }
  if (outcome.kind === "cancelled") {
    return {
      payload: { met: false, disconnected: false, cancelled: true, unmet: [], ...base },
      isError: false,
    };
  }
  if (outcome.kind === "disconnected") {
    return {
      payload: { met: false, disconnected: true, cancelled: false, unmet: outcome.unmet, ...base },
      isError: true,
    };
  }
  return {
    payload: { met: false, disconnected: false, cancelled: false, unmet: outcome.unmet, ...base },
    isError: false,
  };
}
