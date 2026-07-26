# 2026-07-26 — 비활성 버튼/입력 필드 조작 차단

- Date: 2026-07-26
- Jira: ARTEL-133
- Status: Implemented

## Goal

SDK가 UI의 상호작용 가능 여부를 씬 스냅샷에 싣고, 비활성 대상에 대한 `button_click`/`enter_text`를 실패로 돌려준다.

## Non-goals

- 커스텀 컴포넌트의 `[ArtelAction]` 액션 게이팅. 상호작용 가능 여부라는 개념이 없다.
- 비활성 UI를 스냅샷에서 제거하는 것. 에이전트는 "버튼이 있는데 잠겨 있다"를 관측할 수 있어야 한다.
- 오케스트레이션 서버의 조작 후보 필터링. ARTEL-134에서 처리한다.

## Context / Constraints

- `ScannedTarget.CanClick`은 `Button` 컴포넌트 존재 여부만 본다. `button.onClick.Invoke()`를 직접 호출하므로 Unity가 사람 입력에 적용하는 비활성 차단을 통과한다. `EnterText`도 동일하다.
- 판정 기준은 `isActiveAndEnabled && Selectable.IsInteractable()`이다. 셋 다 필요하다.
  - `interactable` 플래그만 보면 부모 `CanvasGroup`이 막아 둔 UI를 놓친다. `IsInteractable()`이 그 둘(`m_GroupsAllowInteraction && m_Interactable`)을 합쳐 준다.
  - 그런데 `IsInteractable()`은 `Behaviour.enabled`와 `gameObject.activeInHierarchy`를 보지 않는다. 사람 입력은 `ExecuteEvents.ShouldSendToComponent`가 `isActiveAndEnabled`로 거른다. 즉 컴포넌트가 꺼졌거나 패널이 `SetActive(false)`된 버튼은 사람은 못 누르는데 `IsInteractable()`은 `true`를 준다. 커서 이동(최대 0.35초) 중에 패널이 닫히는 것이 이 창에서 가장 흔한 일이므로 최종 판정에서 특히 중요하다.
  - Unity 널 검사가 먼저다. 파괴된 오브젝트에서 `isActiveAndEnabled`를 읽으면 `MissingReferenceException`이 난다.
- 나란히 진행 중인 `scan_all_scenes` full 모드는 비활성 오브젝트까지 스냅샷에 싣는다. 그 변경이 들어오면 비활성 오브젝트의 버튼도 스캔되는데, `isActiveAndEnabled` 판정이 그 경우를 `interactable: false`로 일관되게 처리한다. 이 작업은 `origin/develop` 기준의 별도 worktree에서 진행하며 그 변경에 의존하지 않는다.
- 씬 JSON은 오케스트레이션 서버와의 계약이다. 필드 추가만 하고 기존 필드 이름과 형태는 유지한다.
- `GAME_STATE`와 `ALL_SCENES`가 `SceneSnapshotMapper`를 공유하므로 매퍼 한 곳만 고치면 두 경로가 함께 반영된다.
- 커서 이동(`CursorController.MoveTo`)이 여러 프레임에 걸치므로 이동 도중 게임이 버튼을 잠글 수 있다. 최종 판정은 실제 호출 직전에 한 번 더 이뤄져야 한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `SceneScanner.cs`(스캔/실행 대상 `ScannedTarget`), `ActionExecutor.cs`, `Domain/ButtonComponent.cs`, `Domain/EditTextComponent.cs`, `Protocol/Dto/ButtonComponentDto.cs`, `Protocol/Dto/EditTextComponentDto.cs`, `Protocol/Mapping/SceneSnapshotMapper.cs` 확인 완료.
- [x] **Step 1: Implementation**
  - `Domain/ButtonComponent.cs`, `Domain/EditTextComponent.cs`: `bool Interactable` 속성 추가. 생성자 인자는 기존 배치를 따라 데이터 필드 뒤, `states`/`actions` 앞에 둔다 — `ButtonComponent(name, interactable, states, actions)`, `EditTextComponent(name, content, placeholder, interactable, states, actions)`. 두 클래스 모두 `sealed`이므로 하위 클래스 파급은 없다.
  - `Runtime/SceneScanner.cs` `ScannedTarget`: `IsClickInteractable`(= `button != null && button.isActiveAndEnabled && button.IsInteractable()`), `IsTextEntryInteractable` 추가. 후자는 `EnterText`가 쓰는 순서(`InputField` 먼저, 없으면 `TMP_InputField`)를 그대로 따라 같은 대상을 판정한다. 캐시하지 않고 매번 컴포넌트를 읽는 속성이라 스캔 시점과 실행 시점의 값이 각각 그때의 진실이다. `CreateComponents`가 그 값을 도메인 컴포넌트에 넘긴다. `Click()`/`EnterText()`는 비활성이면 아무것도 하지 않고 `false`를 돌려준다.
  - `Protocol/Dto/ButtonComponentDto.cs`, `EditTextComponentDto.cs`: `[JsonProperty("interactable")] bool Interactable`.
  - `Protocol/Mapping/SceneSnapshotMapper.cs`: 도메인 값을 DTO로 옮긴다.
  - `Runtime/ActionExecutor.cs`: 검사 순서를 명시한다. 기존 "알 수 없는 id → 타입 불일치" 사슬 뒤에 상호작용 검사를 잇는다.

    ```text
    if (!TryGetTarget)          fail "Unknown target id: {id}"
    if (!target.CanClick)       fail "Target is not a Button: {id}"
    if (!target.IsClickInteractable) fail "Target is not interactable: {id}"   // 커서를 움직이기 전에 빠르게 거절
    yield return cursorController.MoveTo(...)
    completed(target.Click() ? Success : Failure("Target is not interactable: {id}"))   // 이동 중 잠긴 경우
    ```

    이동 전 검사는 빠른 거절, `Click()` 내부 검사는 이동 중 상태가 바뀐 경우의 최종 판정이다. 이동 전에 `CanClick`이 이미 참이었으므로 이동 뒤 `false`의 현실적 원인은 비활성화(혹은 파괴)뿐이고, 두 경우 모두 같은 메시지로 보고한다. `enter_text`도 동일한 순서를 따른다.
