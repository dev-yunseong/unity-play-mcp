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
    object("Card", "Canvas/Card"),
    object("Panel", "Canvas/Panel"),
  ], noHistory);
  assert.deepEqual(pathsOf(tree), ["Canvas"]);
  assert.deepEqual(pathsOf(tree[0]?.children ?? []), ["Canvas/Card", "Canvas/Panel"]);
});

test("an intermediate segment with no object of its own still stands", () => {
  const tree = foldIntoTree([object("Button", "Canvas/Panel/Row/Button")], noHistory);
  const row = find(tree, "Canvas/Panel/Row");
  assert.notEqual(row, undefined);
  assert.equal(row?.object, undefined);
  assert.equal(row?.objects, 1);
});

test("root selects a subtree", () => {
  const tree = foldIntoTree([
    object("Card", "Canvas/HUD/Card"),
    object("Menu", "Canvas/Pause/Menu"),
  ], noHistory, "Canvas/HUD");
  assert.deepEqual(pathsOf(tree), ["Canvas/HUD/Card"]);
});

test("a root that names nothing answers with an empty tree", () => {
  const tree = foldIntoTree([object("Card", "Canvas/Card")], noHistory, "Nowhere");
  assert.deepEqual(tree, []);
});

test("nodes past the depth are collapsed", () => {
  const tree = foldIntoTree([object("Button", "Canvas/Panel/Row/Button")], noHistory, undefined, 2);
  const panel = find(tree, "Canvas/Panel");
  assert.equal(panel?.collapsed, true);
  assert.equal(panel?.children, undefined);
});

test("a collapsed node reports how many objects sit beneath it", () => {
  const tree = foldIntoTree([
    object("A", "Canvas/Panel/A"),
    object("B", "Canvas/Panel/B"),
    object("C", "Canvas/Panel/Row/C"),
  ], noHistory, undefined, 1);
  const canvas = find(tree, "Canvas");
  assert.equal(canvas?.collapsed, true);
  assert.equal(canvas?.objects, 3);
});

test("a collapsed node reports the reading its subtree last moved on", () => {
  const readings = new Map([["A", 7], ["B", 12], ["C", 3]]);
  const tree = foldIntoTree([
    object("A", "Canvas/Panel/A"),
    object("B", "Canvas/Panel/B"),
    object("C", "Canvas/Panel/Row/C"),
  ], ({ selector }) => readings.get(String(selector)), undefined, 1);
  assert.equal(find(tree, "Canvas")?.lastChangedReading, 12);
});

test("a subtree with no history reports no lastChangedReading", () => {
  const tree = foldIntoTree([object("A", "Canvas/A")], noHistory, undefined, 1);
  const canvas = find(tree, "Canvas");
  assert.equal("lastChangedReading" in (canvas ?? {}), false);
});

test("a leaf is not marked collapsed", () => {
  const tree = foldIntoTree([object("Card", "Canvas/Card")], noHistory, undefined, 1);
  assert.equal(find(tree, "Canvas")?.collapsed, true);
  const deeper = foldIntoTree([object("Card", "Canvas/Card")], noHistory, undefined, 2);
  assert.equal(find(deeper, "Canvas/Card")?.collapsed, undefined);
});

test("depth is counted from the root that was asked for", () => {
  const tree = foldIntoTree([
    object("Button", "Canvas/Panel/Row/Button"),
  ], noHistory, "Canvas", 1);
  assert.deepEqual(pathsOf(tree), ["Canvas/Panel"]);
  assert.equal(tree[0]?.collapsed, true);
});

test("a slash inside a GameObject name splits the path", () => {
  // 알려진 한계다. `path` 를 `/` 로 쪼개므로 이름 안의 `/` 가 마디를 하나 더 만든다.
  const tree = foldIntoTree([object("A/B", "Canvas/A/B")], noHistory);
  assert.deepEqual(pathsOf(find(tree, "Canvas")?.children ?? []), ["Canvas/A"]);
  assert.deepEqual(pathsOf(find(tree, "Canvas/A")?.children ?? []), ["Canvas/A/B"]);
});

test("only the objects the tree actually shows are collected", () => {
  const tree = foldIntoTree([
    object("Shown", "Canvas/Shown"),
    object("Hidden", "Canvas/Deep/Hidden"),
  ], noHistory, undefined, 2);
  const shown = find(tree, "Canvas/Shown");
  const deep = find(tree, "Canvas/Deep");
  assert.notEqual(shown?.object, undefined);
  assert.equal(deep?.collapsed, true);
  assert.equal(deep?.children, undefined);
});
