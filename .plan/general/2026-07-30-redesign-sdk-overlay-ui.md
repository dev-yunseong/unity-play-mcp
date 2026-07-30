# 2026-07-30 — SDK 오버레이 UI 재구현 (개칭 + 첫 실행 전면 키 게이트)

- Date: 2026-07-30
- Jira: ARTEL-151
- Status: Implemented (커밋 3개, EditMode 테스트 기준선 대조 완료)

## Goal

1. `ArtelOnboardingController`가 실제로는 SDK 인게임 오버레이 전체(키 입력, 상태 패널,
   고급 설정, 스캔 덮개)를 담당하므로 이름을 역할에 맞게 바꾼다.
2. 인스턴스 키 입력 지점을 **전면 게이트 하나로** 만든다. 지금은 우상단 440×400 패널
   안에 있어 처음 쓰는 사람이 찾지 못한다.
3. 색·타이포를 `artel-home`의 디자인 토큰에서 가져와 대시보드와 같은 제품으로 보이게
   한다. 현재 SDK 색은 어디서도 유래하지 않은 임의값이다.
4. 등록·연결 실패가 막다른 길이 되지 않게 한다. 지금은 저장된 키로 자동 등록이 실패하면
   사용자가 우상단 작은 패널을 스스로 발견해야 한다.

## Non-goals

- TextMeshPro 도입. 레거시 uGUI(`Text`/`InputField`) 유지.
- 폰트 이식. `--font-sans`(Inter/Pretendard)는 패키지에 폰트 에셋을 넣어야 하고 한글
  글리프까지 필요해 용량·라이선스가 붙는다. `LegacyRuntime.ttf` 유지.
- 라운드 코너(`--radius-*`). uGUI `Image`는 9-slice 스프라이트 에셋이 있어야 둥글어진다.
  사각 유지. 토큰의 색·타이포만 가져온다.
- 레포 간 공유 디자인 시스템, 그리고 **SDK 안의 별도 디자인 상수 파일**. 근거는
  Design 절 참조 — 소비자가 하나뿐이다.
- `KeyboardStatusController`(디버그 키 HUD)·`CursorController`(가상 커서) 색 통일.
  ARTEL-151이 언급하지 않는 표면이고, 커서 외곽선 `#0C1626`에 대응하는 토큰이 없어
  이관하면 눈대중 근사가 다시 생긴다. 실제 중복은 `#47C7FF`와 `#48C8FF` 한 쌍뿐이므로,
  거슬리면 두 값을 같게 맞추는 별건 커밋으로 끝낸다.
- 게이트가 뜬 동안 게임 일시정지. 결정: **멈추지 않는다**(Context 참조).
- 게임패드 전용 입력으로 키를 넣는 경로. 게이트는 개발자가 셋업할 때 한 번 쓰는 화면이고
  마우스·키보드를 전제한다. 한글 IME 조합 입력도 마찬가지로 범위 밖 — 인스턴스 키는
  영숫자와 하이픈이며 붙여넣기가 주 경로다.
- 애니메이션/트윈, 테마 시스템, 다국어.
- 프로토콜·등록 API 변경. `ArtelSdkRegistrationClient`는 손대지 않는다.

## Context / Constraints

기존 구조 (`Packages/kr.artel.sdk/Runtime/`):

| 파일 | 역할 |
|---|---|
| `ArtelOnboardingController.cs` (462줄) | 캔버스·패널·고급 섹션·덮개를 코드로 생성, 등록 코루틴 |
| `ArtelOnboardingViewModel.cs` (224줄) | 상태/문구/등록 HTTP, `Changed` 이벤트 |
| `ArtelOnboardingState.cs` (10줄) | `NeedsKey/Registering/Connecting/Connected` |

- 전면 덮개(`Scan Cover`)가 **이미 있다** (`ArtelOnboardingController.cs:228`). 새 전면
  UI를 만들지 않고 이 덮개를 셸로 승격해 콘텐츠만 갈아 끼운다.
- 덮개 불투명도 1과 `raycastTarget = true`는 ARTEL-152(등록 화면 깜박임)에서 정해진 제약.
  반투명하면 씬 전환이 비치고, raycast를 끄면 덮인 게임 UI로 클릭이 샌다. 게이트도 같은
  덮개를 쓰므로 이 제약이 그대로 게이트의 입력 차단 수단이 된다. `Time.timeScale`을 건드리지
  않는 근거이기도 하다 — 게임 자체 부트스트랩을 깨뜨릴 수 있고, 클릭 차단은 덮개로 충분하다.
