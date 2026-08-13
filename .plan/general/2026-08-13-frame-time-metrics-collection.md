# 2026-08-13 — 프레임 지표 수집 (FPS 분포와 hitch 카운트)

- Date: 2026-08-13
- Jira: ARTEL-343
- Status: Reviewed (fast/medium/heavy 자기 리뷰, 서브에이전트 미사용) → 사용자 피드백 반영 개정
- Jira(흡수): ARTEL-346 전송 경로

## Goal

`Time.unscaledDeltaTime`을 매 프레임 누적하고, 1초마다 WebSocket으로 프레임타임 분포를 서버에 보낸다. 평균 FPS 하나로는 설명되지 않는 체감 끊김을 수치로 남기는 것이 목적이다.

산출 항목: 평균/최소/최대 프레임타임, p95, p99, 1% low FPS, 0.1% low FPS, hitch 카운트, 구간 프레임 수, 실제 측정 시간.

## 개정 이력

초안은 레코더가 5초 집계 주기를 소유하고, 결과를 `ArtelManager.LatestFrameTimes`에 얹어 두기만 했다. 전송은 ARTEL-346으로 미뤘다. 리뷰에서 두 가지가 뒤집혔다.

1. **레코더가 주기를 소유하면 안 된다.** 전송 주기와 두 벌이 되어 어긋난다. 전송이 1초인데 집계가 5초면 같은 스냅샷을 다섯 번 보내거나 구간을 통째로 버린다. 창 길이는 부르는 쪽이 정한다
2. **전송을 빼면 이 작업이 앞뒤가 안 맞는다.** 주기를 걷어내면 `LatestFrameTimes`가 사라지고, 그러면 `Summarize`를 부르는 프로덕션 코드가 하나도 없다. 테스트만 부르는 죽은 경로가 되므로 ARTEL-346의 전송 경로를 이 작업이 흡수한다

## Non-goals

- CPU/GPU 프레임타임 분해 — ARTEL-347
- 프로세스 CPU·메모리 — ARTEL-344
- 디바이스 컨텍스트 수집 — ARTEL-345
- 성능 데이터 시각화·저장

## Context / Constraints

**`Time.unscaledDeltaTime`을 쓴다.** `ActionExecutor`에 `pause_time` 계열 액션이 있어 `Time.timeScale`이 임의로 바뀐다. `deltaTime`은 그 배율을 그대로 받으므로 프레임 성능 지표로 쓸 수 없다.

**포커스 없는 프레임은 제외한다.** Unity는 포커스를 잃은 창의 프레임 페이싱을 스로틀링한다. 그 프레임을 섞으면 hitch가 폭증하고 평균이 무너진다.

**매 프레임 할당 0.** 기존 `SceneStatePoller`처럼 순수 클래스로 두되, 샘플 저장은 고정 크기 링버퍼 `float[]`. 정렬용 스크래치 배열도 생성자에서 한 번만 잡는다. `Array.Sort(float[], int, int)`는 introsort 경로라 힙 할당이 없다.

**구조는 기존 폴러 패턴을 따른다.** `Artel.Tracking.SceneStatePoller`가 순수 로직 + `ArtelManager.Update`가 구동하는 형태다. 같은 모양으로 간다. 순수 클래스에는 `UnityEngine.Time`/`Application`을 직접 읽지 않고 값으로 받아 테스트 가능하게 둔다.

**0.1% low의 한계.** 5초 × 60fps = 약 300 샘플이다. 0.1%는 0.3 프레임이라 의미가 없다. 정의를 "최악 `max(1, ceil(n/1000))` 프레임의 평균"으로 못박고, 작은 창에서는 사실상 최대 프레임타임과 같아진다는 점을 주석으로 남긴다. 1% low도 같은 방식(`max(1, ceil(n/100))`).

**hitch 기준은 프레임 예산의 2배.** 예산은 Unity 쪽에서 결정해 순수 클래스에 초 단위로 넘긴다. 우선순위: `Application.targetFrameRate > 0` → `1/targetFrameRate`; 아니면 vsync가 켜져 있고 모니터 주사율을 읽을 수 있으면 `vSyncCount / refreshRate`; 둘 다 아니면 `1/60`. 예산 자체도 집계 결과에 실어야 수치 해석이 가능하다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료
  - `Runtime/Tracking/SceneStatePoller.cs` — 순수 폴러 + 결과 객체 패턴 기준
  - `Runtime/ArtelManager.cs:187` — `Update()`. `ArtelInput.AdvanceFrame()` 직후가 훅 지점
  - `Runtime/ArtelManager.cs:139` — 폴러 생성 위치 (`Awake` 경로)
  - `Tests/Runtime/Artel.Runtime.Tests.asmdef` — EditMode 테스트 어셈블리
  - 성능 수집 관련 기존 코드 없음 — 신규 디렉터리 `Runtime/Diagnostics/`

