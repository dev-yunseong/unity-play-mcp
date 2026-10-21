# 2026-10-21 — 요청할 때만 UI와 수집 활성화

- Date: 2026-10-21
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/107
- Status: Implemented and validated; 15초 유휴 확장은 2026-10-21-idle-runtime-suspension.md 참고

## Goal

Unity Play MCP를 의존성으로 설치해도 기본 상태에는 UI가 없고, agent가 사용할 때만 수집한다.
`set_overlay { visible: true | false }`로 cursor와 keyboard overlay를 함께 제어한다.

## Non-goals

게임 자체 UI 변경, 외부 UI 라이브러리 도입, input instrumentation 제거, 완전한 zero overhead 보장.

## Context / Constraints

현재 host는 시작할 때 overlay를 만들고 discovery와 성능 측정을 시작한다.
`IAgentTransport.IsConnected`는 client 유무가 아니라 server 실행 여부다.
숨겨진 cursor도 입력 위치와 smooth drag를 동일하게 전달해야 한다.
localhost listener와 요청 확인은 유지한다.

## Approach (Checklist)
- [x] **Step 0: Recon** — host, overlay controllers, transport, MCP action schema 확인.
- [x] **Step 1: Implementation** — overlay resource를 첫 표시까지 생성하지 않고 숨기면 Update를 멈춘다. cursor 위치를 UI와 분리한다. `set_overlay`를 ActionExecutor와 MCP의 tool/batch 공통 contract에 추가한다. active client 수를 transport에서 제공하고 host는 요청에 필요한 discovery/성능 수집만 시작하며 마지막 disconnect에 readings, discovery, UI, input을 정리한다. listener 시작만으로 discovery를 실행하지 않는다. reconnect의 집계 창을 초기화한다.
- [x] **Step 2: Tests** — 기본 hidden, 반복 show/hide, hidden movement/drag, reload, disconnect/reconnect를 Unity tests로 검증한다. MCP schema/dispatch/batch tests를 실행한다.
- [x] **Step 3: Rollout / Rollback** — README에 기본 UI 숨김과 표시 명령, listener의 잔여 비용을 기록한다. draft PR로 전달한다.

## Validation
- **Commands to run:** 확인된 MCP TypeScript build/test; throwaway Unity project EditMode/PlayMode tests.
- **Expected output:** 기존 input 동작 유지, 기본 overlay resource 없음, hidden Update 중단, client 없는 discovery/성능 수집 없음.

## Review decisions

- Host의 `BeginClientSession` / `EndClientSession`이 lifecycle을 소유한다. 마지막 disconnect와 StopTransport는 동일한 정리를 쓴다. 별도 manager 계층은 만들지 않는다.
- Transport는 active client 수와 마지막 client 종료 generation을 제공한다. 프레임 사이 disconnect/reconnect도 종료를 감지한다. 닫힌 client의 queued message는 폐기한다.
- 종료 시 host coroutine과 queued action을 먼저 취소하고 input과 timeScale을 복원한다. PlayApi를 새로 만들어 이전 operation 소유권도 지운다.
- connect와 일반 input은 UI를 켜지 않는다. `set_overlay`는 boolean 하나만 받고 성공 시 적용된 boolean을 반환한다. 첫 표시 때만 UI를 만들고 hide는 canvas와 controller를 비활성화해서 재사용한다. 마지막 disconnect와 host reload는 hidden으로 복귀한다.
- `set_overlay(false)`는 연결 중 input/수집을 멈추지 않는다. cursor는 serialized 논리 위치를 유지하고 hidden movement 뒤 show 시 그 위치를 표시한다.
- Tests: 두 client 중 하나 종료는 유지, 마지막 종료는 정리, 프레임 사이 교체도 정리, drag/batch 도중 종료 후 입력 재개 없음, hidden 기본 resource 없음, 반복 show/hide, reconnect의 새 집계 창.
- `runInBackground`는 listener의 main-thread 요청 처리를 위해 유지한다. 문서에 명시하고 전체 idle 비용이 0이라고 주장하지 않는다.

## Risks & Rollback
- **Risks:** reload 시 UI 중복, hidden cursor가 input 전달을 중단하는 회귀, server-open과 client-connected 혼동.
- **Rollback steps:** 변경 commit revert.

## Open Questions
- 없음. 성능 저하 0이 아닌 불필요한 UI/수집 비용 제거를 검증한다.


## Validation results

- `node mcp/node_modules/typescript/bin/tsc -p mcp/tsconfig.json` — passed.
- `node --test mcp/dist/test/*.test.js` — 368 passed, 0 failed.
- Unity 2022.3.34f1 `-batchmode -nographics -runTests -testPlatform EditMode` — baseline 304/304, final 306/306 passed.
- Unity 2022.3.34f1 `-batchmode -nographics -runTests -testPlatform PlayMode` — baseline 77/77, final 81/81 passed.
- Test project: `C:/Users/jys09/Documents/ChatGPT/Unity Play MCP/idle-runtime-tests`, assembled from `.github/unity-test-project` and the embedded package with PowerShell Copy-Item (equivalent template/copy inputs to CI).
- Graphics-enabled `OverlayEvidenceTests` fixture — 1/1 passed. Three rendered states and reproduction fixture are in `.plan/assets/2026-10-21-on-demand-runtime/`. These are camera-rendered fixture images, not a live MCP end-to-end run.
- `git diff --check` — passed.
- Fast/medium plan findings incorporated; heavy plan review and final pair review passed.
- Pair review caught hidden-reload cursor asset ownership; fixed with serialized asset references and destruction regression coverage.
- Reconnect reuses ProcessResourceSampler and resets its baseline, avoiding a new process reader per session.
- No latency/FPS benchmark was performed. Idle UI/scanning/sampling suspension is structurally tested; listener, input interception, and runInBackground costs remain.
