# Unity Play MCP

Unity Play MCP is packaged for Unity through Unity Package Manager.

## Package

The Unity package lives at:

```text
Packages/dev.yunseong.unityplaymcp
```

Runtime scripts are under `Runtime/` and compiled through
`Artel.Runtime.asmdef`.

## MCP server

`mcp/` is the stdio MCP server a coding agent talks to. It connects to the
WebSocket server the Unity package opens at `ws://127.0.0.1:17311/ws`, folds the
`PULSE` readings into the current scene state, and exposes the game's actions and
screen capture as MCP tools.

```bash
cd mcp && npm install && npm run build && npm test
```

`mcp/README.md` has the client configuration and the environment variables.

## Configuring an agent from Unity

`Edit > Project Settings > Unity Play MCP` writes the `unity-play` server entry
into an agent's MCP configuration file, so the absolute path of the built server
does not have to be typed by hand. It covers Claude Code (`.mcp.json`), Cursor
(`.cursor/mcp.json`), and VS Code (`.vscode/mcp.json`) in the Unity project
directory, and Codex (`~/.codex/config.toml`) in the home directory.

The page finds `mcp/dist/index.js` next to the package's repository checkout. Run
`npm install && npm run build` in `mcp/` first; until that file exists the page
says so and the `Add` buttons stay disabled.

Writing keeps everything else in the file — other MCP servers, and the unrelated
tables in `config.toml`. It does replace the whole `unity-play` entry, so an
`[mcp_servers.unity-play.env]` table added by hand is rewritten away the next
time `Add` is pressed. A JSON file is reformatted as it is rewritten, so hand
formatting does not survive; a `.vscode/mcp.json` that carries comments is refused
rather than rewritten, because rewriting it would delete them.

## Sample

`samples/WordVenture` is included as the sample Unity project. It references
the local package with:

```json
"dev.yunseong.unityplaymcp": "file:../../../Packages/dev.yunseong.unityplaymcp"
```

That is the reference the sample needs after the rename, and it is not the one
the submodule holds today — the sample still points at the old package id and
path. `samples/WordVenture` is a separate repository, so the change has to be
made there before the sample opens against this package.

Open `samples/WordVenture` in Unity to try package runtime components from a real
Unity project.

## Tests and CI

Neither the repository root nor `samples/WordVenture` can run the package's
tests as checked out, so both local runs and CI assemble a throwaway Unity
project first:

```bash
.github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test
```

`.github/workflows/unity-tests.yml` runs EditMode and PlayMode against that
project on every pull request and on every push to `develop`. It needs the Unity
licence secrets `UNITY_LICENSE` (or `UNITY_SERIAL` for Pro/Plus), `UNITY_EMAIL`,
and `UNITY_PASSWORD`; without them the workflow fails and names the missing one.

`.agents/docs/project.md` — *Running package tests* and *Continuous integration*
— has the full editor command line, where to obtain each secret, and how fork
pull requests are handled.