- `Start`가 저장된 키로 곧바로 `RegisterInstanceKey`를 부른다. 그래서 `State`만 보고
  게이트를 켜면 `NeedsKey`인 한 프레임에 게이트가 번쩍인다 — ARTEL-152가 고친 것과 같은
  종류의 깜박임. 게이트 조건은 Step 1c에서 이 한 프레임을 배제하도록 정의한다.
- `FailRegistration`(`ArtelOnboardingViewModel.cs:196-201`)은 404일 때만
  `HasStoredKey`를 지운다(`:141-143`). 즉 타임아웃·500은 `HasStoredKey`를 남기고,
  `Connect` 실패(`:160-184`)도 마찬가지다. Goal 4가 다루는 막다른 길이 여기서 나온다.
- 덮개가 켜져 있으면 우상단 `Artel` 토글과 고급 섹션이 **전부 안 눌린다**. 덮개는 캔버스의
  마지막 자식이고 불투명하며 `raycastTarget`이 켜져 있다(`:228-243`). 즉 게이트가 뜬 동안
  고급 섹션의 `연결` 재시도 버튼도, `키 지우기`도 닿을 수 없다. 실패해서 게이트가 다시
  뜨는 규칙과 합치면 게임으로 돌아갈 길이 없어진다 — Step 1c의 `나중에` 버튼이 이것 때문에
  있다.
- 스캔은 `State`보다 먼저 시작한다. `ScanScenesThenRegister`가 `registrationRunning`을
  올리고 씬을 다 훑은 **뒤에야** `Register`가 `State = Registering`을 넣는다
  (`:96-125`, ViewModel `:111`). 그 사이 몇 초 동안 `State`는 아직 `NeedsKey`다. 따라서
  덮개 콘텐츠 선택은 `State`가 아니라 `registrationRunning`이 정해야 한다.
- `RefreshView`는 `viewModel.Changed`로 돌고, `KeyInput` 세터가 **키 입력마다** `Changed`를
  쏜다(`:208`, ViewModel `:42-43`). 따라서 `RefreshView`에서 하는 일은 모두 멱등이어야
  한다. `ActivateInputField`처럼 멱등하지 않은 호출은 전이 시점에만 불러야 한다 —
  기존 `appliedShowPanel`(`:36`, `:312-316`)이 그 패턴이다.
- 캔버스 `sortingOrder = short.MaxValue - 1`. 위에 남는 건 가상 커서 캔버스뿐. 유지.
- SDK 런타임 어셈블리(`Artel.Runtime`)는 IL 위버가 건너뛴다
  (`ArtelILPostProcessor.cs:20`). 즉 컨트롤러 안의 `Input`은 실제 `UnityEngine.Input`이며
  `ArtelInput`으로 치환되지 않는다. 게이트의 Enter 제출은 사람 입력에만 반응하고 원격
  에이전트의 가상 키보드로는 눌리지 않는다. 의도한 동작.
- 개칭 비용 확인: `ArtelOnboardingViewModel`·`ArtelOnboardingState`는 `internal`이라
  패키지 밖 비용이 0이고, `ArtelOnboardingController`의 GUID
  `3d72ed08c1384359a26db8e21b6759a0`은 이 레포의 어떤 씬·프리팹도 참조하지 않는다.
  참조 지점은 `ArtelManager.cs:89,91`, `README.md:5`,
  `Tests/Runtime/WebSocketTransportTests.cs` 11곳, `.meta` 3개. 그 밖엔 없다.
- 새 테스트는 모두 EditMode 어셈블리(`Tests/Runtime/`, `includePlatforms: ["Editor"]`)에
  들어간다. `Tests/PlayMode/`에는 온보딩 커버리지가 없고 이번에 만들지 않는다.

## Design (artel-home 토큰 이식)

출처: `artel-home/src/styles/tokens.css`, 사용례는 `artel-home/src/App.css`
(`.button`, `.field-input`, `.panel`, `.field-error`).

**별도 상수 파일(`ArtelDesign.cs`)을 만들지 않는다.** 1차 리뷰에서 근거로 삼았던 "세 표면이
같은 시안을 눈대중으로 세 번 근사했다"가 실측 결과 거짓이었다:

| 파일 | 값 | 실제 |
|---|---|---|
| `ArtelOnboardingController.cs:19` | `#2E73D9` | 색상각 ~217°, **파랑** |
| `KeyboardStatusController.cs:13` | `#47C7FF` | ~197° 시안 |
| `CursorController.cs:161` | `#48C8FF` | ~197° 시안, 커서 외곽선용 `Color32` |

