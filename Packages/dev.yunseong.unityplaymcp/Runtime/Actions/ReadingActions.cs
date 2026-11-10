using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [UnityAction("start_readings", "Start live scene readings.", "[]")]
    internal sealed class StartReadingsAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecuteStartReadings(actionId));
            yield break;
        }

        private ActionResultDto ExecuteStartReadings(int actionId)
        {
            if (Context.Readings == null)
            {
                return ActionResultDto.Failure(actionId, "This build cannot take live readings.");
            }

            return Context.Readings.StartReadings()
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(
                    actionId, "Live readings could not start. A release build does not take them.");
        }
    }

    [UnityAction("stop_readings", "Stop live scene readings.", "[]")]
    internal sealed class StopReadingsAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecuteStopReadings(actionId));
            yield break;
        }

        private ActionResultDto ExecuteStopReadings(int actionId)
        {
            if (Context.Readings == null)
            {
                return ActionResultDto.Failure(actionId, "This build cannot take live readings.");
            }

            Context.Readings.StopReadings();
            return ActionResultDto.Success(actionId);
        }
    }
}
