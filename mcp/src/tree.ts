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

/// 객체들의 `path` 를 `/` 로 쪼개 trie 를 세운다.
///
/// PULSE 의 객체는 씬 hierarchy 의 성긴 부분집합이다. `Worth` 가 대부분을 걸러내므로
/// `Canvas/Panel/Row/Button` 은 있는데 `Canvas/Panel` 에는 아무 객체도 없을 수 있다. 그런
/// 중간 마디도 세워야 구조가 이어진다.
function build(objects: readonly PulseObject[]): Building {
  const root = emptyNode("", "");
  for (const object of objects) {
    const path = typeof object.path === "string" ? object.path : "";
    const segments = path.split("/").filter((segment) => segment.length > 0);
    let node = root;
    let walked = "";
    for (const segment of segments) {
      walked = walked === "" ? segment : `${walked}/${segment}`;
      let next = node.children.get(segment);
      if (next === undefined) {
        next = emptyNode(segment, walked);
        node.children.set(segment, next);
      }
      node = next;
    }
    // 같은 경로에 객체가 둘 이상이면 — 만들어진 적 다섯이 한 경로를 나눠 쓰는 경우 — 마지막
    // 것이 마디에 앉는다. 그것들을 가르는 것은 `selector` 의 sibling index 이고, tree 는
    // 사람이 읽는 `path` 로 선다. 수는 `objects` 가 그대로 센다.
    if (node !== root) {
      node.object = object;
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
function descend(root: Building, path: string): Building | undefined {
  let node = root;
  for (const segment of path.split("/").filter((one) => one.length > 0)) {
    const next = node.children.get(segment);
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
