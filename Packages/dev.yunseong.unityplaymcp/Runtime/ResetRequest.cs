using System.Collections.Generic;
using UnityPlayMcp.Protocol;

namespace UnityPlayMcp
{
    /// <summary>
    /// 파싱한 <c>reset_game</c> params 다.
    /// </summary>
    internal struct ResetRequest
    {
        /// <summary>씬 리로드와 함께 <c>PlayerPrefs</c> 도 비울지 여부다.</summary>
        public bool ClearPlayerPrefs;
    }

    /// <summary>
    /// <c>reset_game</c> params 를 읽는다. 모양은 [] 또는 [options] 다.
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

            // ACTION 프로토콜에 버전 필드가 없으므로 params 없는 옛 서버 호출은 씬 리로드만 한다.
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
                // 되돌릴 수 없는 flag 이므로 bool 만 받는다. 문자열 "false" 를 truthy 로 읽으면 저장소가 지워진다.
                if (!(value is bool clearPlayerPrefs))
                {
                    error = "reset_game clearPlayerPrefs must be true or false.";
                    return false;
                }

                request.ClearPlayerPrefs = clearPlayerPrefs;
            }

            // 서버가 필드를 먼저 늘려도 옛 SDK 가 거절하지 않도록 모르는 필드는 무시한다.
            return true;
        }
    }
}
