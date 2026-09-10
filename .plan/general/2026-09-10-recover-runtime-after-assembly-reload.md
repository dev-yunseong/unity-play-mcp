# 2026-09-10 — Play 중 assembly reload 이후 MCP runtime 초기화와 요청 처리 복구

- Date: 2026-09-10
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/57
- Status: Done

## Goal

Play mode 중에 assembly reload 가 일어난 뒤에도 `UnityPlayMcpHost` 가 자기 runtime 과
transport 를 다시 세워, `Update` 가 예외 없이 돌고 `start_readings` 와 `capture_screen` 이
응답하게 한다.

## Non-goals

- 게임 로직 수정, 게임 진행 상태 조작.
- `frameTimeRecorder` 앞에 null 검사를 하나 넣어 초기화 누락을 덮는 것.
- `CursorController` 와 `KeyboardStatusController` 가 `Awake` 에서 만든 GUI 를 reload 뒤에
  다시 세우는 일 (아래 Risks 에 근거와 함께 남긴다).
- `mcp/` 아래 TypeScript server.

## Context / Constraints

### 확정한 원인

issue 는 원인을 확정하지 않았지만, 코드와 Unity 의 문서화된 reload 순서를 맞추면 확정된다.

Unity 가 play mode 중 assembly reload 에서 하는 일은 순서대로 이렇다.

1. 모든 MonoBehaviour 에 `OnDisable` 을 부른다.
2. serialize 대상 field 를 저장한다.
3. domain 을 내리고 새 assembly 로 다시 올린다. static field 는 전부 초기값으로 돌아간다.
4. managed 객체를 다시 만들고 저장해 둔 값을 되돌린다. `[SerializeField]` 가 없는 private
   field 는 되돌아오지 않는다.
5. `OnEnable` 을 부른다. **`Awake` 는 다시 부르지 않는다.**

`UnityPlayMcpHost` 는 소유한 것을 전부 `Awake` → `EnsureRuntime` 에서 만든다.
그래서 reload 뒤에는

- `ownsRuntime`, `hasStarted` 가 `false`, `frameTimeRecorder`·`frameTimingSampler`·
  `actionExecutor`·`jsonCodec`·`pointerEvents`·`webSocketTransport` 가 전부 `null`,
  static `instance` 도 `null` 이 된다.
- `OnEnable`(`UnityPlayMcpHost.cs:156`)은 `hasStarted` 가 `false` 라 아무것도 하지 않는다.
- `Update`(`:196`)는 계속 돌아 `RecordFrameTime`(`:459`)의
  `frameTimeRecorder.Record(...)`(`:466`)에서 매 프레임 `NullReferenceException` 을 던진다.

issue 가 인용한 Editor.log 의 `UnityPlayMcpHost.cs:466`, `Update` 는 `:200` 은 현재 파일의
그 두 줄과 정확히 일치한다. 원인은 "reload 가 `Awake` 를 다시 부르지 않는다" 하나다.

요청이 timeout 된 것도 같은 자리에서 설명된다. `webSocketTransport` 가 `null` 이라
socket 이 열려 있지 않고, 설령 열려 있었어도 `Update` 가 `RecordFrameTime` 에서 죽어
`while (webSocketTransport.TryDequeueMessage(...))` 까지 내려가지 못한다.

### 이미 맞는 쪽

`OnDisable`(`:173`)은 reload 직전에 불려서 `RestoreTimeScale` → `StopTransport` 로
입력 해제(`ReleaseAgentInput`), reading 종료(`EndDiscovery` → `Pulse.Stop`), socket 종료,
`Application.runInBackground` 원복까지 이미 제대로 한다. 즉 **정리는 reload 가 부르는
자리에 있고, 재구성만 reload 가 부르지 않는 자리에 있다.** 이 비대칭이 결함이다.

### 제약

- 게임 프로젝트에 초기화 코드를 요구하지 않는다. 복구는 package 안에서 끝나야 한다.
- editor 전용 hook(`AssemblyReloadEvents`)에 기대지 않는다. runtime assembly 가
  `UnityEditor` 를 `#if` 로 참조하게 되고, `OnEnable` 하나로 되는 일이다.
- 이 machine 에 Unity editor 가 없다. EditMode/PlayMode test 는 CI
  (`.github/workflows/unity-tests.yml`)에서만 돈다.

## Approach (Checklist)