중복은 뒤 두 개 한 쌍뿐이고 둘 다 이번에 손대지 않는 파일에 있다. 오버레이의 액센트는
아예 다른 색이므로 이 작업은 재색상이지 중복 제거가 아니다. 소비자가
`ArtelOverlayController` 하나이므로 상수는 기존 상수가 있던 자리
(`ArtelOnboardingController.cs:15-21`)에 그대로 두고, 출처만 주석으로 남긴다. 두 번째
소비자가 생기면 그때 추출한다.

교체할 상수 — **이 diff가 실제로 칠하는 것만** 정의한다:

| 토큰 | 값 | SDK 상수 | 쓰는 곳 |
|---|---|---|---|
| `--color-bg-canvas` | `#090C10` | `BgCanvas` | 덮개 배경, primary 버튼 **글자색** |
| `--color-bg-surface` | `#10151B` | `BgSurface` | 우상단 패널 배경 |
| `--color-bg-raised` | `#171D25` | `BgRaised` | 입력 필드 배경, secondary 버튼 배경 |
| `--color-border-strong` | `#3B4857` | `BorderStrong` | 입력 필드 테두리, secondary 버튼 테두리 |
| `--color-text-primary` | `#F4F7FA` | `TextPrimary` | 제목, 입력 텍스트, secondary 버튼 글자 |
| `--color-text-secondary` | `#A7B0BC` | `TextSecondary` | 안내 문구, 기본 상태 문구 |
| `--color-text-muted` | `#707B88` | `TextMuted` | 플레이스홀더, 진행 숫자 |
| `--color-action-primary` | `#24C7E8` | `ActionPrimary` | 등록 버튼 배경, 토글 체크마크 |
| `--color-status-critical` | `#FF634F` | `StatusCritical` | 실패 상태 문구 |
| `--color-status-success` | `#48C78E` | `StatusSuccess` | 연결 완료 상태 문구 |

- `--color-border-subtle`은 빼둔다. 패널 테두리를 만들지 않기 때문에 소비자가 없다.
- 상태 문구는 색으로도 구분한다: `Connected`면 `StatusSuccess`, 실패 문구면
  `StatusCritical`, 그 외 `TextSecondary`. `RefreshView`가 `.text`와 함께 `.color`도 쓴다.
  Goal 4의 일부 — 실패를 문장으로만 알리면 눈에 안 걸린다.
- 값은 `new Color32(0x24, 0xC7, 0xE8, 0xFF)` 형태로 쓴다. `ColorUtility.TryParseHtmlString`은
  `bool`을 버리는 래퍼가 필요하고 오타가 런타임에 조용히 잘못된 색이 된다. `Color32`는
  컴파일 타임에 걸리고 `Color`로 암시 변환되며 `CursorController.cs:160-161`이 이미 쓰는
  방식이고, `#24c7e8`과 눈으로 대조되는 것도 그대로다.
- **primary 버튼 글자색은 `BgCanvas`**, 흰색이 아니다. `.button--primary`가
  `color: var(--color-bg-canvas)`인 이유는 `#24C7E8`이 밝아서 흰 글자가 대비 기준을
  넘지 못하기 때문이다. 현재 SDK는 파란 버튼에 흰 글자였으므로 여기서 바뀐다.
- 게이트 타이포는 home보다 한 단계 크게 간다(제목 32, 안내 18, 입력 20, 오류 16).
  대시보드는 코앞의 브라우저지만 게이트는 게임 화면 거리에서 읽는다. 의도한 이탈.
- 덮개 배경은 `--color-overlay-scrim`(72%)도 `--color-overlay-stream`(88%)도 쓸 수 없다.
  ARTEL-152 제약상 알파는 1이어야 한다. `BgCanvas`를 알파 1로 쓴다. 의도한 이탈.
- 테두리는 겉 `Image`(테두리색) 안에 1유닛 들여쓴 `Image`(배경색)를 겹쳐 낸다.
  GameObject 하나가 더 붙으므로 **입력 필드에만** 적용한다. 패널 테두리는 생략.

## Approach (Checklist)

- [x] **Step 0: Recon** — Context·Design 절이 결과.

