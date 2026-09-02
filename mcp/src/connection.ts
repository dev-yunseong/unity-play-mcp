import WebSocket from "ws";

import type { GamePush, PulseStore } from "./pulse.js";

export interface ActionRequest {
  id: number;
  method: string;
  params: unknown[];
}

export interface ActionResult {
  id: number;
  success: boolean;
  returnValue?: unknown;
  error?: string;
}

export interface ActionResultFrame {
  type: "ACTION_RESULT";
  id: number;
  requestId: number;
  frame: number;
  results: ActionResult[];
}

interface ActionFrame {
  type: "ACTION";
  id: number;
  actions: ActionRequest[];
}

interface ErrorFrame {
  type: "ERROR";
  id: number;
  message: string;
}

interface WebSocketLike {
  readonly readyState: number;
  on(event: "open", listener: () => void): this;
  on(event: "message", listener: (data: unknown) => void): this;
  on(event: "close", listener: () => void): this;
  on(event: "error", listener: (error: Error) => void): this;
  send(data: string): void;
  close(): void;
}

interface TimerApi {
  setTimeout(callback: () => void, delayMilliseconds: number): ReturnType<typeof setTimeout>;
  clearTimeout(timer: ReturnType<typeof setTimeout>): void;
}

export interface UnityConnectionOptions {
  url?: string;
  timeoutMilliseconds?: number;
  pulseStore: PulseStore;
  createWebSocket?: (url: string) => WebSocketLike;
  timers?: TimerApi;
  reconnectBaseMilliseconds?: number;
  reconnectMaximumMilliseconds?: number;
  report?: (message: string, error?: unknown) => void;
}

interface PendingAction {
  resolve: (result: ActionResultFrame) => void;
  reject: (error: Error) => void;
  timeout: ReturnType<typeof setTimeout>;
}

/// Unity 에 닿지 못해 실패했다는 것.
///
/// 이것과 그냥 `Error` 를 가르는 이유는 부르는 쪽이 두 실패에 다른 말을 해야 하기 때문이다. 소켓에
/// 닿지 못한 것은 게임이 안 돌고 있다는 뜻이고 사용자가 Play Mode 를 시작해야 풀린다. action 이
/// 실패한 것은 게임은 돌고 있는데 그 요청이 틀렸다는 뜻이다. 문자열을 비교해 가르지 않는다.
export class UnityUnreachableError extends Error {
  constructor(readonly url: string, readonly cause?: unknown) {
    super(
      `Unity is not running. Nothing is listening at ${url}. ` +
      "The Unity editor must be open with the project in Play Mode. " +
      "Ask the user to enter Play Mode, then try again.",
    );
    this.name = "UnityUnreachableError";
  }
}

const OPEN = 1;

export class UnityConnection {
  private readonly url: string;
  private readonly timeoutMilliseconds: number;
  private readonly pulseStore: PulseStore;
  private readonly createWebSocket: (url: string) => WebSocketLike;
  private readonly timers: TimerApi;
  private readonly reconnectBaseMilliseconds: number;
  private readonly reconnectMaximumMilliseconds: number;
  private readonly report: (message: string, error?: unknown) => void;
  private readonly pending = new Map<number, PendingAction>();

  private socket?: WebSocketLike;
  private connectionAttempt?: Promise<WebSocketLike>;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private reconnectDelayMilliseconds: number;
  private nextEnvelopeId = 1;
  private started = false;
  private disposed = false;

  constructor(options: UnityConnectionOptions) {
    this.url = options.url ?? process.env.UNITY_PLAY_MCP_URL ?? "ws://127.0.0.1:17311/ws";
    this.timeoutMilliseconds = options.timeoutMilliseconds
      ?? parsePositiveInteger(process.env.UNITY_PLAY_MCP_TIMEOUT_MS)
      ?? 15_000;
    this.pulseStore = options.pulseStore;
    this.createWebSocket = options.createWebSocket
      ?? ((url) => new WebSocket(url) as unknown as WebSocketLike);
    this.timers = options.timers ?? { setTimeout, clearTimeout };
    this.reconnectBaseMilliseconds = options.reconnectBaseMilliseconds ?? 250;
    this.reconnectMaximumMilliseconds = options.reconnectMaximumMilliseconds ?? 8_000;
    this.reconnectDelayMilliseconds = this.reconnectBaseMilliseconds;
    this.report = options.report ?? (() => undefined);
  }

