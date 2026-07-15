# 2026-07-15 — Scene scan polling 추가

- Date: 2026-07-15
- Jira: ARTEL-20
- Status: Complete

## Goal

1초마다 scene을 scan하고, 직전 scan hash와 달라졌을 때 최신 `GAME_STATE`를 모든 WebSocket 연결에 전송한다.

## Non-goals

- 기존 `scan_scene`, `SCAN_SCENE`, `GET_GAME_STATE` 요청 계약 제거
- background thread에서 Unity object 접근
- scan interval 설정 UI 또는 프로토콜 추가

## Context / Constraints

- Unity scene API와 component 상태는 main thread에서 읽어야 한다.
- hash 입력은 실제 전송되는 Scene DTO JSON과 같아야 한다.
- 최초 scan은 변경이 아니므로 baseline만 만든다.
- 명시적 scan 응답 후 동일 상태를 polling이 다시 broadcast하지 않아야 한다.

## Approach (Checklist)
- [x] **Step 0: Recon** (`ArtelManager`, `SceneScanner`, WebSocket 전송 및 runtime tests 확인)
- [x] **Step 1: Implementation** (`SceneStatePoller`가 unscaled 1초 주기 scan과 변경 판정을 담당하고 Manager는 전송만 수행)
- [x] **Step 2: Tests** (`SceneStateHashTracker` 내부 직렬화와 `SceneStatePoller` interval/명시적 scan baseline 동작 검증)
- [x] **Step 3: Rollout / Rollback** (기존 요청식 scan 호환 확인, 변경 파일 revert 가능)

## Validation
- **Commands to run:** Unity `2022.3.34f1` batchmode EditMode tests; `git diff --check`
- **Expected output:** 12/12 NUnit tests 통과, compiler error와 whitespace 오류 없음

## Risks & Rollback
- **Risks:** scene JSON 순서가 불안정하면 false-positive broadcast 발생; scan 비용이 큰 scene에서 1초 spike 발생; action commit 뒤 후속 snapshot 차이 발생 가능
- **Rollback steps:** polling/hash 관련 변경만 revert하면 기존 요청식 scan으로 복귀

## Open Questions

- 없음. 요구사항의 1초는 고정 interval로 적용한다.
