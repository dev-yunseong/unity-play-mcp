import type { FoldedPulseState, JsonValue, PulseComponent, PulseObject } from "./pulse.js";

/// 화면에 무언가를 내놓는 컴포넌트가 사는 두 namespace.
///
/// 판정이 여기 있고 SDK 에 없는 이유는 그래야 목록을 고치는 데 Unity 재빌드가 필요 없기
/// 때문이다. SDK 는 타입 이름을 `by[].on` 에 실어 보낼 뿐 그것이 무엇인지 말하지 않는다.
///
/// 정확한 타입 이름 열둘(`UnityEngine.UI.Text`, `TMPro.TextMeshProUGUI`, ...)을 따로 적지
/// 않는다. 전부 이 두 접두사로 시작하므로 같은 규칙을 두 곳에 적는 일이 되고, 목록이 늘 때
/// 한쪽만 고쳐지는 것이 판정을 한 곳에 모은 이유 그 자체다.
const SHOWING_NAMESPACES = ["UnityEngine.UI.", "TMPro."];

/// 이 컴포넌트가 화면에 무언가를 보이고 있는가.
///
/// 접두사만으로 충분한 이유: SDK 는 멤버를 하나라도 읽어 낸 컴포넌트만 `by` 에 쓴다. Unity 나
/// TextMeshPro 어셈블리의 타입에 멤버가 붙는 길은 둘뿐이다 — 화면에 무언가를 그려서 SDK 가
/// 그것이 보여 주는 것을 읽었거나, 근거가 그 타입의 멤버를 이름 댔거나. 앞의 것이 찾는
/// 그것이고 뒤의 것도 근거가 이름 댄 Unity UI 컴포넌트다. `ScrollRect` 나 `LayoutElement`
/// 처럼 아무도 안 읽는 것은 `by` 에 아예 나오지 않는다.
///
/// 못 잡는 것: 게임이 `Image` 나 `Button` 을 상속해 만든 타입은 제 이름(`MyGame.HealthBar`)
/// 으로 오므로 접두사에 안 걸린다.
function shows(component: PulseComponent): boolean {
  if (component.members === undefined || component.members.length === 0) return false;
  return SHOWING_NAMESPACES.some((prefix) => component.on.startsWith(prefix));
}

/// 화면 위의 요소 하나. 무엇을 보이고 있고, 어디 있고, 지금 눈에 닿는지.
export interface VisibleElement {
  id: number;
  path: string;
  selector: string;
  scene?: string;
  /// 그것을 보이고 있는 컴포넌트의 타입 이름. SDK 가 `by[].on` 에 실어 보낸 그대로다.
  on: string;
  /// 그 컴포넌트가 지금 보이고 있는 것 — 글자, 채움 비율, 값.
  shows: Record<string, JsonValue>;
  /// 좌상단 기준 화면 픽셀. SDK 의 `rect` 를 그대로 옮긴다.
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

/// 이 요소를 사람이 지금 볼 수 있는가. 셋 중 하나라도 아니면 기본 응답에서 뺀다.
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

/// 마지막 reading 이 말한 것 중 화면에 보이는 요소들. 게임에 묻지 않는다.
///
/// 꺼져 있거나 화면 밖이거나 가려진 것은 기본으로 뺀다. `includeHidden` 이 서면 전부 내고,
/// 각 요소가 제 `active`/`onScreen`/`covered` 로 왜 빠졌을지를 말한다.
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
