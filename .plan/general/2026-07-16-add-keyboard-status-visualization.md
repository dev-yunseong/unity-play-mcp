# 2026-07-16 — 키보드 입력 상태 시각화 추가

- Date: 2026-07-16
- Jira: ARTEL-30
- Status: Complete

## Goal

실제 keyboard 입력과 Agent `key_click` 입력에서 현재 눌린 키를 게임 화면의 상태 창에 표시한다.

## Non-goals

- keyboard layout 전체를 그리는 가상 키보드
- mouse 또는 joystick 입력 표시
- Unity New Input System 지원 확대

## Context / Constraints

- `origin/develop`의 ARTEL-29를 rebase해 `ArtelInput`을 단일 입력 소스로 사용한다.
- `ArtelManager`만 부착하면 상태 창도 자동 생성되어야 한다.
- cursor animation 때문에 직렬화한 ACTION coroutine 계약을 유지한다.
- 기존 사용자 변경인 `.gitignore`, `samples/WordVenture`, `src/`는 건드리지 않는다.

## Approach (Checklist)
- [x] **Step 0: Recon** (ARTEL-29 input proxy와 rebase 충돌 확인)
- [x] **Step 1: Rebase** (`origin/develop` 위로 rebase, key/cursor action dispatch 병합)
- [x] **Step 2: Implementation** (`KeyboardStatusController`와 Manager 자동 생성 연결)
- [x] **Step 3: Tests** (key label format, 자동 생성, key action coroutine 계약 테스트 추가 및 compile 검증)
- [x] **Step 4: Rollout / Rollback** (diff 검토, 사용자 변경 분리, force-with-lease 준비)

## Validation
- **Commands run:** `dotnet build Artel.Runtime.csproj --no-restore`, `git diff --check`, Unity EditMode batch test 시도
- **Result:** runtime compile과 whitespace 검사 성공. Unity test runner는 Licensing Client IPC 60초 timeout으로 실행되지 못함.

## Risks & Rollback
- **Risks:** legacy `KeyCode` 전체 순회 비용, script Update 순서, 작은 해상도에서 overlay 겹침
- **Rollback steps:** keyboard 시각화 커밋을 revert한다.

## Open Questions

- 없음. 상태 창은 화면 하단 중앙에 항상 표시한다.