- [x] **Step 0: Recon**
  - `Runtime/UnityPlayMcpHost.cs`, `Runtime/AgentWebSocketServer.cs`,
    `Runtime/Diagnostics/FrameTimeRecorder.cs`,
    `Runtime/Affordance/Scan/AffordanceBootstrap.cs`,
    `Runtime/Affordance/Scan/Live/Pulse.cs`, `Runtime/CursorController.cs`,
    `Runtime/KeyboardStatusController.cs` 를 읽었다.
  - `Tests/PlayMode/HostBootstrapTests.cs`, `Tests/PlayMode/UnityPlayMcpHostSlot.cs`,
    `Tests/PlayMode/MouseMessageActionTests.cs` 에서 fixture 관례를 확인했다.

- [x] **Step 1: 재구성을 `OnEnable` 로 옮긴다** — `Runtime/UnityPlayMcpHost.cs`
  1. `hasStarted` 에 `[SerializeField, HideInInspector]` 를 붙이고 `Awake` 에서 false 로
     되돌린다. reload 를 건너야 하는 값은 이 한 bit 뿐이고, Unity 가 되돌려 주는 것은 serialize
     된 field 뿐이므로 "이 host 는 Start 를 지났다"는 사실을 이 field 가 나른다. `Awake` 는
     reload 가 부르지 않으므로 그 한 줄이 reload 의 표시를 지우지 않고, play 중에 prefab 이나
     scene 으로 떠 간 true 만 지운다 — 그런 값이 실려 오면 첫 활성화의 `OnEnable` 이 `Start`
     보다 먼저 server 를 연다.
  2. `Awake` 의 중복 판정을 `ClaimHostSlot()` 으로 뽑아 `Awake` 와 `OnEnable` 이 함께 쓴다.
     동작은 지금과 같고, 자리가 비어 있을 때 다시 차지할 수 있게 되는 것만 다르다.
     ```csharp
     private bool ClaimHostSlot()
     {
         if (instance == this) return true;
         if (instance != null) { Destroy(gameObject); return false; }
         instance = this;
         return true;
     }
     ```
     `instance != null` 은 Unity 의 비교라 파괴된 host 를 쥔 자리는 비어 있는 것으로 읽힌다 —
     domain reload 를 끈 설정에서 지난 play 세션의 host 가 static 에 남아도 새 host 가 자리를
     잡는다. 진 host 는 `Destroy` 앞에서 `enabled = false` 로 먼저 꺼진다. `Destroy` 는 프레임
     끝에야 처리되고 그때까지 그 host 의 `Update` 가 도는데, 아무것도 만들지 않은 host 의
     `RecordFrameTime` 은 그 프레임에 바로 던진다.
  3. host 를 세우는 일을 `BeginHosting()` 하나로 모으고 `Start` 와 `OnEnable` 이 함께 부른다.
     reload 가 지우는 셋이 정확히 이 셋이다.
     ```csharp
     private void BeginHosting()
     {
         if (!ClaimHostSlot()) return;   // reload 는 static slot 을 지운다
         EnsureRuntime();                // reload 는 ownsRuntime 을 false 로 되돌린다
         StartTransport();               // reload 는 transport 를 null 로 되돌린다
     }

     private void OnEnable()
     {
         if (!hasStarted) return;        // 첫 연결은 Start 의 몫
         BeginHosting();
     }

     private void Start()
     {
         hasStarted = true;
         BeginHosting();
     }
     ```
     `Start` 도 통째로 부르는 이유는 `Awake` 와 `Start` 사이에 reload 가 끼는 구성이 있어서다.
     scene 이 host 를 `enabled = false` 로 들고 오면 `Awake` 는 그때 돌고 `Start` 는 게임이
     켤 때까지 오지 않는다. 그 사이의 reload 는 `Awake` 가 만든 것을 지우고, `OnEnable` 은
     `hasStarted` 가 아직 false 라 그냥 돌아간다 — 다시 세울 자리가 `Start` 말고 없다.
     `GetComponent` 로 찾는 `CursorController`/`KeyboardStatusController` 는 살아 있으므로
     component 가 두 벌이 되지 않는다. reload 전 `OnDisable` 이 socket 을 닫고
     `Application.runInBackground` 를 게임 값으로 되돌려 놓았으므로, `StartTransport` 가 다시
     읽는 값은 게임의 원래 값이다.
  4. 단순 재활성화(`enabled = false` → `true`)의 기존 동작은 그대로다. 앞에 붙는
     `ClaimHostSlot`(자리를 이미 쥔 host 에는 무의미)과 `EnsureRuntime`(`ownsRuntime` 이 아직
     true) 둘 다 멱등이고, `StartTransport` 도 `AgentWebSocketServer.Start` 가
     `server != null` 이면 그대로 돌아오므로 멱등이다.
  5. `OnEnable` 에서 `ClaimHostSlot` 이 false 를 돌려주는 경우는 하나뿐이다. reload 로 자리가
     빈 사이에 다른 host 가 그것을 차지했고, 그 뒤에 이 host 의 `OnEnable` 이 온 경우.
     그때는 이 host 가 물러나는 쪽이 맞다 — 살아 있는 연결은 자리를 쥔 쪽에 있다.

