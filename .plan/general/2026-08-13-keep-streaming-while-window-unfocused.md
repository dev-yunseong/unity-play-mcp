# 2026-08-13 — 창이 포커스를 잃어도 스트리밍이 유지되게 한다

- Date: 2026-08-13
- Jira: ARTEL-389 (상위 umbrella ARTEL-387)
- Status: Reviewed (fast/medium 1차 → 개정 → heavy 2차 PASS)

## Goal

게임 창이 포커스를 잃어도 스트림과 성능 보고가 살아 있게 한다. 세 가지를 고친다.

1. **프레임 루프가 멈추는 것** — SDK 어디에서도 `Application.runInBackground`를 켜지 않는다. Player
   Settings 기본값이 꺼짐이면 포커스를 잃는 순간 `Update()`가 멈추고, 그 안에서 도는
   `ArtelManager.PumpStreaming()`(= `WebRTC.Update()` 인코딩 펌프 + `streamHost.Tick()`),
   `ScreenVideoSource`의 `WaitForEndOfFrame` 캡처 루프, 그리고 웹소켓 수신 큐 배수가 함께 멈춘다.
2. **복귀 시점에 임대가 즉사하는 것** — `StreamLease`는 절대 시각(`Time.unscaledTime`)으로 마감을
   판정한다. 프로세스가 멈춰 있던 동안 흐른 벽시계 시간이 첫 Tick에 한꺼번에 실려 임대가 이미 만료된
   것으로 읽히고, 돌아오자마자 세션이 무너진다.
3. **성능 보고가 사라지는 것** — 프레임 레코더가 `Application.isFocused == false`인 프레임을 모두
   버린다. `runInBackground`로 루프가 계속 돌아도 집계할 샘플이 없어 PERFORMANCE를 보내지 않는다.
   백그라운드 프레임도 기록하고 기존 `status.isFocused`로 포커스 상태를 보존한다.

## Non-goals

- 갱신 주기·임대 시간 변경 (orchestration-server / ARTEL-388)
- TURN 도입, ICE 재시작
- 모바일 백그라운드에서 웹소켓 재연결 — 앱이 정지되면 소켓도 끊긴다. 이 작업은 "정지되었다 돌아온
  프레임에서 임대가 살아 있게" 하는 데까지만 책임진다
- 데드맨 타이머를 "만료되지 않게" 만드는 것. 뷰어가 실제로 사라지면 게임은 여전히 스스로 멈춰야 한다

## Context / Constraints

**SDK는 고객사 게임 빌드 안에 실린다.** `Application.runInBackground`는 호스트 게임의 전역 설정이다.
런타임에서 켜는 것은 SDK가 남의 게임 동작을 바꾸는 일이므로, 적용 범위를 최소로 잡고 그 사실을 코드
주석과 README에 남긴다 (이슈 AC).

**웹소켓 수신은 별도 스레드지만 처리는 `Update()`에서 한다.** `ArtelWebSocketClient`는
websocket-sharp 콜백에서 `ConcurrentQueue`에 넣고, `ArtelManager.Update()`가 `TryDequeueMessage`로
배수한다. 즉 포커스를 잃은 동안 메시지는 쌓이기만 하고 하나도 처리되지 않는다. 이것이 적용 범위
결정의 핵심이다 — 아래 참조.

**`Time.unscaledTime`은 프레임 루프가 도는 동안만 신뢰할 수 있다.** 데스크톱에서 창이 스텝을 멈추거나
모바일에서 OS가 앱을 정지시키면, 다시 도는 첫 프레임의 delta에 자리비운 시간 전체가 실린다.
`runInBackground`는 standalone/desktop 전용이라 모바일 정지는 덮지 못한다. 그래서 시간 점프 방어는
플랫폼 콜백이 아니라 임대 자체에 있어야 한다.

**임대 판정 흐름** (`ArtelManager.Update` → `PumpStreaming` → `ArtelStreamHost.Tick(now)` →
`IArtelStreamSession.HasExpired(now)` → `StreamLease.HasExpired(now)`). `HasExpired`는 프레임당
정확히 한 번, Tick에서만 호출된다. `Renew`는 STREAM_START/STREAM_RENEW 처리 시점에 같은 시계로
호출된다.