- [x] **Step 1a: 개칭** (동작 변경 없음, 커밋 1)
  - `ArtelOnboardingController.cs` → `ArtelOverlayController.cs` (`.meta`도 `git mv`,
    GUID 유지해야 이 패키지를 쓰는 프로젝트의 Inspector 참조가 끊기지 않는다)
  - `ArtelOnboardingViewModel.cs` → `ArtelOverlayViewModel.cs`
  - `ArtelOnboardingState.cs` → `ArtelConnectionState.cs`, enum 이름도
    `ArtelConnectionState` (내용이 연결 수명주기이지 온보딩이 아니다)
  - GameObject 이름: `Artel Onboarding Canvas` → `Artel Overlay Canvas`,
    `Onboarding Panel` → `Artel Panel`, `Scan Cover` → `Artel Overlay Cover`
    (덮개가 게이트도 겸하므로 Scan은 더 이상 맞지 않음)
  - 호출부 갱신: `ArtelManager.cs`, `README.md`, 테스트

- [x] **Step 1b: 토큰 이식** (커밋 2, 레이아웃·동작 무변경)
  - 상수 5개를 Design 표의 10개로 교체. `Color32` 16진 리터럴
  - `CreateButton`에 primary/secondary 구분 추가 — primary는 `ActionPrimary` 배경 +
    `BgCanvas` 글자, secondary는 `BgRaised` 배경 + `BorderStrong` 테두리 + `TextPrimary`
    글자. 등록만 primary, 나머지(고급/연결/키 지우기/Artel 토글)는 secondary
  - 상태 문구 색을 상태에 따라 칠한다(Design 절)
  - 토글 체크마크(`:401`)는 `ActionPrimary`
  - **기존 패널의 오프셋 숫자는 건드리지 않는다.** 1차 리뷰 지적대로 Non-goal이고,
    440×400 안에서 요소가 밀리는 회귀 표면만 만든다. 단 Step 1c가 패널에서 요소 두 개를
    빼므로 그때 남는 요소의 오프셋은 다시 잡아야 한다 — 그 변경은 1c에 속한다
  - 색만 바뀌므로 이 커밋 단독으로 스크린샷 비교가 된다

