import assert from "node:assert/strict";
import test from "node:test";

import type { PulseObject } from "../src/pulse.js";
import { describeRoot, foldIntoTree, missingExpandPaths, UNLIMITED_DEPTH, type TreeNode } from "../src/tree.js";

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
  // `selector` segment 경계는 `]/` 다. 이름이 `A/B` 인 객체는 segment `"A/B[0]"` 하나이므로
  // `Canvas` 바로 아래 node 하나가 된다.
  const tree = foldIntoTree([object("Canvas[0]/A/B[0]", "Canvas/A/B")], noHistory);
  const canvas = find(tree, "Canvas");
  assert.equal(canvas?.children?.length, 1);
  assert.deepEqual(pathsOf(canvas?.children ?? []), ["Canvas/A/B"]);
  assert.equal(canvas?.children?.[0]?.segment, "A/B");
});

test("siblings sharing a path each get their own node", () => {
  // 적 다섯이 같은 `TurnBattleScene/RangedCat(Clone)` path 를 쓴다. sibling index 로 구별되므로
  // node 가 다섯이어야 한다.
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
  // 같은 이름의 `Card` 형제는 `root: "Canvas/Card"` 로 구별할 수 없고 `descend()` 는 먼저 나온 쪽
  // (sibling index 0)을 고른다. `build()` 의 삽입 순서가 바뀌어도 이 동작이 유지되도록 고정한다.
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

test("members: false leaves only the summary on every level", () => {
  const withObject = { ...object("Canvas[0]/Card[0]", "Canvas/Card"), members: [{ member: "text", value: "x" }] };
  const objects = [withObject as PulseObject, object("Canvas[0]/Panel[0]/Row[0]", "Canvas/Panel/Row")];
  for (const depth of [1, 2, 3]) {
    const tree = foldIntoTree(objects, noHistory, undefined, depth, false);
    const seen: TreeNode[] = [];
    const walk = (nodes: readonly TreeNode[]) => nodes.forEach((node) => { seen.push(node); walk(node.children ?? []); });
    walk(tree);
    assert.equal(seen.some((node) => node.object !== undefined), false, `depth ${depth}`);
  }
  const card = find(foldIntoTree(objects, noHistory, undefined, 2, false), "Canvas/Card");
  assert.equal(card?.hasObject, true);
});

test("describeRoot tells a missing root from an empty one and lists the top level", () => {
  const objects = [object("UI[0]/Card[0]", "UI/Card"), object("UI[0]/Bar[1]", "UI/Bar"), object("Main Camera[1]", "Main Camera")];
  assert.deepEqual(describeRoot(objects, "Canvas"), { found: false, topLevel: ["UI", "Main Camera"] });
  assert.equal(describeRoot(objects, "Main Camera").found, true);
  assert.equal(describeRoot(objects, "UI/Card").found, true);
});

const SCENE = [
  object("UI[0]/LowerBar[0]/Gold[0]", "UI/LowerBar/Gold"),
  object("UI[0]/LowerBar[0]/Gem[1]", "UI/LowerBar/Gem"),
  object("UI[0]/TopBar[1]/Timer[0]", "UI/TopBar/Timer"),
  object("Field[1]/Unit[0]", "Field/Unit"),
];

test("expand opens the chosen node and its ancestors and leaves the rest collapsed", () => {
  const tree = foldIntoTree(SCENE, noHistory, undefined, 1, false, ["UI/LowerBar"]);

  assert.equal(find(tree, "UI")?.collapsed, undefined);
  const lowerBar = find(tree, "UI/LowerBar");
  assert.deepEqual(pathsOf(lowerBar?.children ?? []), ["UI/LowerBar/Gold", "UI/LowerBar/Gem"]);
  assert.equal(find(tree, "UI/TopBar")?.collapsed, true);
  assert.equal(find(tree, "UI/TopBar")?.objects, 1);
  assert.equal(find(tree, "Field")?.collapsed, true);
});

test("only an expanded node carries member values", () => {
  const tree = foldIntoTree(SCENE, noHistory, undefined, 1, false, ["UI/LowerBar/Gold"]);

  assert.notEqual(find(tree, "UI/LowerBar/Gold")?.object, undefined);
  assert.equal(find(tree, "UI/LowerBar/Gold")?.hasObject, undefined);
  assert.equal(find(tree, "UI/LowerBar/Gem")?.object, undefined);
  assert.equal(find(tree, "UI/LowerBar/Gem")?.hasObject, true);
});

test("expand works alongside a deeper depth", () => {
  const tree = foldIntoTree(SCENE, noHistory, undefined, 2, false, ["Field"]);

  assert.equal(find(tree, "UI/LowerBar")?.collapsed, true);
  assert.deepEqual(pathsOf(find(tree, "Field")?.children ?? []), ["Field/Unit"]);
});

test("expand paths that match nothing are reported", () => {
  assert.deepEqual(missingExpandPaths(SCENE, ["UI/LowerBar", "UI/Nowhere", "Nope"]), ["UI/Nowhere", "Nope"]);
  const tree = foldIntoTree(SCENE, noHistory, undefined, 1, false, ["UI/Nowhere"]);
  assert.equal(find(tree, "UI")?.collapsed, true);
});

test("without expand the tree is unchanged", () => {
  const tree = foldIntoTree(SCENE, noHistory, undefined, UNLIMITED_DEPTH);
  assert.equal(find(tree, "UI/LowerBar/Gold")?.collapsed, undefined);
});
