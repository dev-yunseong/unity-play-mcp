import assert from "node:assert/strict";
import test from "node:test";

import type { PulseObject } from "../src/pulse.js";
import { foldIntoTree, type TreeNode } from "../src/tree.js";

function object(selector: string, path: string): PulseObject {
  return { id: 1, path, selector, by: [] };
}

const noHistory = () => undefined;

const pathsOf = (nodes: readonly TreeNode[]): string[] => nodes.map(({ path }) => path);

function find(nodes: readonly TreeNode[], path: string): TreeNode | undefined {
  for (const node of nodes) {
    if (node.path === path) return node;
    const below = node.children === undefined ? undefined : find(node.children, path);
    if (below !== undefined) return below;
  }
  return undefined;
}

test("flat objects become a hierarchy", () => {
  const tree = foldIntoTree([
    object("Canvas[0]/Card[0]", "Canvas/Card"),
    object("Canvas[0]/Panel[1]", "Canvas/Panel"),
  ], noHistory);
  assert.deepEqual(pathsOf(tree), ["Canvas"]);
  assert.deepEqual(pathsOf(tree[0]?.children ?? []), ["Canvas/Card", "Canvas/Panel"]);
});

test("an intermediate segment with no object of its own still stands", () => {
  const tree = foldIntoTree([object("Canvas[0]/Panel[0]/Row[0]/Button[0]", "Canvas/Panel/Row/Button")], noHistory);
  const row = find(tree, "Canvas/Panel/Row");
  assert.notEqual(row, undefined);
  assert.equal(row?.object, undefined);
  assert.equal(row?.objects, 1);
});

test("root selects a subtree", () => {
  const tree = foldIntoTree([
    object("Canvas[0]/HUD[0]/Card[0]", "Canvas/HUD/Card"),
    object("Canvas[0]/Pause[1]/Menu[0]", "Canvas/Pause/Menu"),
  ], noHistory, "Canvas/HUD");
  assert.deepEqual(pathsOf(tree), ["Canvas/HUD/Card"]);
});

test("a root that names nothing answers with an empty tree", () => {
  const tree = foldIntoTree([object("Canvas[0]/Card[0]", "Canvas/Card")], noHistory, "Nowhere");
  assert.deepEqual(tree, []);
});

test("nodes past the depth are collapsed", () => {
  const tree = foldIntoTree(
    [object("Canvas[0]/Panel[0]/Row[0]/Button[0]", "Canvas/Panel/Row/Button")],
    noHistory,
    undefined,
    2,
  );
  const panel = find(tree, "Canvas/Panel");
  assert.equal(panel?.collapsed, true);
  assert.equal(panel?.children, undefined);
});

test("a collapsed node reports how many objects sit beneath it", () => {
  const tree = foldIntoTree([
    object("Canvas[0]/Panel[0]/A[0]", "Canvas/Panel/A"),
    object("Canvas[0]/Panel[0]/B[1]", "Canvas/Panel/B"),
    object("Canvas[0]/Panel[0]/Row[2]/C[0]", "Canvas/Panel/Row/C"),
  ], noHistory, undefined, 1);
  const canvas = find(tree, "Canvas");
  assert.equal(canvas?.collapsed, true);
  assert.equal(canvas?.objects, 3);
});

test("a collapsed node reports the reading its subtree last moved on", () => {
  const readings = new Map([
    ["Canvas[0]/Panel[0]/A[0]", 7],
    ["Canvas[0]/Panel[0]/B[1]", 12],
    ["Canvas[0]/Panel[0]/Row[2]/C[0]", 3],
  ]);
  const tree = foldIntoTree([
    object("Canvas[0]/Panel[0]/A[0]", "Canvas/Panel/A"),
    object("Canvas[0]/Panel[0]/B[1]", "Canvas/Panel/B"),
    object("Canvas[0]/Panel[0]/Row[2]/C[0]", "Canvas/Panel/Row/C"),
  ], ({ selector }) => readings.get(String(selector)), undefined, 1);
  assert.equal(find(tree, "Canvas")?.lastChangedReading, 12);
});

test("a subtree with no history reports no lastChangedReading", () => {
  const tree = foldIntoTree([object("Canvas[0]/A[0]", "Canvas/A")], noHistory, undefined, 1);
  const canvas = find(tree, "Canvas");
  assert.equal("lastChangedReading" in (canvas ?? {}), false);
});

test("a leaf is not marked collapsed", () => {
  const tree = foldIntoTree([object("Canvas[0]/Card[0]", "Canvas/Card")], noHistory, undefined, 1);
  assert.equal(find(tree, "Canvas")?.collapsed, true);
  const deeper = foldIntoTree([object("Canvas[0]/Card[0]", "Canvas/Card")], noHistory, undefined, 2);
  assert.equal(find(deeper, "Canvas/Card")?.collapsed, undefined);
});