- [x] **Step 2: 진단 수집이 입력 처리를 막지 못하게 한다** — `Update` 순서
  issue 의 Constraints 가 짚은 자리다. `RecordFrameTime()` 이 `Update` 의 첫 줄이라 거기서
  던지면 `VirtualInput.AdvanceFrame()` 도, 메시지 큐 배수도 그 프레임에 아예 돌지 않는다.
  지표 하나를 잃는 것과 원격 제어 전체를 잃는 것은 값이 다르다. 순서를 이렇게 못 박는다.
  ```csharp
  using (ProfilerMarkers.HostUpdate.Auto())
  {
      VirtualInput.AdvanceFrame();
      PumpTransport();      // transport null 검사와 early return 은 이 안으로 들어간다
      RecordFrameTime();    // 항상 돈다. early return 뒤가 아니다
  }
  ```
  `if (webSocketTransport == null) return;` 이 `Update` 에 있는 한 마지막 줄은 조건부가 되므로,
  그 검사를 `PumpTransport()` 로 내린다. `RecordFrameTime` 의 문서가 약속하는
  "전송 상태와 무관하게 매 프레임" 이 그렇게 지켜진다. 예외를 삼키지는 않는다 — 삼켰다면
  이번 결함이 로그에 남지 않았을 것이다. `frameTimeRecorder` 는 `Time.unscaledDeltaTime` 을
  인자로만 받고 그 값은 engine 이 프레임 단위로 정하므로, `Update` 안에서의 호출 위치가 샘플
  값을 바꾸지 않는다. 바뀌는 것은 창 하나다: 보고가 이번 프레임의 샘플을 담지 못하고 다음
  창으로 민다. 버려지는 샘플은 없고 창 하나가 60 프레임쯤이라 어느 창에 실리는지만 달라진다.

