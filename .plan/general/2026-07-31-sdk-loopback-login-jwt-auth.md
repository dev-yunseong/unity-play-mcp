# 2026-07-31 — SDK 브라우저 로그인 + JWT 인증 전환

- Date: 2026-07-31
- Jira: None
- Status: Draft

## Goal

SDK 인증을 instance key 입력에서 브라우저 로그인 기반 JWT로 바꾼다.

SDK가 로컬 loopback 서버를 띄우고 브라우저를 열어 로그인시킨 뒤,
`http://127.0.0.1:<port>/callback`로 돌아온 일회용 code를 SDK 토큰으로 교환한다.
브라우저에는 "이 창은 닫아도 됩니다" 페이지가 뜨고, 이후 모든 SDK 호출은 JWT로 인증한다.

## Non-goals

- 모바일/WebGL/콘솔 빌드 로그인 (loopback 불가). Editor + Standalone 한정.
- refresh token 회전, 다중 계정 동시 로그인.
- 웹 대시보드(artel-home)의 로그인 방식 변경. 기존 GitHub OAuth 그대로 재사용.

## Context / Constraints

현재 상태:

- SDK: `ArtelInstanceKey`(PlayerPrefs)에 키 저장, 오버레이 입력창으로 받음.
  `POST /api/sdk/registrations`, `ws/sdk?instanceKey=`, 캡처 업로드 모두 키를 자격증명으로 씀.
- 서버: GitHub OAuth + HS256 JWT 이미 있음 (`SecurityConfig`, `JwtService`).
  TTL 15분, HttpOnly 쿠키 `artel_access_token`, OAuth 성공 시 `frontendOrigin` 고정 redirect.
- `instanceKey`는 "누구냐"가 아니라 "어느 GameInstance냐"다 (`GameInstanceEntity.instanceKey`).
  JWT는 "누구냐"만 말한다. 그래서 인스턴스 식별은 **SDK가 이미 갖고 있는 sdkUuid**로 옮긴다
  (`ArtelSdkIdentity.LoadOrCreate`, PlayerPrefs에 설치별 GUID 1개, 이미 등록 요청에 실려 감).
- sdkUuid는 프로젝트를 알려주지 않는다. 그래서 로그인 후 **프로젝트 선택 1회**가 필요하고,
  그 뒤로는 `(projectId, sdkUuid)`로 인스턴스를 자동 upsert한다. 대시보드에서 키를 미리
  발급하는 단계가 사라지고, 게임을 처음 실행하면 인스턴스가 스스로 나타난다.

제약:

- HttpOnly 쿠키는 loopback 서버로 전달되지 않는다. code 교환 방식이 필요하다.
- JWT를 redirect URL에 직접 싣지 않는다 (브라우저 히스토리/프록시 로그 노출).
- loopback 리다이렉트는 공개 클라이언트라 PKCE가 표준 방어책이다.
- 기존 OAuth 성공 핸들러는 건드리지 않는다. 이미 로그인된 사용자도 그대로 흘러가야 한다.
- 중계 페이지는 artel-home(console.artel.kr)에 둔다. onboarding(artel.kr)에는 세션도
  `AuthProvider`도 없고, OAuth 성공 redirect가 `frontendOrigin`(=console) 고정이라
  거기서 로그인하면 artel.kr로 되돌아오는 왕복 경로를 새로 만들어야 한다.
  브랜딩상 artel.kr 진입점이 필요하면 onboarding에는 console로 넘기는 포워딩 스텁만 둔다.

## Approach (Checklist)

### Step 0: Recon — 완료

- `artel-sdk/Packages/kr.artel.sdk/Runtime/`: `ArtelInstanceKey`, `ArtelOverlayViewModel`,
  `ArtelSdkRegistrationClient`, `ArtelWebSocketClient`, `Capture/CaptureUploader`
- `artel-orchestration-server/src/main/kotlin/kr/artel/orchestration/`:
  `auth/**`, `game/service/SdkRegistrationService`, `sdk/service/SdkWebSocketHandler`,
  `qa/service/QaCaptureService`
