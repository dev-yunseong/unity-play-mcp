import type { PulseObject } from "./pulse.js";

/// tree 의 node 하나. 접히면 `collapsed` 가 붙고 자식 대신 요약만 남는다.
export interface TreeNode {
  segment: string;
  path: string;
  /// 이 node 의 selector(형제 index 포함). `get_scene_state` 의 `expand` 에 그대로 쓸 수 있다.
  selector: string;
  object?: PulseObject;
  /// 멤버 값을 싣지 않은 요약 모양(`members: false`)에서, 이 node 에 객체가 있다는 표시.
  hasObject?: true;
  children?: TreeNode[];
  collapsed?: true;
  /// 이 node 를 root 로 하는 subtree 의 객체 수. 자기 자신도 포함한다.
  objects: number;
  /// subtree 의 멤버 값이 마지막으로 바뀐 `reading`.
  ///
  /// "최근" 의 기준이 호출하는 쪽마다 다르므로 boolean 이 아니라 번호를 준다. 이력이 없는
  /// subtree 에서는 생략한다. 없는 것과 0 은 다르다.
  lastChangedReading?: number;
}

/// 객체 하나의 멤버가 마지막으로 바뀐 `reading`. 이력이 없으면 `undefined`.
///
/// 이력 map 의 키는 `scene/selector` 이므로 그 대응은 tree 가 아니라 호출하는 쪽이 맡는다.
export type LatestReading = (object: PulseObject) => number | undefined;

interface Building {
  segment: string;
  path: string;
  selector: string;
  object?: PulseObject;
  children: Map<string, Building>;
}

interface Summary {
  objects: number;
  latest?: number;
}

function emptyNode(segment: string, path: string, selector = ""): Building {
  return { segment, path, selector, children: new Map() };
}

/// 잘린 계층을 나타내는 접두사. `ScenePath.cs` 가 `path` 와 `selector` 에 똑같이 붙인다
/// (깊이 제한을 넘었거나 망가진 prefab 이 순환을 만든 경우).
const TRUNCATED_PREFIX = ".../";

/// `selector` 를 segment 배열로 쪼갠다. 각 segment 는 `name[siblingIndex]` 이고 `]/` 가
/// 경계이므로 이름 안의 `/` 에서는 쪼개지 않는다 (`ScenePath.cs`). `.../` 접두사는
/// `path.split("/")` 와 개수를 맞추도록 `"..."` 를 첫 segment 로 넣는다.
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

/// segment 에서 표시 이름만 뽑는다. `"..."` 는 그대로 두고, 그 외에는 끝의 `[siblingIndex]` 를
/// 지운다.
///
/// 이름은 `path.split("/")` 가 아니라 `selector` segment 에서 뽑아야 한다. 이름에 `/` 가 있으면
/// (`A/B`) 두 배열의 길이가 달라져 표시 이름이 잘린다.
function nameOf(segment: string): string {
  if (segment === "...") {
    return segment;
  }
  const bracket = segment.lastIndexOf("[");
  return bracket === -1 ? segment : segment.slice(0, bracket);
}

