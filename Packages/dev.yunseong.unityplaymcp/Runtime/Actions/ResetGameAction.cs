using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [UnityAction("reset_game", "Reload the scene that started the run.", "[{ clearPlayerPrefs?: boolean }]")]
    internal sealed class ResetGameAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            // params 를 Build Settings 확인보다 먼저 읽어야 잘못된 호출이 Build Settings 오류로 보고되지 않는다.
            if (!ResetRequestReader.TryRead(parameters, out var request, out var error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            if (Context.StartupSceneBuildIndex < 0)
            {
                // 경로를 알아도 Build Settings에 없는 씬은 로드할 수 없다.
                completed(ActionResultDto.Failure(
                    actionId,
                    "reset_game: the scene the game started in is not in Build Settings: " +
                    Context.StartupScenePath));
                yield break;
            }

            // 씬을 다시 열기 전에 정지 상태와 가상 입력을 해제한다.
            Context.RestoreTimeScale();
            Context.PointerEvents.ReleaseAll();
            VirtualInput.ReleaseAllVirtualInput();

            // 게임은 시작 씬의 Awake/Start 에서 세이브를 읽으므로 로드 전에 지운다. 사이에 yield 를 두지 않는다.
            if (request.ClearPlayerPrefs)
            {
                OwnedPlayerPrefs.DeleteAllExceptOwn();
            }

            DoomPersistentObjects();
            yield return SceneManager.LoadSceneAsync(Context.StartupSceneBuildIndex, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(ResetSettleSeconds);

            completed(ActionResultDto.Success(actionId));
        }

        private const float ResetSettleSeconds = 0.1f;

        // SDK가 코루틴과 소켓을 계속 소유하도록 호스트는 남기고, 다른 영속 오브젝트는 씬과 함께 내린다.
        private static void DoomPersistentObjects()
        {
            var doomed = SceneManager.GetActiveScene();
            var dropped = new List<string>();
            foreach (var root in StraySpawnTracker.DontDestroyOnLoadScene().GetRootGameObjects())
            {
                if (root.GetComponentInChildren<UnityPlayMcpHost>(true) != null)
                {
                    continue;
                }

                SceneManager.MoveGameObjectToScene(root, doomed);
                dropped.Add(root.name);
            }

            if (dropped.Count > 0)
            {
                // 시작 씬 밖에서 생성한 오브젝트는 복구되지 않을 수 있어 이름을 남긴다.
                Debug.Log("[Unity Play MCP] reset_game dropped persistent object(s): " +
                          string.Join(", ", dropped));
            }
        }
    }
}