  async sendActions(actions: ActionRequest[]): Promise<ActionResult[]> {
    if (actions.length === 0) {
      throw new Error("At least one action is required");
    }

    this.started = true;
    const socket = await this.connect();
    const requestId = this.nextEnvelopeId++;
    const frame: ActionFrame = {
      type: "ACTION",
      id: requestId,
      actions,
    };

    const resultFrame = await new Promise<ActionResultFrame>((resolve, reject) => {
      const timeout = this.timers.setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error(`ACTION ${requestId} timed out after ${this.timeoutMilliseconds}ms`));
      }, this.timeoutMilliseconds);

      this.pending.set(requestId, { resolve, reject, timeout });
      try {
        socket.send(JSON.stringify(frame));
      } catch (error) {
        this.settlePendingFailure(requestId, toError(error, `Failed to send ACTION ${requestId}`));
      }
    });
    return resultFrame.results;
  }

  /// 지금 이 순간 소켓이 열려 있는지. 새로 연결하지 않으므로 상태를 보는 쪽이 상태를 바꾸지 않는다.
  isConnected(): boolean {
    return this.socket?.readyState === OPEN;
  }

  /// 이 server 가 Unity 를 찾는 자리.
  get endpoint(): string {
    return this.url;
  }

  async ensureConnected(): Promise<void> {
    this.started = true;
    await this.connect();
  }

  close(): void {
    this.disposed = true;
    this.clearReconnectTimer();
    this.rejectAllPending(new Error("Unity connection closed"));
    this.socket?.close();
    this.socket = undefined;
  }

  private connect(): Promise<WebSocketLike> {
    if (this.disposed) {
      return Promise.reject(new Error("Unity connection has been closed"));
    }
    if (this.socket?.readyState === OPEN) {
      return Promise.resolve(this.socket);
    }
    if (this.connectionAttempt) {
      return this.connectionAttempt;
    }

    this.clearReconnectTimer();
    this.connectionAttempt = new Promise<WebSocketLike>((resolve, reject) => {
      const socket = this.createWebSocket(this.url);
      let settled = false;
      this.socket = socket;

      socket.on("open", () => {
        if (settled) {
          return;
        }
        settled = true;
        this.connectionAttempt = undefined;
        this.reconnectDelayMilliseconds = this.reconnectBaseMilliseconds;
        resolve(socket);
      });
      socket.on("message", (data) => this.handleMessage(data));
      socket.on("close", () => {
        if (!settled) {
          settled = true;
          this.connectionAttempt = undefined;
          reject(new UnityUnreachableError(this.url));
        }
        this.handleDisconnect(socket, new Error("Unity WebSocket closed"));
      });
      socket.on("error", (error) => {
        if (!settled) {
          settled = true;
          this.connectionAttempt = undefined;
          reject(new UnityUnreachableError(this.url, error));
        }
        this.handleDisconnect(socket, toError(error, "Unity WebSocket error"));
      });
    });
    return this.connectionAttempt;
  }

  private handleDisconnect(socket: WebSocketLike, error: Error): void {
    if (this.socket !== socket) {
      return;
    }
    this.socket = undefined;
    this.rejectAllPending(error);
    if (this.started && !this.disposed) {
      this.scheduleReconnect();
    }
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer || this.connectionAttempt || this.disposed) {
      return;
    }
    const delay = this.reconnectDelayMilliseconds;
    this.reconnectDelayMilliseconds = Math.min(delay * 2, this.reconnectMaximumMilliseconds);
    this.reconnectTimer = this.timers.setTimeout(() => {
      this.reconnectTimer = undefined;
      void this.connect().catch((error: unknown) => {
        this.report("Unity WebSocket reconnect failed", error);
        this.scheduleReconnect();
      });
    }, delay);
  }

  private handleMessage(rawData: unknown): void {
    let frame: unknown;
    try {
      frame = JSON.parse(decodeMessage(rawData));
    } catch (error) {
      this.report("Ignored malformed Unity WebSocket frame", error);
      return;
    }
    if (!isTypedFrame(frame)) {
      this.report("Ignored Unity WebSocket frame without a type");
      return;
    }

    if (frame.type === "ACTION_RESULT") {
      if (!isActionResultFrame(frame)) {
        this.report("Ignored malformed ACTION_RESULT frame");
        return;
      }
      const pending = this.pending.get(frame.requestId);
      if (!pending) {
        this.report(`Ignored unmatched or duplicate ACTION_RESULT ${frame.requestId}`);
        return;
      }
      this.pending.delete(frame.requestId);
      this.timers.clearTimeout(pending.timeout);
      pending.resolve(frame);
      return;
    }

    if (frame.type === "PULSE" || frame.type === "PERFORMANCE" || frame.type === "DEVICE_CONTEXT") {
      if (!isGamePushFrame(frame)) {
        this.report(`Ignored malformed ${frame.type} frame`);
        return;
      }
      try {
        this.pulseStore.fold(frame);
      } catch (error) {
        this.report(`Ignored malformed ${frame.type} frame`, error);
      }
      return;
    }

    if (frame.type === "ERROR") {
      if (isErrorFrame(frame)) {
        this.report(`Unity error ${frame.id}: ${frame.message}`);
      } else {
        this.report("Ignored malformed ERROR frame");
      }
      return;
    }

    this.report(`Ignored unknown Unity WebSocket frame type ${frame.type}`);
  }

  private settlePendingFailure(requestId: number, error: Error): void {
    const pending = this.pending.get(requestId);
    if (!pending) {
      return;
    }
    this.pending.delete(requestId);
    this.timers.clearTimeout(pending.timeout);
    pending.reject(error);
  }

  private rejectAllPending(error: Error): void {
    for (const [requestId, pending] of this.pending) {
      this.pending.delete(requestId);
      this.timers.clearTimeout(pending.timeout);
      pending.reject(error);
    }
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer) {
      this.timers.clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
  }
}