- [x] **Step 1c: 게이트** (커밋 3)
  - **키 입력 지점을 하나로 만든다.** 우상단 패널에서 `instanceKeyField`와 `registerButton`
    (`ArtelOnboardingController.cs:203-212`)을 **삭제**하고 게이트로 옮긴다. 같은
    `viewModel.KeyInput`을 두 위젯이 물면 동기화·중복 이름 문제가 생기고, 무엇보다 Goal 2가
    입력 지점을 하나로 만드는 것이다. 패널은 상태 문구 + 고급 섹션만 남는다
  - ViewModel에 추가하는 것은 셋뿐:

    ```csharp
    public bool HasAttemptedRegistration { get; private set; }   // Register 진입 시 true
    public bool HasError { get; private set; }
    public bool ShowGate =>
        State == ArtelConnectionState.NeedsKey &&
        (!HasStoredKey || HasAttemptedRegistration);
    ```

    `!HasStoredKey`가 저장 키 건너뛰기(요구사항 2)와 시작 한 프레임 깜박임을 막고,
    `HasAttemptedRegistration`이 Goal 4를 덮는다 — 404가 아닌 실패나 연결 실패로
    `HasStoredKey`가 남아 있어도 게이트가 다시 뜨고, 필드에 기존 키가 채워진 상태에서
    등록을 다시 누르는 것이 곧 재시도다. 별도 오류 화면·재시도 버튼이 필요 없다.

    `HasError`가 필요한 이유: 실패 문구들이 공통 표지를 갖고 있지 않다 —
    `"설정 오류: "`(`:121`), `"등록 실패: "`(`:143`, `:147`), `"연결 실패: "`(`:182`),
    그리고 접두사가 아예 없는 `"Player Settings의 Version이 비어 있습니다..."`(`:106`).
    문자열 매칭으로는 마지막 것을 놓친다. `FailRegistration`(`:196-201`)과 `Connect`의
    catch(`:178-183`)에서 true, `State = Registering`(`:111`)·`ClearStoredKey`(`:186`)·
    등록 성공(`:155`)·수동 연결 성공(`:175`)에서 false. 게이트 오류 줄은
    `HasError ? Status : string.Empty`,
    상태 문구 색은 `Connected`면 success, `HasError`면 critical, 그 외 secondary
  - 덮개를 콘텐츠 두 그룹으로 나눈다. 덮개 자체(배경·raycast 차단)는 공용
    - `Gate Content` — 제목 `Artel SDK`(32, `TextPrimary`), 안내 문구(18,
      `TextSecondary`), 640×64 입력 필드(20, `BgRaised` + `BorderStrong` 테두리),
      **필드 바로 다음 슬롯**에 오류 문구(16, `StatusCritical`), 그 다음 등록 버튼
      (primary), 그 아래 `나중에` 버튼(secondary)
    - `Progress Content` — 현재 덮개 문구 + `씬 n / N` 진행 숫자(`TextMuted`, 기존 그대로)
  - 게이트 레이아웃은 덮개가 이미 쓰는 `CenterRect` 고정 배치(`:245-260`, `:429-436`)를
    그대로 쓴다. `VerticalLayoutGroup`은 쓰지 않는다 — `childControlHeight`/
    `childForceExpandHeight`가 기본 true인데 `InputField`와 스프라이트 없는 `Image`/
    `Button`은 `ILayoutElement`를 구현하지 않아(`Text`만 한다) `ContentSizeFitter` 아래에서
    높이가 0으로 접힌다. `LayoutElement`를 일일이 붙이는 것보다 고정 배치가 짧고, 오류 줄이
    항상 같은 자리를 차지하는 것도 자동으로 성립한다
  - **막다른 길을 막는 `나중에` 버튼.** 누르면 컨트롤러 로컬 `gateDismissed`가 켜지고
    덮개가 내려간다. 게임은 계속 돌고, 우상단 `Artel` 토글로 패널에 닿을 수 있게 되며
    고급 섹션의 `연결`·`키 지우기`도 다시 눌린다. 이것 없이는 등록 서버가 죽어 있을 때
    SDK가 게임을 세션 내내 못 쓰게 만든다(Context 참조)
  - **게이트로 돌아오는 길**은 고급 섹션의 두 버튼이다. 게이트가 내려가면 등록 버튼과 입력
    필드도 함께 비활성되므로(둘 다 `Gate Content` 안에만 있다) "다음 등록 시도에서 해제"는
    도달할 수 없는 규칙이다. 대신 고급 섹션의 `키 지우기`와 `연결` 리스너를 컨트롤러
    메서드로 감싸 `gateDismissed = false`를 먼저 지운다. `연결`이 있는 이유는 키를 버리지
    않고 재시도할 길을 남기는 것이다 — `키 지우기`만 남기면 24자를 다시 쳐야 한다
  - `나중에`·`키 지우기`·`연결` 리스너는 **`RefreshView()`를 직접 부른다.**
    `gateDismissed`는 컨트롤러 로컬이라 `Changed`가 뜨지 않는다
  - **덮개의 쓰기 주체를 하나로 한다.** `RefreshView`가 전부 정한다:

    ```csharp
    var showGate = viewModel.ShowGate && !registrationRunning && !gateDismissed;
    coverObject.SetActive(showGate || registrationRunning);
    gateContent.SetActive(showGate);
    progressContent.SetActive(registrationRunning);
    ```

    패널은 여기서 무조건 쓰지 않는다. `RefreshView`가 매 `Changed`마다
    `panelObject.SetActive`를 덮어쓰면 `Artel` 토글(`:188`)이 무력화된다 — 패널을 직접 열고
    `연결`을 눌러 성공하면 `SetStatus` → `Changed` → 패널이 닫혀버린다. 기존
    `appliedShowPanel` 전이 가드(`:36`, `:312-316`)를 그대로 유지한다. 실패 경로는
    `FailRegistration`이 `ShowPanel = true`를 넣으므로 영향받지 않는다

    `registrationRunning`을 콘텐츠 선택에 넣는 것이 핵심이다. 스캔은 `State`가 아직
    `NeedsKey`인 채로 몇 초 돌기 때문에(Context 참조), `ShowGate`만 보면 스캔 내내 게이트가
    등록 버튼을 켠 채 얼어 있고 진행 숫자는 꺼진 그룹에 써진다. 코루틴은
    `registrationRunning`을 뒤집고 진행 문구만 쓰며, 뒤집은 **직후 `RefreshView`를 부른다**.
    `ShowCover`/`HideCover`는 사라진다
  - `ActivateInputField()`는 위 `showGate`가 false→true로 바뀌는 **전이에서만** 부른다
    (`appliedShowGate` 플래그, 기존 `appliedShowPanel` 패턴). `RefreshView`가 키 입력마다
    돌기 때문에, 매번 부르면 캐럿과 선택이 초기화되어 타이핑이 깨진다. `Register` 진입부의
    `KeyInput = trimmedKey`(`:110`)도 `Changed`를 쏘므로 이 전이 판정에 걸려야 한다
  - Enter 제출은 `onEndEdit` 안에서 실제 Enter인지 확인한다:
    `if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;`
    전면 덮개는 화면을 다 채운 `raycastTarget`이라 배경 클릭으로도 포커스가 풀리고
    `onEndEdit`가 뜬다. 확인 없이 제출하면 세 글자 치고 배경을 클릭한 사용자가 요청하지도
    않은 등록 실패를 본다
  - 붙여넣기는 `InputField` 기본 Ctrl+V 그대로
  - 표시 규칙:

    | 상황 | `registrationRunning` | 덮개 | 콘텐츠 | 우상단 패널 |
    |---|---|---|---|---|
    | 첫 실행, 키 없음 | false | 켬 | Gate (빈 필드) | 덮개에 가림 |
    | 저장 키로 시작 | true | 켬 | Progress | 끔 |
    | 게이트에서 등록 누름 → 스캔 중 | true | 켬 | Progress | 덮개에 가림 |
    | 연결 완료 (저장 키로 시작) | false | 끔 | — | 끔, `Artel` 토글로만 |
    | 연결 완료 (게이트에서 입력) | false | 끔 | — | 켬 (성공 문구, success 색) |
    | 404 (키 오류) | false | 켬 | Gate (**키 채워짐**) + 오류 | 덮개에 가림 |
    | 타임아웃·500·연결 실패 | false | 켬 | Gate (키 채워짐) + 오류 | 덮개에 가림 |
    | 고급 → 키 지우기 | false | 켬 | Gate (빈 필드) | 덮개에 가림 |
    | `나중에` 누름 | false | 끔 | — | 켬 (또는 `Artel` 토글로) |
    | `나중에` 후 고급 → 키 지우기 | false | 켬 | Gate (빈 필드) | 덮개에 가림 |
    | `나중에` 후 고급 → 연결 | false | 끔 또는 Gate | 실패면 Gate (키 채워짐) | 켬 |

    **"덮개에 가림"은 `activeSelf == false`가 아니다.** `appliedShowPanel` 가드를 유지하기
    때문에 그 행들에서 패널은 여전히 활성이고, 불투명한 마지막 형제 덮개에 안 보이는 것일
    뿐이다. 이 행에 패널 비활성 단정을 쓰면 안 된다. 진짜 꺼지는 건 저장 키 경로 두 행뿐이다
    (`Initialize`가 `ShowPanel = false`, `:74`).

    연결 완료가 두 행으로 갈리는 이유도 같다: `ShowPanel`은 `Initialize`가 저장 키를 찾았을
    때만 false가 되고 그 뒤로 다시 false가 되지 않는다. 게이트로 처음 등록한 사용자는 연결
    후 패널이 떠 있게 되는데, 방금 한 일이 성공했다는 피드백이므로 그대로 둔다.

    404도 필드가 채워진 상태로 뜬다. 404 분기는 `HasStoredKey`와 저장값만 지우고
    (`:139-144`) `keyInput`은 `:110`에서 넣은 값 그대로라 `RefreshView`가 다시 채운다.
    UX로도 이게 맞다 — 한 글자 틀린 것을 고치는 편이 24자를 다시 치는 것보다 낫다.
    `키 지우기`만 필드를 비운다(`ClearStoredKey`가 `keyInput`을 지운다, `:190`).

    `키 지우기` 행은 의도한 동작이다. 고급 섹션의 개발자용 동작이므로 돌아가는 게임 위로
    게이트가 전면에 뜨는 것을 받아들인다. `나중에`로 다시 내릴 수 있다

