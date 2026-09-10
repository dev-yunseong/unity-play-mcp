import { leafNameOf } from "./tree.js";
import { displayedTextOf } from "./visible.js";
import type { FoldedPulseState, PulseObject } from "./pulse.js";

/// `search_targets`가 받는 검색 조건. 세 문자열 필터(`name`, `displayedText`, `component`)는
/// `exact`와 `caseSensitive`를 함께 쓴다 — 필터마다 다른 대소문자 규칙을 두는 것은 이 issue가
/// 풀려는 문제(검색 계약이 불명확하다) 보다 더 복잡한 계약을 새로 만드는 것이라 하나로 묶는다.
export interface SearchQuery {
  /// leaf 이름(형제 순번을 뗀 이름)과 비교한다. `Card(Clone)[3]`의 leaf 이름은 `Card(Clone)`.
  name?: string;
  /// `displayedTextOf`가 낸 문자열과 비교한다. 그런 문자열이 없는 객체는 이 필터가 있으면 곧장 탈락한다.
  displayedText?: string;
  /// `object.by[].on` 중 하나와 비교한다 — 예: `"Button"`은 `"UnityEngine.UI.Button"`을 담은
  /// 객체를 contains 로 찾아낸다.
  component?: string;
  /// true면 `actionsOf`가 하나 이상인 것만, false면 하나도 없는 것만 남긴다. 생략하면 걸지 않는다.
  actionable?: boolean;
  /// 세 문자열 필터 모두에 적용된다. 기본 false = contains.
  exact?: boolean;
  /// 세 문자열 필터 모두에 적용된다. 기본 false = 대소문자 무시.
  caseSensitive?: boolean;
  /// `state.deactive`도 검색 대상에 넣을지. 기본 false — `get_scene_state`의 같은 이름, 같은 기본값.
  includeInactive?: boolean;
  /// 반환할 후보 수 상한. 기본 `DEFAULT_LIMIT`.
  limit?: number;
}

/// 후보 하나. `get_scene_state`가 싣는 `changed`/`statics`/`by`를 빼고, 대상을 골라내고
/// 뒤이어 상세 조회로 넘어가는 데 필요한 것만 남긴다.
export interface SearchCandidate {
  id: number;
  selector: string;
  /// `object.scene ?? state.scene` — `objectKey`와 같은 규칙이다.
  scene: string;
  displayedText?: string;
  /// 이 객체에 실제로 걸린 조작. `actionsOf` 참고. 비어 있으면 조작에 답하지 않는 객체다.
  actions: string[];
  active: boolean;
}

export interface SearchResult {
  candidates: SearchCandidate[];
  /// `limit` 적용 전, 조건에 맞은 전체 개수.
  total: number;
  /// `total`이 반환한 `candidates.length`보다 크다는 것 — 더 있다는 신호다.
  truncated: boolean;
  /// 반환 여부와 무관하게, 조건에 맞은 전체 후보 중 leaf 이름이 둘 이상 겹치는 이름들.
  ///
  /// `limit`으로 잘려 지금은 안 보이는 후보의 이름도 여기 잡힌다 — "지금 보이는 목록 안에서
  /// 겹친다"가 아니라 "이 이름을 가진 서로 다른 인스턴스가 하나 이상 더 있으니 `id`나 전체
  /// `selector`로 구분하라"는 신호이기 때문이다.
  duplicateNames: string[];
}

/// 한 번 검색에서 반환할 후보 수 상한의 기본값.
///
/// `get_scene_state`의 `changed`/`statics`를 통째로 내는 문제를 이 tool 이 없애려는 것이므로,
/// 기본값도 한 화면에 훑어볼 수 있는 크기로 잡는다. 필요하면 `limit`으로 올릴 수 있다.
const DEFAULT_LIMIT = 20;

function normalize(value: string, caseSensitive: boolean): string {
  return caseSensitive ? value : value.toLowerCase();
}

function textMatches(candidate: string, query: string, exact: boolean, caseSensitive: boolean): boolean {
  const left = normalize(candidate, caseSensitive);
  const right = normalize(query, caseSensitive);
  return exact ? left === right : left.includes(right);
}

/// 이 객체에 실제로 걸린 조작을 `offers`에서 그대로 옮긴다.
///
/// 게임 코드가 낸 이름(키 이름, 엔진 메시지 이름)을 그대로 노출할 뿐 새로 지어내지 않는다.
/// `WatchListJson.cs`가 이미 정렬하고 중복을 없애 보내므로 여기서 다시 정렬하거나 걸러내지
/// 않는다.
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

/// 이 객체가 `query`의 조건 전부를 만족하는지. 하나라도 틀리면 그 자리에서 멈춘다.
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

/// 이름/표시 텍스트/컴포넌트/조작 가능 여부로 대상을 좁혀 간결한 후보를 낸다.
///
/// `get_scene_state`의 `selector`(전체 selector 문자열에 건 대소문자 구분 부분 문자열 일치)와는
/// 별개의 계약이다 — 그 동작은 이 함수가 건드리지 않는다.
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
