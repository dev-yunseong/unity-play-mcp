# 2026-07-22 — scan_all_scenes로 빌드 내 전체 씬 스캔

- Date: 2026-07-22
- Jira Issue: ARTEL-88
- Status: Implemented
- Repository: sdk
- Work Type: feat
- Depends on: ARTEL-87 (배치 디스패치 경로)

## Goal

에이전트가 게임 전체의 UI 지도를 한 번에 얻을 수단이 없다. `scan_scene`은 현재 활성 씬만 본다.

Build Settings에 등록된 모든 씬을 인덱스 순서대로 로드하며 각각 스캔하고, 결과를 `ALL_SCENES`
메시지 하나로 묶어 보내는 `scan_all_scenes`을 추가한다.

## Non-goals

- **부작용 없는 에디터 경로.** `EditorSceneManager.OpenScene`은 씬을 실행하지 않아 이 문서의 모든
  부작용을 피하지만, 플레이 모드에서 거부되므로 `scan_all_scenes` 안의 분기가 될 수 없다. 별도 메뉴
  커맨드로 한 번 만들었다가, 검증 수단이 없는 채로 표면만 늘린다는 판단으로 제거했다. 이 방향은
  ARTEL-90(빌드 타임 씬 지도)에서 다시 다룬다.
- **최상위 메시지 형태.** 씬 로드는 여러 프레임에 걸치므로 `HandleMessage`에서 즉시 응답할 수 없다.
  ACTION 배치 전용이다.
- **씬별 실패 격리.** 코루틴 안에서 `yield` 주위를 `try/catch`로 감쌀 수 없다. 기존
  `ExecuteActionRequest`와 같은 취약성을 그대로 둔다.
- **로드 순서 지정 / 부분 스캔.** 인덱스 0부터 전부 훑는다. 필터가 필요하면 별도 이슈.
- **씬 간 상태 보존.** 스캔용으로 잠깐 띄웠다 내리는 것이지, 게임을 진행시키는 것이 아니다.

## Context / Constraints

- **Additive 로드가 필수다.** `LoadSceneMode.Single`은 현재 씬을 파괴하고, `ArtelManager`가 그
  씬에 있다. 자기 자신을 destroy하면 이 코루틴이 중간에 죽는다. Additive는 현재 씬을 살려둔다.
- 이미 열려 있는 씬(대개 현재 씬)은 다시 로드하지 않는다. `GetSceneByPath(path).isLoaded`로 확인해
  제자리에서 스캔하고 언로드도 하지 않는다. 중복 인스턴스와 SDK 자폭을 동시에 피한다.
- `SceneScanner.Scan()`은 활성 씬만 훑으므로 씬마다 `SetActiveScene`이 필요하다.
- 로드 완료 시점에 `Awake`/`OnEnable`은 돌았지만 `Start`는 아직이다. UI 텍스트 상당수가 `Start`에서
  채워지므로 한 프레임(`yield return null`) 기다린 뒤 스캔한다.
- **`SceneStatePoller.ScanNow()`를 쓰지 않는다.** 그 경로는 해시 트래커를 갱신해서, 다른 씬의 스냅샷이
  현재 씬의 "변경 감지" 기준선을 오염시킨다. `scanner.Scan()` + `SceneSnapshotMapper.ToDto`를 직접 쓴다.
- **추적 액션을 커밋하지 않는다.** 방문한 씬의 pending 액션을 여기서 소비하면 다음 `GAME_STATE`에서
  사라진다.
- **방문 씬이 자기 언로드를 빠져나가는 통로가 둘이다.** `DontDestroyOnLoad`는 오브젝트를 별도 씬으로
  옮기고, `Awake`/`Start`가 `Instantiate`한 것은 그 시점 활성 씬 — 씬은 로드가 끝나야 활성화할 수
  있으니 결국 **게임의 원래 씬** — 에 들어간다. 둘 다 결과는 "어딘가에 새 루트 오브젝트"다.
  그래서 로드 직전 전체 루트를 스냅샷하고, 스캔 후 신규 루트를 `MoveGameObjectToScene`으로 곧
  언로드할 씬에 넘긴다. 언로드가 정상 경로로 파괴한다(`OnDestroy` 포함).
  비교와 언로드 사이에 `yield`를 두지 않아 그 틈으로 새로 생긴 것이 빠져나가지 않는다.
- **`DontDestroyOnLoad` 씬 핸들**은 공개 API가 없다. 임시 오브젝트를 넣었다 빼서 얻는다.
  그 씬은 `SceneManager.sceneCount`에도 안 잡혀서 따로 붙여야 한다.
- **정착 대기는 `WaitForSecondsRealtime`.** `timeScale = 0`인 게임에서 영원히 멈추지 않게 한다.
- **격리 씬은 폐기했다.** 활성 슬롯을 뺏는 방식이라 워크 도중 게임 자신의 `Instantiate`도 거기
  떨어지고, `GetActiveScene()`을 읽는 게임 코드가 엉뚱한 씬을 봤다. 사후 스냅샷·이주가 같은 문제를
  덜 침습적으로 푼다.
