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
  gone: string[];
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
}

export interface PulseDiagnostics {
  performance?: DiagnosticFrame;
  deviceContext?: DiagnosticFrame;
}

type Activity = "active" | "deactive";

interface HeldObject {
  activity: Activity;
  object: PulseObject;
}

interface InternalPulseState {
  publicState: FoldedPulseState;
  objects: Map<string, HeldObject>;
}

export function objectKey(object: PulseObject, readingScene: string): string {
  return `${object.scene ?? readingScene}/${object.selector}`;
}

function memberKey(member: PulseMember): string {
  return member.among === undefined
    ? member.member
    : `${member.member}\u0000${member.among}`;
}

function mergeMembers(
  previous: readonly PulseMember[],
  incoming: readonly PulseMember[],
): PulseMember[] {
  const merged = new Map(previous.map((member) => [memberKey(member), member]));
  for (const member of incoming) {
    merged.set(memberKey(member), member);
  }
  return [...merged.values()];
}

function mergeComponents(
  previous: readonly PulseComponent[],
  incoming: readonly PulseComponent[],
): PulseComponent[] {
  const merged = new Map(previous.map((component) => [component.on, component]));
  for (const component of incoming) {
    const oldComponent = merged.get(component.on);
    merged.set(
      component.on,
      oldComponent === undefined
        ? component
        : {
            ...oldComponent,
            ...component,
            members: mergeMembers(oldComponent.members, component.members),
          },
    );
  }
  return [...merged.values()];
}

function mergeObject(previous: PulseObject | undefined, incoming: PulseObject): PulseObject {
  if (previous === undefined) {
    return incoming;
  }
  return {
    ...previous,
    ...incoming,
    by: mergeComponents(previous.by ?? [], incoming.by ?? []),
  };
}

function indexObjects(pulse: PulseFrame, replace: boolean, previous?: InternalPulseState) {
  const objects = replace || previous === undefined
    ? new Map<string, HeldObject>()
    : new Map(previous.objects);

  for (const key of pulse.gone) {
    objects.delete(key);
  }

  const accept = (activity: Activity, incoming: PulseObject) => {
    const key = objectKey(incoming, pulse.scene);
    const oldObject = replace ? undefined : objects.get(key)?.object;
    objects.set(key, { activity, object: mergeObject(oldObject, incoming) });
  };
  pulse.active.forEach((object) => accept("active", object));
  pulse.deactive.forEach((object) => accept("deactive", object));
  return objects;
}

function toPublicState(pulse: PulseFrame, objects: Map<string, HeldObject>): FoldedPulseState {
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
  };
}

function foldInternal(
  previous: InternalPulseState | undefined,
  pulse: PulseFrame,
): InternalPulseState | undefined {
  if (previous !== undefined && pulse.reading <= previous.publicState.reading) {
    return previous;
  }
  const replace = pulse.whole ||
    (previous !== undefined && pulse.scene !== previous.publicState.scene);
  const objects = indexObjects(pulse, replace, previous);
  return { publicState: toPublicState(pulse, objects), objects };
}

export function foldPulseState(
  previous: FoldedPulseState | undefined,
  pulse: PulseFrame,
): FoldedPulseState {
  const indexedPrevious = previous === undefined
    ? undefined
    : {
        publicState: previous,
        objects: new Map<string, HeldObject>([
          ...previous.active.map((object) => [
            objectKey(object, previous.scene),
            { activity: "active" as const, object },
          ] as const),
          ...previous.deactive.map((object) => [
            objectKey(object, previous.scene),
            { activity: "deactive" as const, object },
          ] as const),
        ]),
      };
  return foldInternal(indexedPrevious, pulse)?.publicState ?? toPublicState(pulse, new Map());
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

  getDiagnostics(): PulseDiagnostics {
    return this.diagnostics;
  }
}
