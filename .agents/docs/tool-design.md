# MCP Tool Design

## Why

An agent reads every tool description in its context. Each extra tool, and each
tool that names the same idea differently, costs context and invites the wrong
call. These rules keep the tool surface small and predictable.

## Rules

1. **One tool per intent.** Name the intent (`click`, `hover`, `drag`,
   `enter_text`), not the mechanism. Do not add `<mechanism>_<verb>` variants
   (`pointer_click`, `button_click`) for a different way of doing the same thing;
   pick the one path the game itself uses and make it the tool.
2. **One way to name a target.** Any tool that aims at something takes the
   `TargetRef` fields, defined once in `mcp/src/target-ref.ts` and shaped by
   `targetRefShape()` / `objectRefShape()` in `mcp/src/tools.ts`:
   - `targetId` — instance id from `get_scene_state`
   - `selector` — the exact selector from `get_scene_state` / `search_targets`
   - `x`, `y` — game screen pixels from the top left (the `move_mouse` space)

   Exactly one way per target; `targetRefIssue` enforces it. Never invent a
   tool-specific field (`sourceId`, `elementId`, ...).
3. **Consumers decide what they accept.**
   - Point consumers (`click`, `hover`, `drag`) accept all three and resolve to a
     screen point with `resolvePoint`.
   - Object consumers (`enter_text`, `capture_screen`) need a game object, so they
     accept `targetId` or `selector` and refuse `x`/`y` with a clear message
     (`resolveObjectId`).
4. **Several targets nest.** A tool with one target takes the fields flat. A tool
   with several names each role and nests a full `TargetRef` in it
   (`drag { from, to }`).
5. **Resolve in the MCP server, keep Unity primitive.** ids, selectors, and rects
   are resolved against the folded reading in `target-ref.ts`; Unity receives
   resolved ids or points. Compose behaviours from the primitive actions
   (`move_mouse`, `mouse_down`, `mouse_up`, `enter_text`, ...) instead of adding a
   Unity action per tool. A new Unity action needs a reason the primitives cannot
   express.
6. **Resolve everything before sending anything.** If any target fails to resolve
   (stale reading, not found, ambiguous, inactive, off screen, covered) nothing is
   sent and the error names the tool and the fix.
7. **Tool and `perform_actions` share one contract.** A tool builds the same
   action `perform_actions` accepts and runs it through `expandActions`; do not
   implement the behaviour twice.
8. **Raw input tools stay raw.** `move_mouse`, `mouse_button`, `press_key`,
   `set_axis`, `set_button` take no `TargetRef`. They are the primitives the
   rules above compose.
9. **No compatibility aliases** unless the issue asks for them; remove the old
   name and update docs and tests in the same change.

## Checklist for a new or changed tool

- [ ] Named by intent, and no existing tool already covers it.
- [ ] Targets use `TargetRef`; the accepted kinds are stated in the description.
- [ ] Built from primitive actions; no new Unity action unless justified.
- [ ] Added to `performActionSchema` and expanded by `expandActions`.
- [ ] Description says what a success does and does not prove.
- [ ] `schema.test.ts` tool count and `instructions.ts` updated.
