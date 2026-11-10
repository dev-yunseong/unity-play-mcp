using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [UnityAction("pause_time", "Pause game time.", "[]")]
    internal sealed class PauseTimeAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecutePauseTime(actionId));
            yield break;
        }

        private ActionResultDto ExecutePauseTime(int actionId)
        {
            // 두 번째 pause에서 0을 저장하면 resume 뒤에도 게임이 멈춘 상태로 남는다.
            if (!Context.ScaleBeforePause.HasValue)
            {
                Context.ScaleBeforePause = Time.timeScale;
            }

            Time.timeScale = 0f;
            return ActionResultDto.Success(actionId);
        }
    }

    [UnityAction("resume_time", "Restore the time scale saved by pause_time.", "[]")]
    internal sealed class ResumeTimeAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecuteResumeTime(actionId));
            yield break;
        }

        private ActionResultDto ExecuteResumeTime(int actionId)
        {
            if (!Context.ScaleBeforePause.HasValue)
            {
                // 저장한 값이 없으면 게임의 timeScale을 그대로 둔다.
                return ActionResultDto.Failure(
                    actionId, "resume_time: game time was not paused by pause_time.");
            }

            Time.timeScale = Context.ScaleBeforePause.Value;
            Context.ScaleBeforePause = null;
            return ActionResultDto.Success(actionId);
        }
    }
}