**`ScreenVideoSource`는 손대지 않아도 된다.** `nextCaptureTime`은 지나간 시각이 되므로 시간이 점프하면
즉시 한 프레임을 캡처하고 다시 정상 페이싱으로 돌아온다. 잘못된 상태로 남지 않는다.

### 결정 1 — `Application.runInBackground`를 어디서, 어느 범위로 켜는가

| 안 | 범위 | 판단 |
|---|---|---|
| A. SDK 초기화(`Awake`) 시 1회 | 프로세스 전체 수명 | 과하다. 개발 빌드에서는 `ArtelManager`가 씬에 없어도 자동 생성되므로, Artel에 한 번도 연결하지 않는 실행까지 호스트 게임의 동작이 바뀐다 |
| B. 스트림 세션이 살아 있는 동안만 | STREAM_START ~ 세션 종료 | **동작하지 않는다.** STREAM_START는 `Update()`에서 큐를 배수해야 처리된다. QA가 브라우저로 넘어간 순간 배수가 멈추므로 세션을 시작시킬 메시지 자체가 처리되지 않는다. 세션이 생겨야 켜지는데, 켜져 있어야 세션이 생긴다 |
| **C. 트랜스포트가 연결되어 있는 동안** | `StartTransport()` ~ `StopTransport()` | **채택.** 실제로 동작하는 것 중 가장 좁은 범위다. 원격 조작을 받겠다고 소켓을 연 게임만 백그라운드에서 돈다. 연결하지 않는 빌드는 자기 Player Settings 그대로다 |

C를 택하고, 켜기 직전 값을 기억했다가 `StopTransport()`에서 되돌린다. 호스트 게임이 원래 켜 두었다면
연결이 끝나도 켜진 채로 남는다.

적용 지점은 `webSocketTransport.Start()` / `webSocketTransport.Stop()` 바로 옆이다. 두 호출은 같은
`ownsTransport` 가드 안에 있어 짝이 어긋나지 않는다. 주입된 트랜스포트(`SetWebSocketTransport`,
`ArtelTestPageManager`) 경로는 건드리지 않는다 — 로컬 테스트 페이지는 같은 머신에서 열리고, 남의
컴포넌트가 소유한 연결을 근거로 전역 설정을 바꾸지는 않는다.

### 결정 2 — 시간 점프 방어

임대를 **절대 마감 시각**에서 **남은 시간 카운트다운**으로 바꾸고, 한 프레임이 소진할 수 있는 시간에
상한을 둔다.

```
elapsed = clamp(now - lastSampleTime, 0, MaxCountedFrameSeconds)
remaining -= elapsed
expired = remaining <= 0
```

- 정상 주행: 프레임 delta가 상한보다 훨씬 작으므로 기존과 동일하게 벽시계 그대로 소진한다
- 정지 후 복귀(모바일 백그라운드, 창이 스텝을 멈춘 구간): 몇 분이 흘렀어도 한 프레임에 상한치만
  소진한다. 임대는 남은 시간을 그대로 들고 살아난다
- **데드맨 성질 유지**: 복귀 후에도 갱신이 오지 않으면 남은 시간이 정상 속도로 줄어 임대 만료로
  세션이 끊긴다. "절대 만료되지 않음"이 아니라 "정지된 시간은 대기 시간으로 세지 않음"이다
- 프레임이 실제로 느린 게임(1fps 수준): delta가 상한에 걸려 프레임당 상한치를 소진하므로 벽시계에
  근사하게 만료된다. 이것이 "점프분을 통째로 면제"하는 안보다 나은 이유다 — 면제 방식은 저프레임
  게임에서 임대가 영원히 살아남는다

대안으로 검토하고 버린 것:

- **`OnApplicationFocus`/`OnApplicationPause` 재무장 훅** — 플랫폼별로 호출 보장이 다르고, 콜백 없이
  메인 스레드만 오래 멈추는 경우(로딩, 에디터 일시정지)를 못 잡는다. Unity 콜백이라 EditMode
  테스트에서 구동할 수도 없다. 임대 안에서 delta로 처리하면 플랫폼 독립적이고 순수 C#으로 검증된다
- **`DateTime.UtcNow` 기준으로 바꾸기** — 벽시계는 정지 구간을 더 정확히 세므로 문제를 악화시킨다

