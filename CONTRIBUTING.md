# Unity Play MCP에 기여하기

## 게임 전용 액션 추가

게임에서 자주 확인하는 동작은 **C# 클래스 하나로 MCP 액션에 추가할 수 있습니다.** `IUnityAction`을 구현하고 `[UnityAction]`에 이름, 설명, 인자 순서를 적으면 에이전트가 기존 `perform_actions`에서 호출할 수 있습니다. 게임 쪽 코드에 액션과 설명을 함께 두면 됩니다. MCP 서버 코드나 도구 목록을 따로 수정할 필요가 없습니다.

Unity Play MCP 패키지를 참조하는 게임의 런타임 어셈블리에 액션 클래스를 작성하세요. 이 예제는 Build Settings에 포함된 씬을 열고 로딩이 끝나면 씬 이름을 반환합니다.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlayMcp;
using UnityPlayMcp.Protocol.Dto;

[UnityAction("load_test_scene", "Load a scene included in Build Settings.", "[scene: string]")]
public sealed class LoadTestSceneAction : IUnityAction
{
    public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed)
    {
        if (parameters.Count != 1 || !(parameters[0] is string scene)
            || string.IsNullOrWhiteSpace(scene) || !Application.CanStreamedLevelBeLoaded(scene))
        {
            completed(ActionResultDto.Failure(id, "Pass a scene name from Build Settings."));
            yield break;
        }

        yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        completed(ActionResultDto.Success(id, SceneManager.GetActiveScene().name));
    }
}
```

기본 생성자가 있는 클래스를 사용하세요. 액션 이름은 영문 소문자로 시작하고 영문 소문자, 숫자, 밑줄을 최대 64자까지 쓸 수 있습니다. 중복된 이름과 예약된 이름은 등록에서 제외됩니다. 입력값은 `Execute`에서 검증하고, 실행이 끝나기 전에 `completed`를 한 번 호출하세요.

Play Mode에서 `get_play_capabilities`를 호출하면 `unity.registeredActions`에 액션 이름, 설명, 인자가 표시됩니다. `QA_Menu` 씬을 열려면 `perform_actions`에 아래처럼 요청합니다.

```json
{
  "actions": [
    { "method": "unity_action", "name": "load_test_scene", "params": ["QA_Menu"] }
  ]
}
```

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
