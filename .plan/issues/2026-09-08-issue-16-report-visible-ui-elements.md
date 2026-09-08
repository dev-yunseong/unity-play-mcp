# 2026-09-08 — 화면에 보이는 UI 요소만 모아 본다

- Date: 2026-09-08
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/16
- Status: Ready (fast·medium·heavy review 반영본)

## Goal

PULSE 가 화면에 보이는 UI 요소를 나르게 하고, 그중 지금 눈에 닿는 것만 골라 내는 MCP tool 을
더한다.

1. **SDK** — `Worth.Ask` 에 네 번째 길을 더한다. `Graphic` 하위(`Text`, `Image`,
   `RawImage`), `TMP_Text`, `Selectable` 하위(`Slider`, `Toggle`, `Dropdown`,
   `Scrollbar`, `InputField`) 중 하나를 단 GameObject 는 감시 멤버가 없어도 `active` 에
   실린다. 그 컴포넌트가 보여 주는 것이 기존 `by[].m[]` 에 실리고, 컴포넌트 타입 이름은
   기존 `by[].on` 에 그대로 실린다.
2. **SDK** — 그 요소가 화면 안인지(`onScreen`)와 뒤에 그려진 것에 가려졌는지(`covered`)를
   객체에 싣는다. 자리는 기존 `rect` 를 그대로 쓴다.
3. **MCP** — 새 tool `get_visible_elements` 가 게임에 묻지 않고 store 를 순회해 보이는 요소만
   낸다. 꺼져 있거나 화면 밖이거나 가려진 것은 기본으로 뺀다.
4. **SDK** — `Worth.cs:13` 이 요구하는 리포트/PULSE 규칙 일치를 지키기 위해, affordance
   리포트의 객체 목록도 같은 만큼 넓힌다.

## Non-goals

- `Renderer`(스프라이트·3D) 요소. `ScreenArea.Of` 가 이미 `Renderer` bounds 를 다루므로
  나중에 넓힐 수 있다.
