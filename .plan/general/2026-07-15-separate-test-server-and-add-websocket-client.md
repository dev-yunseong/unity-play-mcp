# 2026-07-15 — 테스트 서버 분리와 WebSocket client 공통화

- Date: 2026-07-15
- Jira: ARTEL-8
- Status: Complete

## Goal

테스트 페이지용 HTTP/WebSocket server 수명주기를 별도 상위 컴포넌트로 묶고, `ArtelManager`는 client/server 공통 transport 계약만 사용한다. transport가 주입되지 않으면 WebSocket client를 생성한다. SDK별 UUID를 REST API로 등록한 뒤 명시적으로 WebSocket에 연결하는 onboarding GUI를 제공한다.

## Non-goals

- scene/action 프로토콜 재설계
- 원격 orchestration server 구현 변경
- 기존 tracking/action 실행 구조 리팩터링

## Context / Constraints

- Unity package 런타임이며 interface는 Inspector에서 직접 직렬화할 수 없다.
- 현재 `ArtelManager`가 WebSocket server를 직접 생성하고 HTTP test page server는 별도 `MonoBehaviour`다.
- server는 연결별 응답, client는 단일 peer 응답이 필요하므로 공통 transport가 메시지 출처를 추상화해야 한다.
- UUID는 실행마다 바뀌면 안 되며, 저장값이 없거나 잘못된 경우에만 새 값으로 교체해야 한다.
- `Server` 도메인 객체는 secure 여부, host, port와 protocol base URI만 소유한다. REST/WebSocket path는 각 API client가 소유한다.
- local test page bind 주소는 remote server 접속 설정과 분리한다.

## Approach (Checklist)
- [x] **Step 0: Recon** (`ArtelManager`, test page/server, WebSocket server, DTO, tests와 Unity 설정 확인)
- [x] **Step 1: Transport contract** (client/server 공통 lifecycle, receive, reply/broadcast 계약 정의; WebSocket client 구현)
- [x] **Step 2: Runtime wiring** (`ArtelManager` transport 주입 지점과 null client fallback, SDK ID 저장소/연결 요청 전달)
- [x] **Step 3: Test infrastructure** (상위 test page host가 HTTP server와 WebSocket server 함께 관리하도록 이동)
- [x] **Step 4: Tests** (UUID 생성/재사용/복구와 WebSocket URL identity 전달 tests)
- [x] **Step 5: Rollout / Rollback** (기존 test page script GUID와 serialized start flag를 새 manager로 이전; diff 검토)
- [x] **Step 6: Server endpoints** (`Server`는 protocol base URI 생성, registration/WebSocket client는 각 path 계약 소유)
- [x] **Step 7: Onboarding GUI** (ViewModel이 등록/연결 상태와 workflow 소유, Controller는 runtime Canvas binding 담당)
- [x] **Step 8: Follow-up validation** (endpoint/DTO/GUI tests, Unity compile/EditMode tests, PR 갱신)

## Validation
- **Commands run:** Unity 2022.3.34f1 임시 project에서 local package import 후 EditMode tests, `git diff --check`
- **Result:** package compile 성공, Server/API path/ViewModel follow-up 포함 EditMode tests 14/14 통과, whitespace 오류 없음

## Risks & Rollback
- **Risks:** 기존 scene이 `ArtelManager.StartServer`를 호출하면 API 호환성 깨짐; query parameter 이름이 orchestration server 계약과 다를 수 있음; background WebSocket callback과 Unity main thread 경계
- **Rollback steps:** transport 계약, test host, identity 변경을 독립적으로 revert하고 기존 server wiring 복원

## Open Questions
- None. 현재 연결 계약은 `sdkId` query parameter로 정의했다.
