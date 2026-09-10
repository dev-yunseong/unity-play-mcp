# 2026-09-10 — assembly reload 뒤 cursor 와 keyboard status overlay 복구

- Date: 2026-09-10
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/65
- Status: In progress

## Goal

play 중 assembly reload 를 건넌 `CursorController` 와 `KeyboardStatusController` 가 자기 overlay 를
다시 세워, `Update` 가 예외를 던지지 않고 cursor 와 눌린 key 가 다시 화면에 그려지게 한다. 다시 세우는
과정에서 reload 를 살아남은 canvas GameObject 가 남아 overlay 가 두 벌이 되는 일이 없어야 한다.

## Non-goals

- overlay 의 모양·배치·색 변경.
- `UnityPlayMcpHost` 의 lifecycle 재수정. `fix/57` 이 이미 고쳤고 이 branch 는 그 위에 쌓는다.
- 단순 null guard 로 overlay 가 사라진 것을 덮기. `MoveTo` 가 조용히 빠져나가는 지금이 바로 그 모습이고,
  그것이 결함을 감춘다.
- `mcp/` 아래 TypeScript server.

## Context / Constraints

### 확정한 원인

Unity 의 play 중 assembly reload 는 `OnDisable` → serialize → domain 교체 → deserialize → `OnEnable`
순으로 가고 `Awake` 는 다시 부르지 않는다. 되돌아오는 것은 serialize 되는 field 뿐이다. 두 controller 는
그리는 것 전부를 `Awake` 에서만 만들고 그 참조를 하나도 serialize 하지 않는다.

- `CursorController.cs:29-33` — `Awake` 가 `CreateCursor()` 로 `cursorTransform`, `cursorTexture`,
  `cursorSprite` 를 만든다. reload 뒤 셋 다 null 이고, `Update`(`:44`)가
  `UnityPlayMcp.DarkTheme` 값이 바뀌는 프레임에 null 인 `cursorTexture` 로 `PaintCursorTexture` 를
  불러 NullReferenceException 을 던진다.
- `CursorController.cs:85` — `MoveTo` 의 `cursorTransform == null` 확인 때문에 pointer 조작은 예외
  없이 무시된다. 그래서 조작은 되는데 capture 한 screen 에 cursor 가 남지 않는다.
- `KeyboardStatusController.cs:38-45` — `Awake` 가 `CacheKeyboardKeys()`, `CreateGui()`,
  `ApplyTheme()`, `RefreshText()` 를 부른다. reload 뒤 `canvasObject`(`:27`) 이하 UI 참조가 전부
  null 이고, `readonly List<KeyCode> keyboardKeys`(`:24`)는 initializer 가 놓은 빈 list 로 돌아온다.
  `Update`(`:74`)가 부르는 `RefreshText`(`:253`)가 표시 문자열이 바뀔 때마다 던지므로, agent 가 무엇을
  누를 때마다 반복된다.

`UnityPlayMcpHost.EnsureRuntime`(`UnityPlayMcpHost.cs:127-135`)은 두 controller 를 `GetComponent` 로
찾는다. component 는 GameObject 와 함께 reload 를 살아남으므로 host 는 그것들을 다시 만들지 않는다.
`fix/57` 이 host 를 되살려도 두 controller 는 죽은 채로 남는 이유가 이것이다.

### 함정: canvas GameObject 는 살아남는다

`CursorController.cs:128-129` 와 `KeyboardStatusController.cs:159-164` 는 canvas 를 controller 자신의
`transform` 아래에 붙인다. reload 는 GameObject 를 하나도 파괴하지 않으므로 그 canvas 는 그대로 남는다.
확인 없이 다시 만들면 cursor 와 status panel 이 두 벌이 된다.

### 설계 선택: 다시 만든다 (serialize 하지 않는다)

두 가지를 저울에 올렸다.

**(A) 참조를 `[SerializeField, HideInInspector]` 로 돌려받기.** `RectTransform` 과 `Text` 같은 scene
component 참조는 확실히 돌아온다. 그런데 `cursorTexture` 는 `new Texture2D(...)` 로, `cursorSprite` 는
`Sprite.Create(...)` 로 runtime 에 만든 asset 이다. 이것들은 scene 에 속한 object 가 아니고,
`HideFlags.HideAndDontSave` 도 붙어 있지 않다 — runtime 에 만든 `ScriptableObject` 가 reload 에서
사라지는 것과 같은 자리에 있다. **이 기계에 Unity 가 없어 그 생존 여부를 확인할 수 없다.** 확인할 수
없는 것에 fix 를 거는 것이 (A) 를 버리는 이유다. `keyboardKeys` 도 `readonly` 라 그대로는 serialize
되지 않고, 100 개 남짓한 list 를 reload 마다 저장했다 돌려받을 값도 아니다 —
`Enum.GetValues(typeof(KeyCode))` 에서 언제든 다시 계산된다.

