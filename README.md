# Unity Play MCP

[![MCP 서버 테스트](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/mcp-tests.yml/badge.svg?branch=develop)](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/mcp-tests.yml)
[![Unity 테스트](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/unity-tests.yml/badge.svg?branch=develop)](https://github.com/dev-yunseong/unity-play-mcp/actions/workflows/unity-tests.yml)

코딩 에이전트가 실행 중인 Unity 게임을 테스트할 수 있게 해 주는 MCP 서버입니다. 에이전트가 장면과 오브젝트 상태를 읽고, 게임에 가상 입력을 보내고, 화면을 캡처합니다.

## 설치

- Unity 2022.3 이상과 Node.js 22.14 이상이 필요합니다.
- Claude Code, Cursor, Visual Studio Code, Codex를 지원합니다.

Unity에서 **Window > Package Manager**를 열고 **Add package from git URL**을 선택합니다. 아래 주소를 입력하세요.

```text
https://github.com/dev-yunseong/unity-play-mcp.git?path=Packages/dev.yunseong.unityplaymcp#latest
```

특정 버전이 필요하면 주소 끝의 `latest`를 해당 릴리스 태그로 바꾸세요. [릴리스 목록](https://github.com/dev-yunseong/unity-play-mcp/releases)

## 에이전트 연결

1. Unity에서 **Edit > Project Settings > Unity Play MCP**를 엽니다.
2. **Configuration scope**에서 프로젝트에만 등록하려면 **Project**, 사용자 설정에 등록하려면 **User**를 고릅니다.
3. 사용하는 에이전트 옆의 **Add**를 누릅니다. **Configured**가 표시되면 에이전트를 다시 시작합니다.
4. Unity에서 Play Mode를 시작하고 에이전트에게 “현재 장면을 읽어 줘”라고 요청합니다.

Codex를 기본 설정으로 실행한다면 **User**를 고르세요. **Project** 설정을 쓰려면 `CODEX_HOME`을 프로젝트의 `.codex` 폴더로 지정하고 Codex를 실행해야 합니다.

연결을 해제하려면 등록할 때 고른 범위에서 **Remove**를 누르세요.

## 사용하기

Play Mode에서 확인할 동작과 기대 결과를 에이전트에게 알려 주세요. 게임 상태를 읽기만 하거나, 입력을 보낸 뒤 상태가 어떻게 바뀌는지 확인할 수 있습니다. [MCP 도구 설명](docs/observe-act-loop.md)

## 문제 해결

- **처음 연결할 때** `npx`가 MCP 서버를 내려받으므로 인터넷 연결이 필요합니다.
- **Unity에 연결되지 않을 때** Unity가 Play Mode인지, 에이전트가 Unity와 같은 컴퓨터에서 실행 중인지 확인하세요.
- **Update required가 표시될 때** Unity Play MCP 설정에서 **Update CLI**를 누른 뒤 에이전트를 다시 시작하세요.

## 라이선스

MIT
