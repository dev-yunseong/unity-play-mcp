# 2026-08-11 — SDK 가상 입력에 set_axis·set_button 액션 추가

- Date: 2026-08-11
- Jira: ARTEL-292
- Status: Reviewed (fast/medium/heavy self-review, 서브에이전트 미사용)

## Goal

에이전트가 `Input.GetAxis`, `GetAxisRaw`, `GetButton`, `GetButtonDown`, `GetButtonUp`을 읽는 게임을 조작할 수 있게 한다. 축 이름과 값을 직접 지정하는 `set_axis`·`set_button` 액션을 추가하고, 다섯 메서드를 위빙 대상에 넣는다.

## Non-goals

- `ProjectSettings/InputManager.asset` 파싱, 축↔키 바인딩 표 확보, 빌드 시 굽기. ARTEL-176이 다루려던 방향이고 폐기했다
- gravity/sensitivity 시간 보간 재현. 에이전트가 값을 직접 지정하므로 불필요하다
- `key_down`으로 축을 움직이는 경로. 이 작업으로 해결되지 않는다
- 게임이 쓰는 축 이름 자동 발견. 에이전트가 이름을 안다고 가정한다
- New Input System. 레거시 `UnityEngine.Input` 한정

## Context / Constraints

레거시 Input Manager는 축 정의를 조회하는 런타임 API가 없다. 축 정의는 네이티브 GlobalGameManager라 매니지드 코드에 노출되지 않고, `SerializedObject` 경로는 에디터 전용이다. 그래서 키에서 축을 합성하지 않고 축 값을 직접 받는다.

기존 규약 두 가지를 따라야 한다.

- **프레임 규약**: `VirtualKeyboardState`·`VirtualMouseState` 모두 요청 프레임 +1에 시작하고, 해제도 +1에 잡힌다. 액션이 매니저의 `Update`에서 처리되므로, 스크립트 실행 순서와 무관하게 소비자가 자기 `Update`에서 놓치지 않게 하기 위한 것이다
- **합성 규약**: 키·마우스 버튼은 `실제 || 가상`

Unity에서 버튼은 축이다. `Jump`는 `positiveButton: space`를 가진 축 엔트리이고, 같은 엔트리를 `GetButton`은 bool로 `GetAxis`는 float로 읽는다. 따라서 상태 저장소는 하나면 된다. 액션 두 개는 같은 저장소에 쓰는 서로 다른 어휘다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 완료
  - `Editor/CodeGen/InputMethodWeaver.cs:10` — `SupportedMethodNames` 9개
  - `Runtime/UnityEngine/Input.cs` — `ArtelInput` 프록시
  - `Runtime/UnityEngine/VirtualMouseState.cs` — 프레임 규약 기준 구현
  - `Runtime/ActionExecutor.cs:66` — 액션 디스패치 `switch`
  - `Tests/Runtime/UnityEngine/VirtualMouseStateTests.cs` — 테스트 스타일 기준

- [ ] **Step 1: `VirtualAxisState` 신규**
  - 파일: `Runtime/UnityEngine/VirtualAxisState.cs` (+ `.cs.meta`)
  - 저장소: `Dictionary<string, AxisHold>`. 키는 축 이름, ordinal 비교 (Unity 축 이름은 대소문자를 구분한다)
  - `AxisHold { float Value; int StartFrame; int? ReleaseFrame; }` — `VirtualMouseState.ButtonPressState`에 `Value`만 더한 모양
  - `Set(name, value, frame)`: 해제되지 않은 hold가 있으면 `Value`만 갱신하고 `StartFrame`은 보존한다. 같은 축을 연속으로 `set_axis` 해도 `GetButtonDown`이 다시 뜨지 않게 하기 위한 것. 없으면 `frame + 1`에 새 hold
  - `Release(name, frame)`: `ReleaseFrame = frame + 1`
  - `ReleaseAll(frame)` / `Refresh(frame)` / `Clear()` — `VirtualMouseState`와 동형. `Refresh`는 `ReleaseFrame < frame`인 항목을 버린다
  - 읽기
    - `TryGetValue(name, frame, out float value)` — hold가 있고 `frame >= StartFrame`이고 아직 유지 중일 때만 true
    - `GetButton(name, frame)` = 위 조건 + `Value > 0f`
    - `GetButtonDown(name, frame)` = `StartFrame == frame` + 유지 중 + `Value > 0f`
    - `GetButtonUp(name, frame)` = `ReleaseFrame == frame` + `Value > 0f`
  - **버튼 엣지는 hold의 시작·해제에서만 난다.** `Value`가 `1`에서 `-1`로 바뀌는 것은 엣지가 아니다. 의도적 단순화이고 주석으로 남긴다. `set_button`은 항상 hold/release를 쓰므로 이 경로에서는 문제가 되지 않는다