**(B) 채택. `OnEnable` 에서 다시 만들되, 살아남은 canvas 를 먼저 걷어낸다.** 길이 하나뿐이라 reload 뒤에
도는 코드가 첫 생성과 정확히 같고, texture 와 sprite 가 reload 를 살아남는지에 기대지 않는다. overlay 는
canvas 하나, 36×48 texture 하나, UI object 예닐곱 개라 다시 만드는 값이 reload 빈도에서 무시할 만하다.

살아남은 canvas 를 집는 방법으로는 이름으로 `transform.Find` 하는 길 대신 **canvas GameObject 참조
하나만 `[SerializeField, HideInInspector]` 로 들고 있는 길**을 고른다. 이름은 test 가 이미 붙잡고 있어
바뀔 수 있고, 이름이 바뀌면 Find 는 조용히 실패해 overlay 가 두 벌이 된다. 참조는 그 자체로 정확하다.

`Destroy` 는 프레임 끝에 처리된다. 그래서 다시 세운 직후의 같은 프레임에는 canvas 가 잠시 둘이고,
자식 수를 세는 test 는 반드시 프레임을 한 번 넘긴 뒤에 세야 한다. 그 사이에 화면에 두 벌이 그려지지
않게 하는 것은 `Destroy` 앞의 `SetActive(false)` 한 줄이다.

### `fix/57` 과의 정합

`UnityPlayMcpHost` 는 `Awake` 와 `Start` 와 `OnEnable` 이 함께 부르는 멱등한 `BeginHosting` 을 두고,
serialize 되지 **않는** `ownsRuntime` 이 false 로 돌아오는 것을 "다시 만들라" 는 신호로 쓴다
(`UnityPlayMcpHost.cs:52-56`, `:127-135`, `:241-263`). 이 branch 는 같은 모양을 그대로 쓴다:
serialize 하지 않는 `builtOverlay` 가 reload 뒤 false 로 돌아오고, 그 false 가 `OnEnable` 에게 다시
세우라고 말한다.

### 제약

- 게임 project 에 별도 초기화 코드를 요구하지 않는다. `OnEnable` 은 Unity 가 부른다.
- reload 를 겪지 않은 경우의 동작을 그대로 둔다. 특히 `enabled` 를 껐다 켜는 것만으로 overlay 를 부수고
  다시 만들면 cursor 가 깜빡이고 위치를 잃는다. 이미 서 있으면 아무것도 하지 않는다.
- `OnDestroy` 의 정리 동작을 건드리지 않는다.
- `Packages/dev.yunseong.unityplaymcp/Runtime/UnityPlayMcpHost.cs` 는 `fix/57` 의 파일이라 읽기만 한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 두 controller, `UnityPlayMcpHost`, `AssemblyReloadSimulation`,
      `HostReloadRecoveryTests`, `CursorControllerTests`, `KeyboardStatusControllerTests`,
      `VirtualInput` 을 읽었다. `Tests/Runtime` asmdef 는 `includePlatforms: ["Editor"]` 라 EditMode 고,
      `Tests/PlayMode` 가 play mode 다. `AssemblyInfo.cs` 가 두 test assembly 에 internal 을 연다.

