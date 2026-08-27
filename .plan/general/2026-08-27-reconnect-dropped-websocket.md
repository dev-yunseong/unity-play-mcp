# 2026-08-27 — 끊긴 웹소켓이 다시 붙게 한다

- Date: 2026-08-27
- Jira: ARTEL-599
- Status: Approved (self-review fast/medium/heavy 통과)

## Goal

소켓이 끊긴 뒤 SDK 가 스스로 다시 붙게 한다. 세 가지를 함께 고친다.

1. `ArtelWebSocketClient.Start()` 가 죽은 소켓 위에서 재연결할 수 있게 한다.
2. 예기치 않은 종료에 대해 닫힘 코드를 보고 지수 backoff 로 자동 재시도한다.
3. 유휴 구간에 ping 을 보내, 중간 프록시가 조용한 연결을 끊는 일 자체를 줄인다.

덧붙여, 새 연결이 열리면 `SceneStatePoller` 의 해시를 비워 새 서버 세션이 `GAME_STATE` 를
한 번 받게 한다.

## Non-goals

- 서버(`orchestration-server`) 와 프록시 설정 변경.
- 끊긴 동안 보내려던 메시지의 큐잉과 재전송.
- 스트리밍(`ArtelStreamHost`) 재협상.
- `ArtelWebSocketServer`(로컬 테스트 페이지) 의 동작 변경.

## Context / Constraints

- 관측된 증상: `[Artel] WebSocket closed: code=1005 reason=` — 닫힘 코드가 없는 close frame.
  서버 `SdkWebSocketHandler` 는 거절할 때 항상 4001 또는 4002 를 붙이므로 인증 거절이 아니다.
- `OnClose` 는 websocket-sharp 수신 스레드에서 온다. 재연결 스케줄링이 Unity API 를 만지면 안 된다.
- `WebSocket.Ping()` 은 pong 을 최대 `WaitTime`(기본 5초) 동안 기다리며 블로킹한다. 메인 스레드에서
  부르면 안 된다.
- `WebSocket` 의 `Dispose` 는 명시적 구현이라 `((IDisposable)socket).Dispose()` 로 불러야 하고,
  닫힘 코드 1001(Away) 로 닫는다.
- `EnableModernTls` 가 붙이는 TLS 1.2 는 소켓 인스턴스마다 다시 붙여야 한다.
- 토큰과 instanceId 는 생성자에서 `url` 로 굳는다. 재연결은 같은 URL 을 쓴다.
- 이 저장소는 Unity 프로젝트가 아니다. 테스트는 `.github/scripts/setup-unity-test-project.sh` 가
  만드는 임시 프로젝트에서 돈다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 확인 완료.
  - `Packages/kr.artel.sdk/Runtime/ArtelWebSocketClient.cs` — `Start()` 의 `client != null` 조기
    반환, `OnClose` 의 로그 전용 처리.
  - `Packages/kr.artel.sdk/Runtime/ArtelManager.cs:334` — `StartTransport` 가 `Start()` 를 부른다.
  - `Packages/kr.artel.sdk/Runtime/Tracking/SceneStatePoller.cs:35` — `Reset` 이 해시를 비운다.
  - `Packages/kr.artel.sdk/Tests/Runtime/WebSocketTransportTests.cs:236` — 기존 테스트가
    `internal static` 헬퍼를 직접 부른다. 재시도 정책도 같은 모양으로 노출하면 붙일 수 있다.

- [ ] **Step 1: 재시도 정책을 순수 함수로**
  - `ArtelWebSocketClient` 안에 `internal static bool TryReconnectDelay(ushort closeCode,
    int attempt, out TimeSpan delay)` 를 둔다. `BuildEndpoint` 와 `EnableModernTls` 가 이미
    같은 모양으로 테스트에 노출돼 있으므로 새 파일을 만들지 않고 그 패턴을 따른다.
  - 4001 은 언제나 false. 그 밖의 코드는 1초에서 시작해 두 배씩, 30초에서 멈춘다.
    8회를 넘으면 false — 누적 약 2분이다. 그 뒤로는 오버레이의 연결 버튼이 수동 경로로 남고,
    Step 2 가 그 버튼을 실제로 동작하게 만든다.
  - 시간에 의존하지 않는다. 테스트가 값을 그대로 검증한다.

