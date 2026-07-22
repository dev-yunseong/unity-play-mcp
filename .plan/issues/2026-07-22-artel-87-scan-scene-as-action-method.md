# 2026-07-22 — scan_scene을 ACTION 배치 메서드로 추가

- Date: 2026-07-22
- Jira Issue: ARTEL-87
- Status: Implemented
- Repository: sdk
- Work Type: feat

## Goal

`scan_scene`은 지금 최상위 메시지 경로에만 있다. `ArtelManager.HandleMessage`가 받는 즉시
`sceneStatePoller.ScanNow()`를 돌리고 그 커넥션으로 회신한다. 반면 액션은 `actionRequests` 큐에
쌓여 코루틴으로 실행되고, `button_click`/`enter_text`는 가상 커서를 옮기느라 여러 프레임을 쓴다.

두 경로가 갈라져 있어서 읽기가 쓰기를 추월한다:

1. 에이전트가 `ACTION`(`button_click`)을 보내고 `ACTION_RESULT`를 기다리지 않은 채 `scan_scene`을 보낸다.
2. 스캔은 `Update`의 메시지 처리 루프에서 즉시 응답한다 — 커서는 아직 이동 중이고 클릭은 일어나지 않았다.
3. 에이전트는 클릭 이전 씬을 클릭 이후 씬으로 오독한다.

`[ArtelAction]` 기록도 같은 문제를 겪는다. 스캔이 pending 액션 스냅샷을 소비하므로(`CommitActions`),
스캔이 먼저 끝나면 그 클릭이 유발한 액션 기록은 다음 스캔으로 밀린다.

`scan_scene`을 액션 배치 안에서도 받게 해서, 같은 큐 위에서 앞선 액션이 끝난 뒤에 스캔하도록 한다.

## Non-goals

- **기존 최상위 경로 제거.** 마이그레이션을 위해 `method == "scan_scene"`, `type == "SCAN_SCENE"`,
  `type == "GET_GAME_STATE"` 세 별칭을 모두 그대로 둔다. 서버와 에이전트가 새 경로로 옮겨간 뒤에
  별도 이슈로 정리한다.
- **`ActionResultDto` 스키마 변경.** 씬 페이로드를 액션 결과에 싣지 않는다. 아래 Approach 참고.
- **주기 폴링 변경.** `PollSceneState`는 그대로다.
- **`ActionExecutor`가 씬 전송을 알게 만들기.** 전송·직렬화·폴러는 `ArtelManager`의 책임이고,
  `ActionExecutor`는 씬 조작만 안다. 그 경계를 유지한다.

## Context / Constraints

- 배치는 `ExecuteActionRequest`에서 순차 실행된다. 앞 액션의 커서 이동이 끝나야 다음 액션이 시작한다.
  스캔을 이 루프 안에 넣으면 순서 보장은 공짜로 얻는다.
- `ActionResultDto`는 `{id, success, error}`뿐이라 씬을 담을 자리가 없다. 필드를 넓히는 대신
  **`GAME_STATE` 메시지를 그대로 먼저 보내고** 액션 결과에는 성공만 남긴다. 이러면 서버의 씬 파싱
  경로가 하나로 유지되고(폴링 push와 동일 타입), 기존 소비자 코드가 갈라지지 않는다.
- 응답 순서는 `GAME_STATE` → `ACTION_RESULT`. 배치에 스캔이 여러 개면 `GAME_STATE`도 그만큼 나간다.
- 배치 경로는 `webSocketTransport.Send`를 쓴다. 최상위 경로의 `message.Reply`와 달리 요청 메시지를
  들고 있지 않기 때문이고, `ACTION_RESULT` 전송과도 같은 채널이다.
- 전송이 없으면(`webSocketTransport == null`) 스냅샷을 커밋하지 않는다. README가 명시한
  "전송 실패 시 스냅샷 전체를 pending으로 남긴다" 규칙과 맞춘다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `ArtelManager.HandleMessage` / `ExecuteActionRequest` / `SendGameState`,
      `ActionExecutor.Execute`의 메서드 디스패치 확인.
- [x] **Step 1: Implementation**
  - `ArtelManager.ExecuteActionRequest`에서 `action.Method == "scan_scene"`을 `ActionExecutor`에
    넘기기 전에 처리한다. `SendGameState()` 후 `ActionResultDto.Success(action.Id)` 기록.
  - 기존 `SendGameState(ArtelWebSocketMessage)`를 `ReplyWithGameState(message)`로 이름만 바꾸고,
    전송 채널로 보내는 무인자 `SendGameState()`를 추가한다.
  - `ActionExecutor`는 손대지 않는다 — `scan_scene`은 거기 도달하지 않으므로
    `"Unsupported method: scan_scene"`은 더 이상 발생하지 않는다.
- [x] **Step 2: Tests** — `ActionBatchTests.cs` 신규.
  - `[button_click, scan_scene]` 배치가 `GAME_STATE`를 먼저, `ACTION_RESULT`를 뒤에 보낸다.
  - 그 `GAME_STATE`가 클릭이 반영된 상태를 담는다.
  - 최상위 `scan_scene` 경로가 그대로 동작한다(회귀 방지).
- [x] **Step 3: Rollout / Rollback** — 순수 추가 변경. 새 경로를 쓰지 않는 클라이언트는 영향 없음.
      되돌리려면 커밋 하나를 revert하면 된다.

## Validation

- **Commands to run:** Unity Test Runner (EditMode) — `Artel.Runtime.Tests` 어셈블리.
- **Expected output:** 신규 3개 포함 전체 통과.
- **실제 수행:** 이 작업 환경에 Unity 에디터가 없어 테스트를 실행하지 못했다. 테스트는 기존
  `CursorControllerTests`의 `Drain` 패턴(코루틴을 직접 소진)과 `WebSocketTransportTests`의
  리플렉션 라이프사이클 호출 패턴을 그대로 따르므로 컴파일·실행 가능성은 높지만 **미검증**이다.
  리뷰어 또는 후속 실행에서 Test Runner를 돌려야 한다.

## Risks & Rollback

- **Risks:**
  - 서버가 `GAME_STATE`를 "폴링/스캔 응답"으로만 가정하고 `ACTION_RESULT` 직전에 오는 경우를
    처리하지 못할 수 있다. 타입은 동일하므로 파싱은 깨지지 않고, 순서 가정만 확인하면 된다.
  - 배치 안 스캔은 큐에 줄을 서므로 앞선 커서 이동만큼 지연된다. 의도된 동작이다.
  - Unity 테스트 미실행 — 위 Validation 참고.
- **Rollback steps:** `git revert`.

## Open Questions

- 최상위 경로 세 별칭을 언제 제거할지. 서버·에이전트 마이그레이션 완료 후 별도 이슈.