- `artel-home/src/`: `App.tsx` 라우트, `auth/`

### Step 1: 서버 — 로그인 코드 교환 (신규, 기존 동작 무영향)

- [ ] `auth/sdk/SdkLoginCodeStore.kt` — 일회용 code 저장소.
      `ConcurrentHashMap<code, (userId, codeChallenge, expiresAt)>`, TTL 5분, 1회 소비.
      `// ponytail: 인메모리라 단일 인스턴스 전제. 레플리카 늘면 Redis/DB로.`
- [ ] `POST /api/auth/sdk/codes` (웹 체인, 쿠키 인증) — body `{codeChallenge}` → `{code}`
- [ ] `POST /api/auth/sdk/token` (공개) — body `{code, codeVerifier}` → `{token, expiresAt, user}`
      SHA-256(verifier) == challenge 검증, code 즉시 소비.
- [ ] `JwtService.issueSdkToken(userId)` — `aud=artel-sdk`, TTL 30일.
      `AuthProperties`에 `sdkAudience`, `sdkTokenTtl` 추가.

### Step 2: 서버 — SDK 체인 분리 + 인증 적용

- [ ] `SecurityConfig`에 `@Order(1)` SDK 필터 체인 추가.
      `securityMatcher`: `/api/sdk/**`, `/ws/sdk`. 디코더는 `aud=artel-sdk`만 통과.
      웹 체인은 기존대로 `aud=artel`만 통과 (30일 SDK 토큰이 웹 API를 못 쓰게 격리).
- [ ] `GET /api/sdk/projects` (신규) — 로그인 사용자가 접근 가능한 프로젝트 목록
      (`projectId`, `projectName`). 30일 토큰에 웹 API 전체를 열지 않기 위해 전용으로 둔다.
- [ ] `SdkRegistrationController/Service` — 요청 본문 `instanceKey` → `projectId` + `sdkUuid`.
      JWT의 userId로 프로젝트 멤버십 확인 후 `(projectId, sdkUuid)`로 인스턴스 조회·생성.
      신규 생성 시 이름은 요청의 표시 이름(제품명/기기명)으로 두고, 대시보드에서 변경 가능.
      멤버가 아니거나 프로젝트가 없으면 404.
- [ ] `game_instance`에 `sdk_uuid` 컬럼 + `(project_id, sdk_uuid)` 유니크 제약 추가.
      기존 `last_sdk_uuid`는 이 컬럼으로 흡수한다.
- [ ] `SdkWebSocketHandler` — 핸드셰이크 파라미터 `instanceKey` → `instanceId` + `token`.
      instanceId는 등록 응답으로 받은 값이라 SDK가 이미 들고 있다.
      `// ponytail: WS 쿼리 토큰. WebSocketSharp 커스텀 헤더로 옮기려면 SDK도 같이 바꿔야 함.`
- [ ] `QaCaptureService`/DTO — `instanceKey` 제거, JWT + `instanceId`로 전환.

### Step 3: artel-home — 로그인 중계 페이지

- [ ] 라우트 `/sdk-login` 추가. 쿼리: `port`, `state`, `challenge`.
      세션 없으면 기존 로그인으로 보내고 복귀 경로 유지, 세션 있으면
      `POST /api/auth/sdk/codes` 호출 후 `http://127.0.0.1:<port>/callback?code=&state=`로 이동.
- [ ] `port`는 1024–65535 정수만 허용, redirect 대상은 `127.0.0.1` 고정 (오픈 리다이렉트 차단).

### Step 4: SDK — loopback 로그인 흐름

- [ ] `Runtime/Auth/ArtelLoopbackLogin.cs` — `HttpListener`를 `127.0.0.1` 임의 포트에 바인딩,
      `Application.OpenURL(frontend + "/sdk-login?...")`, `/callback` 수신 →
      "로그인이 완료되었습니다. 이 창은 닫아도 됩니다." HTML 응답 → code 반환.
      `state` 불일치는 거절. `#if UNITY_EDITOR || UNITY_STANDALONE` 가드.
