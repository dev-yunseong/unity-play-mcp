import type { PulseObject } from "./pulse.js";

/// 한 마디가 낸 모습. 접혔으면 `collapsed` 가 서고 자식 대신 요약만 남는다.
export interface TreeNode {
  segment: string;
  path: string;
  object?: PulseObject;
  children?: TreeNode[];
  collapsed?: true;
  /// 이 마디를 뿌리로 하는 subtree 안의 객체 수. 자기 자신도 센다.
  objects: number;
  /// subtree 안 어느 멤버든 값이 마지막으로 움직인 `reading`.
  ///
  /// boolean 이 아니라 번호를 내는 이유는 "최근" 의 기준이 부르는 쪽마다 다르기 때문이다.
  /// 번호를 주면 자기가 마지막으로 본 것과 견줄 수 있다. 이력이 하나도 없는 subtree 에서는
  /// 아예 나오지 않는다 — 없는 것과 0 은 다른 말이다.
  lastChangedReading?: number;
}

/// 객체 하나의 멤버들이 마지막으로 움직인 `reading`. 이력이 없으면 `undefined`.
///
/// tree 는 이력이 어떻게 저장되는지 몰라도 된다. 이력 map 의 키는 `path` 가 아니라
/// `scene/selector` 라서, 그 대응은 이것을 건네는 쪽이 안다.
export type LatestReading = (object: PulseObject) => number | undefined;

interface Building {
  segment: string;
  path: string;
  object?: PulseObject;
  children: Map<string, Building>;
}

interface Summary {
  objects: number;
  latest?: number;
}

function emptyNode(segment: string, path: string): Building {
  return { segment, path, children: new Map() };
}

/// 잘린 계층을 나타내는 접두사. `ScenePath.cs` 가 `path` 에도 `selector` 에도 똑같이 붙인다
/// (경계보다 깊은 계층이거나, 망가진 프리팹이 순환을 만든 경우).
const TRUNCATED_PREFIX = ".../";

/// `selector` 를 마디 문자열 배열로 쪼갠다. 각 마디는 `name[siblingIndex]` 이고 `]/` 가
/// 경계다 (`ScenePath.cs:57`, `:80`) — 이름 안의 `/` 는 `[`, `]` 를 포함하지 않으므로 `]/`
/// 를 경계로 쪼개면 걸리지 않는다. `.../` 로 시작하면 그 접두사를 떼어 `"..."` 를 첫
/// 마디로 넣는다 — `path` 를 `/` 로 쪼갤 때 `"..."` 가 저절로 첫 조각이 되는 것과 마디
/// 수를 맞추기 위해서다 (`".../A/B".split("/")` 도 `["...", "A", "B"]` 로 셋이다).
function selectorSegments(selector: string): string[] {
  const truncated = selector.startsWith(TRUNCATED_PREFIX);
  const rest = truncated ? selector.slice(TRUNCATED_PREFIX.length) : selector;
  if (rest.length === 0) {
    return truncated ? ["..."] : [];
  }
  const pieces = rest.split("]/");
  const segments = pieces.map((piece, index) => (index < pieces.length - 1 ? `${piece}]` : piece));
  return truncated ? ["...", ...segments] : segments;
}

/// 마디 문자열에서 사람이 읽는 이름만 뽑는다. `"..."` 는 잘린 계층 표시라 그대로 두고, 그
/// 외에는 끝의 `[siblingIndex]` 를 지운다.
///
/// 이 이름은 반드시 `selector` 마디 자체에서 뽑는다 — 같은 순번의 `path.split("/")` 원소를
/// 대신 쓰면 안 된다. 이름 안에 `/` 가 있으면 두 배열의 길이가 서로 달라지기 때문이다:
/// 이름이 `A/B` 인 객체의 `selector` 마디는 `"A/B[0]"` 하나인데 `path` 를 `/` 로 쪼개면
/// `"Canvas/A/B"` 가 `["Canvas", "A", "B"]` 세 조각이 된다. 인덱스로 짝지으면 `A/B` 자리에
/// `"A"` 만 앉아 표시 이름이 조용히 잘린다.
function nameOf(segment: string): string {
  if (segment === "...") {
    return segment;
  }
  const bracket = segment.lastIndexOf("[");
  return bracket === -1 ? segment : segment.slice(0, bracket);
}

