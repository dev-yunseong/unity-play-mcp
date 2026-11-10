# 게임 전용 액션 만들기

테스트 씬 열기처럼 내 게임에서 자주 쓰는 동작을 에이전트가 호출할 수 있게 만들 수 있습니다. 게임 프로젝트에 C# 클래스 하나를 추가하면 됩니다.

## 1. 게임 프로젝트에 액션 클래스 추가하기

게임 프로젝트의 `Assets` 아래에 `LoadTestSceneAction.cs`를 만들고 아래 코드를 넣으세요. 자체 `.asmdef`를 사용한다면 해당 어셈블리에 `UnityPlayMcp.Runtime` 참조를 추가하세요.

`IUnityAction`을 구현하고 `[UnityAction]`에 이름, 설명, 인자 순서를 적습니다. 예제는 Build Settings에 포함된 씬을 열고 로딩이 끝나면 씬 이름을 반환합니다.

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

## 2. Play Mode에서 실행하기

Play Mode에서 에이전트에게 “`load_test_scene` 액션으로 `QA_Menu` 씬을 열어 줘”라고 요청하세요. `QA_Menu`는 프로젝트의 Build Settings에 포함된 씬 이름으로 바꿔 쓰면 됩니다.

MCP를 직접 호출한다면 먼저 `get_play_capabilities`의 `unity.registeredActions`에서 액션 이름, 설명, 인자를 확인하세요. 그다음 아래 입력을 `perform_actions`에 보냅니다.

```json
{
  "actions": [
    { "method": "unity_action", "name": "load_test_scene", "params": ["QA_Menu"] }
  ]
}
```
