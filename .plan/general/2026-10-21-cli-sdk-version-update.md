# 2026-10-21 — SDK와 CLI 버전 확인 및 업데이트

- Date: 2026-10-21
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/109
- Status: Implemented; draft PR handoff

## Goal

실행 중인 CLI와 설치된 SDK의 호환 버전이 다르면 필요한 업데이트를 안내하고,
Unity 설정 창에서 기존 CLI pin을 SDK 호환 버전으로 변경할 수 있게 한다.

## Non-goals

SDK dependency 자체 업데이트, release 발행, 글로벌 npm 설치, 자동 다운로드/재시작,
local build 교체, 버전이 다르다는 이유만으로 기존 input tool 차단.

## Context / Constraints

SDK와 CLI는 독립적으로 version을 올린다. SDK version이 아니라 SDK의
`Editor/McpConfig/mcp-server-version.txt`에 지정된 CLI version을 기준으로 비교한다.
`Add`는 기존 server entry를 통째로 교체하므로 업데이트에는 그대로 사용하지 않는다.
현재 `get_unity_status`는 접속 여부만 보고, `play_capabilities`는 runtimeVersion을 제공한다.

## Approach (Checklist)
- [x] **Step 0: Recon** — version metadata, release scripts, JSON/TOML config, status/capabilities 확인.
- [x] **Step 1: Implementation**
  - Scan assembly의 `CompatibleMcpServerVersion`에 required CLI constant를 두고 `play_capabilities`의 typed DTO로 보낸다. SDK version constant와 파일을 분리해 bump workflow의 SDK/CLI 변경 구분을 유지한다.
  - CLI 실제 package version을 status 등록 시 전달한다. status는 capability query로 matching/mismatch/unknown을 구분하고 현재/필요 버전, Unity 설정 경로와 재시작 안내를 반환한다. older SDK의 unsupported/missing metadata도 unknown으로 표시한다. 기존 도구는 계속 동작한다.
  - config format에 launch command/args 읽기와 표적 Update를 추가한다. JSON은 같은 server의 command/args만 변경하고 다른 field를 보존한다. TOML은 생성기가 쓰는 단일 행 command/args만 읽고 교체하며 comments/env/subtable과 다른 server를 보존한다. 중복·multiline·모호한 표기는 수동 수정 안내로 거절한다.
  - Settings는 SDK-required CLI pin과 현재 npx entry를 비교해 Update required/Matched를 표시한다. Update CLI는 검증한 npx entry만 수정한다. local node/custom launch는 unverified와 수동 안내로 두며 자동 교체하지 않는다. Add는 기존 동작을 유지하고 수정 전 상태를 재검증한다.
  - `set-mcp-server-version.sh`와 publish/bump workflow의 file contract를 runtime constant까지 확장한다.
- [x] **Step 2: Tests** — matching/mismatch/unknown runtime response, tool status integration, JSON/TOML 정보 보존/거절/멱등 업데이트, runtime constant 동기화, 기존 Unity/MCP suites.
- [x] **Step 3: Rollout / Rollback** — README에 Update CLI와 재시작/설치 실패 안내를 기록하고 draft PR. 되돌리기는 commit revert.

## Validation
- **Commands to run:** TypeScript build, MCP node tests, throwaway Unity EditMode/PlayMode suites; release script contract check; Unity settings fixture screenshots.
- **Expected output:** 필요한 버전을 명확히 안내하며, pin 변경 후 재확인하면 matching. current process 업데이트 완료로 오인하지 않는다. 기존 설정 옵션은 보존된다.

## Risks & Rollback
- **Risks:** runtime metadata drift, 모호한 TOML을 잘못 바꾸는 위험, configured pin과 실행 중 process version 혼동.
- **Rollback steps:** commit revert. 업데이트한 config pin은 이전 버전으로 명시적으로 복원한다.

## Open Questions
- 없음. 자동 SDK 변경 없이 SDK가 선언한 CLI 버전에 맞춘다.

## Review decisions

- 자동 수정 대상은 생성기 형태 `command: "npx"`, `args: ["-y", "unity-play-mcp@<exact semver>"]`로 한정한다. `npx.cmd`, wrappers, 추가 인자, 여러 package, unpinned/tag/range는 custom/manual로 분류한다. 비교/수정 가능 여부는 Configurator의 공통 helper에 두고 UI와 format에 중복하지 않는다.
- SDK 요구 버전과 CLI를 exact equality로 비교한다. prerelease도 구분하며, CLI가 더 새로워도 SDK 요구 버전으로 맞추는 downgrade가 될 수 있음을 표시한다. 업데이트 target은 local-first locator 대신 명시적 pinned npx entry로 만든다.
- status는 기존 connection의 `sendActions`로 1500ms timeout의 capability probe를 요청한다. 별도 client/cache/session은 만들지 않고 protocol 검사보다 raw compatibility metadata를 먼저 읽는다. 실패/구형 SDK/누락/잘못된 metadata는 unknown으로 표시하고 기존 connected status는 유지한다. probe 후 연결 상태를 다시 읽으며 reconnect 때마다 재조회한다.
- Settings의 configured pin과 실행 중 CLI는 별개다. 수정 후 Settings는 matching pin + restart required이며, 기존 CLI status는 재시작 전까지 mismatch다. 설치 실패를 업데이트 성공으로 부르지 않는다.
- JSON comments와 duplicate keys는 쓰기 전에 거절한다. TOML은 단일 행 double-quoted command/args만 지원하며 모호한 입력은 수동 안내한다. 원본의 command를 유지하고 package token만 바뀐 args를 갱신한다.
- 추가 tests: timeout/disconnect probe, newer/prerelease CLI mismatch, npx.cmd/extra args/range/wrapper/유사 package 거절, pin 변경 전후 running version 유지.

## Results

- TypeScript compilation and MCP tests: 372/372 passed.
- Unity 2022.3.34f1 EditMode: 313/313 passed, including all 8 new config update cases.
- Unity PlayMode: 77/77 passed on rerun, including requiredServerVersion wire assertion. First run had 4 existing host recovery test failures with socket-address-in-use logs; no listening test port remained before rerun. No production change was made for these transient failures.
- Release script on an isolated fixture: npm manifest, editor pin and runtime constant all changed to 0.4.1; invalid input rejected before mutation.
- Pair review: PASS after rejecting document-wide TOML multiline strings before header detection; both string forms covered by regression fixtures.
- Manual Settings screenshots and actual agent restart/npm download were not verified: Windows computer-use initialization failed with kernel asset path error (os error 3). This remains a draft review limitation.
- No release was published and no real agent configuration was modified.