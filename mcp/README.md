# Unity Play MCP server

Run the server directly with:

```bash
npx -y unity-play-mcp
```

The server connects to `ws://127.0.0.1:17311/ws` by default. Start Unity Play
MCP in a Unity project first, then add the command above to an MCP client.

For local development, install dependencies and run the compiled entry point:

```bash
npm ci
npm run build
node dist/index.js
```

When an MCP server change is released, update this package version and the
Unity package's `Editor/McpConfig/mcp-server-version.txt` to the same compatible
npm version. The release workflow rejects mismatched versions.
