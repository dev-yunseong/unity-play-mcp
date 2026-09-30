import type { ActionRequest, ActionResult } from "./connection.js";
import {
  PLAY_PROTOCOL_VERSION,
  isRecord,
  type EntityRef,
  type PlayScope,
} from "./play-types.js";
import { redactSample, type PlaySample, type SampleNeeds } from "./play-predicate.js";

/// `UnityConnection` 중 새 도구가 쓰는 부분. test 가 socket 없이 대신할 수 있게 좁혔다.
export interface PlayTransport {
  sendActions(actions: ActionRequest[], options?: { timeoutMilliseconds?: number }): Promise<ActionResult[]>;
  ensureConnected(): Promise<void>;
  onDisconnect?(listener: () => void): () => void;
  isConnected(): boolean;
}

/// Unity 가 돌려준 실패를 코드로 구분한다.
export class PlayCallError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly detail: Record<string, unknown> = {},
  ) {
    super(message);
    this.name = "PlayCallError";
  }
}

export interface PlayCapabilities {
  protocolVersion: number;
  runtimeVersion?: string;
  sessionId: string;
  tools: string[];
  inputPaths: string[];
  physics2D: boolean;
  physics3D: boolean;
  ui: boolean;
  providers: Array<{ id: string; status: string; [key: string]: unknown }>;
  observationPolicies: PlayScope[];
  limits?: Record<string, unknown>;
  [key: string]: unknown;
}

let nextPlayActionId = 1_000_000;

/// 새 도구가 Unity 에 거는 호출의 한 곳. 기존 `tools.ts` 의 id 와 겹치지 않는 번호를 쓴다.
export class PlayClient {
  private capabilityAttempt?: Promise<PlayCapabilities>;
  private lastSessionId?: string;
  private readonly sessionListeners = new Set<(sessionId: string, previous: string | undefined) => void>();

  constructor(private readonly transport: PlayTransport) {
    // 연결이 끊기면 Unity 가 다시 뜬 것일 수 있다. 다음 호출에서 capability 를 다시 확인한다.
    transport.onDisconnect?.(() => {
      this.capabilityAttempt = undefined;
    });
  }

  /// Unity 의 session 이 바뀌었음을 알리는 구독. 첫 관찰은 알리지 않는다.
  onSessionChange(listener: (sessionId: string, previous: string | undefined) => void): () => void {
    this.sessionListeners.add(listener);
    return () => this.sessionListeners.delete(listener);
  }

  get sessionId(): string | undefined {
    return this.lastSessionId;
  }

  noteSession(sessionId: string): void {
    if (this.lastSessionId === sessionId) return;
    const previous = this.lastSessionId;
    this.lastSessionId = sessionId;
    if (previous !== undefined) {
      for (const listener of this.sessionListeners) listener(sessionId, previous);
    }
  }

  /// 한 wire method 를 부른다. 실패는 `PlayCallError` 로 던진다.
  async call(method: string, params: unknown[], timeoutMilliseconds?: number): Promise<unknown> {
    const request: ActionRequest = { id: nextPlayActionId++, method, params };
    const results = await this.transport.sendActions(
      [request],
      timeoutMilliseconds === undefined ? {} : { timeoutMilliseconds },
    );
    const result = results.find((item) => item.id === request.id) ?? results[0];
    if (result === undefined) throw new PlayCallError("protocol_error", `${method} returned no result`);
    if (!result.success) throw classifyFailure(method, result.error ?? "");
    return result.returnValue;
  }

  /// Unity 가 새 도구를 지원하는지 확인한다. 지원하지 않으면 `unsupported_capability` 로 던진다.
  ///
  /// 기존 도구는 이 호출과 무관하게 계속 동작한다.
  async capabilities(): Promise<PlayCapabilities> {
    if (this.capabilityAttempt === undefined) {
      const attempt = this.fetchCapabilities();
      this.capabilityAttempt = attempt;
      // 실패한 결과를 붙잡지 않는다. Unity 를 업데이트한 뒤 다시 확인해야 한다.
      attempt.catch(() => {
        if (this.capabilityAttempt === attempt) this.capabilityAttempt = undefined;
      });
    }
    const value = await this.capabilityAttempt;
    this.noteSession(value.sessionId);
    return value;
  }