- [ ] **Step 1: `FrameTimeStatistics` 결과 타입**
  - 파일: `Runtime/Diagnostics/FrameTimeStatistics.cs` (+ `.cs.meta`)
  - `readonly struct`. 필드는 전부 초 단위 프레임타임 또는 FPS
    - `int FrameCount`
    - `float SampledSeconds` — 집계에 들어간 프레임타임의 합
    - `float MeanSeconds`, `MinSeconds`, `MaxSeconds`
    - `float Percentile95Seconds`, `Percentile99Seconds`
    - `float OnePercentLowFps`, `PointOnePercentLowFps`
    - `int HitchCount`, `float HitchThresholdSeconds`
    - `float BudgetSeconds`
  - `SampledSeconds`는 리뷰에서 추가했다. 비포커스 프레임을 버리므로 집계 구간(5초)과 실제로 측정된 시간이 다르다. 이 값이 없으면 소비자가 커버리지를 알 수 없어 "5초 동안 12프레임"과 "0.2초 동안 12프레임"을 구분하지 못한다
  - FPS 값은 프레임타임의 역수. 0 나눗셈 방지는 산출 지점에서 한 번만

- [ ] **Step 2: `FrameTimeRecorder` 순수 누적기**
  - 파일: `Runtime/Diagnostics/FrameTimeRecorder.cs` (+ `.cs.meta`)
  - `internal sealed class`, 네임스페이스 `Artel.Diagnostics`
  - 생성자: `FrameTimeRecorder(int capacity = 600)`
    - `capacity <= 0` → `ArgumentOutOfRangeException`
    - `samples = new float[capacity]`, `scratch = new float[capacity]` 를 여기서 한 번만 잡는다
  - **시간을 전혀 다루지 않는다.** 주기도, 마감 시각도, `Reset`도 없다. 창은 `TrySummarize`를 부르는 쪽이 정한다
  - capacity를 넘기면 오래된 샘플이 밀려난다. 소비자가 오래 안 물어보면 최근 600프레임만 남고, 그게 맞는 동작이다. 실제로 얼마를 덮었는지는 `SampledSeconds`가 알려 준다
  - `void Record(float deltaSeconds, bool counted)`
    - `counted`가 false거나 `deltaSeconds <= 0`이면 버린다
    - **첫 기록 한 건은 무조건 버린다.** `Awake` 직후의 첫 `unscaledDeltaTime`은 씬 로드 시간을 포함해 렌더 프레임 비용이 아니다. 이걸 남기면 모든 세션이 hitch 1로 시작한다
    - 링버퍼에 쓰고 `writeIndex` 전진, `count`는 `capacity`에서 포화
    - **버려진 프레임도 시간은 흐른다.** 집계 경계 판단은 `Record`가 아니라 `TryAggregate`가 시각으로 한다
  - `bool TrySummarize(float budgetSeconds, out FrameTimeStatistics statistics)`
    - 샘플이 없으면(창 내내 비포커스) false. 빈 통계를 내보내면 소비자가 0fps로 읽는다
    - `budgetSeconds <= 0`이면 1/60으로 대체한다. 0을 그대로 쓰면 문턱이 0이 되어 전 프레임이 hitch가 된다
    - 샘플을 `scratch`에 복사 → `Array.Sort(scratch, 0, count)` → 백분위·low FPS·min·max 산출
    - 평균·합·hitch 카운트는 정렬 전 원본 순회로 계산 (정렬과 무관)
    - 산출 후 `count = 0`, `writeIndex = 0` — 연속 호출한 창끼리 겹치지 않는다
    - 예산은 값으로 받는다. 부르는 쪽이 보낼 때만 부르므로 매 프레임 `Screen`을 읽는 문제가 애초에 없다 (초안의 `Func<float>`는 매 프레임 호출되던 구조 때문이었고, 이제 불필요하다)
  - 백분위: 정렬된 배열에서 `index = clamp((int)ceil(p * count) - 1, 0, count - 1)`. 보간 없는 nearest-rank. 결정적이고 샘플 수가 적어도 무너지지 않는다

