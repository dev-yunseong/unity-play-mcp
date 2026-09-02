# Unity Play MCP

[한국어](README.ko.md) | English

Unity Play MCP lets a coding agent read the current Unity scene, perform player actions, and capture screenshots while the game is running.

## Requirements

- Unity 2022.3 or later
- Node.js 22.14 or later
- A supported agent: Claude Code, Cursor, Visual Studio Code, or Codex
- Network access the first time `npx` downloads the MCP server

## Install the Unity package

In Unity, open **Window > Package Manager**, select **Add package from git URL**, and enter:

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp
```

To install a specific release, append its tag after the package path:

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#v0.1.0
```

## Connect your agent

1. In Unity, open **Edit > Project Settings > Unity Play MCP**.
2. Choose a **Configuration scope**: **Project** (default) or **User**. Unity remembers the chosen scope for this project.
3. Find your agent and select **Add**.
4. Check that the row says **Configured**.
5. Restart the agent if it was already running.
6. Enter Play Mode in Unity.
7. Ask the agent to inspect the current scene or capture the game screen.

The settings page writes the `unity-play` entry to the configuration file for the selected scope. **Add**, **Remove**, and the **Configured** status apply only to that scope's file.

**Project scope** (under the Unity project directory):

| Agent | Configuration file |
| --- | --- |
| Claude Code | `<Unity project>/.mcp.json` |
| Cursor | `<Unity project>/.cursor/mcp.json` |
| Visual Studio Code | `<Unity project>/.vscode/mcp.json` |
| Codex | `<Unity project>/.codex/config.toml` * |

**User scope** (under your home directory, shared by every project on this account):

| Agent | Configuration file |
| --- | --- |
| Claude Code | `~/.claude.json` |
| Cursor | `~/.cursor/mcp.json` |
| Visual Studio Code | Windows `%APPDATA%\Code\User\mcp.json`, macOS `~/Library/Application Support/Code/User/mcp.json`, Linux `~/.config/Code/User/mcp.json` |
| Codex | `~/.codex/config.toml` |

\* Codex reads `$CODEX_HOME/config.toml`, and `CODEX_HOME` defaults to `~/.codex`. The project-scope file above applies only when you run `codex` with `CODEX_HOME` set to `<Unity project>/.codex`.

Switching scope does not move or delete an existing entry — there is no automatic migration. **Project** is the default scope, so a Codex entry that an earlier version of this package wrote to `~/.codex/config.toml` stays there after you upgrade. To remove it, switch the scope to **User** and select **Remove**.

When a local build exists at `mcp/dist/index.js`, the settings page uses that build. A package installed from a Git URL normally has no local server, so the page writes `npx -y unity-play-mcp@<compatible version>` instead. The compatible server version is included in the Unity package.

To remove only the Unity Play MCP entry, return to the same settings page and select **Remove**. Other servers and unrelated configuration remain in the file.

## Verify the connection

Enter Play Mode before calling a tool. Unity opens a local WebSocket server at `ws://127.0.0.1:17311/ws`; the MCP server connects to it from the same computer.

Useful first requests include:

- “Read the current Unity scene.”
- “Capture the game screen.”
- “Click the Start button.”

## Troubleshooting

### The agent does not show `unity-play`

Select **Refresh** in the Unity settings page, select **Add** again, and restart the agent. Confirm that Node.js is available with `node --version` and that `npx` is available with `npx --version`.

### The first connection takes time

`npx` downloads the compatible MCP server on first use. Keep network access available, then restart the agent after the download completes.

### The MCP server cannot connect to Unity

Confirm that the correct Unity project is open and in Play Mode. The connection is local only; remote agents and containers need explicit access to the host loopback address.

### The settings page reports an unreadable configuration

The page refuses to rewrite a configuration it cannot preserve safely. Fix the syntax shown in the error and select **Refresh**. Visual Studio Code configuration files containing comments are intentionally refused because rewriting them as JavaScript Object Notation would remove the comments.

## Local development

Clone the repository and build the server:

```bash
cd mcp
npm install
npm run build
npm test
```

When the Unity package is linked from this checkout, the settings page prefers the local `mcp/dist/index.js`, so server changes are available without publishing.

Package tests run through a generated Unity test project. See [project testing instructions](.agents/docs/project.md#running-package-tests) for the exact commands.

## License

MIT
