# Unity Play MCP에 기여하기

게임 프로젝트에서 액션을 추가하는 방법은 [사용자 가이드](docs/project-actions.md)를 참고하세요.

## 액션을 찾고 실행하는 방식

1. Unity가 레지스트리를 처음 만들 때 패키지 자체와 패키지를 직접 참조하는 로드된 어셈블리를 리플렉션으로 살펴봅니다.
2. `IUnityAction`을 구현한 클래스에서 `[UnityAction]`을 읽고, 액션 이름으로 찾을 수 있게 등록합니다. 찾은 타입은 어셈블리 리로드 전까지 재사용합니다.
3. MCP 서버는 Unity에 등록된 프로젝트 액션의 설명을 받아 에이전트에 보여 줍니다. `perform_actions`가 이름과 인자를 보내면 Unity가 해당 클래스의 `Execute`를 실행합니다.

패키지의 기본 액션은 [Actions/BuiltIn](Packages/dev.yunseong.unityplaymcp/Runtime/Actions/BuiltIn)에 모여 있습니다. [공통 인터페이스와 어트리뷰트](Packages/dev.yunseong.unityplaymcp/Runtime/Actions/UnityAction.cs), [등록 코드](Packages/dev.yunseong.unityplaymcp/Runtime/Actions/ActionRegistry.cs)는 상위 폴더에서 확인할 수 있습니다.

## 변경 확인

Unity 쪽을 수정했다면 패키지 테스트를 실행하세요. [Unity 테스트 실행 방법](.agents/docs/project.md#running-package-tests)

MCP 서버를 수정했다면 `mcp/`에서 빌드와 테스트를 실행하세요.

```bash
npm ci
npm run build
npm test
```
