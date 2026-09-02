# Unity Play MCP

한국어 | [English](README.md)

Unity Play MCP를 사용하면 coding agent가 실행 중인 Unity game의 현재 scene을 읽고, player action을 수행하고, screenshot을 받을 수 있습니다.

## 준비 사항

- Unity 2022.3 이상
- Node.js 22.14 이상
- 지원 agent: Claude Code, Cursor, Visual Studio Code, Codex
- `npx`가 MCP server를 처음 내려받을 때 사용할 network

## Unity package 설치

Unity에서 **Window > Package Manager**를 열고 **Add package from git URL**을 선택한 뒤 다음 URL을 입력합니다.

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#latest
```

`latest` tag는 가장 최근에 성공한 GitHub release를 가리킵니다. Unity Package Manager가 이전 version을 cache에서 계속 사용하면 **Update**를 누르거나 package를 제거한 뒤 다시 추가합니다.

특정 release를 설치하려면 package path 뒤에 tag를 붙입니다.

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#v0.1.0
```

## Agent 연결

1. Unity에서 **Edit > Project Settings > Unity Play MCP**를 엽니다.
2. **Configuration scope**를 고릅니다: **Project**(기본값) 또는 **User**. 고른 scope는 이 project에 대해 계속 기억됩니다.
3. 사용하는 agent 행에서 **Add**를 누릅니다.
4. 상태가 **Configured**인지 확인합니다.
5. agent가 이미 실행 중이면 다시 시작합니다.
6. Unity에서 Play Mode를 시작합니다.
7. agent에게 현재 scene 조회나 game screen capture를 요청합니다.

설정 page는 고른 scope의 설정 파일에 `unity-play` entry를 기록합니다. **Add**, **Remove**, **Configured** 상태는 모두 그 scope의 파일에만 적용됩니다.

**Project scope** (Unity project 디렉터리 아래):

| Agent | 설정 파일 |
| --- | --- |
| Claude Code | `<Unity project>/.mcp.json` |
| Cursor | `<Unity project>/.cursor/mcp.json` |
| Visual Studio Code | `<Unity project>/.vscode/mcp.json` |
| Codex | `<Unity project>/.codex/config.toml` * |

**User scope** (홈 디렉터리 아래, 이 계정의 모든 project가 함께 씀):

| Agent | 설정 파일 |
| --- | --- |
| Claude Code | `~/.claude.json` |
| Cursor | `~/.cursor/mcp.json` |
| Visual Studio Code | Windows `%APPDATA%\Code\User\mcp.json`, macOS `~/Library/Application Support/Code/User/mcp.json`, Linux `~/.config/Code/User/mcp.json` |
| Codex | `~/.codex/config.toml` |

\* Codex는 `$CODEX_HOME/config.toml`을 읽고 `CODEX_HOME`의 기본값은 `~/.codex`입니다. 위 project scope 파일은 `CODEX_HOME`을 `<Unity project>/.codex`로 지정해 `codex`를 실행할 때만 적용됩니다.

scope를 바꿔도 이미 있는 entry는 옮겨지거나 지워지지 않습니다. 자동 migration은 없습니다. 기본값이 **Project**이므로, 예전 version이 `~/.codex/config.toml`에 써 둔 Codex entry는 upgrade한 뒤에도 그대로 남습니다. 지우려면 scope를 **User**로 바꾸고 **Remove**를 누르세요.

local build인 `mcp/dist/index.js`가 있으면 설정 page가 그 build를 사용합니다. Git URL로 설치한 package에는 보통 local server가 없으므로, 그때는 `npx -y unity-play-mcp@<compatible version>`을 기록합니다. compatible server version은 Unity package에 함께 들어 있습니다.

Unity Play MCP entry만 제거하려면 같은 설정 page에서 **Remove**를 누릅니다. 다른 server와 관련 없는 설정은 그대로 남습니다.

## 연결 확인

tool을 호출하기 전에 Play Mode를 시작합니다. Unity는 `ws://127.0.0.1:17311/ws`에 local WebSocket server를 열고, 같은 computer에서 MCP server가 연결합니다.