- [ ] **Step 2: `ArtelWebSocketClient` 재연결**
  - `Start()` 의 조기 반환 조건을 `client != null` 에서 "살아 있는 소켓이 있을 때" 로 바꾼다
    (`Connecting` 또는 `Open`). 죽은 소켓은 버리고 새로 만든다.
  - 소켓 생성·핸들러 연결·TLS 설정을 `Connect()` 하나로 모아 최초 연결과 재연결이 같은 길을 쓴다.
  - `OnClose` 에서 정책에 물어 재시도를 `System.Threading.Timer` 로 예약한다. Unity API 를 부르지
    않는다.
  - `Stop()` 이 부른 종료는 `stopping` 플래그로 걸러 재시도하지 않는다.
  - 시도 횟수는 `OnOpen` 이 아니라 **연결이 충분히 오래 살아 있었을 때만** 0으로 되돌린다.
    서버는 중복 인스턴스를 핸드셰이크 뒤에 4002 로 끊으므로 `OnOpen` 은 그 경우에도 불린다.
    거기서 되돌리면 즉시 끊기는 연결이 시도 횟수를 영원히 0으로 유지해 재시도가 끝나지 않는다.
    `Stopwatch` 로 연결이 열려 있던 시간을 재고, 60초를 넘겼을 때만 건강한 세션으로 보고 되돌린다.
  - 소켓이 보낸 이벤트가 현재 소켓의 것인지 확인한다. 버려진 소켓의 늦은 `OnClose` 가 살아 있는
    연결의 재시도를 예약하면 안 된다.
  - 상태 전이는 하나의 `lock` 아래에서만 한다. 타이머 스레드, 수신 스레드, 메인 스레드가 함께
    닿는다.

- [ ] **Step 3: keepalive ping**
  - `OnOpen` 에서 주기 타이머를 켜고 `OnClose` 와 `Stop()` 에서 끈다.
  - 주기는 30초. `WaitTime` 기본값 5초보다 충분히 길어 타이머가 밀리지 않는다.
  - 콜백은 소켓이 `Open` 일 때만 `Ping()` 한다. 실패는 경고 로그로 남기고 강제로 끊지 않는다.
    pong 이 한 번 늦은 것과 연결이 죽은 것은 다르고, 후자는 어차피 `OnClose` 로 온다.
  - `Ping()` 은 블로킹이므로 `lock` 을 쥔 채 부르지 않는다. 소켓 참조만 `lock` 안에서 읽고 나온다.

- [ ] **Step 4: 새 연결에서 `GAME_STATE` 재전송**
  - `ArtelManager.Update` 가 `IsConnected` 의 상승 edge 를 보고 `sceneStatePoller.Reset` 을 부른다.
  - 전송 인터페이스는 건드리지 않는다. edge 판정이 메인 스레드에서만 일어나므로 스레드 문제도 없다.

- [ ] **Step 5: Tests**
  - `WebSocketTransportTests` (EditMode) 에 `TryReconnectDelay` 테스트를 더한다 — 4001 거절,
    backoff 수열, 30초 상한, 8회 초과 거절.
  - 같은 파일에 `Start()` 재진입 테스트를 더한다. 죽은 소켓이 다음 `Start()` 를 막지 않는 것.
  - 연결이 오래 살아 있었을 때만 시도 횟수를 되돌리는 규칙은 시계에 의존하므로 단위 테스트하지
    않는다. 수동 확인으로 남기고 PR 에 그대로 적는다.

- [ ] **Step 6: Rollout / Rollback**
  - 플래그 없이 나간다. 되돌리기는 `git revert` 한 번.

## Validation

- **Commands to run:**
  ```bash
  .github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test
  ```
  이어서 EditMode 와 PlayMode 를 각각 `-runTests` 로 돌리고
  `python3 .github/scripts/summarize-test-results.py` 로 읽는다.
- **Expected output:** 두 스위트 모두 green. 변경 전 merge-base 에서 기준선을 먼저 잡는다.

## Risks & Rollback

- **Risks:**
  - 재시도가 서버를 두드릴 수 있다. 상한 30초와 시도 횟수 제한으로 묶는다.
  - `Ping()` 이 타이머 스레드를 최대 5초 잡는다. 타이머 주기가 그보다 훨씬 길어야 한다.
  - 죽은 소켓의 늦은 `OnClose` 가 새 연결의 재시도를 유발할 수 있다. 이벤트를 보낸 소켓이 현재
    소켓인지 확인해 거른다.
  - Unity 에디터 환경에 Unity 가 설치돼 있지 않으면 테스트를 돌릴 수 없다. 그 경우 돌리지 못했음을
    PR 에 그대로 적는다.
- **Rollback steps:** `git revert`.

## Rejected feedback

- **재시도 정책을 별도 파일 `WebSocketReconnectPolicy.cs` 로 분리하자** — 하지 않는다. 이 저장소는
  테스트가 필요한 순수 헬퍼를 `ArtelWebSocketClient` 의 `internal static` 으로 두는 패턴을 이미
  쓰고 있다(`BuildEndpoint`, `EnableModernTls`). 함수 하나를 위해 타입을 새로 만들면 그 패턴만
  갈라진다.
- **ping 이 실패하면 즉시 끊고 재연결하자** — 하지 않는다. pong 한 번이 늦는 것과 연결이 죽은 것은
  다르다. 정말 죽었다면 `OnClose` 가 온다.

## Open Questions

- 없음. 시도 횟수 상한은 8회로 정했다.
