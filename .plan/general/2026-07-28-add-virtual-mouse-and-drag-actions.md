# 2026-07-28 — SDK 마우스 위치 이동 · 키 입력 토글 · 드래그 앤 드랍

- Date: 2026-07-28
- Jira: ARTEL-154
- Status: Complete

## Goal

Agent가 `move_mouse`, `mouse_down`, `mouse_up`, `key_down`, `key_up` action으로
가상 마우스 위치와 버튼/키의 눌림 상태를 직접 제어하고, ACTION 큐의 직렬 실행을
이용해 `mouse_down → move_mouse → mouse_up` 조합만으로 드래그 앤 드랍을 완성한다.

드래그가 성립하려면 두 경로를 모두 채워야 한다.

1. **polling 경로** — 게임이 `Input.mousePosition`, `Input.GetMouseButton*`으로
   직접 읽는 경우. `ArtelInput` proxy와 ILPP 치환 대상 확장으로 채운다.
2. **EventSystem 경로** — uGUI가 `IBeginDragHandler`/`IDragHandler`/
   `IEndDragHandler`/`IDropHandler`를 `PointerEventData`로 호출하는 경우.
   전용 dispatcher로 채운다.

## Non-goals

- 기존 `button_click`의 `button.onClick.Invoke()` 경로를 pointer event 경로로
  교체하지 않는다. 두 경로는 별개 action으로 공존한다. 통합은 후속 이슈.
- OS 레벨 마우스 커서/이벤트 주입. 가상 상태와 EventSystem dispatch만 다룬다.
- Unity New Input System (`UnityEngine.InputSystem`) 지원.
- `Input.mouseScrollDelta`, `Input.mousePresent`, 터치 계열 API.
- 우클릭/휠 버튼 전용 UI 시나리오 검증. 버튼 인덱스는 받되 검증은 좌클릭 기준.
- 드래그를 한 방에 수행하는 편의 action(`drag_and_drop(a, b)`). 큐 조합으로
  충분하며, 필요해지면 그때 추가한다.

## Context / Constraints

- ACTION 큐는 이미 직렬이다. `ArtelManager.EnqueueAction`(ArtelManager.cs:284)이
  `ProcessActions` 코루틴 하나로 `Dequeue`하고, 배치 내부
  `ExecuteActionRequest`도 action마다 `yield return`한다. 따라서
  `[mouse_down, move_mouse, move_mouse, mouse_up]`의 순서와 프레임 간격이
  보장된다. 큐 구조는 그대로 둔다.
- 현재 `CursorController`(CursorController.cs:44)는 `RectTransform` 하나만 받아
  오버레이 `Image`를 옮긴다. 화면 좌표로 옮기는 진입점이 없고, 이동 중 매 프레임
  콜백도 없다.
- `VirtualKeyboardState`는 `DurationSeconds` 기반 자동 해제만 지원한다
  (VirtualKeyboardState.cs:98). 무기한 홀드 상태 표현이 없다.
- `InputMethodWeaver.SupportedMethodNames`(InputMethodWeaver.cs:10)는 키 계열 5개
  뿐이다. mouse 계열 호출은 IL 치환 대상이 아니다.
- 레포 전체에 `PointerEventData` / `ExecuteEvents` 사용처가 없다. uGUI 드래그
  인터페이스는 지금 어떤 경로로도 호출되지 않는다.
- 최근 씬 스캔이 화면 좌표(`ScreenRectDto`)를 싣기 때문에, Agent는 블록의 화면
  좌표를 읽어 `move_mouse` 파라미터를 계산할 수 있다. 그래서 `move_mouse`는
  화면 좌표 단일 형태로 둔다.
- 그 `rect`는 **좌상단 원점** 픽셀인데 Unity 스크린 공간은 좌하단 원점이다.
  `move_mouse`는 스캔이 보고한 좌표계를 그대로 받고, 뒤집기는 `ActionExecutor`
  한 줄에 가둔다. 호출자가 `rect` 숫자를 그대로 넣으면 되고, Y를 뒤집을지
  말지를 Agent나 릴레이 계층이 판단할 일이 없어진다.
