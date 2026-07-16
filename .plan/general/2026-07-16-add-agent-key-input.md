# 2026-07-16 — Agent 키 입력 추가

- Date: 2026-07-16
- Jira: ARTEL-29
- Status: Complete

## Goal

Agent가 `key_click` action으로 키와 지속 시간을 전달하고, ILPP가 사용자 코드의 지원 `UnityEngine.Input` 호출을 `ArtelInput` proxy로 치환해 physical 입력과 Agent virtual 입력을 동일한 polling semantics로 조회할 수 있게 한다.

## Non-goals

- Unity New Input System 전체 API 추상화
- 임의 OS keyboard event 주입
- semantic game action 자동 추론
- 전체 `UnityEngine.Input` API 지원. MVP는 key 계열과 `anyKey` 계열만 치환한다.

## Context / Constraints

- 기존 `ACTION` batch와 `ActionExecutor` dispatch 구조를 유지한다.
- `GetKeyDown`, `GetKey`, `GetKeyUp`은 소비형이 아닌 frame snapshot이어야 한다.
- WebSocket message는 `ArtelManager.Update()`에서 처리되므로 script execution order와 무관하게 다음 프레임부터 virtual key를 노출한다.
- 사용자 코드는 기존 `UnityEngine.Input` 호출을 유지한다. `Artel.Runtime`을 참조하는 assembly만 IL 치환 대상이다.

## Approach (Checklist)
- [x] **Step 0: Recon** 기존 ACTION DTO, executor, manager update lifecycle과 test assembly 확인
- [x] **Step 1: Implementation** virtual key state와 `ArtelInput` proxy 추가, Input call-site IL 치환과 `key_click` dispatch 연결
- [x] **Step 2: Tests** frame transition, 다중 조회, validation, JSON action 실행 테스트 추가 및 Unity EditMode 실행
- [x] **Step 3: Rollout / Rollback** README 사용법과 제한 기록, diff 검토; 단일 feature commit으로 되돌릴 수 있게 유지

## Validation
- **Commands to run:** `git diff --check`; Unity EditMode test runner의 `Artel.Runtime.Tests`
- **Expected output:** whitespace error 없음; key input/action tests 포함 전체 EditMode test 통과
- **Result:** `git diff --check` 통과; Unity 2022.3.34f1 EditMode `30/30` 통과

## Risks & Rollback

- **Risks:** static virtual state가 domain reload 설정에 따라 남을 수 있음; custom asmdef가 `Artel.Runtime`을 참조하지 않으면 ILPP 대상에서 제외됨; action 처리 프레임과 consumer Update 순서 차이
- **Rollback steps:** feature commit revert 후 사용자 코드를 `UnityEngine.Input`으로 복구

## Open Questions

- 없음. string key overload의 Unity legacy name 전체 호환은 MVP 이후 범위로 두며, MVP action은 `KeyCode` enum 이름과 숫자 값을 지원한다.