- **끝나고 재스캔해야 한다.** `scanner`의 `targetsById`는 마지막으로 훑은 씬 것인데 그 씬은 이미
  언로드됐다. 원래 활성 씬으로 되돌린 뒤 `scanner.Scan()`을 한 번 더 돌려 대상 ID 맵을 복구한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `SceneScanner.Scan`의 활성 씬 의존, `SceneSnapshotMapper.ToDto`,
      `SceneStatePoller.ScanNow`의 해시 트래커 부작용 확인.
- [x] **Step 1: Implementation**
  - `AllSceneScanner` 신규 — 빌드 인덱스 순회, additive 로드/언로드, 활성 씬 전환, 복구까지 담당.
  - `AllScenesMessageDto` / `ScannedSceneDto` 신규. 항목마다 `buildIndex`, `path`, `scene`.
    `scene`은 `GAME_STATE`와 동일한 `SceneDto`라 서버 파싱 경로가 하나로 유지된다.
  - `ArtelManager.ExecuteActionRequest`에 `scan_all_scenes` 분기, `SendAllScenes` 추가.
  - `StraySpawnTracker` 신규 — 로드 직전 전체 루트 스냅샷, 스캔 후 신규 루트를 언로드 대상 씬으로 이주.
  - 테스트 페이지에 `Scan all scenes` 버튼. 응답은 `GAME_STATE`와 같은 렌더러로 그리고, 언로드된
    씬의 컨트롤만 비활성.
- [x] **Step 2: Tests** — `SceneJsonContractTests`에 `ALL_SCENES` 직렬화 계약 테스트,
      `WebSocketTransportTests`에 테스트 페이지 마크업 계약 테스트 추가.
- [x] **Step 3: Rollout / Rollback** — 순수 추가. 호출하지 않으면 영향 없음.

## Validation

- **Commands to run:** Unity Test Runner (EditMode) — `Artel.Runtime.Tests`.
- **Expected output:** 신규 계약 테스트 포함 전체 통과.
- **실제 수행:** 이 작업 환경에 Unity 에디터가 없어 **실행하지 못했다**.
- **자동화 공백:** 씬 로드는 EditMode에서 동작하지 않아 `AllSceneScanner`의 순회 자체는 PlayMode
  테스트가 필요하다. 이 커밋에는 없다. 직렬화 계약과 테스트 페이지 마크업만 자동 검증되고,
  **로드·스캔·복구와 잔여 오브젝트 이주는 미검증**이다. 실제 빌드 씬이 등록된 프로젝트에서 수동
  확인이 필요하다. `StraySpawnTracker`도 EditMode 테스트로 덮을 수 없다 — `DontDestroyOnLoad`가
  플레이 모드 밖에서 동작하지 않는다.
- **테스트 페이지 렌더링은 검증됨:** 페이지 스크립트를 C# 문자열에서 추출해 Node에서 DOM 스텁과
  가짜 `ALL_SCENES` 페이로드로 실행, 살아있는 씬 버튼은 활성 / 언로드된 씬 버튼은 비활성 확인.

## Risks & Rollback

- **다른 씬의 코드가 실제로 실행된다.** `Awake`/`OnEnable`/`Start`가 돌면서 오디오, 네트워크 요청,
  `PlayerPrefs` 쓰기 같은 부작용이 진짜로 일어난다. README에 명시했고, 진행 중인 플레이 세션에서
  부를 메서드가 아니다.
- **정리가 되돌리지 못하는 것.** 방문 씬이 게임 오브젝트 **밑에 자식으로** 붙인 것(루트가 아니라
  씬 간 이동 불가), 정착 시간을 넘겨 실행되는 코루틴·`Invoke`·`async`·웹 요청 콜백,
  파괴된 매니저가 남긴 `static` 필드·이벤트 구독, 변경된 `ScriptableObject` 상태,
  이미 나간 오디오·네트워크 요청. 워크 도중 게임이 스스로 만든 루트도 신규로 판정돼 함께 죽는다.
  **청소지 격리가 아니다.** 부작용이 아예 용납되지 않는 용도라면 씬을 실행하지 않는 방식이 필요하고,
  그건 ARTEL-90의 범위다.
- **응답 크기.** 씬 수 × 블록 트리. 큰 프로젝트에서 한 메시지가 상당히 커질 수 있다. 페이지네이션은
  현재 없다.
- **소요 시간.** 씬당 최소 로드 1회 + 1프레임 + 언로드 1회. 배치 안의 뒤따르는 액션은 전부 대기한다.
- **반환된 블록 ID는 곧 죽는다.** 스캔이 끝나면 그 오브젝트들은 언로드된다. 구조 파악용이지
  `button_click` 대상으로 쓸 수 없다.
- **Rollback steps:** `git revert`.

## Open Questions

- 없음. 최초 요청은 `scan_all_scene`이었고, 복수형 `scan_all_scenes`로 확정했다. 서버·에이전트가
  아직 이 메서드를 부르지 않으므로 마이그레이션 부담은 없다.
- 응답이 커질 때 씬 단위 스트리밍(씬마다 `GAME_STATE` 한 건)으로 바꿀지. 지금은 한 건에 모은다.