- [ ] **Step 1: Implementation**

  `Packages/dev.yunseong.unityplaymcp/Runtime/CursorController.cs`
  - `[SerializeField, HideInInspector] private GameObject overlayCanvas;` 추가. `CreateCursor` 가
    만든 canvas 를 여기 담는다 (지금은 지역 변수다).
  - `private bool builtOverlay;` 추가 (serialize 하지 않는다).
  - `Awake` 를 없애고 `OnEnable` 로 옮긴다: `builtOverlay` 면 그대로 돌아가고, 아니면
    `DiscardOverlay()` → theme 읽기 → `CreateCursor()` → `builtOverlay = true`.
  - `DiscardOverlay()` — `overlayCanvas` 가 null 이면 아무것도 하지 않는다. 아니면 그 아래 `Image` 의
    sprite 와 texture 를 `Destroy` 하고 (reload 를 살아남았을 수 있고, 남았다면 이 자리 말고 놓아줄 곳이
    없다), canvas 를 `SetActive(false)` 한 뒤 `Destroy` 한다. `Destroy` 는 프레임 끝에 처리되므로 먼저
    꺼 두지 않으면 그 프레임에 cursor 가 두 개 그려진다.
  - `OnDestroy` 는 그대로 둔다. `overlayCanvas` 가 field 가 되었어도 여기서 지우지 않는다 — 지금도
    canvas 는 controller 의 자식이라 GameObject 와 함께 사라지고, 없던 정리를 더하는 것은 이 issue 가
    "`OnDestroy` 의 정리 동작을 그대로 둔다" 로 막아 둔 자리다.

  `Packages/dev.yunseong.unityplaymcp/Runtime/KeyboardStatusController.cs`
  - `canvasObject` 를 `[SerializeField, HideInInspector]` 로 바꾼다. 살아남은 canvas 를 집는 손잡이가
    되고, `OnDestroy` 가 쓰는 대상은 그대로다.
  - `private bool builtOverlay;` 추가.
  - `Awake` 를 없애고 `OnEnable` 로 옮긴다: `builtOverlay` 면 돌아가고, 아니면 `DiscardOverlay()` →
    `CacheKeyboardKeys()` → theme 읽기 → `CreateGui()` → `ApplyTheme()` → `RefreshText()` →
    `builtOverlay = true`.
  - `CacheKeyboardKeys` 첫 줄에 `keyboardKeys.Clear()` 를 넣어 두 번 불러도 같은 list 가 되게 한다.
  - 다시 세우는 자리에서 `displayedKeys` 와 `displayedPointer` 를 null 로 되돌린 뒤 `RefreshText()` 를
    부른다. 새로 만든 `Text` 는 빈 문자열로 시작하므로, 이전 값이 남아 있으면 `RefreshText` 가 같다고
    보고 빈 채로 둔다. `CreateGui` 가 아니라 이 자리에 두는 이유는 `CreateGui` 는 GUI 만 만드는 자리로
    남기기 위해서다.
  - `DiscardOverlay()` — `canvasObject` 를 `SetActive(false)` 후 `Destroy`. texture 를 만들지 않으므로
    cursor 쪽보다 짧다.
  - `OnDestroy` 는 그대로 둔다.

  `Packages/dev.yunseong.unityplaymcp/Tests/PlayMode/AssemblyReloadSimulation.cs` (`fix/57` 이 들여온
  파일, 일반화가 이 branch 가 `fix/57` 위에 쌓이는 이유다)
  - `Rehearse(MonoBehaviour)` 로 일반화하고, `Rehearse(UnityPlayMcpHost)` overload 를 남겨
    `UnityPlayMcpHostSlot.Clear()` 를 그 자리에 그대로 둔다. `HostReloadRecoveryTests` 는 한 줄도
    바뀌지 않고 뜻도 그대로다 — overload 해석이 host 를 넘길 때 그 overload 를 고른다.
  - `ForgetUnserializedState` 와 `NewlyConstructedHost` 를 `behaviour.GetType()` 기준으로 바꾸고,
    class 문서에서 host 만 가리키던 문장을 MonoBehaviour 로 넓힌다. `DeclaredOnly` 를 그대로 두는 것은
    지금 rehearse 하는 세 type 이 전부 `sealed` 이고 `MonoBehaviour` 를 바로 상속하기 때문이다.
    비활성 GameObject 에 붙여 값을 읽는 방법도 그대로 쓴다 — 생성이 `Awake` 에서 `OnEnable` 로 옮겨가도
    비활성 GameObject 는 둘 다 부르지 않는다.
  - **readonly collection 을 비운다.** 지금은 `field.IsInitOnly` 를 건너뛴다. 그러면
    `readonly List<KeyCode> keyboardKeys` 가 채워진 채 남아, reload 가 남기는 빈 list 를 재현하지 못한다.
    reflection 으로 readonly field 에 다시 대입하는 것은 runtime 에 따라 거절되므로, 값이
    `System.Collections.ICollection` 이면 그 `Clear()` 를 부른다 — reload 는 initializer 가 놓은 갓 만든
    빈 collection 을 돌려주므로 "비어 있음" 이 그 값이다. host 쪽 `actionRequests` 는 그 test 들에서 이미
    비어 있어 뜻이 달라지지 않는다.