test("depth is counted from the root that was asked for", () => {
  const tree = foldIntoTree([
    object("Canvas[0]/Panel[0]/Row[0]/Button[0]", "Canvas/Panel/Row/Button"),
  ], noHistory, "Canvas", 1);
  assert.deepEqual(pathsOf(tree), ["Canvas/Panel"]);
  assert.equal(tree[0]?.collapsed, true);
});

test("a slash inside a GameObject name does not add a level", () => {
  // `selector` 의 마디 경계는 `]/` 다. 이름이 `A/B` 인 객체는 selector 마디가 `"A/B[0]"`
  // 하나라 `Canvas` 바로 아래 마디 하나로 선다 — `path` 를 그대로 `/` 로 쪼갤 때 생기던
  // 여분의 층이 없다.
  const tree = foldIntoTree([object("Canvas[0]/A/B[0]", "Canvas/A/B")], noHistory);
  const canvas = find(tree, "Canvas");
  assert.equal(canvas?.children?.length, 1);
  assert.deepEqual(pathsOf(canvas?.children ?? []), ["Canvas/A/B"]);
  assert.equal(canvas?.children?.[0]?.segment, "A/B");
});

test("siblings sharing a path each get their own node", () => {
  // 만들어진 적 다섯이 `TurnBattleScene/RangedCat(Clone)` 하나를 나눠 쓴다. sibling index
  // 가 다섯을 가르므로 다섯 마디가 선다 — 예전에는 마지막 하나만 남고 넷이 사라졌다.
  const objects = [0, 1, 2, 3, 4].map((index) =>
    object(`TurnBattleScene[0]/RangedCat(Clone)[${index}]`, "TurnBattleScene/RangedCat(Clone)"));
  const tree = foldIntoTree(objects, noHistory);
  const scene = find(tree, "TurnBattleScene");
  assert.equal(scene?.children?.length, 5);
  assert.equal(scene?.objects, 5);
  const withObjects = (scene?.children ?? []).filter((node) => node.object !== undefined);
  assert.equal(withObjects.length, 5);
  assert.deepEqual(pathsOf(scene?.children ?? []), Array(5).fill("TurnBattleScene/RangedCat(Clone)"));
});

test("a truncated hierarchy is grouped under its own node", () => {
  const tree = foldIntoTree([
    object(".../Ancestor[0]/Child[1]", ".../Ancestor/Child"),
  ], noHistory);
  assert.deepEqual(pathsOf(tree), ["..."]);
  const ancestor = find(tree, ".../Ancestor");
  assert.notEqual(ancestor, undefined);
  const child = find(tree, ".../Ancestor/Child");
  assert.notEqual(child?.object, undefined);
  assert.equal(child?.object?.path, ".../Ancestor/Child");
});

test("root can select inside a truncated hierarchy", () => {
  const tree = foldIntoTree([
    object(".../Ancestor[0]/Child[1]", ".../Ancestor/Child"),
  ], noHistory, ".../Ancestor");
  assert.deepEqual(pathsOf(tree), [".../Ancestor/Child"]);
});

test("root naming a display path shared by siblings selects the first-inserted one", () => {
  // 두 `Card` 형제가 같은 표시 이름을 쓰면 `root: "Canvas/Card"` 만으로는 사람이 둘을 못
  // 가른다 — `descend()` 는 `objects` 배열에서 먼저 나온 쪽(sibling index 0)을 고른다. 이
  // 동작 자체는 이전에도 있던 모호함이라 바뀌지 않지만, `build()` 의 삽입 순서가 나중에
  // 바뀌어도 이 자리가 조용히 흔들리지 않도록 고정해 둔다.
  const tree = foldIntoTree([
    object("Canvas[0]/Card[0]/First[0]", "Canvas/Card/First"),
    object("Canvas[0]/Card[1]/Second[0]", "Canvas/Card/Second"),
  ], noHistory, "Canvas/Card");
  assert.deepEqual(pathsOf(tree), ["Canvas/Card/First"]);
});

test("only the objects the tree actually shows are collected", () => {
  const tree = foldIntoTree([
    object("Canvas[0]/Shown[0]", "Canvas/Shown"),
    object("Canvas[0]/Deep[1]/Hidden[0]", "Canvas/Deep/Hidden"),
  ], noHistory, undefined, 2);
  const shown = find(tree, "Canvas/Shown");
  const deep = find(tree, "Canvas/Deep");
  assert.notEqual(shown?.object, undefined);
  assert.equal(deep?.collapsed, true);
  assert.equal(deep?.children, undefined);
});
