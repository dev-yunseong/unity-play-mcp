import type { FoldedPulseState, PulseMember, PulseObject } from "./pulse.js";
import { redactSecrets } from "./secrets.js";
import { PLAY_LIMITS, isRecord, utf8Bytes, type PlayScope } from "./play-types.js";
import type { PlayEvent } from "./play-predicate.js";

/// 기본 event 의 출처와 한계.
///
/// 이 log 는 pulse reading 사이의 차이만 본다. reading 사이에 생겼다 사라진 변화는 기록하지 못하므로
/// 응답의 `samplingNote` 에 그 한계를 적는다. 게임이 명시적으로 emit 한 provider event 는 `provider`
/// 출처로 따로 들어온다.
export const SAMPLING_NOTE =
  "Default events are differences between scene readings; changes that start and end between two readings are not recorded.";

interface Entry {
  event: PlayEvent;
  bytes: number;
  /// player scope 가 이 event 를 볼 수 있는지. 마지막으로 본 모습 기준이다.
  playerVisible: boolean;
}

export interface EventLogOptions {
  capacity?: number;
  maxBytes?: number;
  now?: () => number;
}

export interface WatchFilter {
  entityId?: number;
  component?: string;
  member?: string;
  providerId?: string;
  kinds?: string[];
  severity?: string;
}

export interface CursorParts {
  epoch: string;
  sequence: number;
}

export function formatCursor(parts: CursorParts): string {
  return `${parts.epoch}:${parts.sequence}`;
}

export function parseCursor(cursor: string): CursorParts | undefined {
  const at = cursor.lastIndexOf(":");
  if (at <= 0) return undefined;
  const sequence = Number(cursor.slice(at + 1));
  return Number.isInteger(sequence) && sequence >= 0 ? { epoch: cursor.slice(0, at), sequence } : undefined;
}

export interface ReadResult {
  events: PlayEvent[];
  /// 다음 호출에 넘길 cursor. event 가 없어도 현재 위치를 돌려준다.
  cursor: string;
  /// cursor 와 현재 buffer 사이에 유실된 event 가 있다.
  gap: boolean;
  oldestCursor?: string;
  /// session 이 바뀌었거나 gap 이 있어 기존 상태를 믿을 수 없다.
  baselineRequired: boolean;
  /// 응답 크기 때문에 뒤의 event 를 남겼다.
  truncated: boolean;
}

function memberKeyOf(member: PulseMember): string {
  return member.among === undefined ? member.member : `${member.member}#${member.among}`;
}

function inSight(object: PulseObject, active: boolean): boolean {
  return active && object["onScreen"] !== false && object["covered"] !== true;
}

interface Snapshot {
  run?: string;
  scene: string;
  reading: number;
  objects: Map<string, { object: PulseObject; active: boolean; members: Map<string, string> }>;
}

/// 세션 단위 ring buffer. 2048개 또는 2MiB 중 먼저 닿는 쪽에서 오래된 것부터 버린다.
export class EventLog {
  private readonly capacity: number;
  private readonly maxBytes: number;
  private readonly now: () => number;
  private entries: Entry[] = [];
  private bytes = 0;
  private nextSequence = 1;
  private oldestSequence = 1;
  private epoch: string;
  private previous?: Snapshot;
  private readonly listeners = new Set<() => void>();

  constructor(epoch = "unbound", options: EventLogOptions = {}) {
    this.epoch = epoch;
    this.capacity = options.capacity ?? PLAY_LIMITS.events.capacity;
    this.maxBytes = options.maxBytes ?? PLAY_LIMITS.events.bytes;
    this.now = options.now ?? Date.now;
  }

  get currentEpoch(): string {
    return this.epoch;
  }

  /// buffer 에 남아 있는 event 수. test 와 진단용이다.
  get size(): number {
    return this.entries.length;
  }