- [ ] **Step 2: Tests**

  새 PlayMode fixture 두 개. reload 뒤 상태는 `Awake`/`OnEnable`/`Update` 가 실제로 도는 play mode
  에서만 재현된다.

  `Tests/PlayMode/CursorReloadRecoveryTests.cs`
  - `MovesTheCursorAfterAnAssemblyReload` — reload 뒤 `MoveTo` 가 cursor 를 실제로 켜고 옮긴다.
    **fix 없이 실패한다** (`cursorTransform` 이 null 이라 `MoveTo` 가 조용히 빠져나가 cursor 가 꺼진 채로
    남는다).
  - `RepaintsTheCursorWhenTheThemeFlipsAfterAReload` — reload 뒤 `UnityPlayMcp.DarkTheme` 을 뒤집고
    프레임을 넘긴다. **fix 없이 실패한다** (`Update` 가 null texture 로 던지고 Unity Test Framework 가
    그 예외를 실패로 친다).
  - `KeepsOneCursorCanvasAfterAReload` — canvas 이름을 가진 자식이 하나뿐이다. fix 없이도 통과한다
    (다시 만들지 않으니 하나다). 이 test 가 지키는 것은 결함이 아니라 이 fix 자신의 위험이다.
  - `KeepsTheCursorOnAPlainReEnable` — `enabled` 를 껐다 켜도 canvas instance 가 같다. 보존 test 로,
    양쪽 다 통과한다.

  `Tests/PlayMode/KeyboardStatusReloadRecoveryTests.cs`
  - `ShowsPressedKeysAfterAnAssemblyReload` — reload 뒤 key 를 누르고 `keyStatusText` 를 읽는다.
    **fix 없이 실패한다** (`RefreshText` 가 던진다). `keyboardKeys` 가 다시 채워지는지도 이 test 가 함께
    본다 — 비어 있으면 눌린 key 가 목록에 오르지 못해 문자열이 `—` 로 남는다.
  - `ShowsHeldMouseButtonsAfterAnAssemblyReload` — `MoveMouse` 로 pointer 를 쥐고 button 을 누른 뒤
    `pointerStatusText` 에 `HOLD` 와 `LEFT` 가 있는지 본다. **fix 없이 실패한다.**
  - `KeepsOneStatusPanelAfterAReload` — 중복 방지. fix 없이도 통과한다.
  - `KeepsTheStatusPanelOnAPlainReEnable` — 보존 test.

  기존 EditMode test 손질 (`Awake` 를 없애므로 필수)
  - `Tests/Runtime/CursorControllerTests.cs:196-204` 와
    `Tests/Runtime/KeyboardStatusControllerTests.cs:38-40` 이 reflection 으로 `Awake` 를 부른다. 같은
    자리를 `OnEnable` 로 바꾼다. 그 외의 assertion 은 건드리지 않는다.

  새 `.cs` 마다 `.cs.meta` 를 함께 만든다. Unity 는 meta 없는 파일에 새 GUID 를 발급하고, 그러면 다음에
  이 repository 를 여는 사람마다 다른 GUID 를 얻는다.

- [ ] **Step 3: Rollout / Rollback** — flag 도 migration 도 없다. `fix/57` 위에 쌓이므로 PR 은
      `--base fix/57` 로 열고, #63 이 merge 되고 branch 가 지워지면 GitHub 이 `develop` 으로 옮긴다.

## Validation

- **Commands to run:**
  - **이 기계에서는 아무것도 돌릴 수 없다.** Unity editor 도 `dotnet`/`mono`/`mcs` 도 없어 C# 을 compile
    조차 하지 못한다.
  - CI 도 대신 돌려 주지 않는다. `unity-tests.yml` 은 `develop` 을 포함한 모든 branch 에서 Unity licence
    활성화 (`No ULF license found`) 에서 실패하고 editor 가 아예 뜨지 않는다. 마지막 green run 은
    2026-09-08 이고, 이 branch 의 결함이 아니다.
  - 사람이 실제 Unity 세션에서 돌려야 하는 것:
    ```bash
    .github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test
    <Unity> -batchmode -nographics -runTests -testPlatform PlayMode \
      -projectPath /tmp/unity-play-mcp-test -testResults /tmp/unity-play-mcp-test/results.xml
    <Unity> -batchmode -nographics -runTests -testPlatform EditMode ...
    python3 .github/scripts/summarize-test-results.py /tmp/unity-play-mcp-test/results.xml PlayMode
    ```