- [ ] **Step 3: 전송 DTO와 매퍼**
  - `Runtime/Protocol/Dto/FrameTimesDto.cs` — 분포 페이로드. **시간 단위는 밀리초.** 초 단위 float은 `0.016666668`이 되어 JSON에서 읽기 어렵고, ms가 관례다
  - `Runtime/Protocol/Dto/PerformanceMessageDto.cs` — `{ type: "PERFORMANCE", id, frameTimes }`. 기존 `GameStateMessageDto`와 같은 형태
    - 프레임 지표를 최상위에 펼치지 않고 `frameTimes` 아래에 묶는다. CPU·메모리(ARTEL-344)와 디바이스 컨텍스트(ARTEL-345)가 같은 메시지에 형제 필드로 붙을 자리다
  - `Runtime/Protocol/Mapping/FrameTimesMapper.cs` — 초→ms 변환이 한 곳에만 있도록 분리

- [ ] **Step 4: `ArtelManager` 연결**
  - 파일: `Runtime/ArtelManager.cs`
  - 상수 `PerformanceReportIntervalSeconds = 1f` — `SceneScanIntervalSeconds` 옆
  - `Awake` 경로에서 `frameTimeRecorder = new FrameTimeRecorder()`
  - `RecordFrameTime()` — `Update()` 최상단, `ArtelInput.AdvanceFrame()` 앞. 전송 상태와 무관하게 프레임은 계속 흘러야 하므로 `webSocketTransport == null` early return보다 위여야 한다. 하는 일은 `Record` 한 줄뿐이다
  - `SendPerformanceReport()` — `PollSceneState()` 옆. 전송 경로이므로 트랜스포트 검사 뒤여야 한다
    - `webSocketTransport.IsConnected` 확인 (`PollSceneState`와 같은 규약)
    - `nextPerformanceReportTime` 타이머로 1초 게이트
    - 게이트를 통과할 때만 `ResolveFrameBudgetSeconds()`를 부른다. `Screen`·`QualitySettings`를 읽으므로 매 프레임 돌 이유가 없다
    - `TrySummarize` → `FrameTimesMapper.ToDto` → `webSocketTransport.Send`
  - `private static float ResolveFrameBudgetSeconds()` — 위 Context의 우선순위 규칙
  - **소켓이 끊긴 동안**: 기록은 계속되고 링버퍼가 밀린다. 재연결 후 첫 보고는 최근 ≤600프레임을 덮으며, 그 창이 1초보다 길다는 사실은 `sampledMs`로 드러난다. 창을 버리지 않는 쪽을 택한 이유는 끊긴 구간의 성능이야말로 QA에서 궁금한 부분이기 때문이다
  - **에디터에서의 `Application.isFocused`**: 에디터 애플리케이션이 포커스를 가졌는지를 뜻한다. Game view가 아니라 에디터 창 기준이므로, 에디터에서 작업 중이면 대체로 true다. 다른 앱으로 넘어간 동안만 빠진다. 의도한 동작이고 주석으로 남긴다

- [ ] **Step 5: 테스트**
  - 파일: `Tests/Runtime/Diagnostics/FrameTimeRecorderTests.cs` (+ `.cs.meta`)
  - 순수 클래스라 Unity 런타임 없이 도는 EditMode 테스트. `[Test]`만 쓰고 `[UnityTest]`는 불필요
  - 케이스
    - 균일한 16.67ms 프레임 → 평균/min/max가 같고 hitch 0, `SampledSeconds`가 합과 일치
    - 샘플이 없으면 `TrySummarize`가 false
    - 한 프레임만 예산 2배 초과 → `HitchCount == 1`, `MaxSeconds`가 그 값
    - 예산 0을 넘기면 1/60으로 대체되어 hitch가 0
    - `counted: false` 프레임은 `FrameCount`에 들어가지 않는다
    - `deltaSeconds <= 0`은 버려진다
    - 첫 기록은 버려진다 — 큰 값 하나를 먼저 넣어도 `HitchCount == 0`
    - 백분위: 100 샘플 중 98개가 16.67ms, 2개가 100ms → `Percentile99Seconds`가 큰 값 쪽, `Percentile95Seconds`는 작은 값. **nearest-rank라 나쁜 프레임이 하나뿐이면 p99가 아니라 max만 움직인다.** 초안 테스트가 이걸 틀렸었다
    - 1% low / 0.1% low가 최악 프레임 기준으로 계산된다
    - 요약 후 창이 비워져 다음 요약에 이전 샘플이 섞이지 않고, 새 프레임 없이 다시 부르면 false
    - capacity를 넘겨 밀어 넣으면 `FrameCount == capacity`이고 오래된 값이 밀려난다
  - 파일: `Tests/Runtime/Diagnostics/FrameTimesMapperTests.cs` — 초→ms 변환과, FPS 필드는 변환 대상이 아니라는 것

## Validation