- [x] **Step 2: Tests** (`Tests/Runtime/WebSocketTransportTests.cs`)
  - 기존 테스트 개칭 (`OnboardingViewModel_*` → `OverlayViewModel_*` 등), 단정 유지
  - `ArtelManager_CreatesOnboardingGuiAutomatically`: 이 테스트는 이미
    `includeInactive: true`로 조회하므로(`:323-325`) 등록 버튼과 입력 필드가 비활성
    `Gate Content`로 옮겨가도 조회는 그대로 살고, 필드도 캔버스에 하나뿐이라 단수 조회가
    유효하다. 버튼 수만 5 → **6**으로 갱신한다(`나중에` 추가)
  - 추가 `OverlayViewModel_ShowsGateOnlyWhenNoKeyStored` — 저장 키 없으면 `ShowGate` 참,
    있으면 거짓 (요구사항 2 + 깜박임 회귀)
  - 추가 `OverlayViewModel_ShowsGateAgainAfterFailureWithStoredKey` — 저장 키가 있는
    상태로 `Register`를 실패시키면 `HasStoredKey`가 남아도 `ShowGate`가 참 (Goal 4).
    기존 `DoesNotPersistKeyWhenRegistrationFails`가 쓰는 빈 `Server` 실패 경로를 재사용
  - 추가 `OverlayGui_ShowsGateOnFirstLaunch` — `Start` 후 덮개 활성, `Gate Content` 활성 /
    `Progress Content` 비활성, 입력 필드가 활성이며 `interactable`
  - 추가 `OverlayGui_HidesCoverWhenConnected` — `Connected`에서 덮개 비활성. 이 행이
    깨지면 게임 화면이 통째로 검게 남는다. `State`는 private 세터(`:27`)이고 `Connected`로
    가는 유일한 길이 실제 HTTP 등록 성공(`:153-157`)이므로 **리플렉션 시임을 쓴다**:
    컨트롤러의 private `viewModel` 필드(`:38`)를 꺼내 `State`를 넣고 `RefreshView`를
    호출한다. `InvokeLifecycle`(`WebSocketTransportTests.cs:421-426`)이 이미 쓰는 방식
  - 추가 `OverlayGui_HidesCoverWhenGateDismissed` — **왕복**을 단정한다:
    `나중에` → 덮개 끔 → 고급 `키 지우기` → 덮개 켬 + `Gate Content` 활성. 양방향
    막다른 길 회귀 방지 (`나중에`로 게임에 돌아갈 수 있고, 거기서 게이트로 되돌아올 수 있다)
  - **기존 `OnboardingGui_HidesScanCoverUntilRegistrationRuns`의 활성 단정이 뒤집힌다.**
    `SetUp`이 키를 지우므로(`:29`) `Start` 이후가 곧 첫 실행 게이트 상황이고, 덮개는 이제
    **활성**이다. `:380`을 `Is.True`로 바꾸고 이름을 `OverlayGui_CoverGeometryAndOpacity`로
    바꾼다. 알파 1 · `raycastTarget` · 마지막 형제 인덱스 단정(ARTEL-152 회귀)은 그대로 둔다
  - 추가 `RegisterButton_LabelMeetsContrastRatio` — 등록 버튼 배경과 라벨의 상대 휘도
    대비가 4.5 이상. 리터럴을 리터럴과 비교하는 동어반복 대신 실제 불변식(밝은 시안 위
    흰 글자 금지)을 지킨다. 재색상해도 살아남는다
  - 수동: 샘플 `samples/WordVenture`에서 (a) 키 지운 첫 실행 (b) 저장 키 있는 재실행
    (c) 잘못된 키 (d) 서버 끈 상태로 저장 키 실행 → 게이트가 키 채워진 채 오류와 함께
    뜨는지 (e) 게이트에서 타이핑 중 캐럿이 튀지 않는지 (f) 게이트에서 등록을 누른 뒤
    스캔이 도는 몇 초 동안 진행 숫자가 보이고 게이트가 얼어 있지 않은지 (g) `나중에`로
    빠져나와 게임이 조작되는지 (h) 그 뒤 `Artel` 토글 → 고급 → `키 지우기`로 게이트가
    다시 뜨는지 (i) 패널을 직접 열어둔 채 `연결`을 눌러 성공해도 패널이 닫히지 않는지

