using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlayMcp;
using UnityPlayMcp.Protocol.Dto;

// 씬이 게임 어셈블리를 로드한다. 액션 타입을 직접 참조하는 코드는 두지 않는다.
public sealed class ActionSmokeScene : MonoBehaviour
{
    [UnityAction("smoke_echo", "Echo a value from the game assembly.", "[value: string]")]
    private sealed class SmokeEchoAction : IUnityAction
    {
        private SmokeEchoAction() { }

        public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ActionResultDto.Success(id, "player:" + (string)parameters[0]));
            yield break;
        }
    }

    [OverrideUnityAction(BuiltInAction.SetButton)]
    private sealed class SmokeSetButtonAction : IUnityAction
    {
        private SmokeSetButtonAction() { }

        public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed)
        {
            var pressed = (bool)parameters[1];
            completed(ActionResultDto.Success(id, (pressed ? "press:" : "release:") + (string)parameters[0]));
            yield break;
        }
    }
}
