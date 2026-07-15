# 2026-07-09 — Unity SDK localhost WebSocket PoC 구현

- Date: 2026-07-09
- GitHub Issue: #5
- Status: Draft

## Goal

Unity SDK가 `ArtelManager`를 통해 localhost WebSocket server를 띄우고, 외부 웹페이지가 Unity scene 정보를 요청/수신한 뒤 `Button` 클릭과 `EditText` 입력 action을 다시 Unity에 전달하는 PoC를 만든다.

핵심 검증은 "Unity UI -> scene JSON -> browser UI render -> ACTION -> Unity UI 반영" 왕복 loop다.

## Non-goals

- onboarding 구현
- stable id 또는 id persistence 보장
- 인증, origin 제한, TLS 같은 production security
- production-grade browser UI
- 복잡한 Unity layout을 HTML에 정밀 복제
- server/client 분리 배포 구조 확정

## Context / Constraints

- Branch: `feat/5`
- Issue: https://github.com/project-artel/artel-sdk/issues/5
- 현재 repo 본체는 `src/Artel`의 작은 .NET skeleton 수준이고, Unity sample은 `samples/WordVenture` 아래에 있다.
- WebSocket server는 테스트용 component가 아니라 core entrypoint인 `ArtelManager`가 생성/관리해야 한다.
- 테스트용 HTTP page server는 별도 `ArtelTestPageServer` MonoBehaviour가 소유한다.
- WebSocket server는 직접 frame parser를 구현하지 않고 Unity-compatible `websocket-sharp` dependency adapter로 구현한다.
- scene id는 PoC에서 scan마다 새로 부여해도 된다.
- scene scan 시점에 `id -> Unity target` mapping을 유지하고, ACTION은 이 mapping을 사용한다.
- scene은 JSON-RPC 요청을 받으면 즉시 scan해서 `GAME_STATE`로 응답한다.
- wire format은 issue에 정의한 `GAME_STATE`/`ACTION` 형태를 따른다. 구현에서는 JSON field 오타인 `childern` 대신 `children`으로 통일한다.

## Approach (Checklist)

- [ ] **Step 0: Recon** (Inspect existing code, locate files)
  - [ ] Unity package 배치 위치를 확정한다. 후보: `Packages/kr.artel.sdk/Runtime` 또는 현재 `src/Artel` 기반 재구성.
  - [ ] sample `WordVenture`가 SDK package를 어떤 방식으로 참조해야 하는지 확인한다.
  - [ ] Unity UI scan 대상 범위를 정한다: `UnityEngine.UI.Button`, `InputField`, `Text`와 TMP 계열 포함 여부.
  - [x] WebSocket implementation 후보를 확인한다. Unity 호환성과 dependency 추가 비용을 우선 본다.

- [ ] **Step 1: Implementation** (Code changes, file paths)
  - [x] `ArtelManager` MonoBehaviour를 추가한다.
    - `Start`/`OnEnable`에서 localhost WebSocket server를 시작한다.
    - `OnDisable`/`OnDestroy`에서 server를 중지한다.
    - port, auto-start를 serialized option으로 둔다.
  - [x] `ArtelWebSocketServer`를 추가한다.
    - client connect/disconnect 관리
    - request message parse
    - `scan_scene` JSON-RPC 처리
    - `ACTION` dispatch
  - [x] scene domain DTO를 추가한다.
    - `GameStateMessage`
    - `SceneNode`
    - `ActionMessage`
    - `JsonRpcAction`
  - [x] `SceneScanner`를 추가한다.
    - active Unity scene root들을 순회한다.
    - UI component type을 `block`, `Button`, `EditText`, `text`로 변환한다.
    - scan마다 integer id를 발급한다.
    - `id -> target` mapping snapshot을 생성한다.
  - [x] `ActionExecutor`를 추가한다.
    - `button_click(id)`: mapped button의 `onClick.Invoke()`
    - `enter_text(id, value)`: mapped input text 갱신 및 change event invoke
    - unknown id/method는 JSON-RPC error 또는 log로 남긴다.
  - [x] 테스트용 localhost page를 추가한다.
    - WebSocket 연결 상태 표시
    - scene scan 요청 button
    - `GAME_STATE.scene` tree 렌더
    - `EditText` input 생성 및 submit/change 시 `enter_text`
    - `Button` click 시 `button_click`
  - [ ] sample scene에서 `ArtelManager`와 `ArtelTestPageServer`를 붙여 수동 검증할 수 있는 최소 path를 만든다.

- [ ] **Step 2: Tests** (Unit tests, manual verification steps)
  - [ ] DTO serialization/deserialization을 가능한 범위에서 unit test 또는 small compile check로 검증한다.
  - [ ] Unity compile validation을 수행한다.
  - [ ] sample scene manual test:
    - WebSocket server starts on localhost.
    - browser page connects.
    - scan request returns `GAME_STATE`.
    - page renders text/edit/button.
    - button click in page invokes Unity button.
    - text input in page updates Unity input field.

- [ ] **Step 3: Rollout / Rollback** (Feature flags, migration steps)
  - [ ] Default disabled 또는 explicit `ArtelManager` component opt-in으로 둔다.
  - [ ] localhost-only default로 외부 network exposure를 피한다.
  - [ ] rollback은 `ArtelManager` component 제거 또는 branch revert로 충분해야 한다.

## Validation

- **Commands to run:**
  - `git status --short --branch`
  - `dotnet build samples/WordVenture/Artel.Runtime.csproj --no-restore`
  - 가능한 경우 `dotnet build src/Artel/Artel.csproj`

- **Expected output:**
  - branch가 `feat/5`
  - compile error 없음
  - generated sample csproj compile 성공
  - WebSocket server start log with vendored `websocket-sharp`
  - browser page에서 `GAME_STATE` 수신
  - browser action 후 Unity UI 반응 확인

## Risks & Rollback

- **Risks:**
  - Unity에서 사용할 WebSocket server library 설치 방식이 package compatibility를 흔들 수 있다.
  - Unity main thread 밖에서 UI component를 만지면 crash 또는 undefined behavior가 날 수 있다.
  - scan id가 unstable이므로 stale ACTION이 잘못된 target에 적용될 수 있다.
  - sample repo가 nested/untracked 상태라 package와 sample 변경 경계가 흐려질 수 있다.
  - localhost server가 Play Mode lifecycle과 충돌하면 port가 점유된 채 남을 수 있다.

- **Rollback steps:** (e.g., `git revert`, toggle flag off)
  - `ArtelManager` component를 scene에서 제거한다.
  - auto-start option을 끈다.
  - merge 후 문제 발생 시 issue #5 commit을 `git revert`한다.

## Open Questions

- Unity package 위치를 `Packages/kr.artel.sdk`로 복원/사용할지, 현재 `src/Artel` 구조를 유지할지 결정 필요.
- 없음.
- TMP support를 이번 PoC 필수 범위에 넣을지 결정 필요.
- 테스트용 web page를 SDK package asset으로 포함할지, sample-only asset으로 둘지 결정 필요.