/// 객체들의 `selector` 로 trie 를 세운다. `path` 는 마디의 표시 이름으로만 쓴다.
///
/// PULSE 의 객체는 씬 hierarchy 의 성긴 부분집합이다. `Worth` 가 대부분을 걸러내므로
/// `Canvas/Panel/Row/Button` 은 있는데 `Canvas/Panel` 에는 아무 객체도 없을 수 있다. 그런
/// 중간 마디도 세워야 구조가 이어진다.
///
/// `selector` 마디 문자열(예: `RangedCat(Clone)[3]`)을 그대로 `Map` 의 키로 쓴다 — 형제마다
/// sibling index 가 달라 유일하다. 그래서 같은 `path` 를 쓰는 형제 다섯은 다섯 마디가 된다.
function build(objects: readonly PulseObject[]): Building {
  const root = emptyNode("", "");
  for (const object of objects) {
    const selector = typeof object.selector === "string" ? object.selector : "";
    const segments = selectorSegments(selector);
    let node = root;
    let walked = "";
    for (const segment of segments) {
      const name = nameOf(segment);
      walked = walked === "" ? name : `${walked}/${name}`;
      let next = node.children.get(segment);
      if (next === undefined) {
        next = emptyNode(name, walked);
        node.children.set(segment, next);
      }
      node = next;
    }
    if (node !== root) {
      node.object = object;
      // 객체가 실제로 앉는 마디는 재구성한 walked path 대신 서버가 준 path 를 그대로 쓴다 —
      // 사람이 읽는 필드는 언제나 근거가 있는 값이어야 한다. 이 마디가 다른 객체를 걷다가
      // 먼저 중간 마디로 만들어졌을 수도 있으므로, 대입은 노드 생성 시점이 아니라 객체를
      // 앉힐 때마다 매번 한다.
      if (typeof object.path === "string") {
        node.path = object.path;
      }
    }
  }
  return root;
}

function summarize(node: Building, latestOf: LatestReading): Summary {
  let objects = 0;
  let latest: number | undefined;

  if (node.object !== undefined) {
    objects += 1;
    latest = latestOf(node.object);
  }

  for (const child of node.children.values()) {
    const below = summarize(child, latestOf);
    objects += below.objects;
    if (below.latest !== undefined && (latest === undefined || below.latest > latest)) {
      latest = below.latest;
    }
  }

  return latest === undefined ? { objects } : { objects, latest };
}

/// `remaining` 층까지 펼치고 그보다 깊은 마디는 접는다.
function render(node: Building, remaining: number, latestOf: LatestReading): TreeNode {
  const { objects, latest } = summarize(node, latestOf);
  const rendered: TreeNode = {
    segment: node.segment,
    path: node.path,
    objects,
    ...(node.object === undefined ? {} : { object: node.object }),
    ...(latest === undefined ? {} : { lastChangedReading: latest }),
  };

  if (node.children.size === 0) {
    return rendered;
  }
  if (remaining <= 0) {
    rendered.collapsed = true;
    return rendered;
  }
  rendered.children = [...node.children.values()]
    .map((child) => render(child, remaining - 1, latestOf));
  return rendered;
}

/// `root` 접두사가 가리키는 마디를 찾는다. 없으면 `undefined`.
///
/// `root` 는 사람이 읽는 `path` 그대로다 — sibling index 를 사람이 쓰게 하지 않는다. `Map`
/// 의 키는 이제 selector 마디라 사람이 읽는 이름과 다르므로, `children` 의 값들 중
/// `child.segment` 가 같은 첫 번째를 찾는다. 같은 이름의 형제가 여럿이면 `objects` 배열에서
/// 먼저 나온 쪽(= `Map` 이 먼저 담은 쪽)을 고른다 — `path` 만으로는 원래 구분할 수 없는
/// 자리라 이전에도 모호했고, 이 변경이 새로 만든 모호함이 아니다.
function descend(root: Building, path: string): Building | undefined {
  let node = root;
  for (const segment of path.split("/").filter((one) => one.length > 0)) {
    let next: Building | undefined;
    for (const child of node.children.values()) {
      if (child.segment === segment) {
        next = child;
        break;
      }
    }
    if (next === undefined) {
      return undefined;
    }
    node = next;
  }
  return node;
}

export const UNLIMITED_DEPTH = Number.MAX_SAFE_INTEGER;

/// 객체들을 hierarchy 로 세워 `root` 아래를 `depth` 층까지 낸다.
///
/// `root` 가 아무 마디도 가리키지 않으면 빈 배열이 온다 — 잘못 짚었다는 것이 그 자체로 답이다.
export function foldIntoTree(
  objects: readonly PulseObject[],
  latestOf: LatestReading,
  root?: string,
  depth: number = UNLIMITED_DEPTH,
): TreeNode[] {
  const built = build(objects);
  const start = root === undefined ? built : descend(built, root);
  if (start === undefined) {
    return [];
  }
  return [...start.children.values()].map((child) => render(child, depth - 1, latestOf));
}
