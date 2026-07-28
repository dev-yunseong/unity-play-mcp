# 2026-07-28 — ARTEL-153 SDK 좌표 체계 world pos 통일 + screen 좌표 병행

- Date: 2026-07-28
- Jira: [ARTEL-153](https://artel-asm.atlassian.net/browse/ARTEL-153)
- Status: Implemented, unmeasured

## Goal

`GAME_STATE` 가 싣는 씬 스냅샷의 각 블록에 위치 정보를 추가한다. 기준 좌표계는
world 하나로 통일하고 (ARTEL-153 티켓 문구), 소비자가 화면 위에서 바로 쓸 수 있는
screen 좌표를 SDK 가 함께 투영해 싣는다.

## Non-goals

- 드래그 앤 드랍 / 마우스 조작 자체의 구현. 회의록의 "더 필요한 거 mouse, 드래그
  앤 드랍" 은 별개 작업이다. 이 작업은 그것이 필요로 할 좌표를 미리 깔아둘 뿐이다.
- 마스킹 / 오클루전 판정. `onScreen` 은 화면 경계와 카메라 전후만 본다. ScrollRect
  안에서 잘린 UI, 다른 오브젝트에 가려진 3D 오브젝트는 `onScreen: true` 로 남는다.
- `SceneScanReport` (`scan_all_scenes`) 쪽 좌표. 그 씬들은 언로드된 상태로
  보고되므로 카메라가 없고 screen 투영이 성립하지 않는다. 필요해지면 별도 작업.

## Context / Constraints

### 결정 근거: world 만으로 부족하고, screen 만으로도 부족하다

티켓은 "world position 기준 통일" 이다. 그 결정 자체는 타당하다 — UI 의
`anchoredPosition` 은 부모 체인 없이 해석 불가능한 로컬 값이라 그대로 실으면
소비자가 아무것도 못 한다. `TransformPoint` 로 해석된 world 를 싣는 것이 맞다.

다만 world 하나로는 두 가지가 안 된다.

1. **Canvas 렌더 모드마다 world 의 의미가 다르다.**
   - `ScreenSpaceOverlay`: world unit == screen pixel. 실제로
     [CursorController.cs:63](../../Packages/kr.artel.sdk/Runtime/CursorController.cs)
     은 `WorldToScreenPoint` 결과를 `.position` (world) 에 그대로 대입하고, 그게
     동작한다. 이 모드에서는 world 와 screen 이 애초에 같은 숫자다.
   - `ScreenSpaceCamera`: 카메라 앞 `planeDistance` 에 실재한다. 화면엔 고정돼
     보이는데 카메라가 움직이면 world 값이 매 프레임 바뀐다.
   - `WorldSpace`: 진짜 3D 공간.

   필드 하나가 세 가지 뜻이 된다.

2. **screen 투영은 SDK 만 정확히 할 수 있다.**
   [CursorController.cs:51-56](../../Packages/kr.artel.sdk/Runtime/CursorController.cs)
   가 이미 하는 분기 — canvas 의 `renderMode` 를 보고 Overlay 면 카메라를 `null`
   로 넘긴다 — 를 소비자는 재현할 수 없다. 소비자는 각 오브젝트가 어느 canvas
   밑인지, 그 canvas 의 `worldCamera` 가 뭔지 모른다. main camera 하나로 밀면
   Overlay UI 전체가 틀린 좌표가 된다. 편의가 아니라 정확성 문제다.

반대로 screen 만 싣는 것도 안 된다: 깊이가 사라져 겹친 오브젝트의 원근 순위를 못
매기고, 카메라 밖 오브젝트가 표현 불가능해지며, 게임상 거리가 투영 왜곡을 먹는다.

두 좌표계는 경쟁 관계가 아니라 서로가 무의미해지는 지점을 메운다. 카메라가 움직이면
3D 의 screen 이 흔들리고, 같은 움직임에 ScreenSpaceCamera UI 의 world 가 흔들린다.
정확히 대칭이라 하나로는 못 덮는다.

### 회의록에서 확인된 것 / 확인 안 된 것

[2번째 스프린트 회의록](https://app.notion.com/p/2-3ab0bce5474c803aaed3c4305f62c996)
(2026-07-28) SDK 항목 전문은 `좌표 추가 (world pos로 통일)` 한 줄이다.

- screen 좌표를 **명시적으로 기각한 기록은 없다.** 논의 자체가 없었다. 따라서 병행이
  회의 결정을 뒤집지 않는다.
- 아래 두 가지는 회의에서 정해지지 않았다. 이 플랜에서 정하고 근거를 남긴다.

### 제약

- **해시 폭주.**
  [SceneStateHashTracker.cs:26](../../Packages/kr.artel.sdk/Runtime/Tracking/SceneStateHashTracker.cs)
  이 `SceneDto` 직렬화 전체를 SHA256 으로 떠서 변경을 감지한다. 양자화하지 않은
  float 를 넣으면 미세한 흔들림 하나에 매 폴링 주기마다 `GAME_STATE` 전송이 뜬다.
- **스트림 해상도 != Screen 해상도.**
  [ScreenVideoSource.cs:163-174](../../Packages/kr.artel.sdk/Runtime/Streaming/ScreenVideoSource.cs)
  의 `ResolveSize` 가 `maxWidth` 로 다운스케일하고 짝수로 내림한다. 소비자가 보는
  프레임의 픽셀 좌표는 `Screen.width/height` 와 다르다.
- **payload 크기.** 씬 전체 블록 트리에 좌표를 붙이면 스냅샷이 몇 배가 된다.

## 결정 사항

### D1. screen 은 normalized 0..1, 원점 top-left

픽셀이 아니라 정규화 값으로 싣는다.

- 스트림이 다운스케일될 수 있어 픽셀은 소비자가 보는 프레임과 어긋난다. 픽셀로
  가려면 `SceneDto` 에 프레임 해상도를 함께 실어야 하는데, 그 해상도는 스트림
  세션마다 달라지고 스트림이 꺼져 있으면 정의되지 않는다.
- Unity screen 은 bottom-left 원점이지만 이미지/비전 모델과 소비자 쪽 좌표 관행은
  top-left 다. SDK 에서 뒤집어 보내지 않으면 소비자마다 다르게 해석한다.

### D2. 좌표는 모든 블록에 넣되, 기본 스캔에서는 켠다

`SceneScanOptions` 에 플래그를 두지 않고 항상 넣는다.

- 좌표의 주 소비자는 조작 (클릭 타겟팅, 예정된 드래그 앤 드랍) 인데, 조작 대상은
  `Button` / `InputField` 만이 아니다. 3D 오브젝트도 대상이고, 부모 컨테이너의
  위치는 레이아웃 이해에 쓰인다.
- 기본 스캔은 이미 비활성 오브젝트를 걷지 않아
  ([SceneScanOptions.cs](../../Packages/kr.artel.sdk/Runtime/Tracking/SceneScanOptions.cs)
  의 `IncludeInactive: false`) 트리 크기가 제한적이다.
- 양자화 후 필드는 world 3개 + rect 4개 + bool 1개다. 측정 없이 플래그부터 다는
  것은 이른 최적화다. **단, Step 2 에서 실제 payload 증가분을 재고, 유의미하면
  그때 `IncludeCoordinates` 플래그를 추가한다.**

## Approach (Checklist)

- [ ] **Step 0: Recon**
  - [ ] `SceneJsonContractTests` 가 고정하고 있는 JSON 계약 확인
  - [ ] `SceneScannerTests` 의 기존 픽스처가 Canvas 를 세팅하는지 확인 (없으면
        테스트용 Canvas/Camera 픽스처를 새로 만들어야 한다)
  - [ ] 대표 씬 하나로 현재 `GAME_STATE` payload 크기 측정 (D2 판단 기준선)

- [ ] **Step 1: Implementation**
  - [ ] `Runtime/Domain/BlockTransform.cs` 신규 — world `Vector3`, screen rect,
        `OnScreen`. 도메인 타입이므로 DTO 와 분리한다.
  - [ ] `Runtime/Domain/SceneBlock.cs` — `Transform` 속성 추가 (nullable).
  - [ ] `Runtime/Tracking/BlockTransformReader.cs` 신규 — 좌표 계산 전담.
    - RectTransform 있음: `GetWorldCorners` 로 4코너 → 각각
      `RectTransformUtility.WorldToScreenPoint` → AABB. world 는
      `TransformPoint(rect.center)`.
    - RectTransform 없음: world 는 `transform.position`, screen 은
      `Camera.main.WorldToScreenPoint` 단일 점 (rect 는 w/h 0).
    - 카메라 선택은 `CursorController` 의 분기를 그대로 따른다 — canvas 의
      `renderMode != ScreenSpaceOverlay` 일 때만 `worldCamera`, 아니면 `null`.
      **이 분기 로직을 `CursorController` 와 공유하도록 뽑아낸다** (중복 두면
      한쪽만 고쳐져 조용히 어긋난다).
    - `WorldToScreenPoint` 의 `z <= 0` 은 카메라 뒤 → `onScreen: false`.
    - rect 가 화면 밖으로 완전히 벗어나면 `onScreen: false`. 값 자체는 그대로 둔다
      (얼마나 벗어났는지가 정보다).
  - [ ] `Runtime/SceneScanner.cs` — `ScanTransform` 에서 reader 호출, `SceneBlock`
        에 전달.
  - [ ] `Runtime/Protocol/Dto/BlockTransformDto.cs` 신규 —
        `world {x,y,z}`, `rect {x,y,w,h}`, `onScreen`.
  - [ ] `Runtime/Protocol/Dto/SceneBlockDto.cs` — `transform` 필드 추가.
  - [ ] `Runtime/Protocol/Mapping/SceneSnapshotMapper.cs` — 매핑 + **양자화**.
        world 는 소수점 4자리, normalized rect 는 소수점 4자리 (1080p 기준
        약 0.2px 해상도). 양자화는 매퍼 한 곳에서만 한다.
  - [ ] `.meta` 파일 확인 — 신규 `.cs` 마다 필요하다. (현재 워킹 트리에 누락된
        `.meta` 가 이미 몇 개 있다. 이 작업 것만 챙기고 나머지는 건드리지 않는다.)

- [ ] **Step 2: Tests**
  - [ ] `SceneScannerTests` — Overlay canvas 아래 RectTransform 의 rect 가
        기대 normalized 값으로 나오는지
  - [ ] `SceneScannerTests` — RectTransform 없는 GameObject 의 world/screen
  - [ ] `SceneScannerTests` — 카메라 뒤 오브젝트가 `onScreen: false`
  - [ ] `SceneScannerTests` — ScreenSpaceCamera canvas 가 Overlay 와 같은
        normalized rect 를 내는지 (**이게 이 작업의 핵심 회귀 방어선**)
  - [ ] `SceneStateHashTrackerTests` — 양자화 임계 미만의 미세 이동이 해시를
        바꾸지 않는지
  - [ ] `SceneJsonContractTests` — 새 필드의 JSON 형태 고정
  - [ ] payload 크기 재측정 → D2 재검토

- [ ] **Step 3: Rollout / Rollback**
  - [ ] 필드 추가일 뿐이라 마이그레이션 없음. 소비자는 모르는 필드를 무시한다.
  - [ ] 롤백은 `git revert` 단일 커밋.

## Validation

- **Commands to run:**
  - Unity Test Runner (EditMode + PlayMode), `Artel.Runtime.Tests` 어셈블리
  - `samples/WordVenture` 실행 후 `ArtelTestPage` 로 `GAME_STATE` 육안 확인
- **Expected output:**
  - 각 블록에 `transform.world` / `transform.rect` / `transform.onScreen`
  - 화면 좌상단 UI 의 rect x/y 가 0 에 가깝고, 우하단이 1 에 가까움
  - 게임을 가만히 둔 상태에서 `GAME_STATE` 가 반복 전송되지 않음 (해시 안정)

## Risks & Rollback

- **Risks:**
  - **양자화 자릿수가 모자라거나 과함.** 4자리는 1080p 에서 약 0.2px 이라 미세
    애니메이션은 여전히 해시를 흔들 수 있다. Step 2 에서 실측하고, 흔들리면
    3자리로 내린다 (약 2px — 클릭 타겟팅에는 충분).
  - **payload 증가.** D2 에서 플래그 없이 가기로 했으므로 Step 2 의 실측이
    게이트다. 유의미하면 그 자리에서 플래그를 추가한다.
  - **`Camera.main` 이 null 인 씬.** 비-UI 오브젝트의 screen 투영이 불가능하다.
    이 경우 `onScreen: false` 와 rect 0 으로 내리고 world 만 유효하게 둔다.
    예외를 던지지 않는다 — 스캔은 어떤 씬 구성에서도 살아남아야 한다.
- **Rollback steps:** `git revert <commit>`. 스키마 추가만이라 소비자 쪽 되돌림
  작업 없음.

## What actually landed

Runtime and tests compile against Unity 2022.3.34f1, and the EditMode suite runs
93 tests: 85 pass, 8 fail. The same 8 fail on a stashed baseline without any of
this work, so nothing here regressed them — they are PlayMode tests
(`DontDestroyOnLoad`, `Awake`) dragged through EditMode by the temporary
`testables` entry used to make the package's tests discoverable at all. All 7 new
tests pass.

Two Step 2 items are **not** done:

- **Payload measurement.** The gate D2 set for itself never ran. Serialized,
  `transform` costs roughly 110-130 bytes per block, so a 200-block scene grows
  by about 25 KB — an estimate from the shape of the JSON, not a measurement.
  Measure before deciding the flag is unnecessary.
- **Sample app check.** `samples/WordVenture` was never played through to eyeball
  a live `GAME_STATE`.

Also worth knowing: `samples/WordVenture/Packages/manifest.json` has no
`testables` entry, so `-runTests` finds zero tests in this package. It was added
temporarily for the runs above and reverted. Adding it for real is its own
decision about the sample project.

## Open Questions

- 소비자 (Agent / Orchestrator) 쪽이 normalized 를 받을 준비가 되어 있는지.
  D1 은 SDK 관점에서 정한 것이고, 소비자가 이미 픽셀을 가정한 코드를 갖고 있다면
  픽셀 + 해상도 동봉으로 바꾸는 편이 낫다. **구현 전에 Agent 담당과 확인 필요.**
- `scan_all_scenes` 에도 좌표가 필요한지. 현재는 Non-goal 로 뒀다.