처음에는 다음과 같이 요청할 수 있습니다.

- “현재 Unity scene을 읽어줘.”
- “game screen을 capture해줘.”
- “Start button을 눌러줘.”

“Unity 켜져 있어?”라고 물으면 agent가 `get_unity_status`를 부릅니다. 게임에 닿는지, pulse가 시작됐는지, 마지막 pulse가 언제 왔는지 답합니다. 다른 tool도 Unity에 닿지 못하면 socket 오류 대신 게임이 돌고 있지 않으니 Play Mode를 시작하라고 답합니다.

MCP server는 agent가 연결할 때 짧은 사용 안내를 함께 넘깁니다. tool들이 어떤 관계인지 매번 설명하지 않아도 됩니다. 안내에는 Unity가 Play Mode여야 한다는 것, 첫 `get_scene_state` 전에 `start_readings`를 부른다는 것, `click`과 `enter_text`가 쓰는 instance id는 `get_scene_state`가 알려준다는 것이 담깁니다.

## 준비된 요청

MCP server는 prompt 네 개를 등록합니다. prompt를 지원하는 agent에서는 문장을 입력하는 대신 골라서 부르는 command로 보입니다. Claude Code는 `/unity-play:<이름>`으로 표시합니다.

| Prompt | 하는 일 | 인자 |
| --- | --- | --- |
| `inspect_scene` | scene을 읽고 무엇이 있는지, 어떤 object를 조작할 수 있는지 보고합니다. | `selector` (선택) |
| `review_screen` | game screen을 capture하고 layout, 가독성, 이상한 곳을 검토합니다. | `focus` (선택) |
| `run_steps` | 적어 준 player action 순서를 수행하고 처음으로 어긋난 단계를 보고합니다. | `steps`, `expectation` (선택) |
| `track_value` | object의 member 값이 pulse를 거치며 어떻게 움직이는지 봅니다. action을 함께 줄 수 있습니다. | `selector`, `action` (선택) |

prompt는 사용자가 고르는 것입니다. agent가 스스로 판단해야 하는 것 — Unity가 Play Mode여야 한다는 것, tool을 어떤 순서로 부르는지 — 은 prompt가 아니라 연결할 때 server가 넘기는 안내에 있습니다.

## 문제 해결

### Agent에 `unity-play`가 보이지 않음

Unity 설정 page에서 **Refresh**를 누르고 **Add**를 다시 누른 뒤 agent를 다시 시작합니다. `node --version`과 `npx --version`으로 Node.js와 `npx`가 설치되어 있는지 확인합니다.

### 첫 연결이 오래 걸림

`npx`는 처음 실행할 때 compatible MCP server를 내려받습니다. network를 사용할 수 있는 상태로 두고, download가 끝난 뒤 agent를 다시 시작합니다.

### MCP server가 Unity에 연결하지 못함

agent에게 `get_unity_status`를 부르게 하면 어느 주소로 연결을 시도하는지, 거기에 무엇이 응답하는지 알려줍니다. 올바른 Unity project가 열려 있고 Play Mode인지 확인합니다. 연결은 local 전용입니다. remote agent나 container에서 사용하려면 host loopback address에 접근할 수 있어야 합니다.

### 설정 page가 설정 파일을 읽을 수 없다고 표시함

설정 page는 기존 내용을 안전하게 보존할 수 없는 파일을 수정하지 않습니다. 표시된 syntax error를 고치고 **Refresh**를 누릅니다. comment가 들어 있는 Visual Studio Code 설정 파일은 JavaScript Object Notation으로 다시 쓰면 comment가 사라지므로 수정하지 않습니다.

## Local development

repository를 clone하고 server를 build합니다.

```bash
cd mcp
npm install
npm run build
npm test
```

Unity package를 이 checkout에서 연결하면 설정 page가 local `mcp/dist/index.js`를 우선 사용하므로 publish하지 않고 server 변경을 확인할 수 있습니다.

Unity package test는 생성한 Unity test project에서 실행합니다. 정확한 command는 [project test 지침](.agents/docs/project.md#running-package-tests)을 확인하세요.

## License

MIT
