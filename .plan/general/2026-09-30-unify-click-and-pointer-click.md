# 2026-09-30 — click 과 pointer_click 을 하나의 click 으로 통합

- Date: 2026-09-30
- GitHub Issue: None
- Status: Implemented. MCP build clean, 238/238 green. Unity EditMode/PlayMode 미실행 — 이 머신에 editor 가 없어 CI 가 첫 컴파일이다.

## Goal

`click` 과 `pointer_click` 을 `click` 하나로 합친다. 입력은 셋 중 하나다.

- `targetId` — scan 이 보고한 instance id
- `selector` — `get_scene_state`/`search_targets` 가 보고하는 것과 같은 selector 문법
  (`Canvas[0]/Panel[2]/Button[0]` 처럼 `name[siblingIndex]` 마디를 `/` 로 잇는다)
- `x`, `y` — `move_mouse` 와 같은 좌상단 원점 게임 화면 좌표

동작은 하나다: 대상을 화면 좌표로 바꾼 뒤 `move_mouse` → `mouse_down` → `mouse_up` 을 한
batch 로 연달아 보낸다. `Button.onClick` 을 직접 부르는 경로(`button_click`)와 Unity 쪽
`pointer_click` action 은 없앤다.

## Non-goals

- `pointer_drag`, `pointer_hover` 통합 (그대로 둔다. 같은 방식으로 옮기는 것은 후속).
- 오른쪽/가운데 버튼 클릭. 엔진이 `OnMouse*` 를 왼쪽에만 보낸다.
- 예전 이름의 호환 alias. 지운다.
- selector 문법 자체를 바꾸는 것. 이미 있는 것을 재사용하고 해석 코드만 한 곳으로 모은다.

## Context / Constraints

- 결정(사용자): Button 도 raycast 경로로만 누른다 / selector 는 기존 문법 재사용 /
  좌표는 `move_mouse` 와 같은 공간 / MCP 쪽에서 move·mouse 를 연달아 보낸다 / 호환성 유지 안 함.
- `ActionExecutor` 의 `ExecuteMouseButton` 은 프레임을 넘기지 않고 동기로 완료한다.
  `VirtualMouseState.Press` 는 눌린 프레임의 **다음** 프레임부터 눌린 것으로 답하므로
  (`PointerActions.Click` 주석), 같은 batch 에서 `mouse_down` 직후 `mouse_up` 을 보내면
  collider 의 `OnMouseDown` 이 빠진다. 그래서 `mouse_down`/`mouse_up` 이 끝난 뒤 한 프레임을
  넘기도록 Unity 쪽을 고친다 (action 이 완료를 늦출 뿐 다른 의미는 안 바뀐다).
- selector/id → 좌표는 MCP 가 마지막 reading(`FoldedPulseState`)에서 푼다.
  `PulseObject.rect`(좌상단 기준 픽셀)의 중심을 겨눈다. reading 이 없거나 낡았으면
  (`stale`) 이유와 복구 방법(`start_readings`)을 담아 실패한다.
- selector 는 `object.selector` 와 **정확히** 일치하는 것만 대상이다(`get_scene_state` 의
  contains 검색과 다르다). 없으면 실패, 둘 이상이면(scene 이 다른 경우) 후보를 나열해 실패.
- 잃는 것: Unity 쪽 `pointer_click` 이 하던 "겨눈 자리에 실제로 닿는 대상인지 누르기 전에
  검사"와 `hit` 보고. 대신 reading 의 `active`/`onScreen`/`covered` 를 보고 눌러도 소용없는
  대상(꺼짐, 화면 밖, 가려짐)이면 누르기 전에 실패한다. 실제 hit 는 누른 뒤
  `get_scene_state` 로 확인한다.

## Approach (Checklist)
- [x] **Step 0: Recon** — 완료. `mcp/src/tools.ts:868`(click/pointer_click 등록, `performActionSchema`),
  `ActionExecutor.cs`(`button_click`/`pointer_click` 분기, `ExecuteMouseButton`),
  `PointerActions.cs`(`Click`), `mcp/src/pulse.ts`/`visible.ts`(`rect`, `covered`, `onScreen`).
