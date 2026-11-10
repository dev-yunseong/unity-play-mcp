using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [UnityAction("set_overlay", "Show or hide the agent overlay.", "[visible: boolean]")]
    internal sealed class OverlayAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count != 1 || !(parameters[0] is bool))
            {
                completed(ActionResultDto.Failure(actionId, "set_overlay requires params [visible: boolean]."));
            }
            else if (Context.SetOverlay == null)
            {
                completed(ActionResultDto.Failure(actionId, "Overlay control is unavailable."));
            }
            else
            {
                var visible = (bool)parameters[0];
                Context.SetOverlay(visible);
                completed(ActionResultDto.Success(actionId, visible));
            }
            yield break;
        }
    }
}
