# Unity Play MCP

[![MCP 서버 테스트](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/mcp-tests.yml/badge.svg?branch=develop)](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/mcp-tests.yml)
[![Unity 테스트](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/unity-tests.yml/badge.svg?branch=develop)](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/unity-tests.yml)

코딩 에이전트가 실행 중인 Unity 게임을 직접 테스트할 수 있게 해 주는 MCP 서버입니다. 에이전트는 장면과 오브젝트 상태를 읽고, 가상 입력을 보내고, 화면을 캡처할 수 있습니다.

[준비 사항](#준비-사항)을 확인한 뒤 [Unity 패키지를 설치](#설치)하고 [에이전트를 연결](#에이전트-연결)하세요. Play Mode에서 장면을 읽을 수 있습니다. 소스 위치와 테스트는 [개발에 참여하려면](#개발에-참여하려면)을 참고하세요.

## 준비 사항

- Unity 2022.3 이상
- Node.js 22.14 이상
- 지원 에이전트는 Claude Code, Cursor, Visual Studio Code, Codex

## 설치

Unity에서 **Window > Package Manager**를 열고 **Add package from git URL**을 선택한 뒤 아래 주소를 입력합니다.

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#latest
```

특정 버전을 설치하려면 끝의 `latest`를 릴리스 태그(예: `v0.4.0`)로 바꿉니다.

### 버전 호환

Unity 패키지 릴리스마다 함께 검증한 MCP 서버 버전을 기록하고, 설정 페이지의 **Add**가 `npx -y unity-play-mcp@<그 버전>`을 씁니다. 이 짝을 유지하세요. 로컬 `mcp/dist` 빌드나 이전 패키지가 추가한 항목을 쓰면 버전이 섞일 수 있습니다.

`get_unity_status`는 실행 중인 CLI와 SDK가 요구하는 CLI 버전을 비교해 다르면 **Update required**를 안내합니다. SDK와 CLI 버전 번호 자체가 같아야 하는 것은 아닙니다. 호환 정보가 없는 구형 SDK는 **unknown**으로 표시합니다.

**Edit > Project Settings > Unity Play MCP**에서 에이전트 설정 범위를 고른 뒤 **Update CLI**를 누르면 환경변수와 다른 서버 옵션을 보존하면서 호환 버전으로 고정합니다. 요구 버전이 더 오래된 경우에도 그 버전으로 맞춥니다. 이후 에이전트를 재시작하면 `npx`가 해당 버전을 설치·실행합니다. 설정에서 버전을 고정해도 실행 중 프로세스는 아직 구버전일 수 있으므로 `get_unity_status`로 다시 확인하세요. 설치 실패 시 에이전트 로그와 네트워크 연결을 확인하세요.

자동 수정은 생성기가 만든 `npx -y unity-play-mcp@<exact-version>` 형태만 지원합니다. 로컬 빌드, 래퍼, 추가 인자, 버전 범위나 태그은 수동으로 업데이트해야 합니다. 주석·중복 키가 있는 JSON이나 모호한 TOML은 덮어쓰지 않으며, SDK 의존성 자체는 변경하지 않습니다.

| Unity 패키지 | MCP 서버 | 비고 |
| --- | --- | --- |
| `v0.4.0` | `0.4.0` | 상태 읽기가 다시 시작되거나 연결이 끊긴 뒤에도 장면을 계속 따라가고, 오래된 상태 데이터에 `stale`을 표시합니다. ID로 객체 위에 포인터 올리기, 스크린샷의 화면 좌표와 프레임과 장면 정보, 인증 값처럼 보이는 값 가리기를 더합니다. 0.3.x 이하 서버는 이 패키지의 스크린샷을 거절합니다. |
| `v0.3.0` | `0.3.0` | 대상 검색, ID로 클릭·드래그, 조건 대기, 스크립트 다시 불러오기 뒤 복구를 더합니다. 서버가 켜진 상태에서 장면 읽기를 다시 시작하면 장면이 이전 화면에 머물 수 있습니다. 0.4.0으로 올리세요. |
| `v0.2.0` | `0.2.0` | 0.2.0 서버가 0.2.0 패키지의 컴포넌트 값을 읽지 못해 장면 상태가 비어 있습니다. 올리세요. |

## 에이전트 연결

1. **Edit > Project Settings > Unity Play MCP**를 엽니다.
2. 사용하는 에이전트 옆의 **Add**를 누릅니다.
3. 상태가 **Configured**로 바뀌었는지 확인합니다.
4. 에이전트가 켜져 있었다면 다시 시작합니다.
5. Unity에서 Play Mode를 시작합니다.
6. 에이전트에게 "현재 장면을 읽어 줘"라고 요청합니다.

MCP 서버는 처음 실행할 때 `npx`가 내려받으므로 첫 연결에는 인터넷이 필요하고 시간이 조금 걸릴 수 있습니다.

연결을 없애려면 같은 화면에서 **Remove**를 누릅니다. 다른 MCP 서버 설정은 건드리지 않습니다.

### 설정 범위

설정 화면 위쪽의 **Configuration scope**에서 설정을 어디에 저장할지 고릅니다.

- **Project** (기본값): 이 Unity 프로젝트에서만 사용합니다.
- **User**: 내 계정의 모든 프로젝트에서 사용합니다.

| 에이전트 | Project | User |
| --- | --- | --- |
| Claude Code | `.mcp.json` | `~/.claude.json` |
| Cursor | `.cursor/mcp.json` | `~/.cursor/mcp.json` |
| Visual Studio Code | `.vscode/mcp.json` | Windows: `%APPDATA%\Code\User\mcp.json`<br>macOS: `~/Library/Application Support/Code/User/mcp.json`<br>Linux: `~/.config/Code/User/mcp.json` |
| Codex | `.codex/config.toml` * | `~/.codex/config.toml` |

Project 파일 경로는 Unity 프로젝트 폴더 기준입니다.

\* Codex는 기본적으로 `~/.codex/config.toml`만 읽습니다. Project 범위의 파일은 `CODEX_HOME`을 `<Unity project>/.codex`로 지정해서 `codex`를 실행할 때만 적용됩니다.

범위를 바꿔도 이미 추가한 항목은 옮겨지거나 지워지지 않습니다. 지우려면 추가했던 범위를 다시 선택하고 **Remove**를 누르세요.

## 사용하기

패키지를 설치해 두어도 에이전트 커서와 키보드 상태 UI는 기본으로 숨겨져 있습니다. 에이전트에게 표시를 요청하면 `set_overlay { "visible": true }`로 켜고, `false`로 끕니다. `perform_actions`에서도 `{ "method": "set_overlay", "visible": false }`를 사용할 수 있습니다. 숨겨도 가상 입력은 동작합니다.

UI는 처음 표시할 때 생성하고, 숨기면 Canvas와 표시용 Update를 비활성화해 재사용합니다. 연결만으로 수집을 시작하지 않습니다. 요청 또는 실행 중 작업이 끝난 뒤 15초 동안 요청이 없으면 UI, 장면 탐색, 실시간 상태 읽기, 성능 수집을 멈추고 연결은 유지합니다. 상태 조회는 성능 수집만, 장면·대상 조회는 필요한 장면 수집을 재개하고 새 상태 데이터를 기다립니다. 단순 입력과 전체 화면 캡처는 수집이나 UI를 켜지 않습니다. UI는 `set_overlay { visible: true }`로 다시 켭니다. 긴 작업과 진행 중인 조건 대기는 중간에 끊지 않습니다. 마지막 클라이언트가 끊기면 진행 중 입력도 정리하며 재연결 후에도 UI는 숨김 상태입니다.

미사용 시 비용이 완전히 0인 것은 아닙니다. 요청을 받을 로컬 연결 대기와 매 프레임 요청 확인, 가상 입력 처리는 남습니다. 백그라운드에서도 요청을 처리하려고 연결을 받는 동안 `Application.runInBackground`를 켜므로 게임 자체의 백그라운드 실행 비용도 발생할 수 있습니다. 자동 호스트 생성은 Editor와 Development Build에 한정됩니다.

Play Mode를 시작한 상태에서 에이전트에게 평소 말투로 요청하면 됩니다. Unity가 `127.0.0.1:17311`에 로컬 연결을 열고, 같은 컴퓨터의 MCP 서버가 여기에 접속합니다.

Unity가 연결되어 있는지 궁금하면 "Unity 켜져 있어?"라고 물어보세요. 에이전트가 연결 상태와 마지막으로 받은 데이터 시각을 알려 줍니다. Play Mode가 아닐 때 다른 요청을 하면 Play Mode를 시작하라고 안내합니다.

### 준비된 요청

자주 쓰는 요청 네 가지는 에이전트 명령으로 등록되어 있습니다. Claude Code에서는 `/unity-play:<이름>`으로 부를 수 있습니다.

| 이름 | 하는 일 | 입력 |
| --- | --- | --- |
| `inspect_scene` | 장면에 무엇이 있고 어떤 오브젝트를 조작할 수 있는지 정리합니다. | `selector` (선택) |
| `review_screen` | 화면을 캡처해 배치, 가독성, 어색한 부분을 검토합니다. | `focus` (선택) |
| `run_steps` | 적어 준 플레이어 행동을 순서대로 수행하고, 처음으로 기대와 달라진 단계를 알려 줍니다. | `steps`, `expectation` (선택) |
| `track_value` | 오브젝트의 값이 시간에 따라 어떻게 바뀌는지 관찰합니다. 행동을 함께 지정할 수 있습니다. | `selector`, `action` (선택) |

### 장면을 관찰하고 입력 결과 확인하기

에이전트는 현재 장면을 관찰하고 입력을 보낸 뒤 게임 상태를 다시 확인할 수 있습니다. 입력이 Unity에 전달됐는지와 게임에서 기대한 결과가 관찰됐는지는 별도로 반환됩니다. 사용 가능한 도구와 버전 조건은 [관찰, 입력, 결과 확인 안내](docs/observe-act-loop.md)를 참고하세요.

### 값을 읽는 간격

에이전트가 게임을 지켜보는 동안 Unity Play MCP는 값을 1초마다 읽고 바뀐 것만 전달합니다. 1초보다 빠르게 변하는 값을 추적해야 할 때는 **Edit > Project Settings > Unity Play MCP**의 **Reading interval (s)**에서 간격을 줄일 수 있습니다.

- 0.02초부터 10초까지 지정할 수 있습니다. 범위를 벗어나면 가까운 값으로 조정됩니다.
- 새 간격은 다음에 시작하는 관찰부터 적용됩니다.
- 너무 짧게 잡으면 게임이 느려져 결과가 실제와 달라질 수 있습니다. 필요한 때만 줄이고 끝나면 되돌리세요.

## 문제 해결

### 에이전트에 `unity-play`가 보이지 않아요

설정 화면에서 **Refresh**를 누르고 **Add**를 다시 누른 뒤 에이전트를 다시 시작하세요. 터미널에서 `node --version`과 `npx --version`이 동작하는지도 확인하세요.

### 첫 연결이 오래 걸려요

`npx`가 MCP 서버를 처음 내려받는 중일 수 있습니다. 인터넷에 연결된 상태로 다운로드가 끝나기를 기다린 뒤 에이전트를 다시 시작하세요.

### Unity에 연결되지 않아요

에이전트에게 "Unity 상태를 확인해 줘"라고 하면 어느 주소로 연결을 시도했고 무엇이 응답했는지 알려 줍니다. 올바른 프로젝트가 열려 있고 Play Mode인지 확인하세요. 연결은 같은 컴퓨터 안에서만 동작합니다. 컨테이너나 원격 환경에서 에이전트를 실행한다면 호스트의 루프백 주소에 접근할 수 있어야 합니다.

### 설정 화면에 "파일을 읽을 수 없다"고 나와요

설정 파일에 문법 오류가 있으면 내용을 보존할 수 없어 수정하지 않습니다. 표시된 오류를 고치고 **Refresh**를 누르세요. Visual Studio Code 설정 파일에 주석이 있는 경우에도, 저장하면 주석이 지워지기 때문에 수정하지 않습니다.

## 개발에 참여하려면

[Unity 패키지](Packages/dev.yunseong.unityplaymcp)는 실행 중인 게임 상태를 읽고 입력을 적용합니다. [TypeScript MCP 서버](mcp)는 이 기능을 에이전트가 호출할 수 있는 MCP 도구로 제공합니다.

저장소 루트에서 Node.js 22.14 이상과 Bash(Linux, macOS, WSL)를 사용해 서버를 확인하세요.

```bash
cd mcp
npm ci
npm run build
npm test
```

Windows용 Node.js를 Git Bash에서 사용한다면, 명령을 실행하기 전에 `npm_config_script_shell`에 Git Bash 실행 파일을 지정하세요.

[MCP 서버 테스트](.github/workflows/mcp-tests.yml)는 PR과 `develop` 푸시에서 같은 명령을 실행합니다. [Unity 테스트](.github/workflows/unity-tests.yml)는 EditMode와 PlayMode를 별도로 실행하며 Unity 라이선스가 필요합니다. [로컬 Unity 테스트 실행 방법](.agents/docs/project.md#running-package-tests)도 참고하세요.

이 저장소의 패키지를 Unity에 연결하면 설정 화면이 로컬 빌드(`mcp/dist/index.js`)를 우선 사용하므로 배포 없이 서버 변경을 확인할 수 있습니다. 릴리스 절차는 [RELEASING.md](RELEASING.md)에 있습니다.

## 라이선스

MIT