- [x] **Step 1: 대상 해석** — `mcp/src/click-target.ts` 새 module: `{targetId}|{selector}|{x,y}` →
  `{x, y}` 또는 사람이 읽을 실패 문장. selector 정확 일치, id 조회, rect 중심, 상태 검사.
- [x] **Step 2: click tool** — `tools.ts` 의 `click`/`pointer_click` 등록을 하나로. 입력 schema 는
  `z.union` 이 아니라 optional 필드 셋 + `superRefine`(정확히 하나의 방식만; `x`·`y` 는 함께)로 한다
  (draft 2020-12 `$ref` 문제 회피, `targetIdSchema` 주석 참고). dispatch 는
  `move_mouse` → `mouse_down`(0) → `mouse_up`(0) 한 batch.
- [x] **Step 3: perform_actions** — `performActionSchema` 에서 `button_click`, `pointer_click` 제거.
  이 자리에 `click` action 을 둘지(같은 해석 module 재사용)는 구현 때 정한다: 두면 tool 과 batch
  가 같은 계약이 된다.
- [x] **Step 4: Unity 정리** — `ActionExecutor` 의 `button_click`/`pointer_click` 분기,
  `ExecuteButtonClick`, `PointerActions.Click`, `ScannedTarget.Click`(다른 곳에서 안 쓰면) 삭제.
  `ExecuteMouseButton` 이 한 프레임을 넘기게 한다.
- [x] **Step 5: 문서/테스트** — `instructions.ts`, README, `instructions.test.ts`(tool 목록),
  `pointer-tools.test.ts`/`perform-actions.test.ts` 갱신. `PointerActionTests`,
  `PointerTargetActionTests` 중 click 을 부르는 것은 `mouse_down`/`mouse_up` 경로 검증으로 바꾼다.
- [x] **Step 6: Rollout / Rollback** — MCP 와 Unity package 를 같이 배포해야 한다(action 이름이
  사라진다). 쪽이 어긋나면 옛 tool 이 "unknown method" 로 실패한다. 릴리스 노트에 명시.

## Validation
- **Commands to run:** `cd mcp && npm test && npm run build` (스크립트명은 package.json 확인).
  Unity 는 `.github/scripts/setup-unity-test-project.sh` 로 만든 프로젝트에서 EditMode/PlayMode
  (`project.md` 참고). 이 머신에 editor 가 없으면 CI 가 첫 컴파일이다.
- **Expected output:** MCP 테스트 전부 green. `click` 이 세 입력 방식 각각에서 정확히
  `move_mouse`,`mouse_down`,`mouse_up` 세 action 을 이 순서로 보낸다. 모호/없음/낡음/가려짐/
  인자 조합 오류가 각각 다른 문장으로 실패한다. PlayMode 에서 collider `OnMouseDown` 과
  uGUI `Button` 이 둘 다 한 번씩 눌린다.

## Risks & Rollback
- **Risks:**
  - 좌표가 rect 중심이라 중심이 비어 있는 모양(도넛, 회전된 스프라이트)에서 빗나갈 수 있다.
    Unity 쪽 aim 은 raycast 로 맞는 점을 찾았다. 이 회귀가 문제면 후속으로 "aim 만 하는" action 을
    남기는 안을 검토한다.
  - reading 이 낡은 상태에서 좌표가 어긋난다 → stale 을 거절한다.
  - 프레임 간격: `mouse_down` 뒤 한 프레임이면 충분한지는 PlayMode 테스트로 확인한다.
  - 예전에 가려져도 눌리던 `Button` 이 이제 실패한다(의도).
- **Rollback steps:** `git revert` 한 번. Unity/MCP 를 같이 되돌린다.

## Open Questions
- (결정됨) `perform_actions` 에도 `click` 을 둔다. `expandActions` 가 tool 과 같은 해석을 쓴다.
- selector 가 둘 이상 일치할 때 "실패 + 후보 나열" 로 충분한가?

## Follow-up (same PR)

- `pointer_drag`/`pointer_hover` 를 `drag`/`hover` 로 이름만 바꿨다(MCP tool, `perform_actions` method,
  Unity wire method, 에러 문장, 문서, 테스트). 입력을 selector/좌표까지 받게 넓히는 것은 후속 PR.
