# 2026-10-21 — 15초 유휴 중단과 요청별 재개

- Date: 2026-10-21
- GitHub Issue: #107 / PR #108 (기존 on-demand 기능 확장)
- Status: Implemented and validated

## Goal

연결 유지와 무관하게 마지막 요청/작업 완료 후 15초 동안 요청이 없으면 UI, discovery, live readings, 성능 수집을 멈춘다. 다음 요청은 필요한 기능만 재개한다.

## Non-goals

새 MCP tool, 자동 UI 재표시, heartbeat 기반 상시 수집, SDK/CLI 버전 PR #110 변경, zero overhead 보장.

## Context / Constraints

기존 PR #108 브랜치 feat/107에 후속 commit으로 반영한다. 연결 종료는 기존 session 정리 경로를 유지한다. 유휴 중단은 연결/PlayApi operation 기록/입력 상태를 파괴하지 않는 별도 경로다. 시간은 Time.realtimeSinceStartupAsDouble 기준으로 timeScale=0에서도 흐른다.

## Approach (Checklist)
- [x] **Step 0: Recon** — host, legacy cache tools, live readings, PlayApi 확인.
- [x] **Step 1: Implementation** — host는 연결만으로 수집을 켜지 않는다. ACTION의 nonempty batch 수신/실행 완료를 activity로 기록하며 malformed/빈 메시지, WebSocket ping, outbound pulse는 연장하지 않는다. processingActions/queue가 남거나 PlayApi input operation lease가 유효하면 유휴 중단을 보류한다. 유휴 중단은 SetOverlay(false), EndDiscovery, 성능 sampling/report off만 수행한다. 연결 유지로 다음 프레임 자동 재개하지 않는다.
  - 내부 wire action runtime_activity ["status"|"readings"]를 추가한다. status는 성능 수집만, readings는 discovery/live/performance만 재개하며 {reading,resumed}를 반환한다. 기존 start_readings도 수집을 시작한다. 단순 입력/캡처/play_*는 기존 on-demand 실행만 하고 배경 수집/UI를 켜지 않는다. set_overlay(true)만 UI를 켠다.
  - MCP status와 캐시 기반 state/visible/search/wait 요청은 runtime_activity로 host에 전달한다. readings 재개 시 이전 run을 현재 상태로 반환하지 않고 fresh reading을 bounded wait하며 실패하면 명확한 오류다. wait_for_condition의 최대 30초 대기 중에만 짧은 주기로 readings activity를 전달하고 finally에서 중단한다. 명시적 stop_readings는 다음 reading 요청까지 유지한다.
- [x] **Step 2: Tests** — 14.999/15초 경계, paused time, 연결 유지 sleep, malformed/ping 비연장, busy 작업 보호, 선택적 wake, 반복 sleep/wake, 마지막 disconnect, fresh cache와 active wait activity 정리, 기존 Unity/MCP suites.
- [x] **Step 3: Rollout / Rollback** — README/tool 설명에 15초와 wake 매핑 기록; PR #108에 검증 기록 추가. 후속 commit revert로 복원.

## Validation
- **Commands to run:** TypeScript build/node full suite; Unity EditMode/PlayMode throwaway project. 시간 경계는 테스트 clock 인자/필드 조작으로 결정론적으로 검증한다.
- **Expected output:** 15초 유휴 시 UI/수집 off, 연결 유지; 다음 요청은 필요한 수집만 on; 이전 cache를 fresh로 오인하지 않음.

## Risks & Rollback
- **Risks:** cached MCP tool이 host activity를 누락하거나 stale 상태를 돌려주는 문제, 긴 작업 중 sleep, reconnect 자동 재개 회귀.
- **Rollback steps:** 이번 follow-up commit revert.

## Open Questions
- 없음. 유휴 시간은 사용자 지정 15초.

## Review decisions

- runtime_activity 응답 typed DTO: {reading: boolean, resumed: boolean, run: string|null}. resumed는 live reading의 새 시작만 뜻한다. MCP는 응답 run과 일치하고 stale이 아닌 whole 기반 state를 기다리므로 pulse가 response보다 먼저 와도 안전하다. 2초 bounded wait 실패 시 이전 cache를 성공으로 반환하지 않는다. 구형 SDK unsupported는 명확한 SDK 업데이트 오류다.
- ACTION 중 method가 비어 있지 않고 params가 null 아닌 항목이 하나 이상일 때만 activity를 갱신한다. unknown method/잘못된 인자도 구조상 정상 요청으로 간주한다. 빈 batch/null-only/malformed JSON/잘못된 envelope는 연장하지 않는다.
- runtime_activity(readings)는 이미 watching이면 whole reading을 재요청하지 않는다. status는 실제 Watching/CurrentRun을 반환한다. reading=false인 경우에만 cache를 stopped 처리하며, 이미 활성화된 readings는 중단하거나 stale 처리하지 않고 성능만 시작한다. performance wake는 recorder와 CPU baseline을 초기화한다.
- MCP 공통 helper가 wake와 fresh run 확인을 맡는다. wait는 시작 시각의 deadline을 공유하며 wake 시간도 timeout 예산에 포함한다. 5초마다 직전 요청 완료 후 다음 timer를 예약하고 finally/abort/disconnect에 timer를 취소한다. 전역 readings는 정지시키지 않는다. 취소 전에 시작된 네트워크 요청은 최대 2초 이내 완료되며 그 후 새 요청은 보내지 않는다.
- Validation adds response-before/pulse-before ordering, mismatched old run, fresh timeout, active wait timer cleanup, mixed batch start/stop and status not restarting readings.

- TargetRef를 해석하는 click/hover/drag/enter_text, selector capture, perform_actions도 공통 readings wake를 사용한다. 좌표만 쓰는 요청은 수집을 켜지 않는다.

## Validation results

- TypeScript compile passed; MCP full suite 372/372 passed.
- Unity 2022.3.34f1 EditMode 306/306 and PlayMode 84/84 passed.
- PlayMode covers 14.999/15 second boundary under timeScale=0, connected idle suspension, raw input/status/readings/overlay selective wake, active work and operation lease protection, malformed/empty traffic, reconnect and last-disconnect cleanup.
- MCP covers response/pulse ordering, old-run rejection, status preserving active cache, shared wake routing, non-overlapping active-wait refresh cleanup, stalled handshake deadline/abort with no late sends, and old SDK upgrade guidance.
- Plan review and pair review PASS. Review found connection setup outside request deadline; bounded AbortSignal now covers connection and action, and prevents late sends.
- Existing UI fixture screenshots remain applicable to visible/hidden states; no additional real-game manual UI run or FPS benchmark was performed. No release was published.