function parsePositiveInteger(value: string | undefined): number | undefined {
  if (value === undefined) {
    return undefined;
  }
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
}

function decodeMessage(data: unknown): string {
  if (typeof data === "string") {
    return data;
  }
  if (data instanceof ArrayBuffer) {
    return Buffer.from(data).toString("utf8");
  }
  if (ArrayBuffer.isView(data)) {
    return Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString("utf8");
  }
  return String(data);
}

function isTypedFrame(value: unknown): value is { type: string } {
  return typeof value === "object" && value !== null && typeof Reflect.get(value, "type") === "string";
}

function isActionResultFrame(value: { type: string }): value is ActionResultFrame {
  const results = Reflect.get(value, "results");
  return value.type === "ACTION_RESULT"
    && typeof Reflect.get(value, "id") === "number"
    && typeof Reflect.get(value, "requestId") === "number"
    && typeof Reflect.get(value, "frame") === "number"
    && Array.isArray(results)
    && results.every(isActionResult);
}

function isActionResult(value: unknown): value is ActionResult {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const returnValue = Reflect.get(value, "returnValue");
  const error = Reflect.get(value, "error");
  return typeof Reflect.get(value, "id") === "number"
    && typeof Reflect.get(value, "success") === "boolean"
    && (returnValue === undefined || isJsonValue(returnValue))
    && (error === undefined || typeof error === "string");
}

function isJsonValue(value: unknown): boolean {
  if (value === null || typeof value === "string" || typeof value === "boolean") {
    return true;
  }
  if (typeof value === "number") {
    return Number.isFinite(value);
  }
  if (Array.isArray(value)) {
    return value.every(isJsonValue);
  }
  if (typeof value === "object") {
    return Object.values(value).every(isJsonValue);
  }
  return false;
}

function isErrorFrame(value: { type: string }): value is ErrorFrame {
  return value.type === "ERROR"
    && typeof Reflect.get(value, "id") === "number"
    && typeof Reflect.get(value, "message") === "string";
}

function isGamePushFrame(value: { type: string }): value is GamePush {
  if (value.type === "PERFORMANCE" || value.type === "DEVICE_CONTEXT") {
    return typeof Reflect.get(value, "id") === "number";
  }
  const gone = Reflect.get(value, "gone");
  return value.type === "PULSE"
    && typeof Reflect.get(value, "id") === "number"
    && typeof Reflect.get(value, "schema") === "number"
    && typeof Reflect.get(value, "reading") === "number"
    && typeof Reflect.get(value, "frame") === "number"
    && typeof Reflect.get(value, "scene") === "string"
    && typeof Reflect.get(value, "whole") === "boolean"
    && typeof Reflect.get(value, "watching") === "number"
    && typeof Reflect.get(value, "unresolved") === "number"
    && typeof Reflect.get(value, "unwatchable") === "number"
    && Array.isArray(Reflect.get(value, "statics"))
    && Array.isArray(Reflect.get(value, "active"))
    && Array.isArray(Reflect.get(value, "deactive"))
    && (gone === undefined || Array.isArray(gone))
    && Array.isArray(Reflect.get(value, "changed"));
}

function toError(value: unknown, fallbackMessage: string): Error {
  return value instanceof Error ? value : new Error(fallbackMessage);
}