- [ ] `Runtime/Auth/ArtelSdkToken.cs` — `ArtelInstanceKey` 대체. PlayerPrefs 저장.
      `// ponytail: PlayerPrefs 평문. OS 키체인 필요해지면 그때.`
- [ ] `ArtelSdkRegistrationClient`/`ArtelWebSocketClient`/`CaptureUploader` —
      `instanceKey` → `Authorization: Bearer <token>` + `instanceId`.
- [ ] `ArtelOverlayViewModel`/`ArtelOverlayController` — 키 입력창 제거,
      「로그인」 버튼 + 로그인 후 프로젝트 선택 목록(`GET /api/sdk/projects`) + 로그아웃.
      선택한 projectId는 PlayerPrefs에 남겨 다음 실행부터 바로 등록으로 넘어간다.
- [ ] 401 응답 시 저장 토큰 삭제하고 로그인 화면으로 복귀.

### Step 5: instance key 제거 (되돌리기 어려움 — 마지막에)

- [ ] Flyway 마이그레이션: `game_instance.instance_key` 컬럼/유니크 제약 drop.
- [ ] `GameInstanceEntity`, `GameInstanceDtos`, `GameInstanceService`(키 생성/재시도),
      `GameInstanceRepository.findActiveByInstanceKey`, 키 생성기 제거.
- [ ] artel-home 인스턴스 상세에서 키 표시/복사 UI 제거.

## Validation

- **Commands to run:**
  - 서버: `./mvnw test` (artel-orchestration-server)
  - SDK: `.agents/docs/project.md`의 throwaway 프로젝트 EditMode 러너
  - home: `npm run lint && npm run build` (artel-home)
- **신규 테스트:**
  - `SdkLoginCodeStore`: 1회 소비, 만료, challenge 불일치 거절
  - `SdkRegistrationService`: 멤버가 아닌 프로젝트로 등록 시 404, 같은 sdkUuid 재등록 시
    인스턴스가 새로 생기지 않고 갱신되는지
  - SDK EditMode: 콜백 URL 파싱, state 불일치 거절, 토큰 저장/삭제
- **수동 검증:** Editor에서 로그인 버튼 → 브라우저 로그인 → 콜백 페이지 문구 확인 →
  프로젝트 선택 → 등록 200 → 대시보드에 인스턴스 자동 생성 확인 → WebSocket 연결 → 캡처 업로드.
  같은 머신에서 재실행 시 인스턴스가 하나로 유지되는지 확인.

## Risks & Rollback

- **Risks:**
  - Step 5 이후 기존 배포 SDK(키 기반)는 즉시 인증 실패한다. 릴리스 노트/버전 게이팅 필요.
  - `HttpListener`는 Editor/Standalone 전용. 다른 플랫폼은 로그인 UI 자체를 숨겨야 한다.
  - 방화벽이 loopback 바인딩을 막으면 로그인 불가 → 실패 메시지에 원인 노출 필요.
  - 30일 SDK 토큰 탈취 시 해당 사용자 인스턴스 전체 접근. audience 분리로 웹 API는 차단하지만,
    폐기(revocation)와 refresh 회전은 별도 이슈로 분리됨(이번 범위 아님).
    그때까지는 만료되면 다시 로그인하는 것이 유일한 갱신 수단이다.
  - 인메모리 code 저장소는 서버 레플리카가 2개 이상이면 깨진다.
  - sdkUuid는 PlayerPrefs에 있다. 지우거나 다른 머신에 복제하면 인스턴스가 갈리거나 겹친다.
- **Rollback steps:** Step 1–4는 기능 추가라 커밋 revert로 되돌아간다.
  Step 5는 컬럼 drop이라 되돌리려면 마이그레이션 + 키 재발급이 필요하다. 별도 PR로 분리한다.

## Open Questions

- SDK 토큰 TTL 30일이 적절한지, 폐기 UI(대시보드에서 SDK 세션 끊기)가 이번에 필요한지.
- 프로젝트가 하나뿐이면 선택 화면을 건너뛸지.
