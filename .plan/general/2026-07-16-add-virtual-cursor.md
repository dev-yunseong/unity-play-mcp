# 2026-07-16 — 가상 마우스 커서 추가

- Date: 2026-07-16
- Jira: ARTEL-30
- Status: Complete

## Goal

SDK가 `button_click` 또는 `enter_text`를 실행할 때 대상 UI 오브젝트로 이동하는 가상 커서를 표시한다. GUI 설정에 따라 즉시 이동하거나 부드럽게 이동하며, 이동 완료 후 액션 이벤트를 실행한다.

## Non-goals

- 키보드 입력 상태 시각화
- 실제 OS 커서 이동 또는 Unity 입력 이벤트 생성
- 사용자 지정 커서 외형 API

## Context / Constraints

- `CursorController`가 커서 생성과 위치 갱신을 소유한다.
- 기존 액션 결과와 UI 이벤트 실행 순서를 유지한다.
- 여러 액션 요청은 커서 이동이 겹치지 않도록 직렬 실행한다.
- 씬 또는 sample submodule 수정 없이 SDK 런타임에서 동작해야 한다.
- 현재 사용자 변경인 `.gitignore`, `samples/WordVenture`, `src/`는 건드리지 않는다.

## Approach (Checklist)
- [x] **Step 0: Recon** (`ActionExecutor`, `SceneScanner`, `ArtelManager`, 테스트 구조 확인)
- [x] **Step 1: Implementation** (`CursorController` 추가, GUI toggle과 coroutine 액션 실행 연결)
- [x] **Step 2: Tests** (즉시 이동 좌표와 이벤트 순서 테스트 추가, 런타임 컴파일 검증)
- [x] **Step 3: Rollout / Rollback** (전체 diff 및 사용자 변경 분리 확인; revert로 롤백)

## Validation
- **Commands run:** 임시 갱신한 Unity 생성 프로젝트로 `dotnet build`, `git diff --check`, Unity EditMode batch test 시도
- **Result:** 런타임 컴파일 및 whitespace 검사 성공. Unity test runner는 Licensing Client IPC 60초 timeout으로 실행되지 못함.

## Risks & Rollback
- **Risks:** World Space/Screen Space Camera Canvas 좌표 변환 차이, 이동 중 들어오는 요청 순서, 런타임 생성 오브젝트 수명
- **Rollback steps:** 기능 커밋을 `git revert`한다.

## Open Questions

- 없음. GUI toggle 기본값은 해제 상태로 둔다.