- [x] **Step 2: Tests**
  - `Tests/Runtime/SceneScannerTests.cs`: 비활성 `Button`, `CanvasGroup`으로 막힌 활성 `Button`, 비활성 `InputField`의 스캔 결과가 `Interactable = false`인지. 활성 대상은 `true`인지.
  - `Tests/Runtime/SceneJsonContractTests.cs`: `button`/`editText` JSON에 `interactable` 필드가 실리는지.
  - `Tests/Runtime/CursorControllerTests.cs`: 비활성 버튼에 `button_click`을 요청하면 `onClick` 리스너가 호출되지 않고 실패 결과가 오는지. 비활성 `InputField`에 `enter_text`를 요청하면 값이 바뀌지 않는지.
  - 이동 뒤 최종 판정: `ScannedTarget.Click()`이 비활성 버튼에서 `onClick`을 호출하지 않고 `false`를 돌려주는지 직접 확인한다. 결정적이고 프레임 타이밍에 의존하지 않는다.
  - 이동 중 잠기는 경로(타이밍 의존 변형)는 접었다. `MoveTo`의 진행이 `Time.unscaledDeltaTime`에 묶여 있어 EditMode 러너에서 0이 나오면 끝나지 않는 테스트가 된다. 같은 가드를 `Click()` 직접 검증이 결정적으로 덮는다.
- [x] **Step 3: Rollout / Rollback** — 플래그 없음. 씬 JSON에 필드가 추가되므로 서버(ARTEL-134)는 필드 부재를 하위 호환으로 처리한다. 배포 순서 제약 없음.

## Validation

- **Commands to run:** Unity Test Runner(EditMode)로 `Packages/kr.artel.sdk/Tests` 실행.
- **Expected output:** 신규 테스트 통과, 기존 `CursorControllerTests`/`SceneScannerTests`/`SceneJsonContractTests`/`ActionBatchTests` 회귀 없음.
- **실행 결과:** 이 환경에는 Unity 에디터가 없어 실행하지 못했다. 컴파일과 테스트 실행 모두 검증되지 않은 채로 남아 있으므로 PR에 명시하고, 리뷰어 또는 CI가 Unity Test Runner로 확인해야 한다.

## Risks & Rollback

- **Risks:**
  - 게임이 커스텀 로직으로 버튼을 잠그면서 `interactable`은 `true`로 두는 경우, 여전히 클릭이 통과한다. 이 변경의 범위 밖이다.
  - 반대로 게임이 `CanvasGroup`을 잠깐 껐다 켜는 연출을 쓰면 그 순간의 스냅샷이 비활성으로 보고된다. 다음 스냅샷에서 회복되므로 수용한다.
- **Rollback steps:** `git revert`. 서버는 필드 부재를 이미 하위 호환으로 처리하므로 서버 롤백이 함께 필요하지 않다.

## Notes

- `SceneStateHashTracker`가 직렬화된 `SceneDto`를 해시하므로, `interactable`이 뒤집히면 `GAME_STATE` 푸시가 발생한다. 잠김/풀림은 에이전트가 알아야 하는 변화이므로 의도한 동작이다.

## Open Questions

- 없음.
