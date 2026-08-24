# 2026-08-21 — SDK 가 원격 스캔 명령을 받는다

- Date: 2026-08-21
- Jira: ARTEL-491
- Status: Done

## Goal

서버가 보낸 `scan_evidence` 액션을 받아 근거를 스캔하고, 앞 이슈(ARTEL-490)의
`EvidenceUploader` 로 그 문서를 올리고, 무엇이 되었는지 `ACTION_RESULT` 로 답한다.

## Non-goals

- 서버 쪽 트리거 API — ARTEL-492.
- 진행률(몇 %). 시작 · 끝만 답한다.
- 자동 스캔(부팅 시 · 주기적). 트리거는 서버가 보내는 액션뿐이다.
- HTTP 를 여기서 새로 짜는 것. 업로드는 ARTEL-490 의 `EvidenceUploader` 를 쓴다.

## Context / Constraints

**서버와 맞춘 계약** (ARTEL-492 가 같은 시각에 서버 쪽을 만든다)

- 서버 → SDK 액션 이름 `scan_evidence`, 파라미터 없음. 어느 빌드에 올릴지는 SDK 가 등록
  응답에서 받아 쥔 `gameBuildId` 가 정한다.
- SDK → 서버 결과는 기존 `ACTION_RESULT` 프레임에
  `{ "action": "scan_evidence", "success": true|false, "error": string|null }`.

**액션 수신 레일은 이미 있다.** 가상 입력(`set_axis` · `set_button`)이 지나는 길은
`ArtelManager.HandleMessage` → `EnqueueAction` → `ProcessActions` → `ExecuteActionRequest`
→ `ActionExecutor.Execute` 의 `switch` 다. `scan_evidence` 는 그 `switch` 에 갈래 하나로
들어간다. 큐가 액션을 하나씩 흘리므로 겹쳐 도는 것은 레일 자체가 막는다.

**함정:** `scan_all_scenes` 는 이것이 아니다. 옛 `AllSceneScanner` 를 돌려 `ALL_SCENES` 를
답하는 다른 기능이고 근거 문서와 무관하다.

**어셈블리 경계**

`Artel.Affordances.Scan` 은 `Artel.Runtime` 과 별개 어셈블리다(`Artel.Runtime` 이 그것을
참조한다). 그래서 `SceneWalk.InProgress` 는 `internal` 이라 Runtime 에서 보이지 않는다.
같은 자리에 이미 `AffordanceBootstrap.Watching => Live.Pulse.InProgress` 라는 선례가 있고
그 주석이 이유를 못 박는다 — 바깥의 호출자는 물어볼 다른 방법이 없다. 순회에도 같은 창을
낸다: `AffordanceBootstrap.Walking => SceneWalk.InProgress`.

## Approach (Checklist)

- [x] **Step 0: Recon** — 끝. 위 Context 가 결과다.

- [x] **Step 1: 스캔 이음매** — `Runtime/Affordance/Scan/AffordanceBootstrap.cs` 와
  `Runtime/Evidence/EvidenceScan.cs`
  - `AffordanceBootstrap` 에 `public static bool Walking => SceneWalk.InProgress`.
  - `IEvidenceScan`: 스캔을 돌리고 문서 바이트와 씬 수를 돌려주는 코루틴 하나.
    구현 `WalkedEvidenceScan` 이 `AffordanceBootstrap.WalkAllScenes()` 를 부르고
    `Walking` 이 내려갈 때까지 프레임을 넘긴 뒤 `ReportPath` 를 읽는다. 스캔 코드는
    새로 쓰지 않는다 — 이미 있는 것을 부른다.
  - 로드된 씬만 읽는 `CaptureNow()` 가 아니라 순회를 부른다. 서버가 청하는 것은 이
    빌드의 씬 명세 표이고, 지금 화면에 올라와 있는 씬 하나로는 그 표가 채워지지 않는다.