  private async fetchCapabilities(): Promise<PlayCapabilities> {
    const raw = await this.call("play_capabilities", []);
    if (!isRecord(raw) || typeof raw.protocolVersion !== "number" || typeof raw.sessionId !== "string") {
      throw new PlayCallError("unsupported_capability",
        "Unity returned an unrecognised play_capabilities payload. Update the Unity Play MCP package.");
    }
    if (raw.protocolVersion !== PLAY_PROTOCOL_VERSION) {
      throw new PlayCallError("unsupported_capability",
        `Unity speaks play protocol ${raw.protocolVersion}, this server speaks ${PLAY_PROTOCOL_VERSION}. `
        + "Update the older side so the versions match.",
        { unityProtocolVersion: raw.protocolVersion, serverProtocolVersion: PLAY_PROTOCOL_VERSION });
    }
    return {
      protocolVersion: raw.protocolVersion,
      sessionId: raw.sessionId,
      tools: Array.isArray(raw.tools) ? raw.tools.filter((item): item is string => typeof item === "string") : [],
      inputPaths: Array.isArray(raw.inputPaths) ? raw.inputPaths.filter((item): item is string => typeof item === "string") : [],
      physics2D: raw.physics2D === true,
      physics3D: raw.physics3D === true,
      ui: raw.ui === true,
      providers: Array.isArray(raw.providers) ? (raw.providers as PlayCapabilities["providers"]) : [],
      observationPolicies: Array.isArray(raw.observationPolicies)
        ? (raw.observationPolicies as PlayScope[]) : ["player", "debug"],
      ...(typeof raw.runtimeVersion === "string" ? { runtimeVersion: raw.runtimeVersion } : {}),
      ...(isRecord(raw.limits) ? { limits: raw.limits } : {}),
    };
  }

  /// 필요한 대상만 지금 샘플링한다. 조건 평가용이다.
  async sample(needs: SampleNeeds, scope: PlayScope, timeoutMilliseconds: number): Promise<PlaySample> {
    const raw = await this.call("play_sample", [{ scope, ...needs }], timeoutMilliseconds);
    const sample = parseSample(raw, scope);
    this.noteSession(sample.stamp.sessionId);
    return redactSample(sample);
  }
}

function classifyFailure(method: string, error: string): PlayCallError {
  if (/unsupported method/i.test(error)) {
    return new PlayCallError("unsupported_capability",
      `Unity does not implement ${method}. Update the Unity Play MCP package; the existing tools keep working.`);
  }
  const busy = /^busy:(\S*)\s*(.*)$/i.exec(error);
  if (busy !== null) {
    return new PlayCallError("busy", busy[2] === "" ? "another operation holds the input lock" : busy[2] as string,
      { operationId: busy[1] === "" ? undefined : busy[1] });
  }
  const coded = /^([a-z_]+):\s*(.*)$/s.exec(error);
  if (coded !== null && KNOWN_CODES.has(coded[1] as string)) {
    return new PlayCallError(coded[1] as string, coded[2] as string);
  }
  return new PlayCallError("unity_error", error === "" ? `${method} failed` : error);
}

const KNOWN_CODES = new Set([
  "stale_ref", "ambiguous_camera", "invalid_request", "scene_changed", "unknown_entity",
  "unsupported_space", "blocked", "not_interactable", "out_of_scope",
]);

function parseSample(raw: unknown, scope: PlayScope): PlaySample {
  if (!isRecord(raw) || !isRecord(raw.stamp) || typeof raw.stamp.sessionId !== "string") {
    throw new PlayCallError("protocol_error", "play_sample returned an invalid payload");
  }
  const stamp = raw.stamp as unknown as PlaySample["stamp"];
  return {
    stamp,
    scope,
    targets: isRecord(raw.targets) ? (raw.targets as PlaySample["targets"]) : {},
    members: Array.isArray(raw.members) ? (raw.members as PlaySample["members"]) : [],
    facts: Array.isArray(raw.facts) ? (raw.facts as PlaySample["facts"]) : [],
  };
}

export function refOf(value: unknown): EntityRef | undefined {
  return isRecord(value) && typeof value.sessionId === "string"
    && Number.isInteger(value.id) && Number.isInteger(value.generation)
    ? (value as unknown as EntityRef)
    : undefined;
}
