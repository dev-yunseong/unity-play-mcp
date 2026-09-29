# 2026-09-29 — 장면 조회에서 인증 값과 범위 밖 statics 를 노출하지 않는다

- Date: 2026-09-29
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/72
- Status: Implemented

## Goal

`get_scene_state` 를 비롯한 읽기 tool 이 인증 토큰·비밀번호·비밀 값의 원문을 돌려주지 않고, `root`/`selector` 로
좁힌 조회는 관련 없는 static 과 변경 목록을 기본으로 싣지 않는다. 가렸거나 뺀 것은 그렇다고 표시한다.

## Non-goals

- 게임의 인증 방식, 토큰 발급·만료 정책 변경.
- package 쪽 reading 파일(`PulseFile`) 형식 변경.

## Context / Constraints

- 실제 토큰은 issue·fixture·log 에 넣지 않는다. test 의 토큰은 `{"fake":"test"}` 등을 base64url 로 이은 가짜다.
- 이름 blacklist 만으로 다 찾을 수 있다고 가정하지 않는다 → 이름(멤버 이름, static 은 선언 타입 단순 이름 + 멤버)과
  값 모양(JWT, Bearer/Basic, PEM, 구분자로 나눈 조각 중 25자 이상 덩어리를 품은 32자 이상의 무작위 문자열)을 함께 본다.
- package 가 멤버 값에 싣는 참조 모양(`{path, active, …}`, `{is}`)과 계층 경로·타입 이름은 가리지 않는다.
- 디버깅용 static 조회는 `includeStatics`/`staticsDeclaring` 로 명시적으로 남긴다. 그때도 값은 가린 그대로다.

## Approach (Checklist)
- [x] **Step 1** — `mcp/src/secrets.ts`: `nameLooksSecret`, `valueLooksSecret`, `redactSecrets`(null·"" 는 두고, 비밀
  이름 아래는 안쪽 키가 평범해도 가림), `REDACTED_TEXT`.
- [x] **Step 2** — `PulseStore` 가 frame 을 읽는 자리(`readComponents`)에서 멤버와 static 을 가린다. 그 아래 상태·이력·
  tree·tombstone·검색·대기가 모두 가린 값만 본다.
- [x] **Step 3** — `get_scene_state` 범위: 좁힌 조회는 `staticsOmitted {count, reason}`, `changed` 는 보여 준 객체의
  것만(`changedOmitted`). `includeStatics`, `staticsDeclaring`. 가린 값이 실린 응답(state·visible·search·wait)에
  `redaction` 설명. 검색 후보의 표시 글자는 `"[redacted]"`.
- [x] **Step 4: Tests** — `mcp/test/secrets.test.ts`.
- [ ] **Step 5: Manual** — 실제 토큰이 있는 게임(ArcaneCasters Client 로비)에서 전량·UI root 조회에 원문이 없는지.

## Validation
- **Commands to run:** `cd mcp && npm run build && npm test`

## Risks & Rollback
- **Risks:** 값 모양 규칙이 긴 무작위 식별자(GUID 등)를 가릴 수 있고, 이름이 평범하고 값이 짧은 비밀(예: 일반 InputField
  의 비밀번호 글자)은 놓친다. 가린 값은 길이만 남아 같은 길이로 바뀐 토큰은 이력에 변화로 잡히지 않는다.
  package 의 reading 파일(`PulseFile`, 소켓이 없을 때)에는 여전히 원문이 쓰인다.
- **Rollback steps:** PR revert.

## Open Questions
- delta reading 의 `statics` 는 그 reading 에서 바뀐 것만 싣는다(기존 동작). store 가 static 을 접어 두는 것은 별도 작업.