/// 객체들의 `selector` 로 trie 를 만든다. `path` 는 node 의 표시 이름으로만 쓴다.
///
/// PULSE 객체는 씬 hierarchy 의 일부만 담으므로(`Worth` 가 걸러낸다) 객체가 없는 중간 node 도
/// 만들어야 구조가 이어진다.
///
/// segment(예: `RangedCat(Clone)[3]`)를 `Map` 키로 쓴다. sibling index 로 유일하므로 `path` 가
/// 같은 형제도 각각 node 가 된다.
function build(objects: readonly PulseObject[]): Building {
  const root = emptyNode("", "");
  for (const object of objects) {
    const selector = typeof object.selector === "string" ? object.selector : "";
    const segments = selectorSegments(selector);
    let node = root;
    let walked = "";
    let walkedSelector = "";
    for (const segment of segments) {
      const name = nameOf(segment);
      walked = walked === "" ? name : `${walked}/${name}`;
      walkedSelector = walkedSelector === "" ? segment : `${walkedSelector}/${segment}`;
      let next = node.children.get(segment);
      if (next === undefined) {
        next = emptyNode(name, walked, walkedSelector);
        node.children.set(segment, next);
      }
      node = next;
    }
    if (node !== root) {
      node.object = object;
      // 객체가 있는 node 는 재구성한 path 대신 게임이 준 path 를 쓴다. 다른 객체를 따라가며 중간
      // node 로 먼저 만들어졌을 수 있으므로 node 생성 때가 아니라 객체를 붙일 때마다 대입한다.
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

/// `expand` 로 지정한 node 들. `targets` 는 지정된 node(멤버 값을 싣는다), `open` 은 그 node 와 조상
/// (자식을 싣는다).
interface Expansion {
  targets: Set<Building>;
  open: Set<Building>;
}

const NO_EXPANSION: Expansion = { targets: new Set(), open: new Set() };

/// `remaining` 단계까지 펼치고 더 깊은 node 는 접는다. `expansion` 에 든 node 는 예외다.
///
/// `members` 가 false 이면 모든 단계에서 `object`(멤버 값) 대신 `hasObject` 만 싣는다. 단계마다
/// 모양이 달라지지 않게 하려는 것이다.
function render(
  node: Building,
  remaining: number,
  latestOf: LatestReading,
  members: boolean,
  expansion: Expansion,
): TreeNode {
  const { objects, latest } = summarize(node, latestOf);
  const showMembers = members || expansion.targets.has(node);
  const rendered: TreeNode = {
    segment: node.segment,
    path: node.path,
    selector: node.selector,
    objects,
    ...(node.object === undefined ? {} : showMembers ? { object: node.object } : { hasObject: true as const }),
    ...(latest === undefined ? {} : { lastChangedReading: latest }),
  };

  if (node.children.size === 0) {
    return rendered;
  }
  // `expand` 로 연 node 와 그 조상은 깊이와 상관없이 자식을 보여 준다.
  if (remaining <= 0 && !expansion.open.has(node)) {
    rendered.collapsed = true;
    return rendered;
  }
  rendered.children = [...node.children.values()]
    .map((child) => render(child, remaining - 1, latestOf, members, expansion));
  return rendered;
}

/// `root` 접두사가 가리키는 node 를 찾는다. 없으면 `undefined`.
///
/// `root` 는 sibling index 없는 `path` 이고 `Map` 키는 selector segment 이므로,
/// `child.segment` 가 같은 첫 자식을 찾는다. 같은 이름의 형제가 여럿이면 먼저 담긴 쪽을 고른다.
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

/// `selector` 가 가리키는 node 와 그 조상(root 제외)을 순서대로. 없으면 `undefined`.
///
/// `Map` 키가 형제 index 를 포함한 segment 이므로 같은 이름의 형제도 구분된다.
function chainTo(root: Building, selector: string): Building[] | undefined {
  const chain: Building[] = [];
  let node = root;
  for (const segment of selectorSegments(selector)) {
    const next = node.children.get(segment);
    if (next === undefined) {
      return undefined;
    }
    chain.push(next);
    node = next;
  }
  return chain;
}

/// selector 의 마지막 segment 에서 sibling index 를 뺀 표시 이름.
///
/// `search.ts` 가 segment 분리 규칙을 따로 두지 않도록 여기서 내보낸다.
export function leafNameOf(selector: string): string {
  const segments = selectorSegments(selector);
  const last = segments[segments.length - 1];
  return last === undefined ? selector : nameOf(last);
}

export const UNLIMITED_DEPTH = Number.MAX_SAFE_INTEGER;

/// 객체들로 hierarchy 를 만들어 `root` 아래를 `depth` 단계까지 돌려준다.
///
/// `root` 에 맞는 node 가 없으면 빈 배열을 돌려준다. 없는 root 와 자식 없는 root 를 가르려면
/// `describeRoot` 를 쓴다.
///
/// `members` 를 false 로 주면 node 마다 이름, 경로, 하위 객체 수, `lastChangedReading` 만 싣는다.
export function foldIntoTree(
  objects: readonly PulseObject[],
  latestOf: LatestReading,
  root?: string,
  depth: number = UNLIMITED_DEPTH,
  members: boolean = true,
  expand: readonly string[] = [],
): TreeNode[] {
  const built = build(objects);
  const start = root === undefined ? built : descend(built, root);
  if (start === undefined) {
    return [];
  }
  const expansion: Expansion = { targets: new Set(), open: new Set() };
  for (const path of expand) {
    const chain = chainTo(built, path);
    if (chain === undefined || chain.length === 0) {
      continue;
    }
    expansion.targets.add(chain[chain.length - 1] as Building);
    for (const node of chain) {
      expansion.open.add(node);
    }
  }
  return [...start.children.values()].map((child) => render(child, depth - 1, latestOf, members, expansion));
}

/// `expand` 에서 씬에 없는 selector. 조용히 무시하면 접힌 것과 없는 것이 같아 보인다.
export function missingExpandPaths(objects: readonly PulseObject[], expand: readonly string[]): string[] {
  const built = build(objects);
  return expand.filter((path) => (chainTo(built, path)?.length ?? 0) === 0);
}

/// `root` 가 씬에 있는지와, 씬의 최상위 객체 이름들.
///
/// `foldIntoTree` 의 빈 배열만으로는 "root 는 맞는데 아래가 비었다" 와 "root 이름이 틀렸다" 를
/// 가를 수 없어서 따로 묻는다. 이름은 sibling index 를 뗀 표시 이름이고 중복은 한 번만 적는다.
export function describeRoot(
  objects: readonly PulseObject[],
  root: string,
): { found: boolean; topLevel: string[] } {
  const built = build(objects);
  const topLevel = [...new Set([...built.children.values()].map((child) => child.segment))];
  return { found: descend(built, root) !== undefined, topLevel };
}
