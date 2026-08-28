using System.Collections.Generic;
using Artel.Protocol;

namespace Artel
{
    /// <summary>
    /// 한 번의 <c>reset_game</c> 호출이 요구하는 것. JSON-RPC params 를 읽은 뒤의 모양이다.
    /// </summary>
    internal struct ResetRequest
    {
        /// <summary>씬 리로드에 더해 게임의 <c>PlayerPrefs</c> 도 비울지.</summary>
        public bool ClearPlayerPrefs;
    }

    /// <summary>
    /// <c>reset_game</c> params 를 읽는다. 모양: [] 또는 [options].
    /// </summary>
    internal static class ResetRequestReader
    {
        public static bool TryRead(List<object> parameters, out ResetRequest request, out string error)
        {
            request = new ResetRequest
            {
                ClearPlayerPrefs = false
            };
            error = null;

            // params 없는 호출은 이 flag 가 생기기 전의 서버가 보내는 모양 그대로다. 씬만
            // 다시 여는 예전 동작으로 받는다 — ACTION 프로토콜에는 버전 필드가 없으므로,
            // 여기서 거절하면 옛 서버가 통째로 막힌다.
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            if (parameters.Count > 1)
            {
                error = "reset_game params are [] or [options].";
                return false;
            }

            if (parameters[0] == null)
            {
                return true;
            }

            if (!ActionParamsObject.TryRead(parameters[0], out var options))
            {
                error = "reset_game options must be an object.";
                return false;
            }

            if (options.TryGetValue("clearPlayerPrefs", out var value) && value != null)
            {
                // bool 만 받는다. 파괴적인 flag 를 truthy 에서 강제 변환하면, 서버가 실수로
                // 보낸 문자열 "false" 조차 저장소를 비우는 명령이 된다. 되돌릴 수 없는 일에는
                // 관대한 파싱을 두지 않는다.
                if (!(value is bool clearPlayerPrefs))
                {
                    error = "reset_game clearPlayerPrefs must be true or false.";
                    return false;
                }

                request.ClearPlayerPrefs = clearPlayerPrefs;
            }

            // 모르는 필드는 무시한다 — CaptureRequestReader 와 같다. 서버가 필드를 먼저
            // 늘려도 옛 SDK 가 액션 전체를 거절하지 않는다.
            return true;
        }
    }
}