- 세션 on/off 스위치. `Worth` 를 통과시키므로 응답은 항상 커진다.
- hierarchy 접기·펼치기(#15), 값 변화 이력(#13).
- `mcp/src/pulse.ts` — #19 가 member key 결함을 고치는 중이다. 건드리지 않는다.
- `mcp/src/tree.ts`, `mcp/test/tree.test.ts` — #18 이 쥐고 있다.
- `mcp/src/instructions.ts` — server 안내문은 tool 설명을 옮겨 적지 않는 자리다. 새 tool 은
  제 설명으로만 소개한다.
- `Image` 의 `sprite` 이름. issue 가 낼 것으로 적은 것은 글자·채움 비율·값 셋이고,
  스프라이트 이름은 리포트의 `SceneEvidenceScan.SpriteOf` 가 이미 답한다.
- `Pulse.SchemaVersion` 을 올리지 않는다. 새 필드는 전부 선택적이고 `by[]` 항목이 늘 뿐이라
  기존 독자가 못 읽는 모양이 아니다 (`Pulse.cs:50` 의 remark 가 번호를 언제 옮기는지 적어
  두었다).

## Context / Constraints

**읽은 것**

- `Runtime/Affordance/Scan/Live/Worth.cs:62` — `Ask` 의 세 길: `byOwner.ContainsKey(type)`,
  `AffordanceCatalog.For(type) != null`, `PersistentCallReader` 가 읽은 호출.
  `Writing`(`Worth.cs:38`) 이 instance id 로 답을 기억한다 (`MaxRemembered = 4096`).
- `Runtime/Affordance/Scan/Live/Readable.cs:189` — `TheGames(type)` 이 `UnityEngine`,
  `TMPro` 로 시작하는 어셈블리를 걸러낸다. 그래서 `UnityEngine.UI.Text` 를 단 객체는
  `Worth` 를 통과하더라도 `Readable.On` 이 빈 목록을 주고, `LiveState.cs:660` 의
  `if (count > 0)` 때문에 `by` 항목이 아예 안 써진다. **네 번째 길만으로는 acceptance
  criteria 둘째 줄(무엇을 보이고 있는지)도, `by[].on` 에 타입 이름을 싣는 것도 못 한다.**
- `Runtime/Affordance/Scan/Live/LiveState.cs:499` — `Object` 가 컴포넌트마다
  `Readable.On(type, named)` 을 부르고, 돌아온 `Watched` 마다 `Value` → `Read` 로 값을
  쓴다. `Read`(`LiveState.cs:786`)는 `member.Field.GetValue(on)` 으로 시작한다.
- `Runtime/Affordance/Scan/Live/LiveState.cs:751` — `Along(from, path)` 은 필드와 인자 없는
  프로퍼티를 둘 다 걷는다. 프로퍼티를 읽는 길이 이미 있다.
- `Runtime/Affordance/Scan/ScreenArea.cs:38` — `Of(Transform)` 이 좌상단 기준 픽셀 `Rect` 를
  준다. `ScreenArea.cs:63` 이 이미 `GetComponentInParent<Canvas>()` 를 쓴다 — `Canvas` 는
  `com.unity.modules.ui` 의 타입이라 uGUI 참조 없이 쓸 수 있다.
- `Runtime/Affordance/Scan/SceneEvidenceScan.cs:192` — 객체 admission 은 `if (!wrote)`.
  컴포넌트 admission(`SceneEvidenceScan.cs:578`)은 `evidence != null || calls.Count > 0`.
  `SceneEvidenceScan.cs:298` 의 주석이 텍스트와 이미지를 **컴포넌트로는** 쓰지 않는 이유를
  적어 두었다.
- `Runtime/TargetLookup.cs:64` — `click` 이 대상을 거절하는 기준은
  `selectable.isActiveAndEnabled && selectable.IsInteractable()` 다. `IsInteractable()` 은
  메서드라 `Along` 이 못 걷는다. 읽을 수 있는 것은 `interactable` 프로퍼티뿐이고, 그것은
  부모 `CanvasGroup` 을 셈하지 않는다.
- `Runtime/Affordance/Scan/UnityPlayMcp.Affordances.Scan.asmdef` — `UnityEngine.UI` 도
  `Unity.TextMeshPro` 도 참조하지 않는다. `Graphic`/`Selectable`/`TMP_Text` 를 컴파일
  대상으로 삼을 수 없고, `SceneEvidenceScan.Derives` 처럼 타입 이름으로 맞춰야 한다.
- 두 test assembly 는 asmdef 에 `UnityEngine.UI` 를 적지 않고도 그것을 쓴다
  (`Tests/Runtime/CursorControllerTests.cs`, `Tests/PlayMode/PointerActionTests.cs`).
  **asmdef 은 안 고친다.**
- `mcp/src/pulse.ts:11` — `PulseComponent.members`, 그러나 SDK 는 `"m"` 으로 쓴다. 그 결함
  때문에 `by` 가 비어 있지 않은 frame 은 지금 fold 자체가 실패한다. **#19 가 고친다.**
- `mcp/src/pulse.ts:19` — `PulseObject` 에 index signature 가 있어 SDK 가 더한 필드는
  `mergeObject` 의 spread 를 그대로 통과한다. `PulseFrame` 에는 없다 — pulse frame 머리에
  새 필드를 얹으면 `pulse.ts` 를 고쳐야 하므로 그 길은 막혀 있다. 그래서 화면 크기를 MCP
  쪽으로 보내는 대신 `onScreen` 을 SDK 가 계산해 객체에 싣는다.
- `Worth.Forget()` 은 어디서도 불리지 않는다(`grep`). 새 캐시에 `Forget` 을 달지 않는다.

**제약**

- 초당 열 번 도는 자리다. 네 번째 길은 반드시 `Worth` 의 기존 instance id 캐시를 타야 한다.
- 이 저장소 머신에 Unity editor 가 없다. EditMode·PlayMode 는 로컬에서 못 돌린다.
- 실제 게임에 붙여 응답 크기와 리포트 목록 길이를 재는 issue 의 validation note 는 돌아가는
  게임이 없어 수행 불가다. PR 에 그대로 적는다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 위 `Context` 가 recon 결과다.

- [ ] **Step 1: `Drawn` (신규, `Runtime/Affordance/Scan/Live/Drawn.cs`)**

  왜 `Readable` 안의 private helper 가 아니라 별도 파일인가: 부르는 곳이 셋이다 — `Worth`
  (네 번째 길), `Readable`(멤버 목록), `SceneEvidenceScan`(리포트 admission). 그리고 이것은
  **instance id** 캐시를 쥐는데 `Readable` 의 캐시는 **타입** 키다. 두 수명을 한 클래스에
  두면 어느 쪽이 언제 버려지는지가 흐려진다.

  - `Is(Type)` — base type 사슬을 `UnityEngine.UI.Graphic`, `UnityEngine.UI.Selectable`,
    `TMPro.TMP_Text` 세 이름에 맞춘다. `SceneEvidenceScan.Derives` 와 같은 이유로 이름
    비교다. `Slider` 는 `Graphic` 이 아니라 `Selectable` 이므로 두 뿌리가 다 필요하다.
  - `Any(GameObject subject)` → `bool` — 객체가 그런 컴포넌트를 하나라도 나르는가.
    instance id 로 답을 기억한다(`Dictionary<int, bool>`, `MaxRemembered = 4096`, 차면
    `Clear()` — `Worth`/`LiveState.Offers` 와 같은 거래). 컴포넌트 구성이 객체 수명 동안
    안 바뀐다는 전제는 `LiveState` 의 `Offers` 캐시가 이미 쓰는 것과 같다.
  - `Add(List<Watched> into, HashSet<string> taken, Type type)` → `void` — `into` 와
    `taken` 을 제자리에서 고친다. `Is(type)` 이 거짓이면 아무것도 안 한다. 참이면 아래 일곱
    이름 중 그 타입에 실제로 있는 **인자 없는 읽기 프로퍼티**마다 `Watched` 를 하나
    넣는다:

    ```
    Member    = property.Name          // Read 가 Field == null 일 때 이 이름으로 읽는다
    Property  = null                   // Named() 가 Property ?? Member 를 쓴다
    Declaring = property.DeclaringType?.FullName ?? type.FullName
    Type      = property.PropertyType.FullName
    Static    = false
    Field     = null                   // "컴포넌트 자신에서 Member 를 읽어라"
    Owner     = type
    Asked     = false                  // 근거가 청한 것이 아니다
    ```

    이름이 `taken` 에 이미 있으면 건너뛰고, 넣은 이름은 `taken` 에 더한다.

  - **일곱 이름과 그 이유**: `text`, `fillAmount`, `value`, `normalizedValue`, `isOn`,
    `texture`, `interactable`.
    - `text` = 글자 (`Text`, `TMP_Text`, `InputField`)
    - `fillAmount`, `normalizedValue` = 채움 비율 (`Image`, `Slider`, `Scrollbar`)
    - `value`, `isOn` = 값 (`Slider`, `Scrollbar`, `Dropdown`, `Toggle`)
    - `texture` = `RawImage` 가 보여 주는 것
    - `interactable` = `Selectable` 이 지금 눌리는지
    - 뒤의 둘이 없으면 `RawImage` 와 `Button` 은 멤버가 하나도 없어 `LiveState.cs:660` 의
      `if (count > 0)` 에 걸리고, **`by[].on` 에 타입 이름이 영영 안 실린다.** 그러면 새
      tool 이 그 요소를 볼 방법이 없다. `interactable` 은 그 밖에도 "계속 버튼이 비활성으로
      보인다" 같은 줄이 직접 묻는 값이다.
    - `interactable` 은 컴포넌트 제 스위치이고 `TargetLookup.cs:64` 가 `click` 에 쓰는
      `IsInteractable()` 과 다르다 — 후자는 부모 `CanvasGroup` 까지 본다. 메서드라 `Along`
      이 못 걷는다. 그 차이를 `Drawn` 의 doc comment 에 적는다.
    - `color` 는 안 싣는다. `Color` 는 struct 라 `Read` 의 마지막 분기로 떨어져
      `{"is":"UnityEngine.Color"}` 만 나온다 — 아무 말도 안 하면서 자리만 찬다.

- [ ] **Step 2: `Read` 가 프로퍼티도 읽게 한다 (`LiveState.cs:786`)**
  - `held = member.Field == null ? Along(on, member.Member) : member.Field.GetValue(on);`
  - `Drawn` 이 내는 이름은 언제나 한 마디다(프로퍼티 이름에는 `.` 이 없다). `Along` 이
    `Split('.')` 로 걷지만 걸음은 하나다.
  - **값**: `Along` 은 `GetField`/`GetProperty` 를 매번 다시 푼다. `Field.GetValue` 쪽은
    `Readable.Ask` 가 한 번 잡아 둔 `FieldInfo` 를 쓴다. 즉 그려지는 요소마다 초당 열 번
    조회가 하나씩 는다. 이미 있는 `Via` 경로가 정확히 같은 값을 치르고 있으므로 새로운
    종류의 위험은 아니다. `PropertyInfo` 를 `Watched` 에 캐시하는 것은 지금 하지 않는다 —
    `Watched` 에 필드를 하나 더 다는 일이고, 그 값이 실제로 문제인지 잴 게임이 없다.
    이 문장을 `Read` 의 주석에 남겨 다음 사람이 재고 고칠 수 있게 한다.
  - `Watched.Field` 의 doc comment 에 계약을 적는다: **`null` 이면 컴포넌트 자신에서
    `Member` 를 읽는다.** 지금 그런 `Watched` 를 만드는 곳은 `Drawn.Add` 뿐이고 그것은
    인스턴스 전용이라 `Statics` 경로에는 닿지 않는다.

- [ ] **Step 3: `Readable.Ask` 가 `Drawn` 을 부른다 (`Readable.cs:89`)**
  - 기존 필드 순회를 그대로 `private static void Fields(List<Watched> into,
    HashSet<string> taken, Type type)` 로 옮긴다. `GetFields` 의 try/catch 와 `Skip` 검사,
    `Watched` 생성이 통째로 그 안으로 간다. `into` 와 `taken` 을 제자리에서 고치고 아무것도
    돌려주지 않는다. 예외가 나면 그냥 돌아온다 — 지금의 `return members;` 와 같은 뜻이다.
  - `Ask` 는 이렇게 된다:
    ```
    var members = new List<Watched>();
    var taken   = new HashSet<string>(StringComparer.Ordinal);
    ... named 를 members 로, 그 Field.Name 을 taken 으로 ...
    if (TheGames(type)) { Fields(members, taken, type); }
    Drawn.Add(members, taken, type);
    return members;
    ```
  - `Drawn.Add` 를 **분기 밖에서** 부른다. `Image` 를 상속한 게임 컴포넌트도 여전히
    `fillAmount` 를 그리는데, `GetFields` 는 기반 클래스의 private 필드를 안 주므로 그 값은
    이 길로만 나온다.
  - 답은 `Readable` 의 기존 타입 캐시(`MaxRemembered = 2048`)에 그대로 실린다.

- [ ] **Step 4: `Worth.Ask` 의 네 번째 길 (`Worth.cs:62`)**
  - `Writing` 이 bool 대신 `Worth.Admitted` (`No`, `Evidence`, `Drawn`) 를 돌려준다. 캐시도
    그 값을 기억한다. 부르는 곳은 `LiveState.In` 하나뿐이다.
  - 네 번째 길은 컴포넌트 순회 **뒤에** 둔다. 근거를 나르면서 동시에 라벨을 단 객체 —
    `Image` 를 얹은 `Button` — 는 `Evidence` 로 세야 한다. 순회 앞에 두면 그런 객체가
    전부 `Drawn` 이 되어 Step 6 의 예산 구분이 뜻을 잃는다. 값은 객체마다 한 번이고 그
    뒤로는 캐시가 답한다.
  - 클래스 `remarks` 에 네 번째 길과, 그것이 리포트 쪽에서도 같이 넓어진다는 것을 적는다.

- [ ] **Step 5: `Sight` (신규, `Runtime/Affordance/Scan/Live/Sight.cs`)**
  - `internal static bool OnScreen(Rect area, int width, int height)` — 넓이와 높이가
    0 보다 크고 `(0,0,width,height)` 와 겹치는가.
  - `internal static bool Covers(Rect later, Rect area)` — `later` 의 넓이가 0 보다 크고
    `area` 의 한가운데를 품는가.
  - `internal static Dictionary<int, string> Survey(List<Transform> walked, int width,
    int height)` — instance id 를 `,"onScreen":true,"covered":false` 같은 조각에 건다.
    `Offered`(`LiveState.cs:1080`)가 이미 쓰는 모양이라 `Object` 는 그것을 장부에 대고
    붙이기만 한다. 보이는 요소가 아닌 transform 은 map 에 아예 안 들어간다.
  - 가려짐은 **걷는 순서**로 판단한다. `GetComponentsInChildren<Transform>(true)` 는
    depth-first pre-order 이고 한 canvas 안의 uGUI 그리는 순서도 그렇다 — 뒤에 오는 것이
    위에 그려진다. `j > i` 인 요소가 `i` 의 한가운데를 덮으면 `i` 는 가려진 것으로 본다.
  - **같은 canvas 아래끼리만 견준다.** 요소마다
    `transform.GetComponentInParent<Canvas>()` 를 한 번 풀어 그 instance id 를 함께 쥐고,
    그것이 다른 두 요소는 아예 비교하지 않는다. root canvas 가 둘인 게임 — HUD 위에 모달을
    얹는, UI 가 겹치는 바로 그 게임 — 에서 hierarchy 의 root 순서는 그리는 순서와 아무
    관계가 없기 때문이다. 이렇게 하면 놓치는 쪽으로 틀린다(모달이 HUD 를 덮어도 안 덮었다고
    한다). 덮었다고 잘못 말하는 것보다 낫다: 앞의 실수는 요소가 하나 더 나오는 것이고 뒤의
    실수는 있는 요소가 사라지는 것이다.
  - 자기 자손은 뺀다(`IsChildOf`): 버튼 위의 캡션이 그 버튼을 가렸다고 말하면 안 된다.
  - **꺼져 있는 것은 아예 안 본다.** `In` 은 `GetComponentsInChildren<Transform>(true)` 로
    걷고 `Worth` 는 `activeInHierarchy` 를 보지 않으므로 `walked` 에는 꺼진 것이 섞여 있다.
    `ScreenArea.Of` 는 꺼진 `RectTransform` 에도 멀쩡한 사각형을 준다 — `GetWorldCorners` 는
    켜짐을 안 본다. 그래서 닫힌 모달(같은 canvas 아래, HUD 뒤에 선언된 전체 화면 `Image`)이
    HUD 요소 전부를 `covered: true` 로 만들고, tool 의 기본 필터가 그것들을 지운다. 화면에
    실제로 보이는 것이 목록에서 사라지는 것이므로 이것은 놓치는 쪽으로 기운 실수가 아니다.
    덮개로도 세지 않고 map 에도 넣지 않는다 — tool 이 `active === false` 를 어차피 기본으로
    빼므로 넣지 않아도 잃는 것이 없다.
  - **그래도 틀릴 수 있다.** `Canvas.sortingOrder`, `overrideSorting`, 투명한 그림,
    `RectMask2D` 로 잘린 것을 이 규칙은 모른다. tool 설명에 그렇게 적고 `capture_screen` 을
    가리킨다.
  - 값: 요소 n 개에 대해 최악 O(n²) 이지만 첫 덮개에서 멈추고, `IsChildOf` 는 canvas 검사와
    사각형 검사를 통과한 뒤에만 돈다. **상한은 두지 않는다.** 상한을 두면 그 위에서
    `covered` 가 조용히 거짓이 되는데, 이 패키지는 다른 모든 자리에서 "모르는 것" 을
    "아니오" 로 보고하는 것을 거절한다.

- [ ] **Step 6: `In` 을 두 단계로 나눈다 (`LiveState.cs:434`)**
  - 한 요소의 `covered` 는 **나중에** 만나는 요소들의 사각형에 달려 있다. 한 번 걷기로는
    나올 수 없는 값이라 단계를 나눈다. **씬은 여전히 한 번만 걷는다** — 순회가 비싼
    절반이라는 것이 `Compose` 의 전제다.
  - 1단계: 기존 순회 그대로 걷되 `Worth.Writing` 을 통과한 transform 과 그 root index 를
    `List<Transform> walked`, `List<int> from` 에 모으기만 한다. `seen`/`dropped` 회계는
    그대로 1단계에 남는다.
  - **화면 요소에 제 예산을 준다.** `LiveState.cs:470` 의 상한은 `already >= MaxHolders *
    MaxHolders` 이고 `seen` 의 키는 `transform.gameObject.GetType()` — 언제나
    `UnityEngine.GameObject` 다. 즉 실제 상한은 **pulse 하나에 객체 256 개**이고, `seen` 은
    `Objects` 에서 한 번 만들어져 모든 씬이 나눠 쓴다. 네 번째 길이 그 예산을 함께 쓰면
    canvas 하나가 그것을 다 먹고, 앞의 세 길로 들어온 객체 — pulse 가 존재하는 이유인 그
    객체들 — 가 밀려난다. 지금 나가던 것이 안 나가게 되는 회귀다.
    `Worth.Writing` 이 돌려준 `Admitted` 가 `Drawn` 이면 `seen` 의 키를 `typeof(Drawn)` 으로
    잡는다. 상한 검사는 한 줄도 안 바뀌고 예산만 둘이 된다 — 근거 쪽 256, 화면 요소 쪽 256.
    `dropped` 는 지금처럼 하나로 세어 `holder-limit:N` 으로 나간다. 무엇이 몇 개 빠졌는지를
    두 갈래로 나눠 말하는 것은 이 issue 가 답할 물음이 아니다.
  - 2단계: `var sight = Sight.Survey(walked, Screen.width, Screen.height);` 한 번, 그다음
    모은 것마다 `Object(...)`.
  - survey 는 씬마다 한 번이다. 씬을 넘는 그리는 순서는 어차피 canvas sorting 이 정하고,
    canvas 로 묶는 규칙이 그것을 이미 막는다.
  - `Object` 에 `Dictionary<int, string> sight` 를 넘긴다. `Where` 바로 뒤에서
    `private static bool Sighted(StringBuilder text, Transform transform, Ledger ledger,
    string identity, Dictionary<int, string> sight)` 가 붙인다:
    id 가 map 에 없으면 `false`, 있으면 `ledger.Keep(identity + "|sight", rendered)` 로
    걸러 `text.Append(rendered)` 하고 `true`. `Offered` 와 정확히 같은 모양이다.
  - 목록에 없는 객체 — 보이는 요소가 아닌 것 — 는 아무 필드도 안 쓴다. 없음은 "안
    가려졌다" 가 아니라 "보이는 요소가 아니다" 다.

- [ ] **Step 7: 리포트 목록을 같이 넓힌다 (`SceneEvidenceScan.cs:192`)**
  - 객체 admission 만 넓힌다: `if (!wrote && !Drawn.Any(subject)) { return false; }`
  - 컴포넌트 admission(`SceneEvidenceScan.cs:578`)은 **안 건드린다.**
    `SceneEvidenceScan.cs:298` 이 텍스트와 이미지를 컴포넌트로 쓰지 않는 이유를 적어 두었고
    그 이유는 그대로 유효하다. 넓어지는 것은 issue 가 말한 그것 — 리포트의 **객체 목록** —
    이고, 새로 실리는 객체는 이미 리포트가 모으는 `label`/`sprite`/`visuals` 를 들고
    `components` 는 빈 채로 온다.
  - `using UnityPlayMcp.Affordances.Live;` 를 더한다. 같은 어셈블리의 이웃 namespace 다.

- [ ] **Step 8: MCP tool 의 판정 (`mcp/src/visible.ts` 신규)**
  - `visibleElements(state: FoldedPulseState, query: VisibleQuery): VisibleElement[]` —
    순수 함수.
  - **판정은 namespace 접두사 하나다**: `by[].on` 이 `UnityEngine.UI.` 또는 `TMPro.` 로
    시작하고, 그 컴포넌트가 멤버를 하나라도 들었으면 보이는 요소로 본다. 정확한 타입 이름
    목록은 **두지 않는다** — 열두 개를 적어 봐야 전부 그 두 접두사로 시작하므로 같은 규칙을
    두 곳에 적는 일이고, 목록이 늘 때 한쪽만 고쳐지는 것이 이 issue 가 SDK 대신 TypeScript
    에 판정을 둔 이유 그 자체다.
  - 이것이 맞는 이유: SDK 는 `Readable.On` 이 멤버를 준 컴포넌트만 `by` 에 쓴다
    (`LiveState.cs:660`). `UnityEngine`/`TMPro` 어셈블리의 타입에 멤버가 붙는 길은 둘뿐이다
    — `Drawn` 이 넣은 것이거나, 근거가 그 타입의 멤버를 이름 댄 것이거나. 앞의 것이 정확히
    이 tool 이 찾는 것이고, 뒤의 것도 근거가 이름 댄 Unity UI 컴포넌트라 UI 요소가 맞다.
    `ScrollRect`, `LayoutElement`, `Mask` 처럼 아무도 안 읽는 것은 `by` 에 아예 안 나온다.
  - **못 잡는 것**: 게임이 `Image` 나 `Button` 을 상속해 만든 타입은 제 이름
    (`MyGame.HealthBar`)으로 오므로 접두사에 안 걸린다. tool 설명에 적는다.
  - 요소 하나: `{ id, path, selector, scene?, on, shows, rect?, active, onScreen?,
    covered? }`. `shows` 는 그 컴포넌트의 `members` 를 `{ 멤버 이름: 값 }` 으로 접은 것이다.
    한 객체에 보이는 컴포넌트가 둘이면 항목도 둘이다.
  - `rect` 는 객체의 top-level `rect` 다. `Where`(`LiveState.cs:1007`)가 `world` 와 `rect`
    를 객체 바로 아래에 쓴다 — `pulse.ts:24` 의 `where?` 필드는 지금 SDK 가 안 쓰는 이름이다.
  - 기본 필터: `active === false`, `onScreen === false`, `covered === true` 를 뺀다.
    `includeHidden` 이 서면 전부 내고, 각 요소가 왜 빠졌을지를 제 필드로 말한다.
  - `selector` 는 `get_scene_state` 와 같은 부분 문자열 규칙이다.

- [ ] **Step 9: `tools.ts` 에 `registerTool` block 하나**
  - `get_scene_state` block 바로 뒤에 둔다. 같은 store 에 대고 답하는 tool 이 나란히 서는
    것이 읽기 쉽다. `tools.ts` 편집은 이 block 하나로 끝낸다.
  - 이름 `get_visible_elements`, 입력 `{ selector?: string, includeHidden?: boolean }`.
  - 설명에 반드시 넣을 것: 게임에 묻지 않고 마지막 reading 을 읽는다 / 기본으로 꺼진 것과
    화면 밖과 가려진 것을 뺀다 / **`covered` 는 사각형 겹침 추측이라 canvas 구성에 따라
    틀릴 수 있고, 확실한 답은 `capture_screen`** / 게임이 상속해 만든 UI 타입은 안 걸린다.

- [ ] **Step 10: Tests**
  - `mcp/test/visible.test.ts` (신규, `node:test`) — **`FoldedPulseState` literal 에 대고
    돌린다.** `PulseStore.fold` 를 태우면 #19 가 고치는 중인 member key 결함에 묶이고,
    그쪽이 어느 이름으로 정하든 이 test 가 따라 깨진다.
    - 보이는 요소 타입만 고른다 — 게임 script 를 단 컴포넌트는 안 나온다.
    - `UnityEngine.UI.*` 인데 멤버가 없는 컴포넌트는 안 나온다.
    - `shows` 가 `text`/`fillAmount`/`value`/`isOn` 을 실어 낸다.
    - 기본으로 `covered: true` 를 뺀다. `includeHidden` 이 그것을 도로 낸다.
    - 기본으로 `onScreen: false` 를 뺀다.
    - 기본으로 `deactive` 목록의 요소를 뺀다. `includeHidden` 이 `active: false` 로 낸다.
    - `selector` 가 부분 문자열로 좁힌다.
    - 한 객체에 보이는 컴포넌트가 둘이면 항목도 둘이다.
    - `rect` 를 그대로 옮겨 싣는다.
  - `Packages/.../Tests/Runtime/VisibleElementTests.cs` (EditMode, 신규):
    - `Drawn.Is` 가 `Text`/`Image`/`RawImage`/`Slider`/`Toggle`/`Scrollbar`/`Button` 을
      통과시키고 `Transform`/`Rigidbody` 를 거절한다.
    - `Drawn.Add` 가 `Text` 에서 `text`, `Image` 에서 `fillAmount`, `Slider` 에서 `value`,
      `Toggle` 에서 `isOn`, `Button` 에서 `interactable` 을 낸다. `Field` 가 `null` 이고
      `Asked` 가 `false` 다. `taken` 에 있는 이름은 안 낸다.
    - `Sight.OnScreen`/`Sight.Covers` 를 순수 `Rect` 로 검증한다 — 화면 밖 왼쪽, 넓이 0,
      한가운데를 덮는 것과 걸치기만 하는 것. **`Screen` 을 안 읽으므로 batch mode 에서도
      결정적이다.**
    - `Sight.Survey` 가 꺼져 있는 덮개를 덮개로 세지 않는다 — 닫힌 모달이 HUD 를 지우면 안
      된다.
    - `Worth.Writing` 이 `Text` 만 단 객체에 `Admitted.Drawn`, 아무것도 안 단 객체에
      `Admitted.No`, `byOwner` 에 있는 타입을 단 객체에 `Admitted.Evidence` 를 답한다.
      `Text` 와 `byOwner` 타입을 **둘 다** 단 객체도 `Admitted.Evidence` 다 — 그것이
      Step 6 의 예산 구분을 서게 하는 순서다.
    - 예산 회계 자체(`In` 안의 두 줄)는 test 로 못 덮는다. `In` 은 private 이고, 객체 300 개를
      `Compose` 에 통과시키려면 test 어셈블리에 없는 구운 watch list 가 필요하다 —
      `PulseGoneTests` 가 같은 이유를 이미 적어 두었다. 위의 분류 test 가 그 두 줄이 읽는
      값을 덮는다.
  - `Packages/.../Tests/PlayMode/VisibleElementPulseTests.cs` (PlayMode, 신규):
    - Canvas 아래 `Text` 를 세우고 `LiveState.Compose` 를 돌려 그 객체가 `active` 에
      실리는지, `"on":"UnityEngine.UI.Text"` 와 `"member":"text"` 와 그 글자가 나오는지,
      `onScreen` 이 실리는지 본다.
  - asmdef 은 안 고친다. 두 test assembly 가 이미 `UnityEngine.UI` 를 쓴다.

- [ ] **Step 11: Rollout / Rollback** — feature flag 없음. issue 의 "알려진 결과" 가 세션
  스위치를 명시적으로 non-goal 로 두었다. rollback 은 revert 한 번.

## Validation

- **돌린 것과 결과:**
  - `~/.nvm/versions/node/v24.18.0/bin/npm ci && npm run build && npm test` (`mcp`) —
    **124 pass, 0 fail** (기준선 114 + 새 test 10). zsh 의 nvm lazy load 가 bare `npm` 에서
    재귀하므로 절대 경로로 부른다.
  - Unity 2022.3.34f1 (`/mnt/c/Program Files/Unity/Hub/Editor/2022.3.34f1/Editor/Unity.exe`,
    `ProjectVersion.txt` 가 고정한 그 버전) 로 두 suite 를 돌렸다. throwaway project 는
    `.github/scripts/setup-unity-test-project.sh /mnt/c/Temp/unity-play-mcp-16-test` 로
    Windows 파일 시스템에 조립한다 — Unity.exe 에 native 경로를 줘야 한다.
    - **EditMode 276 pass, 0 fail** (기준선 261 + 새 test 15)
    - **PlayMode 25 pass, 0 fail** (기준선 22 + 새 test 3)
  - baseline 은 merge-base(`origin/develop`, fa5562a)를
    `/mnt/c/Temp/unity-play-mcp-baseline-test` 에 따로 조립해 먼저 잡았다 — **EditMode
    261 pass, PlayMode 22 pass, 둘 다 0 fail.** 실패를 이 변경 탓으로 돌리기 전에 대조할
    기준이다.
  - 결과는 exit code 가 아니라 `summarize-test-results.py` 로 읽었다. exit code 2 는
    "돌았고 일부 실패" 다.
- **못 하는 것 (PR 에 그대로 적는다):**
  - **실제 게임에 대고 응답 크기와 리포트 목록 길이를 못 잰다.** 돌아가는 게임이 없다.
    issue 의 validation note 가 요구하는 것이고, fixture 로는 답이 안 나온다.
  - **멤버 값이 store 까지 못 온다.** `mcp/src/pulse.ts` 의 member key 결함(#19) —
    `mergeComponents` 가 `component.members` 를 읽는데 Unity 는 `"m"` 을 보낸다 — 때문에
    `mergeMembers` 의 `for (const member of incoming)` 가 `undefined` 를 돌려 `TypeError` 를
    던지고, `PulseStore.fold` 가 그것을 잡아 그 frame 을 통째로 버린다. 즉 새 tool 은 빈
    멤버를 내는 것이 아니라 **"No scene reading has arrived" 를 낸다.** 이것은 이 변경이
    만든 결함이 아니라 `by` 가 비어 있지 않은 모든 frame 에 이미 해당하는 것이고, #19 가
    merge 된 뒤에 풀린다. 그래서 이 PR 은 #19 뒤에 merge 되어야 한다.

## Risks & Rollback

- **응답이 커진다.** 스위치가 없으므로 UI 가 수백 개인 게임에서 모든 pulse 가 커진다.
  issue 가 이것을 "알려진 결과" 로 받아들였다. 실측은 못 했다.
- **리포트 목록이 커진다.** 라벨과 그림만 있는 객체가 리포트에 들어온다. 컴포넌트 목록은
  안 넓혔으므로 객체당 비용은 작지만, 객체 수는 늘어난다. 실제 게임에서 재지 못했다.
- **`covered` 가 틀릴 수 있다.** canvas sorting, `overrideSorting`, 투명한 그림, mask 를
  모른다. 같은 canvas 로 묶어 놓쳐도 안 덮었다고 말하도록 기울여 두었지만 위험은 남는다.
  tool 설명이 그렇게 말하고 `capture_screen` 을 가리키는 것이 이 위험에 대한 답이다.
- **리포트가 `object-limit` 에 걸릴 수 있다.** `SceneEvidenceScan.cs:22` 의
  `MaxObjects = 5000` 은 씬 하나의 상한이고, 객체 admission 이 넓어지면 그 예산을 쓰는
  객체가 는다. 넘으면 `object-limit` gap 과 함께 잘리고, 예전에 들어가던 객체가 빠질 수
  있다. 5000 은 256 보다 훨씬 넉넉해 pulse 쪽만큼 급하지 않아 예산을 나누지 않았다. 실제
  게임에서 재지 못했다.
- **`Sight.Survey` 가 O(n²) 이다.** 상한을 안 두었으므로 요소가 아주 많은 씬에서 pulse 당
  값이 늘어난다.
- **화면 좌표를 요소마다 두 번 계산한다** (`Sight.Survey` 와 `Where`). `GetWorldCorners` 와
  투영이라 싸지만 공짜는 아니다. 하나로 합치려면 `Where` 가 `Sight` 에 의존해야 하고 장부
  키 두 개가 한 계산을 나눠 갖게 된다.
- **`Along` 이 프로퍼티를 매 pulse 다시 푼다.** 그려지는 요소마다 초당 열 번 리플렉션 조회
  하나. 기존 `Via` 경로가 이미 같은 값을 치른다. 잴 게임이 없어 캐시는 안 넣는다.
- **`Read` 의 `Field == null` 분기.** 다른 곳에서 `Field` 없는 `Watched` 를 만들기 시작하면
  뜻이 흐려진다 — 그래서 `Watched.Field` 의 doc comment 에 그 계약을 적는다.
- **`interactable` 이 `click` 의 기준과 다르다.** 부모 `CanvasGroup` 을 셈하지 않으므로
  `interactable: true` 인데 `click` 이 거절할 수 있다. `Drawn` 의 doc comment 에 적는다.
- **Rollback:** `git revert`. schema version 을 안 올렸고 새 필드는 전부 선택적이라 이전
  MCP server 도 새 SDK 의 pulse 를 읽는다. 반대 방향도 마찬가지다.

## Open Questions

- 없음.

## Rejected feedback

- **fast #7 "tools.ts 삽입 위치를 정확한 줄 번호로"** — 줄 번호 대신 이웃(`get_scene_state`
  block 바로 뒤)으로 적었다. 다른 worker 들이 같은 파일 근처를 만지는 중이라 줄 번호는
  구현할 때 이미 틀려 있다.
- **medium #1 이 낸 두 갈래 중 "정확한 이름 목록만"** — 접두사만 쓰는 쪽을 택했다. 열두
  이름이 전부 그 두 접두사로 시작하므로 목록은 접두사 규칙에 온전히 먹히고, 두 곳에 적힌
  한 규칙은 반드시 어긋난다. 대신 "게임이 상속해 만든 타입은 안 걸린다" 를 tool 설명에
  적는 것으로 좁아지는 만큼을 밝힌다.
- **fast #8 "요소 수 상한을 두고 gap 으로 알린다"** — 안 둔다. 상한 위에서 `covered` 가
  조용히 거짓이 되고, 그것은 이 패키지가 다른 모든 자리에서 거절하는 모양이다. 상한이
  필요하다는 것은 잴 게임이 생긴 뒤에 알 일이다.
- **heavy 가 요구한 "화면 요소 300 개가 근거 객체를 밀어내지 않는 EditMode test"** — 그
  모양으로는 안 쓴다. `In` 이 private 이고 `Compose` 를 태우려면 구운 watch list 가 필요한데
  test 어셈블리에 그것이 없다 (`PulseGoneTests` 의 클래스 주석이 같은 제약을 적어 두었다).
  대신 그 두 줄이 읽는 값 — `Worth.Writing` 의 `Admitted` 분류 — 을 네 경우로 덮는다.
