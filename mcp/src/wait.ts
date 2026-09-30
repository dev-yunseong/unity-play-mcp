import type { PulseStore, UnreadableFrame } from "./pulse.js";
import { sameValue, type FoldedPulseState, type JsonValue, type PulseObject } from "./pulse.js";

/// 대기 조건이 가리키는 멤버 하나. `selector` 는 `PulseObject.selector` 와 정확히 같아야 맞는다
/// (`get_scene_state` 의 부분 일치와 다르다). `among` 은 `===` 로 비교하므로 안 주면 `among` 이 없는
/// 멤버에만 맞는다. `pulse.ts` 의 `memberKey` 와 같은 규칙이다.
export interface MemberTarget {
  selector: string;
  on: string;
  member: string;
  among?: number;
}

/// 멤버 하나가 특정 값과 같아야 하는 조건.
export interface MemberCondition extends MemberTarget {
  equals: JsonValue;
}

/// 기다릴 조건. 준 field 는 모두 만족해야 한다(AND). `sinceReading`/`sinceFrame` 은 "그 값보다 큰" 이다.
/// `timeoutMilliseconds` 는 pulse 가 오지 않아도 대기를 끝내는 상한이다.
export interface WaitCondition {
  /// `sinceReading`/`sinceFrame` 기준선을 잡은 reading 의 run. 현재 run 이 다르면 번호를 비교할 수 없고
  /// 그 reading 은 기준선 뒤에 온 것이므로 둘 다 충족한 것으로 본다.
  sinceRun?: string;
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

/// 현재 상태가 조건을 만족하지 못하는 이유들. 빈 배열이면 모두 충족한 것이다.
///
/// `state` 가 `undefined` 면 "조건 불일치" 와 "읽을 상태 없음" 을 구별하도록 이유 하나만 돌려준다.
///
/// `newRun` 이면 대기 중 Unity 가 새 run 을 시작한 것이다. 번호는 비교할 수 없지만 그 reading 은
/// 기준선 뒤에 온 것이므로 `sinceReading`/`sinceFrame` 은 충족한 것으로 본다.
export function unmetReasons(
  state: FoldedPulseState | undefined,
  condition: WaitCondition,
  newRun = false,
): string[] {
  if (state === undefined) {
    return ["no scene reading has arrived yet; call start_readings"];
  }

  const reasons: string[] = [];
  if (!newRun && condition.sinceReading !== undefined && state.reading <= condition.sinceReading) {
    reasons.push(`reading is ${state.reading}, not newer than ${condition.sinceReading}`);
  }
  if (!newRun && condition.sinceFrame !== undefined && state.frame <= condition.sinceFrame) {
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

/// `waitForCondition` 은 연결을 만들거나 끊지 않으므로 `onDisconnect` 만 받는다.
export interface WaitConnectionLike {
  onDisconnect(listener: () => void): () => void;
}

/// 조건 충족, timeout, 연결 끊김, 취소 중 하나가 일어날 때까지 기다린다.
///
/// 변화가 없으면 게임이 pulse 를 보내지 않으므로 `onReading` 과 함께 timeout 타이머를 반드시 건다.
/// 먼저 일어난 하나로 끝내고 나머지 구독과 타이머를 모두 정리한다.
export function waitForCondition(
  store: PulseStore,
  connection: WaitConnectionLike,
  condition: WaitCondition,
  signal: AbortSignal,
  timers: WaitTimerApi = { setTimeout, clearTimeout },
): Promise<WaitOutcome> {
  return new Promise((resolve) => {
    const initialState = store.getState();
    const initialGeneration = store.getGeneration();
    const newRun = (state: FoldedPulseState | undefined) =>
      store.getGeneration() !== initialGeneration
      || (condition.sinceRun !== undefined && state?.run !== undefined && state.run !== condition.sinceRun);
    const unmetNow = (state: FoldedPulseState | undefined) =>
      unmetReasons(state, condition, newRun(state));
    const initialUnmet = unmetNow(initialState);
    // 낡은 상태가 조건에 맞아도 게임 상태를 보장하지 않으므로 다음 reading 을 기다린다 (#69).
    if (initialUnmet.length === 0 && store.getStaleness() === undefined) {
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
      const unmet = unmetNow(state);
      if (unmet.length === 0 && store.getStaleness() === undefined) {
        settle({ kind: "met", state });
      }
    });
    const unsubscribeDisconnect = connection.onDisconnect(() => {
      const state = store.getState();
      settle({ kind: "disconnected", state, unmet: unmetNow(state) });
    });
    const timeoutTimer = timers.setTimeout(() => {
      const state = store.getState();
      const unmet = unmetNow(state);
      settle({
        kind: "timeout",
        state,
        unmet: unmet.length === 0 && store.getStaleness() !== undefined
          ? ["the held reading is stale; no fresh reading arrived before the timeout"]
          : unmet,
      });
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

/// `WaitOutcome` 을 tool 응답 payload 로 바꾼다. timeout 과 cancelled 는 정상 결과이므로
/// `disconnected` 만 `isError` 다.
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