**상한값 `1f`.** 정상 프레임(≤33ms)보다 한참 크고, 서버가 주는 임대(15s 관측)보다 한참 작다. 한 번의
스톨이 임대를 죽이지 못하면서, 저프레임 구간에서도 만료가 과도하게 늦춰지지 않는다.

**기존 테스트 2건은 수정이 필요하다.** `Tick_TearsDownTheSessionWhenTheLeaseExpires`와
`Renew_PushesTheLeaseDeadlineOut`은 0초에서 15초로 한 번에 점프하며 Tick한다. 그 점프가 바로 이 작업이
"프레임이 아니라 정지"라고 판정하는 입력이다. 두 테스트는 프레임 크기(0.25s)로 시간을 전진시키는
헬퍼를 쓰도록 고쳐 쓴다. 단언(임대 시간 전에는 살아 있고, 지나면 죽는다)은 그대로 두고 시간을 흘리는
방식만 실제 프레임 루프에 맞춘다. 이는 검증 약화가 아니라 의도한 계약 변경이며 PR에 명시한다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료
  - `Runtime/ArtelManager.cs:199` `Update()`, `:223` `StartTransport()`, `:256` `StopTransport()`,
    `:529` `PumpStreaming()`
  - `Runtime/Streaming/StreamLease.cs` — 마감 시각 1개짜리 데드맨 타이머
  - `Runtime/Streaming/ArtelStreamHost.cs:87` `Tick`
  - `Runtime/Streaming/ScreenVideoSource.cs:122` 캡처 루프 (변경 불필요)
  - `Tests/Runtime/ArtelStreamHostTests.cs` — 진짜 `StreamLease`를 쓰는 fake 세션

