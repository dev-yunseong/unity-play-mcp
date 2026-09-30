import { holdsRedaction, REDACTED_TEXT } from "./secrets.js";
import type {
  FoldedPulseState,
  JsonValue,
  PulseComponent,
  PulseMember,
  PulseObject,
} from "./pulse.js";

/// 화면에 무언가를 그리는 component 의 namespace.
///
/// 목록을 고칠 때 Unity 재빌드가 필요 없도록 판정을 SDK 가 아니라 여기 둔다. 개별 타입 이름은
/// 모두 이 접두사로 시작하므로 따로 적지 않는다.
const SHOWING_NAMESPACES = ["UnityEngine.UI.", "TMPro."];

/// 이 component 가 화면에 무언가를 보이고 있는지 판정한다.
///
/// SDK 는 멤버를 읽은 component 만 `by` 에 싣고, Unity/TextMeshPro 타입은 화면에 보이는 값이나
/// `evidence` 가 지목한 멤버가 있을 때만 멤버를 가지므로 접두사로 충분하다. 게임이 `Image` 등을
/// 상속한 타입(`MyGame.HealthBar`)은 잡지 못한다.
///
/// `members` 가 optional 이므로(#19) `elementsOf` 가 다시 검사하지 않도록 type predicate 로 둔다.
function shows(component: PulseComponent): component is PulseComponent & { members: PulseMember[] } {
  if (component.members === undefined || component.members.length === 0) return false;
  return SHOWING_NAMESPACES.some((prefix) => component.on.startsWith(prefix));
}

/// 화면 위의 요소 하나와 그 내용, 위치, 가시성.
export interface VisibleElement {
  id: number;
  path: string;
  selector: string;
  scene?: string;
  /// 이 요소를 그리는 component 의 타입 이름(`by[].on`).
  on: string;
  /// 보이고 있는 값(글자, 채움 비율 등).
  shows: Record<string, JsonValue>;
  /// 좌상단 기준 화면 픽셀. SDK 의 `rect` 그대로다.
  rect?: JsonValue;
  active: boolean;
  onScreen?: boolean;
  covered?: boolean;
}

export interface VisibleQuery {
  selector?: string;
  includeHidden?: boolean;
}

function flagOf(object: PulseObject, name: string): boolean | undefined {
  const value = object[name];
  return typeof value === "boolean" ? value : undefined;
}

/// 이 객체가 화면에 보이는 첫 문자열 값. `search.ts` 가 후보 요약에 쓴다.
///
/// 사람이 읽을 값은 대개 문자열이므로 `fillAmount` 나 `isOn` 같은 값은 건너뛴다.
export function displayedTextOf(object: PulseObject): string | undefined {
  for (const component of object.by ?? []) {
    if (!shows(component)) continue;
    for (const member of component.members) {
      if (typeof member.value === "string") return member.value;
      // 가린 값은 글자가 없는 것과 다르므로 가렸다는 표시를 돌려준다 (#72).
      if (holdsRedaction(member.value)) return REDACTED_TEXT;
    }
  }
  return undefined;
}

/// 이 요소를 지금 볼 수 있는지 판정한다. 아니면 기본 응답에서 뺀다.
function inSight(element: VisibleElement): boolean {
  return element.active && element.onScreen !== false && element.covered !== true;
}

function elementsOf(object: PulseObject, active: boolean): VisibleElement[] {
  const found: VisibleElement[] = [];
  for (const component of object.by ?? []) {
    if (!shows(component)) continue;
    const showing: Record<string, JsonValue> = {};
    for (const member of component.members) {
      showing[member.among === undefined ? member.member : `${member.member}#${member.among}`] =
        member.value;
    }
    found.push({
      id: object.id,
      path: object.path,
      selector: object.selector,
      ...(object.scene === undefined ? {} : { scene: object.scene }),
      on: component.on,
      shows: showing,
      ...(object.rect === undefined ? {} : { rect: object.rect as JsonValue }),
      active,
      ...(flagOf(object, "onScreen") === undefined ? {} : { onScreen: flagOf(object, "onScreen") }),
      ...(flagOf(object, "covered") === undefined ? {} : { covered: flagOf(object, "covered") }),
    });
  }
  return found;
}

/// 마지막 reading 에서 화면에 보이는 요소들. 게임에 새로 묻지 않는다.
///
/// 꺼져 있거나 화면 밖이거나 가려진 것은 기본으로 뺀다. `includeHidden` 이면 전부 돌려주고
/// 각 요소의 `active`/`onScreen`/`covered` 로 이유를 알 수 있다.
export function visibleElements(
  state: FoldedPulseState,
  query: VisibleQuery = {},
): VisibleElement[] {
  const found: VisibleElement[] = [];
  for (const object of state.active) {
    found.push(...elementsOf(object, true));
  }
  for (const object of state.deactive) {
    found.push(...elementsOf(object, false));
  }

  const narrowed = query.selector === undefined
    ? found
    : found.filter((element) => element.selector.includes(query.selector as string));

  return query.includeHidden === true ? narrowed : narrowed.filter(inSight);
}