- 가상 입력 조회는 frame snapshot이어야 하며 소비되지 않아야 한다. 기존
  `physical || virtual` 합성 규칙을 유지한다.
- 액션 수신은 `ArtelManager.Update()`에서 일어나므로, 눌림 시작 프레임은
  기존 `Click`과 동일하게 `currentFrame + 1`로 둔다. script execution order와
  무관하게 다음 프레임부터 관측된다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 완료. ACTION 큐 직렬성, `CursorController` 진입점,
      `VirtualKeyboardState` 수명 모델, weaver 치환 범위, pointer event 부재 확인.

- [x] **Step 1a: 가상 키 홀드** — `VirtualKeyboardState`
  - `KeyClickState.DurationSeconds`를 `float?`로 바꾼다. `null`이면 무기한 홀드.
  - `Press(KeyCode, currentFrame)`: `StartFrame = currentFrame + 1`, duration `null`.
  - `Release(KeyCode, currentFrame)`: `ReleaseFrame = currentFrame + 1`.
    미등록 키 해제는 no-op.
  - `Refresh`는 duration이 `null`인 상태를 자동 해제하지 않는다.
  - `ReleaseAll(currentFrame)`: 홀드 중인 모든 키를 해제한다.
  - 기존 `Click(key, duration, frame)` 동작은 그대로 유지한다.

- [x] **Step 1b: 가상 마우스** — `Runtime/UnityEngine/VirtualMouseState.cs` 신규
  - `Vector2 Position`, `bool HasPosition` (한 번도 이동한 적 없으면 physical 위임).
  - 버튼 인덱스(0/1/2)별 `Press`/`Release`, `GetButtonDown`/`GetButton`/`GetButtonUp`,
    `ReleaseAll`, `Clear`. 프레임 규칙은 키보드와 동일하게 맞춘다.
  - `KeyClickState`와 공통 로직이 생기면 그때 뽑는다. 선제 추상화는 하지 않는다.

- [x] **Step 1c: proxy 확장** — `ArtelInput`
  - `mousePosition` (`Vector3`): 가상 이동이 한 번이라도 있었으면 가상 위치,
    아니면 `UnityEngine.Input.mousePosition`.
  - `GetMouseButton/Down/Up(int)`: `physical || virtual`.
  - `MoveMouse`, `PressMouseButton`, `ReleaseMouseButton`, `PressKey`, `ReleaseKey`
    internal 진입점.
  - `ReleaseAllVirtualInput()`: 키·버튼 전부 해제. 연결 종료 시 호출.
  - `AdvanceFrame`과 `ResetVirtualKeyboard`가 마우스 상태도 함께 다루도록 확장.

- [x] **Step 1d: weaver** — `InputMethodWeaver.SupportedMethodNames`에
  `get_mousePosition`, `GetMouseButton`, `GetMouseButtonDown`, `GetMouseButtonUp`
  추가. signature 매칭 방식은 그대로.

- [x] **Step 1e: 커서 이동** — `CursorController`
  - `IEnumerator MoveTo(Vector2 screenPosition, Action<Vector2> onMoved)` 추가.
    기존 `MoveTo(RectTransform)`은 대상 중심 좌표를 구해 이 오버로드에 위임한다.
  - 보간 루프의 매 프레임마다 `onMoved`를 호출해 가상 마우스 위치와 pointer
    이동 이벤트가 실제 커서와 같은 궤적을 그리게 한다.

