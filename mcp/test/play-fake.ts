import type { ActionRequest, ActionResult } from "../src/connection.js";
import { PlayClient, type PlayTransport } from "../src/play-client.js";
import type { PlaySample } from "../src/play-predicate.js";

/// handler 가 던지면 Unity 가 실패를 돌려준 것으로 본다.
export class UnityFailure extends Error {}

export type Handler = (params: unknown[]) => unknown | Promise<unknown>;

export interface RecordedCall {
  method: string;
  params: unknown[];
  timeoutMilliseconds?: number;
}

/// socket 없이 Unity 의 wire method 를 흉내 내는 transport.
export class FakeUnity implements PlayTransport {
  readonly calls: RecordedCall[] = [];
  handlers = new Map<string, Handler>();
  private readonly listeners = new Set<() => void>();
  connected = true;

  constructor(handlers: Record<string, Handler> = {}) {
    for (const [method, handler] of Object.entries(handlers)) this.handlers.set(method, handler);
  }

  on(method: string, handler: Handler): this {
    this.handlers.set(method, handler);
    return this;
  }

  async sendActions(actions: ActionRequest[], options: { timeoutMilliseconds?: number } = {}): Promise<ActionResult[]> {
    const results: ActionResult[] = [];
    for (const action of actions) {
      this.calls.push({ method: action.method, params: action.params, ...(options.timeoutMilliseconds === undefined ? {} : { timeoutMilliseconds: options.timeoutMilliseconds }) });
      const handler = this.handlers.get(action.method);
      if (handler === undefined) {
        results.push({ id: action.id, success: false, error: `Unsupported method: ${action.method}` });
        continue;
      }
      try {
        const returnValue = await handler(action.params);
        results.push({ id: action.id, success: true, ...(returnValue === undefined ? {} : { returnValue }) });
      } catch (error) {
        if (error instanceof UnityFailure) {
          results.push({ id: action.id, success: false, error: error.message });
        } else {
          throw error;
        }
      }
    }
    return results;
  }

  async ensureConnected(): Promise<void> {
    if (!this.connected) throw new Error("Unity is not running. Nothing is listening at ws://x");
  }

  onDisconnect(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  isConnected(): boolean {
    return this.connected;
  }

  disconnect(): void {
    this.connected = false;
    for (const listener of [...this.listeners]) listener();
  }

  get listenerCount(): number {
    return this.listeners.size;
  }

  methods(): string[] {
    return this.calls.map((call) => call.method);
  }

  count(method: string): number {
    return this.calls.filter((call) => call.method === method).length;
  }
}

export const SESSION = "session-a";

export function capabilitiesPayload(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    protocolVersion: 1,
    runtimeVersion: "0.5.0",
    sessionId: SESSION,
    tools: ["observe", "act_and_observe"],
    inputPaths: ["button", "pointer", "key", "axis"],
    physics2D: true,
    physics3D: true,
    ui: true,
    providers: [],
    observationPolicies: ["player", "debug"],
    ...overrides,
  };
}

export function stampOf(overrides: Record<string, unknown> = {}): PlaySample["stamp"] {
  return {
    sessionId: SESSION, scene: "Main", frame: 100, reading: 1,
    sampledAtMonotonicMs: 1000, gameTimeSeconds: 5, ...overrides,
  } as PlaySample["stamp"];
}

export function entity(id: number, overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    ref: { sessionId: SESSION, id, generation: 1 },
    label: `Thing${id}`, types: ["Button"], active: true,
    facts: [], actions: [], ...overrides,
  };
}

export function client(unity: FakeUnity): PlayClient {
  return new PlayClient(unity);
}

export function baseUnity(extra: Record<string, Handler> = {}): FakeUnity {
  return new FakeUnity({
    play_capabilities: () => capabilitiesPayload(),
    ...extra,
  });
}

/// 결정적인 시계. `sleep` 이 시간을 앞으로 보낸다.
export class FakeClock {
  time = 0;
  now = (): number => this.time;
  sleep = async (ms: number): Promise<void> => {
    this.time += ms;
  };
}