- [x] **Step 3: test** — `Tests/PlayMode/`
  1. `AssemblyReloadSimulation.cs` (새 파일): reload 가 host 에 남기는 상태를 만든다.
     `enabled = false`(→ `OnDisable`) → serialize 되지 않는 field 되돌리기 → static slot 비우기
     → `enabled = true`(→ `OnEnable`).
     되돌릴 값은 손으로 적지 않는다. 비활성 GameObject 에 `UnityPlayMcpHost` 를 하나 더 붙이면
     `Awake` 가 돌지 않아 field initializer 가 놓은 값만 남는데, 그것이 곧 reload 뒤의 값이다.
     그 host 에서 값을 베끼므로 `nextMessageId = 1` 처럼 0 이 아닌 초기값도 어긋나지 않고,
     나중에 누가 `Awake` 에서만 채우는 field 를 늘려도 함께 지워진다. `[SerializeField]` 가
     붙은 field 는 Unity 가 되돌려 주므로 건너뛰고, `readonly` field 도 건너뛴다 — reflection
     으로 쓰는 것이 runtime 에 따라 거절되고, 지금 걸리는 것은 비어 있는 `actionRequests`
     하나뿐이라 새로 만든 빈 것과 구별되지 않는다.
     지울 것을 고르는 규칙은 Unity 의 규칙 그대로다: `[NonSerialized]` 면 지우고, public 이거나
     `[SerializeField]` 가 붙었으면 남긴다. `[SerializeField]` 만 보면 나중에 public field 가
     하나 늘었을 때 simulation 이 진짜 reload 보다 가혹해져 있지도 않은 결함을 붙잡는다.
     `AffordanceBootstrap`/`Pulse` 와 `VirtualInput` 의 static 은 건드리지 않는다. `OnDisable` 이
     이미 그것들을 멈추고 놓은 상태로 돌려놓고, 그것이 reload 직전에 실제로 일어나는 일이다.
  2. `HostReloadRecoveryTests.cs` (새 파일). Unity Test Framework 는 test 중 올라온 예외를
     실패로 치므로, 복구가 안 되면 프레임을 넘기는 것만으로 붉어진다.
     - `RebuildsItsRuntimeAfterAnAssemblyReload`: 복구 뒤 두 프레임을 넘기고
       `frameTimeRecorder` 가 다시 있는지.
     - `RebuildsItsRuntimeWhenTheReloadLandsBeforeStart`: 꺼진 채로 들어온 host 가 `Start`
       전에 reload 를 맞은 경우.
     - `ReopensTheTransportAfterAnAssemblyReload`: 복구 뒤 server 가 다시 서 있는지.
       host 에 `internal bool TransportOpen` 을 둔다 — transport 를 쥐고 있고 그것이 stop 되지
       않았는지를 말하며, 누가 붙었는지는 말하지 않는다.
       `Reading` 이 이미 같은 이유로 `internal` 인 선례다. `StartReadings` 가 참을 돌려주는
       것으로는 대신할 수 없다 — transport 가 null 이면 pulse 는 파일로 떨어지고 그래도 참이다.
     - `AnswersStartReadingsAfterAnAssemblyReload`: 복구 뒤 `StartReadings()` 가 참이고
       `Reading` 이 참이다.
     - `KeepsTheRecoveredHostWhenASecondOneAppears`: 복구 뒤 host 를 하나 더 붙이면 새 쪽이
       스스로 물러나고 먼저 있던 쪽이 socket 을 계속 쥔다.
     - `ClaimsTheSlotWhenItStillHoldsADestroyedHost`: domain reload 를 끈 설정에서 지난 세션의
       파괴된 host 가 자리에 남은 경우.
     - `KeepsRecordingFrameTimesWithNoClientConnected`: server 는 섰고 붙은 client 는 없는
       프레임에서도 프레임타임이 쌓이는지. `RecordFrameTime` 이 "전송 상태와 무관하게 매 프레임"
       이라는 제 문서를 지키는지를 본다.
     - `KeepsHandlingRequestsWhenFrameTimeRecordingThrows`: `frameTimeRecorder` 를 null 로 만들고
       한 프레임을 넘겨, 예외가 로그에 오르면서도 그 프레임의 `PumpTransport` 는 이미 지나갔는지.
       Step 2 의 순서를 실제로 지키는 test 다 — 순서를 되돌리면 이것만 붉어진다.
     - `ReopensTheTransportOnAPlainReEnable`: reload 가 아니라 그냥 껐다 켜는 길. `OnEnable` 을
       통째로 바꿨으므로 기존 동작을 함께 지킨다 (acceptance criterion 4).
     - `GivesTheGameItsRunInBackgroundBackAcrossAReload`: reload 를 건넌 host 가 제가 빌린
       `Application.runInBackground` 를 게임의 값으로 착각하지 않는지 (acceptance criterion 4).

     이 중 `Rehearse` 를 부르지 않는 넷 — `KeepsRecordingFrameTimesWithNoClientConnected`,
     `ReopensTheTransportOnAPlainReEnable`, `ReleasesHeldInputWhenItGoesDown`,
     `ClaimsTheSlotWhenItStillHoldsADestroyedHost` — 은 reload 결함의 증거가 아니라 기존 동작을
     지키는 test 다. 수정 전 코드에서도 통과한다.
     - `ReleasesHeldInputWhenItGoesDown`: 키를 눌러 둔 채 host 를 비활성화하면 놓인다. reload
       직전 `OnDisable` 이 하는 일이고, 이번 변경이 그것을 건드리지 않았다는 증거다.
  3. 두 파일에 `.meta` 를 함께 만든다. 이 package 는 모든 source 옆에 `.meta` 를 커밋한다.

- [x] **Step 4: 남는 결함을 기록한다**
  아래 Risks 의 `CursorController`/`KeyboardStatusController` 항목을 issue #57 에 코멘트로
  남기고 PR 의 Risks 에도 적는다. 별도 issue 로 뗄 것을 권한다 — 이 PR 의 write scope 밖이고,
  두 component 가 만든 canvas 를 걷어내고 다시 세우는 일이라 크기도 다르다.

- [x] **Step 5: Rollout / Rollback**
  - flag 도 migration 도 없다. package 안의 동작 변경 하나다.
  - rollback 은 `git revert`.

## Rejected feedback

