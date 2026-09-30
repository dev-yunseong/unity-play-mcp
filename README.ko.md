# Unity Play MCP

한국어 | [English](README.md)

실행 중인 Unity 게임을 AI coding agent가 직접 보고 조작하게 해 주는 MCP server입니다. agent는 현재 scene을 읽고, 버튼을 누르고, 텍스트를 입력하고, 화면을 캡처할 수 있습니다.

이런 일을 시킬 수 있습니다.

- "지금 scene에 뭐가 있는지 알려줘."
- "Start 버튼을 누르고 화면을 캡처해 줘."
- "튜토리얼을 끝까지 진행해 보고, 막히는 곳이 있으면 알려줘."

## 준비 사항

- Unity 2022.3 이상
- Node.js 22.14 이상
- 지원하는 agent: Claude Code, Cursor, Visual Studio Code, Codex

## 설치

Unity에서 **Window > Package Manager**를 열고 **Add package from git URL**을 선택한 뒤 아래 주소를 입력합니다.

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#latest
```

특정 버전을 설치하려면 끝의 `latest`를 release tag(예: `v0.2.0`)로 바꿉니다.

## Agent 연결

1. **Edit > Project Settings > Unity Play MCP**를 엽니다.
2. 사용하는 agent 옆의 **Add**를 누릅니다.
3. 상태가 **Configured**로 바뀌었는지 확인합니다.
4. agent가 켜져 있었다면 다시 시작합니다.
5. Unity에서 Play Mode를 시작합니다.
6. agent에게 "현재 scene을 읽어 줘"라고 요청합니다.

MCP server는 처음 실행할 때 `npx`가 내려받으므로 첫 연결에는 인터넷이 필요하고 시간이 조금 걸릴 수 있습니다.

연결을 없애려면 같은 화면에서 **Remove**를 누릅니다. 다른 MCP server 설정은 건드리지 않습니다.

### 설정 범위

설정 화면 위쪽의 **Configuration scope**에서 설정을 어디에 저장할지 고릅니다.

- **Project** (기본값): 이 Unity project에서만 사용합니다.
- **User**: 내 계정의 모든 project에서 사용합니다.

| Agent | Project | User |
| --- | --- | --- |
| Claude Code | `.mcp.json` | `~/.claude.json` |
| Cursor | `.cursor/mcp.json` | `~/.cursor/mcp.json` |
| Visual Studio Code | `.vscode/mcp.json` | Windows: `%APPDATA%\Code\User\mcp.json`<br>macOS: `~/Library/Application Support/Code/User/mcp.json`<br>Linux: `~/.config/Code/User/mcp.json` |
| Codex | `.codex/config.toml` * | `~/.codex/config.toml` |

Project 파일 경로는 Unity project 폴더 기준입니다.

\* Codex는 기본적으로 `~/.codex/config.toml`만 읽습니다. Project scope 파일은 `CODEX_HOME`을 `<Unity project>/.codex`로 지정해서 `codex`를 실행할 때만 적용됩니다.

scope를 바꿔도 이미 추가한 항목은 옮겨지거나 지워지지 않습니다. 지우려면 추가했던 scope를 다시 선택하고 **Remove**를 누르세요.

## 사용하기

Play Mode를 시작한 상태에서 agent에게 평소 말투로 요청하면 됩니다. Unity가 `127.0.0.1:17311`에 local 연결을 열고, 같은 컴퓨터의 MCP server가 여기에 접속합니다.

Unity가 연결되어 있는지 궁금하면 "Unity 켜져 있어?"라고 물어보세요. agent가 연결 상태와 마지막으로 받은 데이터 시각을 알려 줍니다. Play Mode가 아닐 때 다른 요청을 하면 Play Mode를 시작하라고 안내합니다.

### 준비된 요청

자주 쓰는 요청 네 가지는 agent의 command로 등록되어 있습니다. Claude Code에서는 `/unity-play:<이름>`으로 부를 수 있습니다.

| 이름 | 하는 일 | 입력 |
| --- | --- | --- |
| `inspect_scene` | scene에 무엇이 있고 어떤 object를 조작할 수 있는지 정리합니다. | `selector` (선택) |
| `review_screen` | 화면을 캡처해 layout, 가독성, 어색한 부분을 검토합니다. | `focus` (선택) |
| `run_steps` | 적어 준 player 행동을 순서대로 수행하고, 처음으로 기대와 달라진 단계를 알려 줍니다. | `steps`, `expectation` (선택) |
| `track_value` | object의 값이 시간에 따라 어떻게 바뀌는지 관찰합니다. 행동을 함께 지정할 수 있습니다. | `selector`, `action` (선택) |

### 값을 읽는 간격

agent가 게임을 지켜보는 동안 Unity Play MCP는 값을 1초마다 읽고 바뀐 것만 전달합니다. 1초보다 빠르게 변하는 값을 추적해야 할 때는 **Edit > Project Settings > Unity Play MCP**의 **Reading interval (s)**에서 간격을 줄일 수 있습니다.

- 0.02초부터 10초까지 지정할 수 있습니다. 범위를 벗어나면 가까운 값으로 조정됩니다.
- 새 간격은 다음에 시작하는 관찰부터 적용됩니다.
- 너무 짧게 잡으면 게임이 느려져 결과가 실제와 달라질 수 있습니다. 필요한 때만 줄이고 끝나면 되돌리세요.

## 문제 해결

### agent에 `unity-play`가 보이지 않아요

설정 화면에서 **Refresh**를 누르고 **Add**를 다시 누른 뒤 agent를 다시 시작하세요. 터미널에서 `node --version`과 `npx --version`이 동작하는지도 확인하세요.

### 첫 연결이 오래 걸려요

`npx`가 MCP server를 처음 내려받는 중일 수 있습니다. 인터넷에 연결된 상태로 다운로드가 끝나기를 기다린 뒤 agent를 다시 시작하세요.

### Unity에 연결되지 않아요

agent에게 "Unity 상태를 확인해 줘"라고 하면 어느 주소로 연결을 시도했고 무엇이 응답했는지 알려 줍니다. 올바른 project가 열려 있고 Play Mode인지 확인하세요. 연결은 같은 컴퓨터 안에서만 동작합니다. container나 원격 환경에서 agent를 실행한다면 host의 loopback 주소에 접근할 수 있어야 합니다.

### 설정 화면에 "파일을 읽을 수 없다"고 나와요

설정 파일에 문법 오류가 있으면 내용을 보존할 수 없어 수정하지 않습니다. 표시된 오류를 고치고 **Refresh**를 누르세요. Visual Studio Code 설정 파일에 주석이 있는 경우에도, 저장하면 주석이 지워지기 때문에 수정하지 않습니다.

## 개발에 참여하려면

```bash
cd mcp
npm install
npm run build
npm test
```

이 repository의 package를 Unity에 연결하면 설정 화면이 local build(`mcp/dist/index.js`)를 우선 사용하므로, publish하지 않고 server 변경을 바로 확인할 수 있습니다. Unity package test는 [테스트 실행 방법](.agents/docs/project.md#running-package-tests)을 참고하세요. release 절차는 [RELEASING.md](RELEASING.md)에 있습니다.

## License

MIT