- **Expected output:** 두 platform 모두 green. 특히 `HostReloadRecoveryTests` 가 `fix/57` 에서와 똑같이
  통과해야 한다 — `AssemblyReloadSimulation` 을 일반화한 것이 그 fixture 의 뜻을 바꾸지 않았다는 증거가
  그것뿐이다.

## Risks & Rollback

- **Risks:**
  - **compile 되지 않은 코드다.** 이 기계에 C# compiler 가 없다. 작성한 test 는 한 번도 실행되지 않았다.
    merge 전에 사람이 두 suite 를 실제 Unity 에서 돌려야 한다.
  - `[SerializeField, HideInInspector] GameObject` 참조가 play 중 reload 를 건너 돌아온다는 것에 기댄다.
    Unity 가 문서로 약속하는 자리이고 `fix/57` 의 `hasStarted` 가 같은 mechanism 을 쓰지만, 여기서 확인할
    수는 없다. 만약 null 로 돌아오면 overlay 가 두 벌이 된다 — `KeepsOneCursorCanvasAfterAReload` 와
    `KeepsOneStatusPanelAfterAReload` 가 그것을 붙잡는다.
  - reload 를 살아남은 `Texture2D` 와 `Sprite` 를 `DiscardOverlay` 가 놓아준다. 이미 파괴된 뒤라면
    null 확인에 걸려 그냥 지나간다. 어느 쪽이든 안전하지만, 어느 쪽이 실제인지는 모른다.
  - `Awake` 를 없애 생성 시점이 `Awake` 에서 `OnEnable` 로 밀린다. Unity 는 scene 로드에서 모든
    `Awake` 를 돌린 뒤 `OnEnable` 을 돌리므로, 다른 component 의 `Awake` 가 cursor 를 읽으면 늦어진다.
    지금 `cursorTransform` 을 읽는 것은 `MoveTo` 와 `Update` 뿐이고 둘 다 그 뒤에 온다.
  - `AssemblyReloadSimulation` 이 readonly collection 을 비우게 되어 `HostReloadRecoveryTests` 가 보는
    것이 미세하게 넓어진다. 걸리는 field 는 비어 있는 `actionRequests` 하나뿐이라 결과는 같아야 한다.
- **Rollback steps:** `git revert`. 상태 이관도 migration 도 없다.

## Plan review

review subagent 가 session rate limit (HTTP 429) 에 걸려 돌지 못했다. fast·medium·heavy 세 역할을 이
자리에서 직접 한 번씩 돌렸고, 결과는 위 본문에 이미 반영했다. 사람이 돌린 review 가 아니라는 것을 그대로
적어 둔다.

접은 지적:

- **`builtOverlay` 를 없애고 `cursorTransform == null` / `keyStatusText == null` 로 판단하라.** 접는다.
  여섯 개 참조 중 하나를 골라 나머지의 대리로 쓰는 것이라, 나중에 그 하나만 다른 자리에서 채워지면
  조용히 어긋난다. bool 하나가 "이 domain 에서 만들었는가" 를 그대로 말하고, `fix/57` 의 `ownsRuntime`
  과 같은 모양이다.
- **두 controller 의 `OnEnable` + `DiscardOverlay` 를 공통 base class 로 묶어라.** 접는다. 두 메서드가
  하는 일이 다르고 (한쪽만 texture 를 놓아준다), `MonoBehaviour` 상속을 한 층 더 쌓는 것은
  `coding-style.md` 가 막는 deep inheritance 다. 중복은 여섯 줄이다.
- **`DiscardOverlay` 의 sprite·texture 정리를 빼라 (살아남는지도 모르면서 쓰는 코드다).** 접는다.
  살아남지 않으면 null 확인에 걸려 지나가고, 살아남으면 여기 말고 그것을 놓아줄 자리가 없다. 다섯 줄로
  reload 마다의 누수 하나를 닫는다. 다만 이것이 이 plan 에서 가장 약한 항목이라는 것은 인정하고, PR 의
  Risks 에 그대로 적는다.

## Open Questions

- runtime 에 만든 `Texture2D` 와 `Sprite` 가 play 중 domain reload 를 실제로 살아남는가. (B) 를 고른
  덕에 답이 어느 쪽이든 fix 는 성립하지만, 답을 알면 `DiscardOverlay` 의 asset 정리가 필요한지 없는지가
  정해진다. Unity 가 있는 사람이 reload 한 번으로 확인할 수 있다.