- [x] **Step 1f: pointer event dispatch** — `Runtime/PointerEventDispatcher.cs` 신규
  - `EventSystem.current`가 없으면 조용히 no-op. polling 경로는 계속 동작한다.
  - `Press(position, button)`: `RaycastAll` → `pointerDownHandler`(hierarchy) →
    `pointerPress` 보관, `GetEventHandler<IDragHandler>`로 `pointerDrag` 보관,
    `initializePotentialDrag`.
  - `Move(position)`: `delta` 갱신, `pointerEnter`/`pointerExit` 전이 처리.
    버튼이 눌린 상태이고 `pointerDrag`가 있으면 첫 이동에서 `beginDragHandler`,
    이후 매 이동에서 `dragHandler`.
  - `Release(position, button)`: `pointerUpHandler` → 드래그 중이었으면
    `endDragHandler` + 현재 raycast 대상에 `dropHandler`, 아니었고 같은 대상이면
    `pointerClickHandler`. 상태 초기화.
  - `PointerEventData`의 `position`, `delta`, `pressPosition`,
    `pointerPressRaycast`, `pointerCurrentRaycast`, `button`, `dragging`,
    `eligibleForClick`을 EventSystem이 채우는 값과 같게 유지한다.

- [x] **Step 1g: action dispatch** — `ActionExecutor`
  - `move_mouse` — params `[x, y]`. 커서를 화면 좌표로 이동시키며 매 프레임
    가상 위치와 `dispatcher.Move`를 갱신한다.
  - `mouse_down` / `mouse_up` — params `[]` 또는 `[button]` (기본 0, 0~2 검증).
  - `key_down` / `key_up` — params `[keyCode]`. 기존 `TryReadKeyCode` 재사용.
  - 파라미터 파싱 실패 시 기존 형식과 같은 문구로 실패 결과를 만든다.
  - `ArtelManager.Awake`에서 dispatcher를 만들어 `ActionExecutor`에 주입한다.

- [x] **Step 1h: 홀드 누수 차단**
  - `ArtelManager.StopTransport`(및 `OnDisable`) 경로에서
    `ArtelInput.ReleaseAllVirtualInput()`과 dispatcher 상태 초기화를 호출한다.
  - 연결이 끊긴 뒤 키/버튼이 영구히 눌린 채 남지 않게 한다.

- [x] **Step 1i: 상태 표시** — `KeyboardStatusController`가 눌린 마우스 버튼과
  가상 커서 좌표도 함께 표시한다. 홀드 상태는 화면에 보이지 않으면 디버깅이
  불가능하다. 표시 문자열 조립은 기존 `FormatPressedKeys` 옆에 둔다.

- [x] **Step 2: Tests** — 순수 로직은 EditMode(`Artel.Runtime.Tests`), 런타임
  생명주기가 필요한 것은 신규 PlayMode 어셈블리(`Artel.Runtime.PlayModeTests`)
  - `VirtualKeyboardStateTests` — 홀드가 duration 없이 유지되고 `Release` 프레임에만
    `GetKeyUp`이 참, `ReleaseAll` 동작.
  - `VirtualMouseStateTests` — down/hold/up 프레임 규칙, 위치 갱신,
    이동 전에는 `HasPosition`이 거짓.
  - `PointerEventDispatcherTests` — 드래그 인터페이스 4종을 구현한 fixture에
    press→move→move→release 시 `beginDrag` 1회, `drag` N회, `endDrag` 1회,
    드롭 대상에 `drop` 1회가 순서대로 오는지. EventSystem 부재 시 예외 없이 no-op.
  - `CursorControllerTests` — 화면 좌표 오버로드가 커서를 옮기고 `onMoved`를
    호출하는지.
  - `ActionBatchTests` — `[mouse_down, move_mouse, mouse_up]` 배치 한 건이
    fixture에 완전한 드래그 시퀀스를 남기는지. 파라미터 검증 실패 케이스.
  - 수동 검증: 로컬 test page에서 좌표 입력 → 이동 → 버튼 홀드 → 이동 → 해제.

- [x] **Step 3: Test page & 문서**
  - `ArtelTestPage`에 좌표 입력 + 이동, 마우스 버튼 down/up, 키 down/up 컨트롤 추가.
  - `Packages/kr.artel.sdk/README.md`에 새 action 5종, 드래그 조합 예시 JSON,
    `mousePosition` 합성 규칙, EventSystem 없을 때의 동작, 홀드 해제 책임을 기록.

