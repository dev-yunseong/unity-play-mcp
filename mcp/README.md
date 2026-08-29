# Unity Play MCP server

This stdio MCP server connects coding agents to a running Unity game using the
Unity Play MCP package.

Build it with `npm install && npm run build`, then configure the MCP client to
run `node mcp/dist/index.js` from the repository root. The game listens at
`ws://127.0.0.1:17311/ws` by default.

`UNITY_PLAY_MCP_URL` overrides the WebSocket URL. `UNITY_PLAY_MCP_TIMEOUT_MS`
sets the action timeout in milliseconds and defaults to `15000`.

Call `start_readings` when a play session begins, then use `get_scene_state` to
inspect the folded scene. `capture_screen` returns the image directly as an MCP
image content block. The remaining tools send input, time, reset, and reading
actions to Unity. `perform_actions` sends a sequence in one game-side batch.