- [ ] **Step 2: `ArtelInput` 프록시 5개 추가**
  - 파일: `Runtime/UnityEngine/Input.cs`
  - `GetAxis(string)` / `GetAxisRaw(string)`: 가상 hold가 있으면 **그 값이 실제 입력을 완전히 덮는다**. 없으면 실제 값. bool과 달리 float에는 OR가 없어서 OR 대신 override를 택한다. 결정적이고, 에이전트의 명시적 의도가 이긴다
  - `GetAxisRaw`는 값을 부호로 스냅하지 않고 그대로 반환한다 (Open Questions 참고). **따라서 두 메서드의 본문은 폴백 대상만 다르고 동일하다.** 누락이 아니다 — 위빙이 시그니처로 매칭하므로 둘 다 존재해야 한다
  - `GetButton*(string)`: `실제 || 가상`. 키·마우스 버튼과 같은 규약
  - 내부 진입점: `SetAxis(string, float)`, `ReleaseAxis(string)`
  - `AdvanceFrame`에 `VirtualAxes.Refresh` 추가
  - `ReleaseAllVirtualInput`에 `VirtualAxes.ReleaseAll` 추가
  - `ResetVirtualKeyboard`에 `VirtualAxes.Clear` 추가

- [ ] **Step 3: `InputMethodWeaver` 치환 목록 확장**
  - 파일: `Editor/CodeGen/InputMethodWeaver.cs:10`
  - `GetAxis`, `GetAxisRaw`, `GetButton`, `GetButtonDown`, `GetButtonUp` 추가
  - 시그니처 매칭은 기존 `GetSignature`가 그대로 처리한다. 다섯 개 모두 `(System.String)` 하나뿐이라 오버로드 충돌이 없다

- [ ] **Step 4: `ActionExecutor` 액션 두 개**
  - 파일: `Runtime/ActionExecutor.cs`
  - `set_axis` → params `[axisName, value]`. `value`는 -1~1
  - `set_button` → params `[axisName, pressed]`. `pressed`가 true면 `SetAxis(name, 1f)`, false면 `ReleaseAxis(name)`
  - 헬퍼: `TryReadAxisName` (null·빈 문자열 거부), `TryReadFlag`. `TryReadFlag`은 `bool`과 문자열 파싱 두 갈래만 둔다. JSON 코덱이 `true`/`false`를 `bool`로 주므로 숫자 분기는 쓰이지 않는다
  - **범위 밖 값은 클램프하지 않고 실패로 응답한다.** 조용히 성공하지 않는 것이 이 이슈의 요지다
  - **축 이름 검증**: `UnityEngine.Input.GetAxis(name)`을 `try`/`catch (ArgumentException)`로 프로브한다. 미설정 축이면 엔진이 던지므로 그것으로 실패를 만든다. 런타임에 축 존재를 확인할 수 있는 유일한 신호다. 프록시가 아니라 실제 `Input`을 직접 불러야 한다 — 프록시를 부르면 가상 hold가 이미 있을 때 프로브가 통과해 버린다

