# 2026-09-29 — 대상 ID로 커서를 이동해 hover 상태를 읽는다

- Date: 2026-09-29
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/70
- Status: Implemented

## Goal

`targetId` 하나로 버튼을 누르지 않고 포인터를 대상 위에 올려, uGUI `OnPointerEnter`/`OnPointerExit` 와 collider
`OnMouseEnter`/`OnMouseOver` 가 게임의 입력 경로로 나가게 한다. 응답은 쓴 좌표와 도착 뒤 실제로 포인터 아래 있는
대상을 싣는다.

## Non-goals

- 클릭·드래그 재구현. 툴팁 내용 해석. 아무 객체도 없는 필드 좌표 선택(`move_mouse` 의 몫).

## Context / Constraints

- #59 의 `PointerTargeting.TryAim`(다섯 probe, uGUI/collider 경로 선택, 가림·화면 밖 판정)과 `CursorController.MoveTo`
  + `pointerMoved`(= `VirtualInput.MoveMouse` + `PointerEventDispatcher.MoveTo`)를 그대로 쓴다.
- id 는 `Resources.InstanceIDToObject` 로 푼다. Unity 는 세션 안에서 instance id 를 재사용하지 않으므로 파괴된 대상이나
  장면 전환 전의 id 는 다른 대상으로 풀리지 않고 "no live object has id" 로 실패한다.

## Approach (Checklist)
- [x] **Step 1: package** — `PointerActions.Hover`: 겨누기 → 이동(누름 없음) → 한 프레임 → 파괴/비활성/`StillReaches`
  재확인 → `PointerHitDto`. `PointerTargeting.StillReaches` 추가. `ActionExecutor` 에 `pointer_hover`.
- [x] **Step 2: server** — `pointer_hover` tool, `perform_actions` 가지, 안내문 한 줄.
- [x] **Step 3: Tests** — `Tests/PlayMode/PointerHoverActionTests.cs`(UI 카드 enter, 카드 사이 exit/enter, collider
  enter/over, 가림, 비활성/파괴/교체된 id, 도착 사이 비활성·가림, params), `perform-actions`/`pointer-tools`/`schema` test.
- [ ] **Step 4: Manual** — 실제 게임 카드 툴팁·hover 확대.

## Validation
- **Commands to run:** `cd mcp && npm run build && npm test`; CI `Unity Tests` PlayMode.

## Risks & Rollback
- **Risks:** 도착 뒤 확인이 실패해도 포인터는 이미 그 자리에 있고 그 아래 것은 hover 를 받았다(tool 설명에 적음).
- **Rollback steps:** PR revert. 새 method 라 기존 호출에 영향이 없다.

## Open Questions
- 없음.