  /// event 가 들어올 때 호출된다. 구독 해제 함수를 돌려준다.
  onAppend(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  get listenerCount(): number {
    return this.listeners.size;
  }

  /// 지금 위치. 이후 event 만 받으려는 호출이 쓴다.
  head(): string {
    return formatCursor({ epoch: this.epoch, sequence: this.nextSequence - 1 });
  }

  /// Unity session 이 바뀌면 buffer 를 비우고 새 epoch 를 시작한다. 이전 cursor 는 만료된다.
  changeSession(sessionId: string): void {
    if (sessionId === this.epoch) return;
    const previousEpoch = this.epoch;
    this.epoch = sessionId;
    this.entries = [];
    this.bytes = 0;
    this.nextSequence = 1;
    this.oldestSequence = 1;
    this.previous = undefined;
    if (previousEpoch !== "unbound") {
      this.append({ kind: "session_changed", data: { from: previousEpoch, to: sessionId } }, true);
    }
  }

  /// 연결이 끊겼다. 끊긴 동안의 변화는 알 수 없으므로 다음 reading 은 기준선으로만 쓴다.
  noteDisconnected(): void {
    this.previous = undefined;
    this.append({ kind: "disconnected", data: { note: "changes while disconnected are unknown" } }, true);
  }

  /// Unity 가 명시적으로 emit 한 event 를 넣는다. 출처와 발생 frame 을 보존한다.
  appendProvider(event: {
    kind: string;
    providerId: string;
    name?: string;
    entity?: PlayEvent["entity"];
    data?: unknown;
    stamp?: PlayEvent["stamp"];
    playerVisible: boolean;
  }): void {
    this.append({
      kind: event.kind,
      providerId: event.providerId,
      ...(event.name === undefined ? {} : { name: event.name }),
      ...(event.entity === undefined ? {} : { entity: event.entity, entityId: event.entity.id }),
      ...(event.data === undefined ? {} : { data: event.data }),
      ...(event.stamp === undefined ? {} : { stamp: event.stamp }),
      provenance: "provider",
    }, event.playerVisible);
  }

  /// 새 reading 을 이전 reading 과 비교해 spawn/despawn/property 변화를 기록한다.
  ///
  /// run 이나 scene 이 바뀌면 번호와 객체를 비교할 수 없으므로 기준선만 다시 잡는다.
  observeReading(state: FoldedPulseState): void {
    const snapshot = this.snapshotOf(state);
    const before = this.previous;
    this.previous = snapshot;
    const stamp = { frame: state.frame, scene: state.scene };

    if (before === undefined) return;
    if (before.scene !== snapshot.scene) {
      this.append({ kind: "scene_changed", data: { from: before.scene, to: snapshot.scene }, stamp }, true);
      return;
    }
    if (before.run !== snapshot.run || snapshot.reading <= before.reading) {
      this.append({ kind: "baseline_reset", data: { reason: "reading run restarted" }, stamp }, true);
      return;
    }

    for (const [key, now] of snapshot.objects) {
      const was = before.objects.get(key);
      if (was === undefined) {
        this.append({
          kind: "entity_spawned", entityId: now.object.id, name: now.object.selector,
          data: { path: now.object.path, selector: now.object.selector }, stamp, provenance: "sample",
        }, inSight(now.object, now.active));
        continue;
      }
      if (was.active !== now.active) {
        this.append({
          kind: now.active ? "entity_activated" : "entity_deactivated", entityId: now.object.id,
          name: now.object.selector, data: { selector: now.object.selector }, stamp, provenance: "sample",
        }, inSight(now.object, now.active) || inSight(was.object, was.active));
      }
      for (const component of now.object.by ?? []) {
        for (const member of component.members ?? []) {
          const memberKey = `${component.on}\u0000${memberKeyOf(member)}`;
          const previousValue = was.members.get(memberKey);
          const currentValue = JSON.stringify(member.value ?? null);
          if (previousValue === undefined || previousValue === currentValue) continue;
          this.append({
            kind: "member_changed", entityId: now.object.id, name: now.object.selector,
            component: component.on, member: memberKeyOf(member),
            data: {
              from: redactSecrets(member.member, JSON.parse(previousValue)),
              to: redactSecrets(member.member, member.value),
            },
            stamp, provenance: "sample",
          }, inSight(now.object, now.active));
        }
      }
    }
    for (const [key, was] of before.objects) {
      if (snapshot.objects.has(key)) continue;
      this.append({
        kind: "entity_despawned", entityId: was.object.id, name: was.object.selector,
        data: { path: was.object.path, selector: was.object.selector }, stamp, provenance: "sample",
      }, inSight(was.object, was.active));
    }
  }

  private snapshotOf(state: FoldedPulseState): Snapshot {
    const objects = new Map<string, { object: PulseObject; active: boolean; members: Map<string, string> }>();
    const add = (object: PulseObject, active: boolean) => {
      const members = new Map<string, string>();
      for (const component of object.by ?? []) {
        for (const member of component.members ?? []) {
          members.set(`${component.on}\u0000${memberKeyOf(member)}`, JSON.stringify(member.value ?? null));
        }
      }
      objects.set(`${object.scene ?? state.scene}/${object.selector}`, { object, active, members });
    };
    state.active.forEach((object) => add(object, true));
    state.deactive.forEach((object) => add(object, false));
    return {
      ...(state.run === undefined ? {} : { run: state.run }),
      scene: state.scene,
      reading: state.reading,
      objects,
    };
  }

  private append(
    partial: Partial<PlayEvent> & { kind: string },
    playerVisible: boolean,
  ): void {
    const sequence = this.nextSequence++;
    const event: PlayEvent = {
      cursor: formatCursor({ epoch: this.epoch, sequence }),
      sequence,
      stamp: {},
      provenance: "sample",
      ...partial,
    };
    const entry: Entry = { event, bytes: utf8Bytes(event), playerVisible };
    this.entries.push(entry);
    this.bytes += entry.bytes;
    while (this.entries.length > this.capacity || (this.bytes > this.maxBytes && this.entries.length > 1)) {
      const dropped = this.entries.shift() as Entry;
      this.bytes -= dropped.bytes;
      this.oldestSequence = dropped.event.sequence + 1;
    }
    for (const listener of [...this.listeners]) listener();
  }

  /// `afterCursor` 뒤의 event 를 돌려준다. 없으면 현재 buffer 처음부터다.
  read(
    afterCursor: string | undefined,
    scope: PlayScope,
    filter: WatchFilter = {},
    limit: number = PLAY_LIMITS.observe.events.default,
    maxResponseBytes: number = PLAY_LIMITS.observe.textBytes.default,
  ): ReadResult {
    let after = 0;
    let gap = false;
    let baselineRequired = false;
    if (afterCursor !== undefined) {
      const parsed = parseCursor(afterCursor);
      if (parsed === undefined || parsed.epoch !== this.epoch) {
        // 다른 session 의 cursor 다. 조용히 새 session 의 event 를 이어 주지 않는다.
        gap = true;
        baselineRequired = true;
        after = this.oldestSequence - 1;
      } else {
        after = parsed.sequence;
        if (after < this.oldestSequence - 1) {
          gap = true;
          baselineRequired = true;
        }
      }
    }

    const out: PlayEvent[] = [];
    let used = 0;
    let truncated = false;
    let lastSequence = after;
    for (const entry of this.entries) {
      if (entry.event.sequence <= after) continue;
      // 허용하지 않은 event 는 응답 크기 계산에도 넣지 않는다.
      if (scope === "player" && !entry.playerVisible) {
        lastSequence = entry.event.sequence;
        continue;
      }
      if (!matches(entry.event, filter)) {
        lastSequence = entry.event.sequence;
        continue;
      }
      if (out.length >= limit || (out.length > 0 && used + entry.bytes > maxResponseBytes)) {
        truncated = true;
        break;
      }
      out.push(entry.event);
      used += entry.bytes;
      lastSequence = entry.event.sequence;
    }

    return {
      events: out,
      cursor: formatCursor({ epoch: this.epoch, sequence: truncated ? lastSequence : Math.max(lastSequence, this.nextSequence - 1) }),
      gap,
      ...(this.entries.length === 0 ? {} : { oldestCursor: formatCursor({ epoch: this.epoch, sequence: this.oldestSequence }) }),
      baselineRequired,
      truncated,
    };
  }
}

function matches(event: PlayEvent, filter: WatchFilter): boolean {
  if (filter.entityId !== undefined && event.entityId !== filter.entityId) return false;
  if (filter.component !== undefined && event.component !== filter.component) return false;
  if (filter.member !== undefined && event.member !== filter.member) return false;
  if (filter.providerId !== undefined && event.providerId !== filter.providerId) return false;
  if (filter.kinds !== undefined && filter.kinds.length > 0 && !filter.kinds.includes(event.kind)) return false;
  if (filter.severity !== undefined) {
    const severity = isRecord(event.data) ? event.data.severity : undefined;
    if (severity !== filter.severity) return false;
  }
  return true;
}
