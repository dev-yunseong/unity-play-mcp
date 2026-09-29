# 2026-09-29 — 스크린샷에 입력 좌표 변환과 scene/frame 메타데이터를 제공한다

- Date: 2026-09-29
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/71
- Status: Implemented

## Goal

`capture_screen` 이 이미지와 함께, 실제 캡처 경로에서 잰 `Screen.width`/`Screen.height`, 이미지가 담은 화면 영역(좌상단
기준), 축별 scale, 캡처 frame 과 scene 을 돌려준다. server 는 이미지 픽셀 → `move_mouse` 좌표 변환식과, 든 scene
reading 이 그 이미지와 어떤 관계인지(frame 선후, 같은 장면인지)를 함께 싣는다.

## Non-goals

- OCR, 게임별 의미 해석, 스크린샷 좌표로 자동 클릭.

## Context / Constraints

- 기존 image content block 과 text 첫 줄(`WxH; clipped=…`)은 그대로 둔다.
- 값은 추정하지 않는다. `ScreenCapturer` 가 blit 에 쓴 source rect·화면 크기, `WaitForEndOfFrame` 뒤의
  `Time.frameCount`, 그 순간의 활성 씬을 그대로 싣는다.
- 호환: 0.2.x server 와 현재 develop server 는 capture payload 를 `.strict()` 로 읽어 새 필드가 있으면 캡처를
  거절한다. 새 server 는 metadata 가 없는 0.2.x package 의 payload 도 받는다. package 가 호환 server 버전을
  고정하므로(#73) 배포 쌍에서는 문제가 없고, 혼합 조합의 한계는 release note 에 적는다.

## Approach (Checklist)
- [x] **Step 1: package** — `CapturedImage` 에 화면 크기·source·frame·scene. `CaptureRegion.Requested`.
  `CaptureRect.TopLeft`/`Scale`. `CaptureResultDto` 에 `screen`/`region`/`requestedRegion`/`scale`/`frame`/`scene`.
- [x] **Step 2: server** — schema 에 optional metadata, `describeCapture`(변환식, 잘림 설명, reading 관계와 120 frame 초과
  경고, 다른 장면 경고, metadata 없는 package 안내).
- [x] **Step 3: Tests** — `CaptureScreenTests`(1920×1080→1024×576, 해상도 변경, crop 원점, 잘림, DTO wire),
  `mcp/test/capture.test.ts`.
- [ ] **Step 4: Manual** — 실제 게임에서 이미지 좌표를 `move_mouse` 로 되써 같은 지점을 가리키는지.

## Validation
- **Commands to run:** `cd mcp && npm run build && npm test`; CI `Unity Tests`.

## Risks & Rollback
- **Risks:** 새 package + 0.2.x/현재 develop server 조합에서는 캡처가 "invalid capture payload" 로 실패한다.
- **Rollback steps:** PR revert.

## Open Questions
- #69 의 `stale` 표시와 합칠 때 reading 관계에도 `stale` 을 싣는다(두 PR 병합 뒤).