- [x] **Step 3: Rollout / Rollback** — 피처 플래그 없음. SDK 인게임 UI 한정, 서버 계약
      무변경. 커밋 3개(개칭 / 토큰 / 게이트)로 나누는 것은 **리뷰**를 위한 것이다:
      기계적 개칭과 의미 변경이 섞이지 않고, 색만 바뀐 커밋은 스크린샷으로 대조된다.
      롤백 단위로는 게이트 커밋만 단독 revert가 확실하다 — 토큰은 대체로, 개칭은 뒤
      커밋들이 같은 파일을 건드리므로 단독 revert가 충돌한다.

## Validation

- **Commands to run:** Unity 2022.3.34f1 batch mode, EditMode. 절차를
  `.agents/docs/project.md`의 `## Running package tests`에 기록했다 (레포 루트가 Unity
  프로젝트가 아니고 샘플 서브모듈에 `testables`가 없어 임시 프로젝트가 필요하다).
- **Result:** 기준선 `f5c690d` 136개 중 128 통과 / 8 실패, 변경 후 142개 중 134 통과 /
  같은 8 실패. 새 테스트 6개 전부 통과, 회귀 없음. 8건은 임시 프로젝트 환경 탓이다
  (빌드 씬 없음, EditMode에서 `DontDestroyOnLoad` 불가).
- **아직 실행으로 검증되지 않은 것:** `ArtelManager_CreatesOverlayGuiAutomatically`는
  기준선에서도 매니저 `Awake`의 `DontDestroyOnLoad`에서 던지고 죽는다. 즉 버튼 수 6
  단정에 도달하지 못한다. 별건으로 분리.