- **Commands to run:**
  - `project.md`의 throwaway 프로젝트 EditMode 러너로 `FrameTimeRecorderTests` 실행
  - 변경 전 merge-base에서 베이스라인 먼저 확보 — `project.md`가 기록한 환경적 실패 8건과 구분해야 한다
  ```bash
  Unity -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath <throwaway-project> -testResults results.xml -logFile unity.log
  ```
- **실행 결과 (2026-08-13)**: 신규 Diagnostics 테스트 14/14 통과. `origin/develop` 베이스라인 대비 **신규 실패 0건**
  - 브랜치: 총 207, 통과 196, 실패 11
  - develop: 총 193, 통과 182, 실패 11 (동일 집합)
  - 실패 11건은 양쪽에서 같은 이름으로 나는 환경적 실패다
- **Unity 가용성**: WSL 안에는 Unity가 없지만 **Windows 쪽 설치가 `/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1`에 있고 WSL interop으로 그대로 실행된다.** `project.md`의 macOS 경로만 보고 "환경에 Unity 없음"이라고 판단하면 안 된다
- **throwaway 프로젝트**: 패키지를 `file:` UNC로 참조하지 말고 Windows 파일시스템의 `<project>/Packages/kr.artel.sdk`로 **복사해 임베디드 패키지로** 쓴다. Unity가 `\\wsl$\` 경로를 제대로 다루지 못한다
- **`project.md` 기록이 낡았다**: 환경적 실패를 8건으로 적어 뒀지만 실제 develop 베이스라인은 11건이다 (`OverlayViewModel_*` 3건 추가). 이 작업 범위 밖이라 문서는 고치지 않았다
- **Manual:** 이 저장소 루트는 Unity 프로젝트가 아니다. 이슈의 Validation Notes(부하 씬에서 hitch 증가, `targetFrameRate` 30 반영)는 `samples/WordVenture`에서 확인해야 하며, Windows/macOS 실기 확인은 ARTEL-346으로 전송 경로가 생긴 뒤가 실효적이다. 이번 PR에서 못 돌린 항목은 그대로 명시한다

## Risks & Rollback

- **Risks:**
  - `ArtelManager.Update` 최상단에 코드가 추가된다. 매 프레임 도는 자리라 비용이 곧 SDK 오버헤드다. 링버퍼 쓰기 한 번 + 시각 비교 한 번으로 제한한다
  - 1초 창(약 60 샘플)에서 0.1% low는 최대 프레임타임과 같아진다. 통계가 아니라 정의의 한계이므로 주석과 필드명으로 오해를 막는다
  - `ResolveFrameBudgetSeconds`가 플랫폼별로 다른 값을 낼 수 있다. 예산 자체를 결과에 실어 사후 해석이 가능하게 한다
  - **1초마다 메시지가 하나 더 늘어난다.** 기존 `GAME_STATE`는 씬 해시가 바뀔 때만 나가지만 `PERFORMANCE`는 무조건 나간다. 페이로드는 숫자 12개라 작지만, 트래픽 증가가 부담이면 주기를 늘리는 것으로 조정한다
  - 서버가 `PERFORMANCE` 타입을 모르면 무시하거나 오류를 낼 수 있다. 서버 수신 구현과 배포 순서를 맞춰야 한다
- **Rollback steps:** 신규 파일 삭제 + `ArtelManager` 변경 `git revert`. 기존 동작에 얹는 구조라 부작용 없이 되돌아간다

## Rejected feedback

- **"`PointOnePercentLowFps`는 300 샘플에서 무의미하니 빼라"** — 이슈 AC가 명시적으로 요구한다. 정의(최악 `max(1, ceil(n/1000))` 프레임 평균)를 문서화하는 선에서 유지한다
- **"`ArtelManager` 연결을 이번 범위에서 빼고 순수 클래스만 만들라"** — 그러면 "집계 주기마다 산출된다"는 AC를 구동할 주체가 없어 검증이 불가능해진다. 연결은 유지하되 소비자는 `LatestFrameTimes` 노출까지로 제한한다
- **"`Runtime/Diagnostics/` 신규 디렉터리는 과하다"** — `Tracking`, `Streaming`, `Capture`처럼 관심사별 디렉터리가 이미 관례다. 관례를 따르는 편이 일관적이다

## Open Questions

- 전송 주기 1초는 사용자가 정했다. 설정 노출은 아직 없다 — 필요해지면 `Server`처럼 `[SerializeField]`로 뺀다
- capacity 600은 60fps 기준 10초다. 1초 주기면 아주 넉넉하고, 240Hz에서도 1초에 240 샘플이라 여유가 있다. 소켓이 끊겨 오래 못 보낸 경우에만 밀린다
- 서버 측 `PERFORMANCE` 수신·저장은 orchestration-server 별도 이슈다. 이 PR은 SDK가 보내는 쪽만 만든다
