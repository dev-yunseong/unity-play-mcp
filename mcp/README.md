# Unity Play MCP server

This stdio MCP server connects coding agents to a running Unity game using the
Unity Play MCP package.

Build it with `npm install && npm run build`, then configure the MCP client to
run `node mcp/dist/index.js` from the repository root. The game listens at
`ws://127.0.0.1:17311/ws` by default.

`UNITY_PLAY_MCP_URL` overrides the WebSocket URL. `UNITY_PLAY_MCP_TIMEOUT_MS`
sets the action timeout in milliseconds and defaults to `15000`.

Call `start_readings` when a play session begins, then use `get_scene_state` to
inspect the folded scene. It answers from what the server has already collected;
it does not ask the game for a fresh scan. The game pushes readings ten times a
second and delivers them once a second, so the store fills whether or not anyone
is asking.

`get_scene_state` also reports objects the game destroyed, under `gone`, with the
reading each one disappeared at. Set `includeHistory` to see how every member's
value moved over its last ten changes — the response carries the readings and the
frames they were taken on, so a value that went up and back down inside one second
is still visible.

Pass `root` or `depth` to read the scene as a hierarchy instead of a flat list.
`root` is a path prefix and `depth` counts levels down from it. A node deeper
than `depth` comes back collapsed, carrying how many objects sit beneath it and
the reading its subtree last moved on — compare that number against the one you
saw last to decide whether opening it is worth it. The tree is built by splitting
each object's `path` on `/`, so a GameObject whose own name contains a slash
gains an extra level. Tree mode leaves out `changed`; every node's
`lastChangedReading` answers the same question in the shape of the tree.

`capture_screen` returns the image directly as an MCP image content block. The remaining tools send input, time, reset, and reading
actions to Unity. `perform_actions` sends a sequence in one game-side batch.
