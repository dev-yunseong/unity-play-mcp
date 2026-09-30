# Unity Play MCP

[한국어](README.ko.md) | English

An MCP server that lets an AI coding agent see and play your running Unity game. The agent can read the current scene, click buttons, type text, and capture the screen.

Things you can ask for:

- "What's in the scene right now?"
- "Click Start and capture the screen."
- "Play through the tutorial and tell me where it gets stuck."

## Requirements

- Unity 2022.3 or later
- Node.js 22.14 or later
- A supported agent: Claude Code, Cursor, Visual Studio Code, or Codex

## Install

In Unity, open **Window > Package Manager**, select **Add package from git URL**, and enter:

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#latest
```

To pin a version, replace `latest` at the end with a release tag such as `v0.4.0`.

### Version compatibility

Each Unity package release records the MCP server version it was tested with, and **Add** on the settings page writes `npx -y unity-play-mcp@<that version>`. Keep that pair. A local `mcp/dist` build or an entry added by an older package can mix versions.

| Unity package | MCP server | Notes |
| --- | --- | --- |
| `v0.4.0` | `0.4.0` | Keeps following the scene after readings restart or the connection drops, and marks an old reading `stale`. Adds hovering over an object by id, screen coordinates and the frame/scene on screenshots, and hides values that look like credentials. A 0.3.x or older server rejects this package's screenshots. |
| `v0.3.0` | `0.3.0` | Adds target search, clicking and dragging objects by id, waiting for a condition, and recovery after script reloads. If readings restart while the server keeps running (stop and start readings, re-entering Play Mode, a reload), the scene can stay on the previous screen. Upgrade to 0.4.0. |
| `v0.2.0` | `0.2.0` | The 0.2.0 server cannot read the 0.2.0 package's component values, so scene state stays empty. Upgrade. |

## Connect your agent

1. Open **Edit > Project Settings > Unity Play MCP**.
2. Select **Add** next to your agent.
3. Check that the status changes to **Configured**.
4. Restart the agent if it was running.
5. Enter Play Mode in Unity.
6. Ask the agent to "read the current scene".

The MCP server is downloaded by `npx` the first time it runs, so the first connection needs internet access and may take a moment.

To disconnect, select **Remove** on the same page. Your other MCP servers are left alone.

### Configuration scope

Use **Configuration scope** at the top of the settings page to choose where the entry is saved:

- **Project** (default): only this Unity project.
- **User**: every project on your account.

| Agent | Project | User |
| --- | --- | --- |
| Claude Code | `.mcp.json` | `~/.claude.json` |
| Cursor | `.cursor/mcp.json` | `~/.cursor/mcp.json` |
| Visual Studio Code | `.vscode/mcp.json` | Windows: `%APPDATA%\Code\User\mcp.json`<br>macOS: `~/Library/Application Support/Code/User/mcp.json`<br>Linux: `~/.config/Code/User/mcp.json` |
| Codex | `.codex/config.toml` * | `~/.codex/config.toml` |

Project paths are relative to your Unity project folder.

\* By default Codex only reads `~/.codex/config.toml`. The project file applies only if you run `codex` with `CODEX_HOME` set to `<Unity project>/.codex`.

Changing the scope does not move or delete an entry you already added. To remove it, switch back to the scope you added it under and select **Remove**.

## Using it

With Play Mode running, just ask the agent in plain language. Unity opens a local connection on `127.0.0.1:17311`, and the MCP server on the same computer connects to it.

To check the connection, ask "Is Unity running?" The agent reports whether it can reach the game and when it last received data. If you ask for anything else while Unity is not in Play Mode, the agent tells you to start Play Mode.

### Ready-made requests

Four common requests are registered as commands in your agent. In Claude Code, they appear as `/unity-play:<name>`.

| Name | What it does | Arguments |
| --- | --- | --- |
| `inspect_scene` | Summarizes what is in the scene and which objects can be interacted with. | `selector` (optional) |
| `review_screen` | Captures the screen and reviews layout, readability, and anything that looks off. | `focus` (optional) |
| `run_steps` | Performs the player actions you describe, in order, and reports the first step that didn't match what you expected. | `steps`, `expectation` (optional) |
| `track_value` | Watches how an object's values change over time, optionally while an action runs. | `selector`, `action` (optional) |

### Reading interval

While an agent is watching the game, Unity Play MCP reads values every second and sends only what changed. If you need to catch values that change faster than that, shorten the interval under **Edit > Project Settings > Unity Play MCP > Reading interval (s)**.

- Accepted range is 0.02 to 10 seconds. Values outside it are adjusted to the nearest limit.
- A new interval applies the next time watching starts.
- Very short intervals can slow the game down enough to distort what the agent sees. Shorten it only when you need to, then set it back.

## Troubleshooting

### The agent doesn't show `unity-play`

Select **Refresh** on the settings page, select **Add** again, and restart the agent. Also check that `node --version` and `npx --version` work in a terminal.

### The first connection is slow

`npx` may still be downloading the MCP server. Stay online until the download finishes, then restart the agent.

### It can't connect to Unity

Ask the agent to check Unity's status. It tells you which address it tried and what answered. Make sure the right project is open and in Play Mode. The connection only works on the same computer; if your agent runs in a container or on a remote machine, it needs access to the host's loopback address.

### The settings page says it can't read a file

If a configuration file has a syntax error, the page won't touch it because it can't keep your content safe. Fix the error shown and select **Refresh**. A Visual Studio Code file that contains comments is also left alone, because saving it would erase them.

## Contributing

```bash
cd mcp
npm install
npm run build
npm test
```

When the package is linked from this repository, the settings page prefers the local build (`mcp/dist/index.js`), so server changes take effect without publishing. For Unity package tests, see [how to run tests](.agents/docs/project.md#running-package-tests). The release process is in [RELEASING.md](RELEASING.md).

## License

MIT