- [x] **Step 2: 액션 갈래** — `Runtime/ActionExecutor.cs`
  - `Execute` 의 `switch` 에 `case "scan_evidence"`. 파라미터 없음.
  - `ExecuteCaptureScreen` 과 같은 모양: 이음매가 없으면 그렇다고 답하고, 스캔이 실패하면
    그 사유를, 업로드가 실패하면 그 사유를 결과에 싣는다.
  - 릴리스 빌드는 거절한다 (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`). 근거는 애초에
    구워지지 않으므로 빈 문서를 올리는 것보다 거절이 정직하다.
  - `ArtelManager.Awake` 에서 `CaptureUploader` 옆에 `WalkedEvidenceScan` 과
    `EvidenceUploader` 를 물린다.

- [x] **Step 3: 결과 프레임** — `Runtime/Protocol/Dto/ActionResultDto.cs`
  - `[JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]`.
    `Ignore` 라서 이 필드를 채우지 않는 기존 액션의 결과는 바이트 하나 달라지지 않는다 —
    `returnValue` 가 같은 이유로 그렇게 되어 있고 그것을 지키는 테스트도 이미 있다.
  - `Success(id, action, returnValue)` / `Failure(id, action, error)` 3-인자 팩토리.
    기존 2-인자 오버로드와 인자 수가 달라 모호해지지 않는다.
  - 무엇을 올렸는지는 `EvidenceScanResultDto` 로 `returnValue` 에 실린다 — 문서 지문과
    씬 수. 성공했다는 말만으로는 화면이 방금 앉은 표가 이번 스캔의 것인지 가릴 수 없다.

- [x] **Step 4: Tests** — `Tests/Runtime/EvidenceScanActionTests.cs` (EditMode)
  - 이음매 없는 executor 가 거절하고 그 사유를 답한다
  - 스캔 실패 사유가 결과의 `error` 에 실리고, 그때 업로드는 시도되지 않는다
  - 업로드 실패 사유가 결과의 `error` 에 실린다
  - 성공하면 `action` · 지문 · 씬 수를 답한다
  - 직렬화: `action` 을 채운 결과는 세 필드를 싣고, 채우지 않은 결과에는 그 키가 없다

- [x] **Step 5: Rollout / Rollback** — 플래그 없음. 서버가 `scan_evidence` 를 보내지
  않으면 아무 코드도 돌지 않고, 되돌리기는 `git revert` 한 번이다.

## Validation

- **Commands to run:**

  ```bash
  .github/scripts/setup-unity-test-project.sh <dest>
  Unity -batchmode -nographics -runTests -testPlatform EditMode \
    -projectPath <dest> -testResults <dest>/results.xml -logFile <dest>/unity.log
  python3 .github/scripts/summarize-test-results.py <dest>/results.xml EditMode
  # PlayMode 도 같은 방식으로
  ```

- **실측 (Unity 2022.3.34f1):**
  - 기준선 — EditMode 274 passed / 0 failed, PlayMode 14 passed / 0 failed
  - 변경 후 — EditMode 280 passed / 0 failed, PlayMode 14 passed / 0 failed
  - 는 6건이 `EvidenceScanActionTests` 다.
- **자동 테스트로 덮지 못한 것:**
  - 씬 순회 자체 (`WalkAllScenes` → `SceneWalk`). 플레이 모드에서 빌드의 씬을 하나씩
    띄우는 일이고, 테스트 프로젝트의 Build Settings 는 비어 있다.
  - 릴리스 빌드 거절 갈래. `UNITY_EDITOR` 가 언제나 켜져 있는 에디터 테스트에서는 그
    `#else` 가 컴파일되지 않는다.
  - 서버가 실제로 보낸 프레임이 이 갈래에 닿는 것. 짝 이슈(ARTEL-492)가 붙어야 확인된다.

## Risks & Rollback

- **Risks:**
  - `WalkAllScenes()` 는 진행 중이던 실행을 버리고 빌드의 모든 씬을 차례로 띄운다.
    `scan_evidence` 는 사람이 home 에서 누른 명령이므로 그것이 청한 바지만, 돌고 있는 QA
    런 위에서 부르면 그 런을 망친다. 서버 쪽 트리거가 그 판단을 쥔다.
  - 씬 순회는 씬당 최대 30초를 기다린다. 액션 결과가 그만큼 늦게 도착하고, 그 동안 액션
    큐가 막힌다. 서버는 결과를 붙잡고 기다리지 않으므로 계약상 문제는 아니다.
- **Rollback steps:** `git revert`.

## Open Questions

- 이슈 본문은 "받았다는 것과 끝났다는 것을 나눠 답한다"를 제약으로 단다. 이번 작업의
  지시는 결과를 기존 `ACTION_RESULT` 한 프레임으로 못 박았으므로, 받았다는 답을 따로
  보내지 않고 끝났을 때 한 번만 답한다. 서버는 그 답을 기다리지 않고 `ingested_at` 이
  바뀌는 것으로 완료를 알므로 화면은 막히지 않는다.