- **`ClearHosts()` 를 `UnityPlayMcpHostSlot` 으로 모아라** (pair review): 맞는 말이지만 이번
  변경과 무관한 정리다. 네 fixture 를 함께 건드리면 이 PR 이 고치는 것이 무엇인지 흐려진다.

- **"reload 뒤 `Awake` 가 다시 돌아 `instance = this` 를 하는지 확인하라"** (fast #3, #7):
  전제가 반대다. reload 가 `Awake` 를 부르지 않는다는 것이 이 결함의 원인이고, 부른다면
  `EnsureRuntime` 이 다시 돌아 애초에 예외가 나지 않았다.
- **"simulation 이 `OnDisable` 을 먼저 부르는지 test 하라"** (fast #4): simulation 이
  `enabled = false` 로 직접 부른다. 그것을 확인하는 test 는 Unity 를 test 하는 것이다.
- **"`RecordFrameTime` 을 뒤로 옮기면 측정 창에 큐 배수 시간이 섞인다"** (fast #5):
  섞이지 않는다. `Time.unscaledDeltaTime` 은 engine 이 프레임마다 정하는 값이고 어디서 읽든
  같다.
- **"`hasStarted` 에 `[SerializeField]` 가 붙어 있는지 확인하는 test 를 두라"** (내 초안):
  뺐다. simulation 이 `[SerializeField]` 를 보고 지울 것을 고르므로, attribute 를 떼면
  `hasStarted` 도 함께 지워져 `RebuildsItsRuntimeAfterAnAssemblyReload` 가 먼저 실패한다.

## Validation

- **Commands to run:**
  - 이 machine 에서는 `unity` 가 Windows Hub alias 뿐이라 EditMode/PlayMode 를 돌릴 수
    없다. 돌렸다고 적지 않는다.
  - CI: `.github/workflows/unity-tests.yml` 이 PR 에서 EditMode 와 PlayMode 를 모두 돈다.
  - 사람이 Unity 에서 확인할 것: Play 중 script 를 고쳐 assembly reload 를 일으킨 뒤
    (1) Console 에 `RecordFrameTime` 예외가 없고,
    (2) `[Unity Play MCP] WebSocket server started` 가 다시 찍히고,
    (3) `start_readings` 와 `capture_screen` 이 15초 timeout 없이 응답하는지.
- **Expected output:** PlayMode suite 가 새 fixture 를 포함해 green.

## Risks & Rollback

- **Risks:**
  - `[SerializeField] hasStarted` 는 scene 에 놓인 host 의 scene asset 에도 저장된다.
    edit mode 값은 항상 `false` 라 의미가 없고, `[HideInInspector]` 로 사람 눈에서 뺀다.
  - reload 직전 `OnDisable` 이 실제로 불린다는 것에 기댄다. 불리지 않으면 예전 socket 이
    port 17311 을 쥔 채 남아 `StartTransport` 가 bind 에 실패한다. Unity 문서가 명시하는
    순서이고, 이번 사건의 로그도 socket 이 닫힌 뒤의 timeout 과 어긋나지 않는다.
  - **남는 결함(이번 PR 밖):** `CursorController` 와 `KeyboardStatusController` 도 GUI 를
    `Awake` 에서 만들고 그 참조를 serialize 하지 않는다. reload 뒤
    `CursorController.Update`(`Runtime/CursorController.cs:44`)는 `cursorTexture` 가
    `null` 이라 theme 이 처음 바뀌는 프레임에 한 번 던지고, 그 뒤로는 `darkTheme` 이
    같아져 조용해진다. `MoveTo`(`:85`)에는 `cursorTransform == null` 가드가 있어 pointer
    action 은 예외 없이 무시된다. `KeyboardStatusController.RefreshText`(`:253`)는
    표시 문자열이 바뀔 때마다 `keyStatusText` 가 `null` 이라 던진다. 즉 reload 뒤 화면
    overlay 는 죽어 있고 로그에 예외가 남지만, MCP 요청 경로는 이번 수정으로 살아난다.
    두 component 를 고치려면 각자가 만든 canvas 를 걷어내고 다시 세워야 해서 이 issue 의
    범위를 넘는다. issue 에 코멘트로 남기고 별도 issue 로 분리한다.
- **Rollback steps:** `git revert` 후 develop 재배포. 상태 이관이 없다.

## Open Questions

- 없음. reload 뒤 `Start` 가 다시 불리는지는 확인하지 못했지만, 불리더라도
  `StartTransport` 와 `EnsureRuntime` 이 멱등이라 결과가 달라지지 않는다.
