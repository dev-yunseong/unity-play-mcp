import { createHash } from "node:crypto";

import { PLAY_LIMITS } from "./play-types.js";

/// 입력을 만드는 wire method. 이것들은 operation lock 을 따른다.
///
/// `capture_screen` 과 reading 시작/정지는 입력이 아니라 lock 없이 동작한다.
const MUTATING_METHODS = new Set([
  "button_click", "pointer_click", "pointer_drag", "pointer_hover", "enter_text", "move_mouse",
  "mouse_down", "mouse_up", "key_click", "key_down", "key_up", "set_axis", "set_button",
  "pause_time", "resume_time", "reset_game",
]);

export function isMutatingMethod(method: string): boolean {
  return MUTATING_METHODS.has(method);
}

/// 이 프로세스에서 진행 중인 `act_and_observe` 를 알리는 gate.
///
/// MCP server 는 client 마다 프로세스가 따로이므로 다른 client 와의 경합은 Unity 쪽 lock 이 막는다.
/// 이 gate 는 같은 프로세스의 기존 입력 도구가 진행 중인 operation 의 입력 사이에 끼지 않게 한다.
export class InputGate {
  private active?: string;

  get activeOperationId(): string | undefined {
    return this.active;
  }

  acquire(operationId: string): boolean {
    if (this.active !== undefined && this.active !== operationId) return false;
    this.active = operationId;
    return true;
  }

  release(operationId: string): void {
    if (this.active === operationId) this.active = undefined;
  }
}

/// 진행 중인 operation 이 있을 때 기존 입력 도구가 돌려주는 문장.
export function busyText(operationId: string): string {
  return `busy: operation "${operationId}" is running and owns the input. `
    + "Wait for it to finish (or retry that operationId) before sending other input.";
}

/// 요청을 정규화해 hash 한다. 같은 요청이면 key 순서와 무관하게 같은 값이다.
export function canonicalHash(value: unknown): string {
  return createHash("sha256").update(canonicalJson(value)).digest("hex");
}

export function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value !== null && typeof value === "object") {
    const record = value as Record<string, unknown>;
    return `{${Object.keys(record).filter((key) => record[key] !== undefined).sort()
      .map((key) => `${JSON.stringify(key)}:${canonicalJson(record[key])}`).join(",")}}`;
  }
  return JSON.stringify(value) ?? "null";
}

interface LedgerEntry<T> {
  hash: string;
  startedAt: number;
  promise: Promise<T>;
  settled: boolean;
  result?: T;
}

export type LedgerBegin<T> =
  | { kind: "new"; finish: (run: () => Promise<T>) => Promise<T> }
  | { kind: "replay"; promise: Promise<T> }
  | { kind: "conflict" };

/// 같은 `operationId` 재시도가 입력을 다시 보내지 않게 하는 결과 보관소.
///
/// 최근 `keep`개 또는 `keepMs` 중 넓은 쪽을 보관한다. 진행 중인 항목은 버리지 않고, 완료된 항목만
/// 오래된 순서로 버린다. 버린 뒤 같은 id 가 오면 새 요청으로 본다(Unity 쪽 기록이 남아 있으면 그쪽이
/// 다시 실행을 막는다).
export class OperationLedger<T> {
  private readonly entries = new Map<string, LedgerEntry<T>>();

  constructor(
    private readonly now: () => number = Date.now,
    private readonly keep: number = PLAY_LIMITS.operations.keep,
    private readonly keepMs: number = PLAY_LIMITS.operations.keepMs,
  ) {}

  get size(): number {
    return this.entries.size;
  }

  begin(operationId: string, hash: string): LedgerBegin<T> {
    this.evict();
    const held = this.entries.get(operationId);
    if (held !== undefined) {
      return held.hash === hash ? { kind: "replay", promise: held.promise } : { kind: "conflict" };
    }
    return {
      kind: "new",
      finish: (run) => {
        const entry: LedgerEntry<T> = { hash, startedAt: this.now(), settled: false, promise: undefined as unknown as Promise<T> };
        entry.promise = run().then(
          (result) => {
            entry.settled = true;
            entry.result = result;
            return result;
          },
          (error: unknown) => {
            // 실패한 실행도 다시 실행하지 않는다. 입력이 일부 나갔을 수 있다.
            entry.settled = true;
            throw error;
          },
        );
        this.entries.set(operationId, entry);
        return entry.promise;
      },
    };
  }

  /// 개수가 `keep`을 넘고 동시에 `keepMs`보다 오래된 완료 항목만 버린다. 그래서 최소 최근 `keep`개와
  /// 최근 `keepMs` 를 모두 보관한다. 진행 중인 항목은 버리지 않는다.
  private evict(): void {
    const cutoff = this.now() - this.keepMs;
    for (const [id, entry] of this.entries) {
      if (this.entries.size <= this.keep) return;
      if (entry.settled && entry.startedAt < cutoff) this.entries.delete(id);
    }
  }
}

/// 이 프로세스의 기본 gate. 기존 입력 도구와 `act_and_observe` 가 같이 쓴다.
export const defaultInputGate = new InputGate();
