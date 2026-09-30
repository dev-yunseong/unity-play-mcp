import { leafNameOf } from "./tree.js";
import { displayedTextOf } from "./visible.js";
import type { FoldedPulseState, PulseObject } from "./pulse.js";

/// `search_targets` 의 검색 조건. 세 문자열 필터(`name`, `displayedText`, `component`)는 계약을
/// 단순하게 두려고 `exact` 와 `caseSensitive` 를 함께 쓴다.
export interface SearchQuery {
  /// leaf 이름(형제 순번을 뗀 이름)과 비교한다. `Card(Clone)[3]`의 leaf 이름은 `Card(Clone)`.
  name?: string;
  /// `displayedTextOf` 의 문자열과 비교한다. 그 문자열이 없는 객체는 탈락한다.
  displayedText?: string;
  /// `object.by[].on` 중 하나와 비교한다. 예: `"Button"` 은 contains 로 `"UnityEngine.UI.Button"` 에 맞는다.
  component?: string;
  /// true면 `actionsOf`가 하나 이상인 것만, false면 하나도 없는 것만 남긴다. 생략하면 걸지 않는다.
  actionable?: boolean;
  /// 세 문자열 필터 모두에 적용된다. 기본 false = contains.
  exact?: boolean;
  /// 세 문자열 필터 모두에 적용된다. 기본 false = 대소문자 무시.
  caseSensitive?: boolean;
  /// `state.deactive` 도 검색할지. 기본 false 로 `get_scene_state` 와 같다.
  includeInactive?: boolean;
  /// 반환할 후보 수 상한. 기본 `DEFAULT_LIMIT`.
  limit?: number;
}

/// 후보 하나. `changed`/`statics`/`by` 를 빼고 대상 선택과 상세 조회에 필요한 것만 남긴다.
export interface SearchCandidate {
  id: number;
  selector: string;
  /// `object.scene ?? state.scene` — `objectKey`와 같은 규칙이다.
  scene: string;
  displayedText?: string;
  /// 이 객체에 걸린 조작(`actionsOf`). 비어 있으면 조작에 답하지 않는다.
  actions: string[];
  active: boolean;
}

export interface SearchResult {
  candidates: SearchCandidate[];
  /// `limit` 적용 전, 조건에 맞은 전체 개수.
  total: number;
  /// `total` 이 반환한 `candidates.length` 보다 큰지.
  truncated: boolean;
  /// 조건에 맞은 전체 후보(`limit` 으로 잘린 것 포함) 중 leaf 이름이 겹치는 이름들.
  ///
  /// 이 이름은 `id` 나 전체 `selector` 로 구분해야 한다는 신호다.
  duplicateNames: string[];
}

/// 반환할 후보 수 상한의 기본값. 한 화면에 훑어볼 수 있는 크기로 잡는다.
const DEFAULT_LIMIT = 20;

function normalize(value: string, caseSensitive: boolean): string {
  return caseSensitive ? value : value.toLowerCase();
}

function textMatches(candidate: string, query: string, exact: boolean, caseSensitive: boolean): boolean {
  const left = normalize(candidate, caseSensitive);
  const right = normalize(query, caseSensitive);
  return exact ? left === right : left.includes(right);
}

/// 이 객체에 걸린 조작을 `offers` 에서 그대로 옮긴다.
///
/// `WatchListJson.cs` 가 이미 정렬하고 중복을 없애 보내므로 다시 정렬하지 않는다.
function actionsOf(object: PulseObject): string[] {
  const offers = object.offers;
  if (offers === undefined) return [];

  const actions: string[] = [];
  if ((offers.clicks?.length ?? 0) > 0) {
    actions.push("click");
  }
  for (const key of offers.keys ?? []) {
    actions.push(`key:${key.key}`);
  }
  for (const pointer of offers.pointers ?? []) {
    actions.push(`pointer:${pointer}`);
  }
  return actions;
}

interface Pooled {
  object: PulseObject;
  active: boolean;
}

/// 이 객체가 `query` 의 조건을 모두 만족하는지 판정한다.
function matchesQuery(object: PulseObject, query: SearchQuery, actions: string[]): boolean {
  const exact = query.exact ?? false;
  const caseSensitive = query.caseSensitive ?? false;

  if (query.name !== undefined
    && !textMatches(leafNameOf(object.selector), query.name, exact, caseSensitive)) {
    return false;
  }

  if (query.displayedText !== undefined) {
    const displayedText = displayedTextOf(object);
    if (displayedText === undefined
      || !textMatches(displayedText, query.displayedText, exact, caseSensitive)) {
      return false;
    }
  }

  if (query.component !== undefined) {
    const hasComponent = (object.by ?? []).some(
      (component) => textMatches(component.on, query.component as string, exact, caseSensitive),
    );
    if (!hasComponent) return false;
  }

  if (query.actionable !== undefined && (actions.length > 0) !== query.actionable) {
    return false;
  }

  return true;
}

function toCandidate(pooled: Pooled, state: FoldedPulseState, actions: string[]): SearchCandidate {
  const { object, active } = pooled;
  const displayedText = displayedTextOf(object);
  return {
    id: object.id,
    selector: object.selector,
    scene: object.scene ?? state.scene,
    ...(displayedText === undefined ? {} : { displayedText }),
    actions,
    active,
  };
}

/// 이름, 표시 텍스트, component, 조작 가능 여부로 대상을 좁혀 간결한 후보를 돌려준다.
///
/// `get_scene_state` 의 `selector`(대소문자 구분 부분 일치)와는 별개의 규칙이다.
export function searchTargets(state: FoldedPulseState, query: SearchQuery = {}): SearchResult {
  const pool: Pooled[] = state.active.map((object) => ({ object, active: true }));
  if (query.includeInactive === true) {
    pool.push(...state.deactive.map((object) => ({ object, active: false })));
  }

  const matched: SearchCandidate[] = [];
  for (const pooled of pool) {
    const actions = actionsOf(pooled.object);
    if (!matchesQuery(pooled.object, query, actions)) continue;
    matched.push(toCandidate(pooled, state, actions));
  }

  const nameCounts = new Map<string, number>();
  for (const candidate of matched) {
    const name = leafNameOf(candidate.selector);
    nameCounts.set(name, (nameCounts.get(name) ?? 0) + 1);
  }
  const duplicateNames = [...nameCounts.entries()]
    .filter(([, count]) => count > 1)
    .map(([name]) => name)
    .sort();

  const limit = query.limit ?? DEFAULT_LIMIT;
  const candidates = matched.slice(0, limit);

  return {
    candidates,
    total: matched.length,
    truncated: matched.length > candidates.length,
    duplicateNames,
  };
}
