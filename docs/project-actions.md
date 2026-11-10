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

## 기본 액션 목록

`perform_actions`에서 아래 이름을 사용할 수 있습니다. 현재 교체를 지원하는 기본 액션은 **`set_button`**입니다.

| 호출 이름 | `BuiltInAction` 값 | 하는 일 | 교체 |
| --- | --- | --- | --- |
| `move_mouse` | `MoveMouse` | 화면 좌표로 포인터 이동 | — |
| `mouse_down` | `MouseDown` | 마우스 버튼 누르기 | — |
| `mouse_up` | `MouseUp` | 마우스 버튼 떼기 | — |
| `enter_text` | `EnterText` | 입력 필드에 텍스트 입력 | — |
| `key_click` | `KeyClick` | 키를 잠시 눌렀다 떼기 | — |
| `key_down` | `KeyDown` | 키 누르기 | — |
| `key_up` | `KeyUp` | 키 떼기 | — |
| `set_axis` | `SetAxis` | Input Manager 축 값 설정 | — |
| `set_button` | `SetButton` | 이름으로 버튼을 누르거나 떼기 | 지원 |
| `pause_time` | `PauseTime` | 게임 시간 멈추기 | — |
| `resume_time` | `ResumeTime` | 멈추기 전의 시간 배율로 복구 | — |
| `reset_game` | `ResetGame` | 시작 씬 다시 불러오기 | — |
| `start_readings` | `StartReadings` | 실행 중 장면 상태 수집 시작 | — |
| `stop_readings` | `StopReadings` | 장면 상태 수집 중단 | — |
| `capture_screen` | `CaptureScreen` | 전체 화면이나 오브젝트 영역 캡처 | — |
| `set_overlay` | `SetOverlay` | 에이전트 입력 오버레이 표시 또는 숨기기 | — |

## 기본 버튼 동작 교체하기

게임에 자체 입력 시스템이 있다면 `IUnityAction`을 구현한 클래스에 `[OverrideUnityAction(BuiltInAction.SetButton)]`을 붙이세요. 같은 `set_button` 호출이 게임 구현으로 전달됩니다. 아래 예제에서는 게임의 입력 코드가 `GameButtonAction.Changed`를 구독해 버튼 상태를 받습니다.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp;
using UnityPlayMcp.Protocol.Dto;

[OverrideUnityAction(BuiltInAction.SetButton)]
public sealed class GameButtonAction : IUnityAction
{
    public static event Action<string, bool> Changed;

    public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed)
    {
        if (parameters.Count != 2 || !(parameters[0] is string name) || !(parameters[1] is bool pressed))
        {
            completed(ActionResultDto.Failure(id, "Expected a button name and pressed state."));
            yield break;
        }

        Changed?.Invoke(name, pressed);
        completed(ActionResultDto.Success(id));
    }
}
```

`set_button` 도구에 `{"name":"Jump","pressed":true}`를 보내면 게임 구현이 `Jump` 누르기를 받습니다. 떼기는 `pressed: false`로 보냅니다. `perform_actions`에서도 기존처럼 `{"method":"set_button","name":"Jump","pressed":true}`를 사용합니다. 게임이 직접 관리하는 눌림 상태는 연결이 끊겼을 때도 게임 쪽에서 정리하세요.

같은 대상을 교체하는 클래스가 여러 개이거나 교체 인스턴스를 만들 수 없으면 기본 구현이 실행됩니다. 지원하지 않는 enum 값을 지정하면 Unity Console에 경고가 표시됩니다.
