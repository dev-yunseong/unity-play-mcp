# 2026-08-13 — 프레임 지표 수집 (FPS 분포와 hitch 카운트)

- Date: 2026-08-13
- Jira: ARTEL-343
- Status: Reviewed (fast/medium/heavy 자기 리뷰, 서브에이전트 미사용)

## Goal

`Time.unscaledDeltaTime`을 매 프레임 누적해 집계 주기마다 프레임타임 분포를 산출한다. 평균 FPS 하나로는 설명되지 않는 체감 끊김을 수치로 남기는 것이 목적이다.

산출 항목: 평균/최소/최대 프레임타임, p95, p99, 1% low FPS, 0.1% low FPS, hitch 카운트, 집계 구간 프레임 수.

## Non-goals

- CPU/GPU 프레임타임 분해 — ARTEL-347
- 서버 전송·스키마 확정 — ARTEL-346. 이 작업은 집계 결과를 만들어 매니저에 노출하는 데까지다
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
  - 생성자: `FrameTimeRecorder(float intervalSeconds, int capacity = 600)`
    - `intervalSeconds <= 0` → `ArgumentOutOfRangeException` (`SceneStatePoller`와 같은 규약)
    - `capacity <= 0` → `ArgumentOutOfRangeException`
    - `samples = new float[capacity]`, `scratch = new float[capacity]` 를 여기서 한 번만 잡는다
  - **`Reset(currentTime)`을 두지 않는다.** 리뷰에서 걷어냈다. 소켓 수명과 무관하므로 호출자가 없고, 남기면 쓰이지 않는 API가 된다. 대신 첫 `TryAggregate` 호출이 마감 시각을 세운다 (`nextAggregateTime`이 미설정이면 `currentTime + intervalSeconds`로 잡고 false 반환)
  - `void Record(float deltaSeconds, bool counted)`
    - `counted`가 false거나 `deltaSeconds <= 0`이면 버린다
    - **첫 기록 한 건은 무조건 버린다.** `Awake` 직후의 첫 `unscaledDeltaTime`은 씬 로드 시간을 포함해 렌더 프레임 비용이 아니다. 이걸 남기면 모든 세션이 hitch 1로 시작한다
    - 링버퍼에 쓰고 `writeIndex` 전진, `count`는 `capacity`에서 포화
    - **버려진 프레임도 시간은 흐른다.** 집계 경계 판단은 `Record`가 아니라 `TryAggregate`가 시각으로 한다
  - `bool TryAggregate(float currentTime, Func<float> resolveBudgetSeconds, out FrameTimeStatistics statistics)`
    - `currentTime < nextAggregateTime` → false
    - 경계는 넘겼는데 샘플이 하나도 없으면(전 구간 비포커스) `nextAggregateTime`만 밀고 false. 빈 통계를 만들어 내보내지 않는다
    - `nextAggregateTime = currentTime + intervalSeconds`
    - **예산은 여기서만 해석한다.** 리뷰에서 `float budgetSeconds` 인자를 델리게이트로 바꿨다. 값으로 받으면 집계하지 않는 프레임에서도 `Application.targetFrameRate`·`Screen.currentResolution`을 매 프레임 읽게 된다. 호출자는 정적 메서드 그룹을 `static readonly Func<float>`에 담아 넘기므로 델리게이트 할당도 1회뿐이다
    - 샘플을 `scratch`에 복사 → `Array.Sort(scratch, 0, count)` → 백분위·low FPS·min·max 산출
    - 평균·합·hitch 카운트는 정렬 전 원본 순회로 계산 (정렬과 무관)
    - 산출 후 `count = 0`, `writeIndex = 0` — 구간은 겹치지 않는다
  - 백분위: 정렬된 배열에서 `index = clamp((int)ceil(p * count) - 1, 0, count - 1)`. 보간 없는 nearest-rank. 결정적이고 샘플 수가 적어도 무너지지 않는다

- [ ] **Step 3: `ArtelManager` 연결**
  - 파일: `Runtime/ArtelManager.cs`
  - 상수 `FrameTimeIntervalSeconds = 5f` — `SceneScanIntervalSeconds` 옆
  - `Awake` 경로에서 `frameTimeRecorder = new FrameTimeRecorder(FrameTimeIntervalSeconds)`
  - `private static readonly Func<float> FrameBudgetResolver = ResolveFrameBudgetSeconds;` — 델리게이트 할당 1회
  - `Update()` 최상단 — `ArtelInput.AdvanceFrame()` 앞. 전송 상태와 무관하게 프레임은 계속 흘러야 하므로 `webSocketTransport == null` early return보다 위여야 한다
    ```csharp
    frameTimeRecorder.Record(Time.unscaledDeltaTime, Application.isFocused);
    if (frameTimeRecorder.TryAggregate(Time.unscaledTime, FrameBudgetResolver, out var frameTimes))
    {
        LatestFrameTimes = frameTimes;
    }
    ```
  - `internal FrameTimeStatistics? LatestFrameTimes { get; private set; }` — ARTEL-346이 읽어 갈 지점. 이번 작업의 소비자는 여기까지다
  - `private static float ResolveFrameBudgetSeconds()` — 위 Context의 우선순위 규칙
  - **에디터에서의 `Application.isFocused`**: 에디터 애플리케이션이 포커스를 가졌는지를 뜻한다. Game view가 아니라 에디터 창 기준이므로, 에디터에서 작업 중이면 대체로 true다. 다른 앱으로 넘어간 동안만 빠진다. 의도한 동작이고 주석으로 남긴다