- **수동 검증 미실시:** (a)~(i) 항목은 샘플 프로젝트를 열어야 하므로 남아 있다.

## Risks & Rollback

- **Risks:**
  - `.meta` GUID를 잃으면 이 패키지를 쓰는 프로젝트의 Inspector 참조가 끊긴다. `git mv`로
    `.cs`와 `.meta`를 함께 옮겨 방지한다.
  - 덮개 표시를 틀리면 게임 화면이 통째로 가려진다. 쓰기 주체를 `RefreshView` 하나로 좁혀
    구조적으로 막고, `HidesCoverWhenConnected`로 고정한다.
  - **SDK가 게임을 못 쓰게 만들 수 있다.** 덮개가 우상단 패널과 고급 섹션을 다 덮으므로,
    등록이 계속 실패하면 재시도 외에 길이 없다. `나중에` 버튼이 유일한 탈출구다.
    **반대 방향도 막힌다** — 게이트가 내려가면 등록 버튼과 입력 필드도 함께 비활성되므로,
    고급 섹션의 `키 지우기`·`연결`이 `gateDismissed`를 지워주지 않으면 그 세션에서 SDK를
    다시 등록할 수 없다. 왕복 전체를 `HidesCoverWhenGateDismissed`로 고정한다. 세 리스너
    중 하나라도 지우면 회귀한다.
  - `RefreshView`가 키 입력마다 도는 경로에 멱등하지 않은 호출을 넣으면 타이핑이 깨진다.
    `ActivateInputField`가 유일한 그런 호출이고 전이 플래그로 막는다.
  - 토큰이 `artel-home`에 복사된 채 갈라진다. CSS → C# 자동 동기화 수단이 없다. Design 표에
    16진값을 출처와 함께 적어두는 것이 유일한 대조 수단이며, home이 토큰을 바꾸면 SDK는
    따라오지 않는다 — 받아들인다.
- **Rollback steps:** `git revert <게이트 커밋>`이 확실한 단위. 그보다 아래로 되돌리려면
  세 커밋을 역순으로 revert한다.

## Rejected feedback

- **`ArtelDesign.cs`로 상수를 추출하자** (원안) — 근거였던 3중 중복이 실측에서 거짓.
  소비자가 하나인 파일은 만들지 않는다.
- **`KeyboardStatusController`·`CursorController`도 토큰으로 이관** (원안) — 범위 밖이고,
  커서 외곽선에 대응하는 토큰이 없어 눈대중이 다시 생긴다. Non-goal로 이동.
- **기존 패널 오프셋을 `--space-*` 배수로 정리** (원안) — Non-goal과 모순이고 회귀 표면만
  만든다. 단 Step 1c가 패널에서 위젯을 빼면서 생기는 오프셋 조정은 필요한 변경이므로 남긴다.
- **`OverlayGui_UsesHomeDesignTokens` 색 일치 테스트** (원안) — 같은 레포의 리터럴을 같은
  리터럴과 비교하는 동어반복. 다른 레포의 `tokens.css` 드리프트는 잡을 수 없다. 실제
  불변식인 대비비 테스트로 교체.
- **실패 전용 오류 화면 + 재시도 버튼** (fast 리뷰 6번의 한 가지 해법) — 게이트에 키가
  채워진 채로 뜨면 등록 버튼이 곧 재시도다. 콘텐츠 그룹을 하나 더 만들지 않는다.
- **게임패드 전용 입력·IME 조합 대응** (fast 리뷰 7·8번) — Non-goal로 명시. 개발자가 셋업
  때 한 번 쓰는 화면이고 키는 붙여넣기가 주 경로다.
- **게이트를 `VerticalLayoutGroup` + `ContentSizeFitter`로 짜기** (원안, "네이티브 기능이
  수동 산수보다 낫다"는 근거였다) — heavy 리뷰에서 뒤집혔다. `InputField`와 스프라이트 없는
  `Image`/`Button`은 `ILayoutElement`를 구현하지 않아 높이가 0으로 접힌다. 요소마다
  `LayoutElement`를 붙여야 하므로 네이티브 쪽이 오히려 길어진다. 덮개가 이미 쓰는
  `CenterRect` 고정 배치를 재사용한다.
- **`ShowGate`만으로 덮개 콘텐츠를 정하기** (원안) — 스캔이 `State`보다 먼저 시작하므로
  스캔 몇 초 동안 게이트가 얼어붙는다. `registrationRunning`을 판정에 넣는다.

## Open Questions

- 없음. 일시정지 없음 / 저장 키면 게이트 건너뛰기 / 실패 시 게이트 복귀 — 확정.
