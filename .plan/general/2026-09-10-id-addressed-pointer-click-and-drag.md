# 2026-09-10 — collider 클릭과 드래그를 대상 ID로 수행

- Date: 2026-09-10
- GitHub Issue: [#59](https://github.com/dev-yunseong/unity-play-mcp/issues/59)
- Branch: `feat/59` (from `origin/develop`, 504d789)
- Status: Implemented. MCP build clean, 142/142 green. Unity EditMode/PlayMode 미실행 — 이 머신에 editor 가 없어 CI 가 첫 컴파일이다.

## Goal

ID 하나로 collider 대상을 클릭하고, ID 두 개로 드래그한다. 지금 `click(targetId)` 는
`ActionExecutor.ExecuteButtonClick` 을 타고 `Button` 이 없으면
`Target is not a Button: -3518` 로 끝난다. `OnMouseDown` 으로 적을 받는 2D 게임에는 닿을 방법이
없고, 에이전트는 `get_scene_state` 가 준 rect 에서 좌표를 직접 계산해
`move_mouse` / `mouse_down` / `mouse_up` 을 순서대로 보내야 한다. 그 좌표가 실제로 collider 를
맞히는지는 보내 보기 전에는 알 수 없다.

새 action 두 개를 더한다.

- `pointer_click` — `params: [targetId]`
- `pointer_drag` — `params: [sourceId, targetId]`

둘 다 실제 입력 경로만 쓴다. `Button.onClick.Invoke()` 같은 직접 호출은 하지 않고,
가상 마우스를 대상 위로 옮긴 뒤 버튼을 눌렀다 놓아 `VirtualMouseMessenger` 의 `OnMouse*` 와
`PointerEventDispatcher` 의 uGUI 이벤트가 게임에 닿게 한다.

## Non-goals

- `button_click` 의 동작 변경. 지금 `Button.onClick` 을 직접 부르는 경로는 그대로 둔다. MCP 의
  `click` tool 도 그대로 `button_click` 으로 간다. raycast 로 가릴 수 있는 `Button` 을 기존
  호출자가 클릭하고 있을 수 있으므로, 실제 입력 경로로 갈아타는 것은 이 issue 의 acceptance
  criteria 가 요구하지 않는 회귀 위험이다.
- 왼쪽 말고 다른 마우스 버튼. `VirtualMouseMessenger.DrivingButton` 이 0 이고 엔진도 `OnMouse*`
  를 왼쪽 버튼에만 보낸다. `button` 인자를 받으면 uGUI 대상에서는 오른쪽 클릭이 되고 collider
  대상에서는 조용히 아무 일도 안 하는 비대칭이 생긴다. 그 비대칭을 어떻게 알릴지는 따로 정할
  일이라 여기서 하지 않는다.
- 드래그 속도/경로 인자. `CursorController.movementDurationSeconds` (기본 0.35초) 가 정하는
  glide 를 그대로 쓴다.
- 게임별 카드 조합 자동화, 승리 조건 조작 (issue 의 Non-goals).
- `Camera.main` 이 아닌 카메라로 그리는 대상. `VirtualMouseMessenger.Pick` 이 이미 `Camera.main`
  만 보고, 이 변경은 그 규칙을 그대로 쓴다.

## Context / Constraints

- **`OnMouse*` 는 frame 으로만 배달된다.** `VirtualMouseMessenger.Tick` 을 미는 것은
  `VirtualInput.AdvanceFrame` 이고, 그것을 부르는 것은 `UnityPlayMcpHost.Update` 다
  (`Runtime/UnityPlayMcpHost.cs:202`). 한 프레임 안에서 누르고 놓으면 messenger 는 눌린 적이
  없는 것으로 본다. `VirtualMouseState.Press` 는 `StartFrame = currentFrame + 1` 로 찍으므로
  (`Runtime/UnityEngine/VirtualMouseState.cs:95`), 누른 프레임에는 `GetButton` 이 아직 false 다.
  그래서 누름과 놓음 사이에 최소 한 프레임이 필요하다.
- **uGUI 이벤트는 동기다.** `PointerEventDispatcher.Press` / `Release` 는 그 자리에서
  `ExecuteEvents` 를 부른다. 프레임을 기다리는 것은 오직 `OnMouse*` 때문이다.
- **좌표계가 둘이다.** scan 과 `move_mouse` 는 좌상단 기준 픽셀
  (`Runtime/Affordance/Scan/ScreenArea.cs`), 엔진과 `CursorController` 는 좌하단 기준이다.
  내부 계산은 Unity 좌표로 하고 결과 보고에서만 뒤집는다.
- **`UnityPlayMcpHost.cs` 는 건드리지 않는다.** 다른 track 이 그 파일의 lifecycle 을 다시 쓰는
  중이다. 연결 해제 때의 해제는 이미 `ReleaseAgentInput` 이
  `pointerEvents.ReleaseAll()` + `VirtualInput.ReleaseAllVirtualInput()` 로 하고 있으므로
  (`Runtime/UnityPlayMcpHost.cs:367`) 새로 붙일 것이 없다.
- **`mcp/src/tools.ts` 는 track 58, 60 과 공유한다.** 추가는 붙어 있는 블록으로만 하고 실제
  로직은 새 module 에 둔다.
- **wire 는 위치 인자다.** `{ "method": ..., "params": [...] }`. TypeScript 쪽 계약은
  `toWireAction`, Unity 쪽은 `ActionExecutor.Execute` 의 switch 다. 둘을 같이 움직인다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 끝냄. 손댈 자리는
      [ActionExecutor](../../Packages/dev.yunseong.unityplaymcp/Runtime/ActionExecutor.cs),
      [TargetLookup](../../Packages/dev.yunseong.unityplaymcp/Runtime/TargetLookup.cs),
      [PointerEventDispatcher](../../Packages/dev.yunseong.unityplaymcp/Runtime/PointerEventDispatcher.cs),
      [VirtualMouseMessenger](../../Packages/dev.yunseong.unityplaymcp/Runtime/UnityEngine/VirtualMouseMessenger.cs),
      [tools.ts](../../mcp/src/tools.ts).

- [x] **Step 1: 겨눌 자리를 찾는다.** 새 파일
      `Runtime/PointerTargeting.cs` 에 `PointerAim` 구조체와 `PointerTargeting` 정적 클래스.

      - `ColliderUnder(Vector2)` — 엔진이 배달할 그 오브젝트 하나. `VirtualMouseMessenger.Pick`
        의 본문을 여기로 옮기고 messenger 는 이것을 부른다. 규칙(`Camera.main` 에서 쏜 ray,
        2D 와 3D 를 같은 거리로 비교, `Camera.eventMask` 로 거른다)이 한 자리에만 있게 된다.
      - `Reaches(GameObject subject, GameObject hit, bool throughGraphics)` — hit 이 subject
        자신이거나 그 자손이면 참. 조상은 `throughGraphics` 일 때, 그것도 두 쪽의
        `GetEventHandler<IPointerClickHandler>` 가 같을 때만 참이다.
        `VirtualMouseMessenger.Send` 는 맞은 오브젝트 하나에 `SendMessage` 할 뿐 위아래로 걷지
        않으므로 collider 경로의 조상 hit 은 대상을 가린 것이고, uGUI 는 `ExecuteHierarchy` 로
        위로 걷지만 "같은 handler 에 닿는다" 를 믿는 대신 실제로 물어본다 — 그러지 않으면
        `raycastTarget` 이 켜진 전체 화면 부모 panel 이 모든 자식을 가려 놓고도 전부 성공으로
        보고된다.
      - `TryAim(GameObject target, PointerEventDispatcher graphics, out PointerAim aim, out string error)`
        — 대상의 화면 면적을 구하고(`RectTransform` 이면 네 코너, 아니면 `Collider` /
        `Collider2D` / `Renderer` 의 bounds), 그 안의 후보점 5개(가운데, 그리고 가로세로 25%/75%
        지점 네 곳)를 차례로 시험한다. 대상이 `Graphic` 이나 `Canvas` 아래 `RectTransform` 이면
        `PointerEventDispatcher` 의 raycast 로, 아니면 `ColliderUnder` 로 확인한다. 처음으로
        `Reaches` 가 참인 점이 `PointerAim.ScreenPosition` 이 된다.

      후보점이 5개인 이유: 가운데 한 점만 보면 도넛 모양 collider 나 가운데가 다른 것에 가린
      카드에서 실패한다. issue 의 Validation Notes 가 적은 "rect 안의 점에서도 실제 collider hit
      여부가 달라 실패" 가 그 경우다.

- [x] **Step 2: dispatcher 에게 무엇이 아래 있는지 묻는다.** `PointerEventDispatcher` 에
      `internal GameObject GraphicUnder(Vector2 screenPosition)` 을 더한다. 이미 있는 private
      `Raycast` 를 그대로 쓰고 hover 는 건드리지 않는다 — 후보점을 시험하는 동안 엉뚱한 자리로
      `pointerEnter` 가 나가면 안 된다.

- [x] **Step 3: id 로 GameObject 를 꺼낸다.** `TargetLookup` 에
      `public bool TryGetGameObject(int id, out GameObject gameObject)` 를 더하고 기존
      `TryGetTarget` 이 그것을 쓰게 한다. `ScannedTarget` 은 그대로 둔다.

- [x] **Step 4: 두 coroutine.** 새 파일 `Runtime/PointerActions.cs` 에 `PointerActions` 클래스.
      `ActionExecutor` 는 이미 810줄이고 여기에 더 얹을 이유가 없다. `ActionExecutor` 는 생성자에서
      `PointerActions` 를 하나 만들고 switch 에 case 두 개를 더하는 것으로 끝난다. 누르고 놓는 일은
      `ActionExecutor.SetButton` 하나에만 있어야 하므로 (`Runtime/ActionExecutor.cs:270` 의 주석)
      `Action<int, bool>` 로 넘긴다.

      `pointer_click` 의 frame 순서. coroutine 은 `Update` 뒤에 돌므로, 한 행은 "프레임 N 의
      `AdvanceFrame` 이 배달한 것" 과 "그 프레임 뒷부분에서 coroutine 이 한 일" 로 나뉜다. uGUI
      이벤트는 `SetButton` 이 부른 그 자리에서 동기로 나가고, `OnMouse*` 만 다음 프레임의
      `AdvanceFrame` 을 기다린다.

      | 프레임 | 그 프레임의 `AdvanceFrame` 이 배달 | coroutine 이 하는 일 (Update 뒤) |
      |---|---|---|
      | N | — | 겨눌 자리 확인, `MoveTo(point, pointerMoved)` → uGUI `pointerEnter` (동기) |
      | N+1 | `OnMouseEnter` / `OnMouseOver` | `SetButton(0, true)` → uGUI `pointerDown` (동기). `VirtualMouseState.Press` 가 `StartFrame = N+2` 로 찍는다 |
      | N+2 | `OnMouseDown` (`GetButton` 이 N+2 부터 참) | `SetButton(0, false)` → uGUI `pointerUp` + `pointerClick` (동기). `ReleaseFrame = N+3` |
      | N+3 | `OnMouseUp` / `OnMouseUpAsButton` | 결과 보고 |

      즉 `yield return null` 은 이동 뒤 한 번, 누름 뒤 한 번, 놓음 뒤 한 번 — 모두 세 번이다.
      누름 뒤의 한 번이 없으면 messenger 는 눌린 적이 없는 것으로 보고 `OnMouseDown` 이 통째로
      빠진다.

      `pointer_drag` 는 그 사이에 활강을 끼운다: 원본 자리 확인 → 목적지 자리 확인 → 원본으로 이동
      → 한 프레임 → 누름 → 한 프레임 → `cursorController.MoveTo(destination, pointerMoved, glide: true)`
      → 한 프레임 → 놓음 → 한 프레임 → 보고. 활강이 매 프레임 위치를 보고하므로 그 프레임마다
      `PointerEventDispatcher` 가 `beginDrag`/`drag` 를 내고 messenger 가 `OnMouseDrag` 를 낸다.

      두 자리를 **누르기 전에** 모두 확인한다. 확인이 실패하면 아무것도 쥐지 않은 채로 끝난다.
      누른 뒤로는 그 뒤 전체를 `try` / `finally` 로 감싸고 `finally` 에서 `SetButton(0, false)`
      한다.

      `finally` 가 덮는 것은 coroutine 안에서 나는 실패다 — 목적지가 도중에 사라지거나 예외가
      나서 일찍 끝나는 경우. **연결 해제는 `finally` 에 기대지 않는다.** Unity 가 오브젝트 파괴로
      멈춘 coroutine 의 `finally` 를 돌려준다는 보장이 없기 때문이다. 그 경우는 이미
      `UnityPlayMcpHost.OnDisable` → `StopTransport` → `ReleaseAgentInput` 이
      `pointerEvents.ReleaseAll()` 과 `VirtualInput.ReleaseAllVirtualInput()` 로 처리하고
      (`Runtime/UnityPlayMcpHost.cs:173`, `:339`, `:367`), 두 해제 모두 멱등이라 뒤늦은
      `finally` 가 겹쳐 돌아도 아무 일도 하지 않는다.

      놓는 자리에서는 hit 을 다시 확인하지 않는다. 드래그되는 카드가 포인터를 따라오는 게임이
      흔하고, 그때 포인터 아래 있는 것은 목적지가 아니라 그 카드다. 다시 확인하면 정상 드래그가
      전부 실패로 보고된다. 따라서 `PointerDragResultDto.to` 가 싣는 `hitId` 는 **누르기 전에**
      목적지 좌표에서 확인한 그 오브젝트이지, 놓는 순간 포인터 아래 있던 것이 아니다. DTO 주석에
      그렇게 적는다.

- [x] **Step 5: 결과 payload 를 타입으로 선언한다.** `Runtime/Protocol/Dto/PointerHitDto.cs` 와
      `Runtime/Protocol/Dto/PointerDragResultDto.cs`.

      ```
      PointerHitDto        { targetId, hitId, hit, x, y }
      PointerDragResultDto { from: PointerHitDto, to: PointerHitDto }
      ```

      `x`, `y` 는 좌상단 기준이다 — scan 이 보고하는 좌표계이자 `move_mouse` 가 받는 좌표계라,
      에이전트가 그대로 되쓸 수 있다. 이 다섯 field 가 acceptance criteria 의 "실제 hit 대상 및
      처리 결과를 보고" 에 답하는 전부다.

- [x] **Step 6: 실패를 네 가지로 가른다.** 에러 문장이 원인을 이름과 숫자로 말한다. 비활성
      판정은 `GameObject.activeInHierarchy` 하나로 한다 — 자신이 꺼졌든 부모가 꺼졌든 포인터가
      닿지 못하는 것은 같고, 둘을 가르는 것은 이 action 이 답할 물음이 아니다. 꺼진 `Canvas` 나
      `raycastTarget = false` 는 여기서 걸리지 않고 불일치로 나타난다. 그것이 맞다: 오브젝트는
      살아 있고 포인터가 닿지 못할 뿐이다.

      - 파괴/미지: `pointer_click: no live object has id -3518. It was never scanned, or the game destroyed it.`
      - 비활성: `pointer_click: target -3518 (Enemy) is not active in the scene.`
      - 겨눌 면적 없음: `pointer_click: target -3518 (Enemy) has no Collider, Renderer, or RectTransform to aim at.`
      - 불일치: `pointer_click: the pointer reached Card_Shoot#-4102 instead of Enemy#-3518 at (640, 360). Something is drawn or colliding on top of the target.`

- [x] **Step 7: MCP 쪽.** 새 module 을 만들지 않는다.

      `params` 를 만드는 일이 `[action.targetId]` 와 `[action.sourceId, action.targetId]` 뿐이라
      옮길 로직이 없다. `captureScreenParams` 가 제 파일 값을 하는 것은 세 가지 모양으로 갈리기
      때문이고, 여기는 갈리지 않는다. 인자 interface 를 따로 선언하는 것도
      `performActionSchema` 가 이미 `z.infer` 로 같은 모양을 주므로 계약을 두 벌 두는 것이 된다
      (`coding-style.md` 의 Data Shapes).

      `mcp/src/tools.ts` 에 붙어 있는 블록 세 개만 더한다: `performActionSchema` 의 가지 둘,
      `toWireAction` 의 case 둘 (`button_click` 과 같은 모양의 한 줄짜리),
      tool 등록 둘. `click` tool 의 description 한 줄만 고쳐 `Button` 이 아닌 대상은
      `pointer_click` 으로 가라고 말한다. `src/instructions.ts` 에도 한 줄 더한다.

      `params` 배열의 계약은 Unity 쪽 `PointerHitDto` 와 `ActionExecutor` 의 switch 가 쥐고,
      TypeScript 쪽에서는 `toWireAction` 이 유일한 자리로 남는다 — 지금과 같다.

- [x] **Step 8: 테스트.**

      PlayMode (`Tests/PlayMode/PointerTargetActionTests.cs`, 새 파일 — 기존 파일에 넣으면 다른
      track 과 충돌 면적이 늘어난다):
      - `pointer_click` 이 collider 대상의 `OnMouseDown` / `OnMouseUp` / `OnMouseUpAsButton` 에
        닿는다 (`MouseMessageFixtureBehaviour` 재사용).
      - `pointer_click` 이 uGUI `IPointerDownHandler` / `IPointerClickHandler` 대상에 닿는다
        (`PointerFixtureBehaviour` 재사용).
      - `pointer_click` 이 `Button` 을 `onClick` 직접 호출 없이 실제 입력으로 누른다.
      - `pointer_click` 이 hit 한 오브젝트의 id 를 `returnValue` 로 보고한다.
      - `pointer_click` 이 비활성 대상을 거절하고, 그 문장이 파괴 대상의 문장과 다르다.
      - `pointer_click` 이 다른 것에 가린 대상을 거절하고 실제 hit 이름을 문장에 담는다.
      - `pointer_drag` 가 원본에 `beginDrag` → `drag` → `endDrag` 를, 목적지에 `drop` 을 낸다.
      - `pointer_drag` 중 포인터가 원본을 떠난 뒤에도 원본이 `drag` 를 계속 받는다 (pointer
        capture 보존).
      - `pointer_drag` 가 목적지 해석에 실패하면 버튼을 쥐지 않은 채로 끝난다.
      - `pointer_drag` coroutine 이 중단되면 버튼이 놓인다 (`finally`).

      params 거절은 따로 EditMode 파일을 두지 않고 위 PlayMode 파일에 넣었다
      (`PointerActions_RefuseParamsTheyCannotRead`). executor 를 세우는 준비가 이미 거기
      있고, 파일 하나를 더 만들 만큼 다른 종류의 검사가 아니다.

      MCP:
      - `mcp/test/perform-actions.test.ts` 의 `wireCases` 에 `pointer_click`, `pointer_drag` 를
        더하고 method 개수 16 → 18. `rejectedCases` 에 소수 `targetId`, 빠진 `sourceId`,
        `pointer_click` 이 받지 않는 field 를 더한다.
      - `mcp/test/schema.test.ts` 의 `REGISTERED_TOOL_COUNT` 17 → 19,
        `anyOf.length` 16 → 18.
      - `mcp/test/pointer-tools.test.ts` (새 파일): 가짜 connection 으로 `pointer_click` 과
        `pointer_drag` tool 이 실제로 내보내는 `{ method, params }` 를 확인한다.
        `mcp/test/tools.test.ts` 가 `dispatchActions` 에 대고 하는 것과 같은 방식이고, 새
        파일이라 다른 track 과 충돌하지 않는다.

- [x] **Step 9: Rollout / Rollback** — 되돌리기는 branch revert. 설정도 migration 도 flag 도 없다.
      기존 action 은 하나도 안 바뀌므로 옛 MCP server 와 새 Unity package 를 섞어 써도
      `Unsupported method: pointer_click` 하나만 나온다.

## Validation

- **Commands to run:**
  ```bash
  unset -f node npm npx
  export PATH=/home/yunseong/.nvm/versions/node/v24.18.0/bin:$PATH
  cd /home/yunseong/dev/unity-play-mcp-59/mcp
  npm install && npm run build && npm test
  ```
- **Expected output:** `tsc` 무경고, `node --test` 전부 통과.
- **Not run:** EditMode 와 PlayMode. 이 Linux 머신에 Unity editor 가 없다 (`unity` 는 Windows
  Hub alias 뿐). `.github/workflows/unity-tests.yml` 이 PR 에서 두 suite 를 돌린다. 실제 게임
  (`samples/WordVenture` 의 TurnBattleScene) 으로 확인하는 것은 이 branch 에서 하지 못한다.

## Risks & Rollback

- **Risks:** 드래그되는 오브젝트가 포인터를 따라오면 `PointerEventDispatcher.Release` 의
  `drop` 이 목적지가 아니라 그 오브젝트에 간다. 이것은 dispatcher 의 기존 동작이고
  `pointer_drag` 가 새로 만드는 문제가 아니다 — 수동 `move_mouse`/`mouse_down`/`mouse_up`
  순서도 똑같은 경로를 탄다.
- **Second risk:** `pointer_click` 이 최소 4프레임을 쓴다. 한 프레임에 끝나던 `button_click`
  보다 왕복이 길다. 프레임 순서를 보존하라는 것이 acceptance criteria 이므로 줄일 수 없다.
- **Third risk:** 후보점 5개로도 못 맞히는 모양(가는 테두리만 collider 인 도넛)이 있다. 그때는
  불일치로 거절하면서 실제 hit 이름을 말하므로, 에이전트가 `move_mouse` 로 직접 겨누는 지금
  방법으로 되돌아갈 수 있다.
- **Merge risk:** `mcp/src/tools.ts`, `mcp/src/instructions.ts`, `mcp/test/schema.test.ts` 는
  branch `feat/58`, `feat/60` 도 고친다. 충돌이 예상되고, 셋 다 추가만 하므로 양쪽을 남기는
  해결이 맞다.
- **Rollback steps:** branch 의 commit 을 revert 한다. package 밖은 아무것도 안 바뀐다.

## Pair review 에서 접은 것

- **`HitOf` 가 파괴된 `GameObject` 를 읽는다.** 실제 버그였다. 겨눈 뒤 보고까지 세 프레임이
  지나는데 그 사이 죽는 적을 클릭하거나 소모되는 카드를 드래그하면
  `MissingReferenceException` 이 coroutine 을 뚫고 나가고, host 의 `ProcessActions` 가
  `processingActions = true` 인 채로 멈춰 그 세션의 이후 액션이 전부 조용히 버려진다.
  `PointerAim` 이 겨눌 때 id 와 이름을 베껴 들고, `HitOf` 는 그 값만 읽는다.
- **`Reaches` 의 조상 허용이 너무 넓다.** 위 Step 1 대로 좁혔다.
- **면적을 재는 방법과 hit 을 묻는 방법이 갈렸다.** `Canvas` 밖 `RectTransform` 이 corner 로
  재어지고 collider 로 물어져 엉터리 면적이 나왔다. 둘 다 `AnswersAsGraphic` 하나를 따른다.
- **`Camera.main` 이 없을 때 대상 탓을 했다.** 그 경우에 제 문장을 준다.
- **불일치 문장에서 좌표가 빠졌다.** 되살렸다. 물러난 에이전트가 `move_mouse` 로 돌아가려면
  어디를 겨눴는지 알아야 한다.
- **테스트 구멍.** collider 가 자식에 앉은 배치, 겨눌 면적 없음, 화면 밖 — 셋을 더했다.
- **`Pick` 이 한 줄짜리 passthrough 가 되었다.** `Tick` 에 직접 부르고 주석만 남겼다.

## Rejected feedback

- **비활성 판정을 `target.activeSelf && target.GetComponentInParent<Canvas>().enabled` 로 하라.**
  받지 않는다. `GetComponentInParent<Canvas>()` 는 canvas 밖 오브젝트에서 null 이라 그대로 부르면
  터지고, collider 대상은 애초에 canvas 아래 있지 않다. 꺼진 canvas 는 "비활성" 이 아니라
  "포인터가 닿지 못함" 이고, 그것은 이미 불일치 문장이 실제 hit 이름과 함께 말한다.
  `activeInHierarchy` 하나로 충분하다.
- **`finally` 가 정말 도는지 확인하는 통합 테스트를 더하라.** 절반만 받는다. coroutine 안에서
  나는 실패는 테스트한다 (Step 8 의 "목적지 해석에 실패하면 버튼을 쥐지 않은 채로 끝난다").
  파괴로 중단된 coroutine 의 `finally` 는 테스트하지 않는다 — 그 보장에 기대지 않기로 했고
  (Step 4), 기대지 않는 것을 테스트하면 테스트가 없는 계약을 있는 것처럼 만든다. 연결 해제
  경로는 `PointerActionTests.MouseDown_HeldButtonIsReleasedWhenTheConnectionStops` 가 이미
  덮는다.
- **glide 중 occluder 가 끼어들면 어떻게 되는가.** pointer capture 가 답이고, 그것을 그대로
  테스트한다: Step 8 의 "포인터가 원본을 떠난 뒤에도 원본이 `drag` 를 계속 받는다". 새로 할 일이
  없다.
- **`Drag` 의 `finally` 자체를 테스트하라.** 받지 않는다. 지금 `try` 안에서 던질 수 있는 것이
  없어 그 `finally` 의 유일한 산 경로는 enumerator `Dispose` 이고, 그것이 도는지에 기대지
  않기로 이미 정했다 (Step 4). 기대지 않는 것을 테스트하면 없는 계약을 있는 것처럼 만든다.
  **`finally` 는 테스트되지 않는다** — 방어로 남기는 것이고, 눌린 입력을 실제로 푸는 것은
  `PointerDrag_HoldsNothingWhenTheDestinationCannotBeResolved` 가 덮는 누르기 전 확인과,
  기존 `PointerActionTests.MouseDown_HeldButtonIsReleasedWhenTheConnectionStops` 가 덮는
  host 의 `ReleaseAgentInput` 이다.

## Open Questions

- 없음.