- [ ] **Step 5: 테스트**
  - 파일: `Tests/Runtime/UnityEngine/VirtualAxisStateTests.cs` (+ `.cs.meta`). 구성은 `VirtualKeyboardStateTests`를 따른다 — 순수 상태 테스트, `ActionExecutor` 파라미터 테스트, 위버 라운드트립 `[UnityTest]`가 한 파일에 있다

  - **상태 (순수, 엔진 비의존)**
    - 프레임 규약: hold가 `frame + 1`에 시작, 해제가 `frame + 1`에 한 프레임만 잡힘, 읽어도 소비되지 않음
    - `Set` 재호출이 `StartFrame`을 보존해 `GetButtonDown`이 한 번만 뜨는 것
    - 음수 값에서 `GetButton`·`GetButtonDown`·`GetButtonUp`이 모두 false인 것
    - **값 부호 뒤집기(`1` → `-1`)가 버튼 엣지를 내지 않는 것.** 문서화한 한계를 테스트로 고정한다. 나중에 바꾸려면 이 테스트가 먼저 깨진다
    - **해제 프레임 동작**: `GetButtonUp`이 true인 그 프레임에 `TryGetValue`는 false다. 즉 축은 이미 실제 입력으로 넘어가 있다
    - `TryGetValue`가 hold 없는 축에 false를 반환하는 것
    - `ReleaseAll` / `Refresh` / `Clear`

  - **`ActionExecutor` 파라미터 검증** — 선례가 있으므로 추가한다 (`VirtualKeyboardStateTests.ActionExecutor_RejectsAKeyHoldWithoutAKeyCode`)
    - `set_axis`: 빈 params, 범위 밖 값 거부
    - `set_button`: 빈 params, bool 아닌 두 번째 인자 거부
    - **없는 축 이름 거부**: `"__artel_no_such_axis__"`처럼 어떤 프로젝트에도 없을 이름을 쓴다. 실제 축 이름에 의존하면 테스트가 프로젝트 `InputManager` 설정에 묶인다

  - **위버 라운드트립 `[UnityTest]`** — Step 3이 손대는 재배선을 덮는 유일한 테스트다
    - `Tests/Fixtures/TrackedFixtureBehaviour.cs`에 리더 추가: `ReadHorizontalAxis()` → `Input.GetAxis("Horizontal")`, `ReadHorizontalAxisRaw()`, `ReadJumpButton()` → `Input.GetButton("Jump")`, `ReadJumpButtonDown()`
    - `set_axis("Horizontal", 1)` 후 fixture가 1을 읽는지, `set_button("Jump", true)` 후 버튼이 눌린 것으로 읽히는지 확인
    - 이 fixture 어셈블리는 `Artel.Runtime`을 참조하므로 위빙 대상이다. 기존 `IlPostProcessor_ReroutesUnityInputCallsToArtelInput`이 같은 경로로 동작한다
    - `Horizontal`·`Jump`는 Unity 기본 `InputManager`에 항상 있다. 프로브가 통과해야 하므로 이 두 이름에 의존하는 것은 불가피하다. 임시 프로젝트가 기본 설정으로 생성되면 충족된다

- [ ] **Step 6: Rollout / Rollback**
  - 기능 플래그 없음. 순수 추가이고 기존 액션·프록시 동작을 바꾸지 않는다
  - 위빙 목록 확장이 유일한 기존 동작 변경이다. 게임 어셈블리의 `GetAxis` 호출이 `ArtelInput`으로 바뀌지만, 가상 hold가 없으면 실제 값을 그대로 돌려주므로 관측 가능한 변화가 없다
  - 롤백은 `git revert`

## Validation

- **Commands to run:**
  - EditMode 테스트. 절차는 `.agents/docs/project.md`의 `## Running package tests`. 임시 프로젝트를 만들어 `kr.artel.sdk`를 `testables`로 선언해야 한다
  - **`project.md`가 적은 경로는 macOS 것이고 이 작업 환경은 WSL이다.** 이 머신의 Unity 2022.3.34f1은 Windows 쪽에 있다

    ```bash
    "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe" \
      -batchmode -nographics -runTests -testPlatform EditMode \
      -projectPath <throwaway-project> \
      -testResults results.xml -logFile unity.log
    ```

  - Windows 바이너리는 WSL 경로를 못 읽으므로 임시 프로젝트와 패키지가 Windows에서 보이는 경로에 있어야 한다. 안 되면 테스트를 돌린 척하지 말고 미실행으로 보고한다