- [ ] **Step 1: `StreamLease`를 delta 기반 카운트다운으로**
  - 파일: `Runtime/Streaming/StreamLease.cs`
  - `float deadline` → `float remainingSeconds` + `float lastSampleTime`. 둘 다 기본값 `0f`로 두고
    생성자에서 따로 초기화하지 않는다 (C# 필드 기본값). 갱신 전 상태가 "이미 만료"인 것은 지금
    `deadline = 0`이 만드는 동작과 같고, `ArtelStreamSession.Start()`가 항상 먼저 `Renew`한다
  - `private const float MaxCountedFrameSeconds = 1f`
  - `Renew(currentTime)`: `remainingSeconds = durationSeconds`, `lastSampleTime = currentTime`
  - `HasExpired(currentTime)`:
    ```csharp
    var elapsed = Math.Min(Math.Max(currentTime - lastSampleTime, 0f), MaxCountedFrameSeconds);
    lastSampleTime = currentTime;
    remainingSeconds -= elapsed;
    return remainingSeconds <= 0f;
    ```
  - 주석: 왜 상한이 있는지(정지된 프로세스는 갱신을 읽을 기회가 없었다), 왜 면제가 아니라 상한인지,
    그리고 **`HasExpired`가 프레임당 한 번(`ArtelStreamHost.Tick`) 샘플링되는 호출이라 경과 시간을
    소비한다는 계약**. 질의가 상태를 바꾸는 모양이지만, 호출자가 하나뿐이고 그 하나가 프레임 루프라
    "advance/query" 두 메서드로 쪼개는 것은 호출 지점 하나짜리 분리다. 주석으로 계약을 못박는 쪽을
    택한다
  - `UnityEngine` 의존은 넣지 않는다 (`System.Math`로 clamp). 순수 C#이라 EditMode에서 그대로 돈다

- [ ] **Step 2: `ArtelManager`에서 `Application.runInBackground`**
  - 파일: `Runtime/ArtelManager.cs`
  - `ArtelManager` 클래스 필드 추가: `private bool hostRunInBackground;` (`webRtcPump` 옆)
  - 저장·켜기는 **`StartTransport()`의 클라이언트 생성 블록 안**, `ownsTransport = true;` 바로 뒤에
    둔다. 복구는 `StopTransport()`에서 `webSocketTransport`를 `null`로 되돌린 직후에 둔다. 즉
    소유한 클라이언트의 수명과 정확히 같은 범위다
  - **`webSocketTransport.Start()` 옆이 아니라 생성 블록 안인 이유**: `StartTransport()`는 이미
    연결된 상태에서 다시 불릴 수 있다. 오버레이 고급 섹션의 `연결` 버튼이
    `ArtelOverlayController.ConnectWebSocket()`(`:301`) → `StartTransport`로 이어지고,
    `ArtelOverlayViewModel.CanConnect`는 `Connected` 상태를 배제하지 않는다. 그때
    `webSocketTransport != null`이라 생성은 건너뛰지만 `webSocketTransport.Start()` 줄에는 도달하고,
    `ArtelWebSocketClient.Start()`는 `client != null`이면 조용히 반환한다. `Start()` 옆에서
    저장했다면 두 번째 누름이 **우리가 켜 둔 `true`를 호스트의 원래 값으로 기억**해 버려, 연결이
    끝나도 고객사 게임에 켜진 채로 남는다. 생성 블록 안에 두면 저장이 클라이언트당 한 번뿐이라 별도
    플래그가 필요 없다
  - 주입된 트랜스포트는 자동으로 제외된다: 생성 블록은 `webSocketTransport == null`일 때만 돌고,
    주입 경로(`SetWebSocketTransport(transport, false)`)는 그 안으로 들어오지 않는다. 복구 지점도
    `!ownsTransport` 가드(`:280`) 뒤라 짝이 어긋나지 않는다. 새 조건문이 필요 없다
  - 주석(영문, 파일의 기존 목소리): 이것이 호스트 게임의 전역 설정이라는 것, 연결 수명으로 범위를
    한정한 이유, 모바일에서는 효과가 없어 임대 쪽 방어가 따로 필요하다는 것

- [ ] **Step 3: 테스트**
  - 파일: `Tests/Runtime/ArtelStreamHostTests.cs`
  - `FakeStreamSession`은 손대지 않는다. 진짜 `StreamLease`에 위임하므로 새 동작이 그대로 실린다
  - 헬퍼: `private static void TickFrames(ArtelStreamHost host, float fromSeconds, float toSeconds)`
    — `FrameSeconds = 0.25f`씩 **누적 절대 시각**으로 `host.Tick(time)`을 호출한다
    (`for (var time = from + FrameSeconds; time <= to; time += FrameSeconds) host.Tick(time);`).
    반환값은 두지 않는다 — 각 테스트가 다음 구간의 시작 시각을 스스로 적는다. 프레임 루프가 절대
    시각을 넘기는 실제 호출 규약과 같다
  - 기존 2건 수정 (단언은 유지, 시간 전진 방식만 프레임 단위로)
    - `Tick_TearsDownTheSessionWhenTheLeaseExpires`: `TickFrames(host, 0f, LeaseSeconds - 1f)` 후
      살아 있고 → `TickFrames(host, LeaseSeconds - 1f, LeaseSeconds + 1f)` 후 죽는다
    - `Renew_PushesTheLeaseDeadlineOut`: 0→10s 프레임 전진, 10s에 STREAM_RENEW, `LeaseSeconds`
      시점까지 전진해도 살아 있고, `10 + LeaseSeconds`를 넘겨 전진하면 죽는다
  - 신규 `Tick_DoesNotExpireOnTheFrameThatResumesFromASuspendedApp`:
    `TickFrames(host, 0f, 0.5f)` → `host.Tick(600.5f)` 한 번 → `HasLiveSession` 참
  - 신규 `Tick_StillExpiresAfterResumeWhenNoRenewalArrives`: 위와 같이 점프시킨 뒤
    `TickFrames(host, 600.5f, 600.5f + LeaseSeconds + 1f)` → `HasLiveSession` 거짓.
    **데드맨 성질이 유지된다는 단언이 이 작업의 핵심 회귀 방어다**
  - 신규 `Renew_AfterResumeKeepsTheSessionAlive`: 점프 후 STREAM_RENEW(`600.5f`) →
    `TickFrames(host, 600.5f, 600.5f + LeaseSeconds - 1f)` 에도 살아 있다
  - `Application.runInBackground` 자체는 테스트하지 않는다. Unity 전역 상태를 EditMode에서 토글하면
    테스트 간 격리가 깨지고(테스트 규칙: mutable global state 금지), 검증 대상도 Unity의 동작이다.
    수동 확인 항목으로 남긴다

- [ ] **Step 4: 문서**
  - `Packages/kr.artel.sdk/README.md` — 새 절 `## Running while the window is not focused`를
    `## Included dependencies` 바로 앞에 넣는다 (런타임 동작 설명들 뒤, 부록 앞)
  - 내용: 연결 중 `Application.runInBackground`를 켜고 연결이 끝나면 되돌린다는 것(호스트 게임의
    전역 설정이라는 사실 명시), 왜 필요한지, 모바일에서는 적용되지 않는다는 것, 임대는 정지 구간을
    대기 시간으로 세지 않지만 뷰어가 사라지면 여전히 만료된다는 것

- [ ] **Step 5: Rollout / Rollback**
  - 플래그 없음. 되돌리기는 커밋 revert 하나

## Validation

- **Commands to run:**
  - Unity는 WSL 안에는 없지만 Windows 설치를 interop으로 부를 수 있다
    (`/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1`). 이전 플랜
    (`2026-08-13-frame-time-metrics-collection.md`)이 기록한 방식대로, 패키지를 Windows 파일시스템의
    throwaway 프로젝트에 **임베디드 패키지로 복사**해서 EditMode 러너를 돌린다 (`\\wsl$\` 경로는
    Unity가 제대로 다루지 못한다)
  ```bash
  Unity.exe -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath <throwaway-project> -testResults results.xml -logFile unity.log
  ```
  - 브랜치와 merge-base(`origin/develop`) 양쪽에서 돌려 **베이스라인 대비 신규 실패 0건**을 확인한다.
    환경적 실패가 develop에서도 동일하게 난다 (직전 플랜 기준 11건)
- **Expected output:** `ArtelStreamHostTests` 전건 통과, 신규 실패 없음
- **실행 결과 (2026-08-13)**: 베이스라인 대비 **신규 실패 0건**
  - merge-base `2c3b795`: 총 228, 통과 217, 실패 11
  - 브랜치(pair review 반영 후 최종): 총 232, 통과 221, 실패 11 (동일한 이름의 환경적 실패 집합)
  - 비포커스 성능 보고 반영 후: 총 232, 통과 221, 실패 11. `FrameTimeRecorderTests` 13/13 통과
  - `ArtelStreamHostTests` 12건 + `ScreenVideoSourceTests` 4건 전부 통과. 신규 4건
    (`Tick_SurvivesTheFrameThatResumesFromASuspendedApp`,
    `Tick_StillExpiresAfterAResumeWhenNoRenewalFollows`, `Renew_AfterAResumeKeepsTheSessionAlive`,
    `Tick_ChargesAnOverlongFrameTheCapRatherThanExcusingIt`) 포함
  - 실행 방법: `/mnt/c/temp/artel-389-tests`에 throwaway 프로젝트를 만들고 패키지를 임베디드로
    복사. `-projectPath`는 **상대 경로**로 넘겨야 한다 (WSL interop에서 `C:\...` 절대 경로를 주면
    Unity가 cwd에 이어 붙여 실패한다). `Assets/` 디렉터리가 없으면 프로젝트로 인식하지 않는다
- **Manual (이 저장소에서 자동화 불가):** 스트리밍 중 게임 창에서 다른 앱으로 30초 이상 넘어갔다
  돌아와 스트림이 살아 있는지. 이 저장소 루트는 Unity 프로젝트가 아니고 실기 WebRTC 경로는 EditMode에서
  닿지 않으므로, 못 돌린 항목은 PR에 그대로 적는다

## Risks & Rollback

- **Risks:**
  - **호스트 게임의 전역 설정을 바꾼다.** 연결 중에만 켜고 되돌리지만, 연결된 동안에는 포커스를 잃어도
    게임이 계속 돌아 CPU/GPU를 쓴다. 그것이 원격 QA 세션의 요구사항이므로 의도된 비용이다. 연결 중
    호스트 게임이 스스로 이 값을 바꾸면 `StopTransport`가 그것을 덮어쓴다 — 관측된 적 없는 경로라
    감지 로직을 두지 않는다
  - **프로세스가 죽는 경로에서는 복구되지 않는다.** 되돌리기는 `StopTransport`에서만 일어난다. 프로세스
    종료 시 값은 어차피 사라지므로 문제되지 않는다
  - **연결된 상태에서 `연결` 버튼을 다시 누르는 경로.** 저장을 클라이언트 생성 블록 안에 두는 것으로
    막는다 (Step 2). 이 자리가 바뀌면 호스트의 원래 값이 영구히 유실되므로, 리뷰에서 위치가 바뀌면
    이 위험을 다시 본다
  - **임대 만료가 최대 상한치만큼 늦어질 수 있다.** 프레임이 1초보다 오래 걸리는 게임에서는 만료가
    벽시계보다 늦다. 데드맨의 목적(무한정 인코딩 방지)에는 영향이 없다
  - **상한 1s는 임대가 그보다 충분히 길다는 전제에 걸려 있다.** ARTEL-388이 임대를 1초 근처로 줄이면
    스톨 한 번이 임대를 통째로 소진하게 되므로, 그 이슈가 임대 시간을 바꾸면 이 상수를 같이 본다
  - **기존 테스트 2건의 시간 전진 방식이 바뀐다.** 단언은 유지하지만 계약 변경이므로 리뷰에서 눈에
    띄어야 한다
- **Rollback steps:** 커밋 `git revert`. 신규 파일이 없고 기존 두 파일에 얹는 구조라 부작용 없이
  되돌아간다

## Pair review (구현 후, `pair-review-critic`)

VERDICT: PASS. 비차단 지적 4건 중 3건 반영:

- **상한이 "면제가 아니라 과금"이라는 성질에 테스트가 없다** → `Tick_ChargesAnOverlongFrameTheCapRatherThanExcusingIt`
  추가 (프레임당 5초, 50초가 지나도 살아 있고 임대만큼의 프레임이 지나면 죽는다). AC(4)가 기대는
  성질이라 반영 가치가 크다. `StreamLease` 주석의 "wall-clock terms" 문구도 정확히 고쳤다 —
  1fps 미만에서는 벽시계보다 늦게 만료되는 것이 맞다
- **README의 "연결을 잡고 있는 동안"이 모호하다** → 소켓이 붙어 있는 동안이 아니라 트랜스포트를
  열어 둔 동안(재시도 중 포함)이라고 명시
- **`SetWebSocketTransport(transport, takeOwnership: true)`가 저장 없이 복구에 닿는 함정** → 현재
  호출자가 없어 가드는 두지 않되, 필드 주석에 그 경로가 유일한 파손 지점이라고 적었다

반영하지 않은 1건은 아래 Rejected feedback 참조.

## Rejected feedback

- **"`HasExpired`를 advance/query 두 메서드로 쪼개라"(medium, 비차단 지적)** — 호출자가
  `ArtelStreamHost.Tick` 하나뿐이다. 호출 지점 하나를 위해 인터페이스(`IArtelStreamSession`)까지
  넓히는 분리는 이 저장소 규칙(demonstrated complexity 없는 추상화 금지)에 걸린다. 계약을 주석으로
  못박는 선에서 끝낸다
- **"`HasExpired`를 명령형으로 개명하라(`ConsumeFrameAndCheckExpiry` 등)"(pair review, 비차단)** —
  지적 자체는 맞다(이름이 부작용을 숨긴다). 그러나 다섯 곳(`StreamLease`, `IArtelStreamSession`,
  `ArtelStreamSession`, `ArtelStreamHost`, 테스트 fake)을 건드리는 개명인데, 시도한 후보들이
  호출부에서 오히려 더 나쁘게 읽힌다(`!session.SpendLease(now)`). 대신 계약을 `StreamLease`와
  `IArtelStreamSession` **양쪽 주석**에 적어 두 표면 모두에서 읽히게 했다

## Open Questions

- 상한 `1f`는 서버 임대(관측 15s)와 갱신 주기를 전제로 고른 값이다. ARTEL-388에서 임대 시간이 크게
  줄면 이 상한도 같이 봐야 한다. 지금은 상수로 두고 노출하지 않는다
- 주입된 트랜스포트(`ArtelTestPageManager`) 경로는 `runInBackground`를 켜지 않는다. 로컬 테스트
  페이지로 스트리밍을 확인하는 흐름이 실제로 쓰이게 되면 재검토한다