- [ ] **Step 4: 테스트**
  - 파일: `Tests/Runtime/Diagnostics/FrameTimeRecorderTests.cs` (+ `.cs.meta`)
  - 순수 클래스라 Unity 런타임 없이 도는 EditMode 테스트. `[Test]`만 쓰고 `[UnityTest]`는 불필요
  - 케이스
    - 균일한 16.67ms 프레임 → 평균/min/max가 같고 hitch 0
    - 경계 전에는 `TryAggregate`가 false
    - 한 프레임만 예산 2배 초과 → `HitchCount == 1`, `MaxSeconds`가 그 값
    - `counted: false` 프레임은 `FrameCount`에 들어가지 않는다
    - `deltaSeconds <= 0`은 버려진다
    - 첫 기록은 버려진다 — 큰 값 하나를 먼저 넣어도 `HitchCount == 0`
    - `SampledSeconds`가 집계된 프레임타임의 합과 같다
    - 집계하지 않는 프레임에서는 예산 델리게이트가 호출되지 않는다 (호출 횟수를 세는 델리게이트로 검증)
    - 전 구간 비포커스면 경계를 넘겨도 false이고, 다음 구간 경계는 밀려 있다
    - 백분위: 100 샘플 중 99개가 10ms, 1개가 100ms → `Percentile99Seconds`가 큰 값 쪽, `Percentile95Seconds`는 10ms
    - 1% low / 0.1% low가 최악 프레임 기준으로 계산된다
    - 집계 후 구간이 리셋되어 다음 집계에 이전 샘플이 섞이지 않는다
    - capacity를 넘겨 밀어 넣으면 `FrameCount == capacity`이고 오래된 값이 밀려난다

## Validation

- **Commands to run:**
  - `project.md`의 throwaway 프로젝트 EditMode 러너로 `FrameTimeRecorderTests` 실행
  - 변경 전 merge-base에서 베이스라인 먼저 확보 — `project.md`가 기록한 환경적 실패 8건과 구분해야 한다
  ```bash
  Unity -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath <throwaway-project> -testResults results.xml -logFile unity.log
  ```
- **Expected output:** 신규 테스트 전부 통과. 기존 실패 8건은 베이스라인과 동일해야 하고 늘어나면 안 된다. 종료 코드가 아니라 `results.xml`을 판정 근거로 쓴다
- **Unity 가용성**: `project.md`가 적어 둔 러너 경로는 macOS 것이고, 현재 작업 환경은 WSL이다. 실행 전에 Unity 설치 여부를 확인하고, **없으면 테스트를 돌렸다고 보고하지 않는다.** 이 경우 코드 리뷰와 정적 검토까지만 하고 PR 본문에 "EditMode 테스트 미실행 — 환경에 Unity 없음"을 명시한다. 리뷰어가 로컬에서 돌릴 수 있도록 명령은 그대로 남긴다
- **Manual:** 이 저장소 루트는 Unity 프로젝트가 아니다. 이슈의 Validation Notes(부하 씬에서 hitch 증가, `targetFrameRate` 30 반영)는 `samples/WordVenture`에서 확인해야 하며, Windows/macOS 실기 확인은 ARTEL-346으로 전송 경로가 생긴 뒤가 실효적이다. 이번 PR에서 못 돌린 항목은 그대로 명시한다

## Risks & Rollback

- **Risks:**
  - `ArtelManager.Update` 최상단에 코드가 추가된다. 매 프레임 도는 자리라 비용이 곧 SDK 오버헤드다. 링버퍼 쓰기 한 번 + 시각 비교 한 번으로 제한한다
  - 5초 창(약 300 샘플)에서 0.1% low는 최대 프레임타임과 사실상 같다. 통계가 아니라 정의의 한계이므로 주석과 필드명으로 오해를 막는다
  - `ResolveFrameBudgetSeconds`가 플랫폼별로 다른 값을 낼 수 있다. 예산 자체를 결과에 실어 사후 해석이 가능하게 한다
  - `LatestFrameTimes`는 ARTEL-346이 붙기 전까지 소비자가 없다. 죽은 코드로 방치되지 않도록 ARTEL-346이 이 이슈에 blocked by로 걸려 있다
- **Rollback steps:** 신규 파일 삭제 + `ArtelManager` 변경 `git revert`. 기존 동작에 얹는 구조라 부작용 없이 되돌아간다

## Rejected feedback

- **"`PointOnePercentLowFps`는 300 샘플에서 무의미하니 빼라"** — 이슈 AC가 명시적으로 요구한다. 정의(최악 `max(1, ceil(n/1000))` 프레임 평균)를 문서화하는 선에서 유지한다
- **"`ArtelManager` 연결을 이번 범위에서 빼고 순수 클래스만 만들라"** — 그러면 "집계 주기마다 산출된다"는 AC를 구동할 주체가 없어 검증이 불가능해진다. 연결은 유지하되 소비자는 `LatestFrameTimes` 노출까지로 제한한다
- **"`Runtime/Diagnostics/` 신규 디렉터리는 과하다"** — `Tracking`, `Streaming`, `Capture`처럼 관심사별 디렉터리가 이미 관례다. 관례를 따르는 편이 일관적이다

## Open Questions

- 집계 주기 5초가 적절한가. ARTEL-346이 "1~5초, 설정 가능"으로 잡혀 있어 거기서 확정될 값이다. 이번에는 상수로 두고 설정 노출은 넘긴다
- capacity 600(10초 @60fps)이면 5초 창에서는 넉넉하다. 고주사율(240Hz)에서는 5초에 1200 샘플이라 링버퍼가 밀린다. 밀려도 최근 600 프레임 기준으로는 정확하므로 이번 범위에서는 수용하고, 필요하면 ARTEL-346에서 주기와 함께 조정한다