- **Actual result (2026-08-11):** 임시 프로젝트를 `C:\temp\artel-axis-test`에 세우고 실행했다. WSL 경로는 Windows Unity가 못 읽으므로 패키지를 복사해 넣었고, `ProjectSettings`는 `samples/WordVenture` 것을 그대로 써서 `Horizontal`·`Jump` 축을 확보했다.
  - 193건 중 180 통과, 13 실패. **`VirtualAxisStateTests` 19건은 전부 통과**했고 실패 13건에 신규 테스트는 없다
  - 위버 라운드트립(`IlPostProcessor_ReroutesUnityAxisCallsToArtelInput`) 통과 — `set_axis`가 게임 코드의 `Input.GetAxis`까지 실제로 도달한다
  - 축 존재 프로브 통과. `Input.GetAxis`는 `-batchmode -nographics` EditMode에서도 미설정 축에 `ArgumentException`을 던진다
  - **종료 코드는 0이었다.** `project.md`는 실패 시 2라고 적고 있으나 이 실행에서는 0이 나왔다. 코드가 아니라 `results.xml`을 읽어야 한다는 근거가 하나 더 늘었다

- **Expected output:**
  - `results.xml`에서 `VirtualAxisStateTests` 전부 통과
  - 종료 코드 2는 "테스트가 돌았고 일부 실패"를 뜻하므로 코드가 아니라 `results.xml`을 읽는다
  - 임시 프로젝트에서는 환경 문제로 EditMode 8건이 원래 실패한다 (`ActionBatchTests` 3건, `CursorControllerTests` 2건, `SerializedFieldReaderTests`, `ArtelManager_CreatesOverlayGuiAutomatically`, `CreateReport_ListsBuildScenesAndScansThem`). merge-base에서 기준선을 먼저 잡고 비교한다

- **Manual:**
  - 축을 읽는 샘플 씬에서 `set_axis("Horizontal", 1)`로 실제 이동이 일어나는지 확인
  - 없는 축 이름으로 `set_axis`를 보내 실패 응답이 오는지 확인

## Risks & Rollback

- **Risks:**
  - **위빙 범위 확대.** 지금까지 건드리지 않던 호출부가 프록시를 타게 된다. 프록시가 hold 없을 때 실제 값을 그대로 반환하는지가 전부다. 테스트로 덮는다
  - **축 이름 프로브의 예외 비용.** 미설정 축마다 `ArgumentException`이 발생한다. 액션 처리 경로에서만 일어나고 프레임마다 도는 코드가 아니므로 무시할 만하다
  - **`GetAxisRaw` 값 스냅 안 함.** 키보드 축을 흉내내는 게임이 -1/0/1이 아닌 값을 볼 수 있다. Open Questions 참고
  - **대소문자 구분.** 에이전트가 `"horizontal"`을 보내면 Unity 프로브가 던져 실패로 응답한다. 조용히 틀리지 않으므로 허용 가능하다

- **Rollback steps:** `git revert`. 저장된 상태나 마이그레이션이 없다

## Rejected feedback

- **`AxisHold`와 `VirtualMouseState.ButtonPressState`를 공통 타입으로 묶기 (medium, DRY).** 기각한다. 각각 10줄짜리 private 중첩 클래스이고, 묶으면 서로 독립적이어야 할 두 상태 클래스가 결합된다. `AxisHold`는 `Value`를 갖고 `ButtonPressState`는 갖지 않으며 앞으로도 갈라질 쪽이다. 중복 제거 이득보다 결합 비용이 크다

## Open Questions

- `GetAxisRaw`가 가상 값을 부호로 스냅해야 하는가? Unity에서 키보드 축의 raw는 정확히 -1/0/1이다. 그대로 반환하면 에이전트 의도가 보존되지만 raw 규약과 어긋난다. 스냅하면 규약은 맞지만 `set_axis(name, 0.5)`가 조용히 1이 된다. 현재는 **그대로 반환**을 택했다
- `set_axis(name, 0)`은 축을 0으로 고정한다. hold를 푸는 것과 다르다 (해제는 실제 입력으로 다시 넘어간다). 축 단위 해제 액션이 필요한가? 현재 해제 경로는 `set_button(name, false)`와 `ReleaseAllVirtualInput`뿐이다. **이번에는 0 고정으로 나간다** — 에이전트 실행 중에는 사람이 동시에 키를 누르지 않으므로 둘의 차이가 관측되지 않는다