- [x] **Step 4: Rollout / Rollback** — 순수 추가 변경. 기존 action과 weaver
  치환 대상은 그대로라 기존 클라이언트 동작이 바뀌지 않는다. 단일 feature
  commit 단위로 되돌릴 수 있게 유지한다.

## Validation

- **Commands to run:** `git diff --check`; Unity 2022.3.34f1 batchmode
  `-runTests -testPlatform EditMode` 및 `-testPlatform PlayMode`
- **Expected output:** whitespace error 없음; 신규 테스트 통과, 기존 실패 목록 불변
- **Result:**
  - `git diff --check` 통과.
  - EditMode `100/108`. 실패 8건은 clean develop 베이스라인(`88/96`)의 실패와
    동일한 목록이며 신규 회귀는 0건이다. 전부 EditMode가 `Awake`,
    `OnEnable`, `DontDestroyOnLoad`를 실행하지 않아서 나는 기존 문제다.
  - PlayMode `9/9`.

### 이번에 드러난 테스트 인프라 문제

- `samples/WordVenture/Packages/manifest.json`에 `testables`가 없어 Unity가
  패키지 안의 테스트 어셈블리를 아예 발견하지 못한다. 이 상태로 batchmode를
  돌리면 `total=0`으로 **exit code 0** 이 나온다. 조용히 통과처럼 보인다.
  이번 검증은 워크트리 샘플 프로젝트에만 임시로 `testables`를 넣어 돌린 뒤
  되돌렸다. `samples/WordVenture`는 별도 서브모듈이라 이 레포 커밋으로는
  고칠 수 없다. 별도 이슈가 필요하다.
- 기존 테스트 8건이 EditMode에서 실패 중이다. 위 `testables` 문제 때문에
  아무도 실행하지 않아 드러나지 않았다. 이 변경의 범위 밖이라 손대지 않았다.
- 그래서 런타임 생명주기가 필요한 신규 테스트는 EditMode에 얹지 않고
  `Artel.Runtime.PlayModeTests` 어셈블리를 새로 만들어 그쪽에 두었다.

## Risks & Rollback

- **Risks:**
  - `Input.mousePosition` 치환은 키 치환보다 호출 빈도가 훨씬 높다. proxy가
    매 프레임 다수 호출되므로 조회 경로에 할당이 없어야 한다.
  - 가상 위치가 physical 위치를 덮는 규칙(`HasPosition` 이후 영구 우선)은 사람이
    직접 조작하는 세션과 섞이면 혼란을 준다. 연결 종료 시 초기화로 완화한다.
  - `mouse_down` 후 `mouse_up`이 오지 않으면 버튼이 눌린 채 남는다. 연결 종료
    해제로 막지만, 연결이 살아있는 채로 클라이언트가 잊으면 남는다.
  - pointer event 수동 조립은 EventSystem의 실제 구현과 어긋날 수 있다. 특히
    `pointerEnter` 전이와 `eligibleForClick` 판정. fixture 테스트로만 검증되며
    실제 게임 UI에서의 차이는 남는 위험이다.
  - `ExecuteEvents` 경로가 `button_click`과 별개라, 같은 버튼을 두 경로로
    누르면 게임 쪽에서 중복 처리로 보일 수 있다.
  - `scan_all_scenes`처럼 씬을 오가는 동작 중 홀드가 유지되면 대상 오브젝트가
    파괴돼 dispatcher가 죽은 참조를 들고 있을 수 있다. 대상 유효성을 매번 확인한다.
- **Rollback steps:** feature commit revert. weaver 치환 대상이 되돌아가므로
  사용자 코드 변경 없이 기존 동작으로 복귀한다.

## Open Questions

- 없음. "토글"은 `key_down`/`key_up` 명시 쌍으로 구현한다. 상태를 뒤집는 단일
  action은 SDK와 Agent가 서로 다른 상태를 믿을 때 복구가 불가능해 채택하지 않는다.
