# 2026-09-03 — `get_unity_status`가 읽지 못한 frame을 `undefined`로 보고하지 않는다

- Date: 2026-09-03
- GitHub Issue: #48
- Status: Ready for review

## Goal

`get_unity_status`가 문장에 `undefined`를 넣지 않는다. `get_unity_status`와
`get_scene_state`가 reading 도착 여부에 대해 같은 답을 한다. `PULSE` frame이 접히지
못하고 버려질 때 그 사실이 `get_unity_status` 응답에 남는다.

## Non-goals

- #19 — `PulseComponent.members` / 게임이 보내는 `m` wire key 어긋남 자체를 고치지 않는다.
  이 변경 뒤에도 그 어긋남은 여전히 frame을 읽지 못하게 만들지만, 이제는 "읽지 못했다"고
  말하지 "도착했다"고 거짓말하지 않는다.
- `PulseObject` / `PulseComponent` / `PulseMember`의 중첩 필드를 `zod` 같은 parser로
  경계에서 완전히 검증하는 일은 하지 않는다. 아래 "Data Shapes 트레이드오프"에 이유를 적는다.
- `connection.ts`의 `report` 콜백이 갖는 기존 관대한 fallback(`() => undefined`) 자체는
  그대로 둔다. `index.ts`에서 실제 콜백을 넘겨 그 fallback이 더 이상 실전 경로에서 쓰이지
  않게만 한다.

## Root Cause

`PulseStore.fold()`(`mcp/src/pulse.ts`)는 `PULSE` frame이 오면 실제로 접기(`foldInternal`)를
시도하기 **전에** `this.lastReadingAt = this.now()`부터 찍는다. `connection.ts`의 shallow
검증(`isGamePushFrame`)은 frame 최상위 필드(`reading`, `frame`, `scene`, `whole`, …)의
타입만 보고, `active[].by[].members` 같은 중첩 구조는 보지 않는다. 게임이 `members` 대신
`m`을 보내는 #19 상태에서는 `mergeMembers`가 `for (const member of incoming)`에서
`incoming`(= `undefined`)을 순회하려다 `TypeError: incoming is not iterable`을 던진다.

이 예외는 `connection.ts`의 `try { this.pulseStore.fold(frame); } catch { this.report(...) }`
에서 잡히지만, 그 시점엔 이미 `lastReadingAt`이 "지금"으로 갱신된 뒤이고 `this.pulseState`는
갱신되지 않은 채로 남는다. 그 결과:

- `get_scene_state` → `store.getState()`가 `undefined` → "No scene reading has arrived."
- `get_unity_status` → `store.getLastReadingAt()`은 정의됨(방금 도착) → `describeStatus`가
  "reading 도착함" 분기를 타지만 `state?.reading/frame/scene`은 전부 `undefined` → 문장에
  `undefined`가 세 번 찍힌다.

로컬 재현(`mcp/repro.mjs`, 커밋하지 않음): `members` 대신 `m`을 담은 `active` object로
`store.fold()`를 부르면 `fold threw: incoming is not iterable`, 그 뒤
`getState() === undefined`, `getLastReadingAt()`은 실수 타임스탬프를 반환한다. 세 줄
`undefined` 증상과 정확히 일치한다.

추가로, `connection.ts`의 shallow 검증 실패(`Ignored malformed PULSE frame`)와
`index.ts`의 socket 오류 보고는 전부 `report` 콜백을 부르지만, `index.ts`가 그 콜백을
아예 넘기지 않아 fallback `() => undefined`로 조용히 사라진다. #48이 지적한 "그 사실이
사람에게 보이는 곳에 남는다" 조건은 이 경로도 포함한다.

## Data Shapes 트레이드오프

`.agents/docs/coding-style.md`는 경계를 넘는 payload를 선언된 타입으로 한 번에 parse하라고
한다. `PULSE`는 그 경계의 payload가 맞지만, `active`/`deactive` 안의 `PulseObject.by[]` /
`PulseComponent.members[]`는 게임이 임의의 component·member 집합을 실어 보내는 자리라
스키마가 열려 있다 — `coding-style.md`가 raw JSON을 허용하는 예외("스키마가 그 지점에서
정말 열려 있다")에 해당한다. `zod`로 `PulseObject`를 완전히 검증하려면 `by`/`members`의
모양을 미리 못박아야 하는데, 그건 #19가 다루는 wire contract 자체를 다시 설계하는 일이라
이 issue의 범위를 넘는다.

그래서 이 변경은 "가장 작은 일관된 변경" 쪽을 택한다: 중첩 구조를 parse하는 대신,
`foldInternal`이 실제로 예외를 던질 수 있다는 사실을 경계(= `PulseStore.fold()`)에서
받아들이고, 그 예외를 "frame은 도착했지만 읽지 못했다"는 하나의 명시적 상태로 바꾼다.
이 상태 자체는 `{ at: number; reason: string }`로 타입이 있다 — 그래서 이 변경이
untyped map을 새로 들이는 것은 아니다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `mcp/src/tools.ts`, `mcp/src/connection.ts`, `mcp/src/pulse.ts`,
      `mcp/test/status.test.ts`, `mcp/test/pulse.test.ts`, `mcp/test/connection.test.ts`를
      읽고 root cause를 로컬 재현으로 확인함.
- [x] **Step 1: `PulseStore`가 읽지 못한 frame을 구분해 들고, 성공하면 스스로 지운다**
      (`mcp/src/pulse.ts`)
  - `export interface UnreadableFrame { at: number; reason: string }`를 선언한다 —
    `tools.ts`가 같은 모양을 다시 선언하지 않고 이 타입을 import해서 쓴다(두 파일이 독립적으로
    같은 shape을 선언하면 하나만 바뀌었을 때 조용히 어긋난다).
  - `fold()`의 `PULSE` 분기에서 `foldInternal` 호출을 try/catch로 감싼다.
  - 성공하면 지금처럼 `this.pulseState`와 `this.lastReadingAt`을 함께 갱신하고,
    **`this.lastUnreadableFrame = undefined`도 같이 정리한다.** `PulseStore`는 fold를
    호출 순서대로 처리하므로 "가장 최근 사건이 읽기 실패였는지"를 이미 알고 있다 — 그
    knowledge를 `tools.ts`에서 timestamp 비교로 다시 만들지 않는다. 이 정리 덕분에
    `getLastUnreadableFrame()`은 정확히 "마지막으로 시도한 fold가 실패했고 그 뒤로 아무
    것도 성공하지 않았다"는 경우에만 값을 반환한다.
  - 실패하면 `this.lastReadingAt`을 건드리지 않고(= 도착한 reading으로 세지 않는다),
    `this.lastUnreadableFrame = { at: this.now(), reason: reasonOf(error) }`를 채운다.
    `reasonOf(error) = error instanceof Error ? error.message : String(error)`.
  - 새 getter `getLastUnreadableFrame(): UnreadableFrame | undefined`를 추가한다.
  - `fold()`는 더 이상 `PULSE` 경로에서 예외를 밖으로 던지지 않는다. `connection.ts`의
    기존 `try { this.pulseStore.fold(frame); } catch { ... }`는 다른 예상 못 한 실패를
    막는 방어선으로 그대로 둔다. **주의**: 이 방어선은 이 issue가 고치는 케이스(중첩
    `by[].members` 파싱 실패)에서는 더 이상 걸리지 않는다 — `PulseStore.fold()`가 안에서
    잡기 때문이다. 즉 `Ignored malformed PULSE frame` 로그 문자열 자체는 이 repro에서
    다시 나타나지 않고, 실패는 오직 `get_unity_status`의 새 문장으로만 보인다. shallow
    검증 실패(`isGamePushFrame`이 false를 돌려주는 경우, 예: `{ type: "PULSE", reading: 1 }`
    처럼 최상위 필드 자체가 없는 경우)는 여전히 `connection.ts`에서 걸려 그 로그 문자열을
    그대로 낸다 — 이건 다른 경로다.
- [x] **Step 2: `describeStatus`가 두 분기 모두에서 실패 사실을 붙일 수 있게 한다**
      (`mcp/src/tools.ts`)
  - `UnreadableFrame`을 `pulse.ts`에서 import하고 `UnityStatus`에
    `lastUnreadableFrame?: UnreadableFrame`을 추가한다.
  - "just now" / "`Xs ago`" 나이 계산을 `describeAge(now: number, at: number): string`
    으로 뽑아 reading 문장과 새 실패 문장이 같은 표현을 공유하게 한다.
  - 지금 구조는 `lastReadingAt === undefined`일 때 별도 `return`으로 끝나 버려서, 그
    분기에는 실패 문장을 붙일 자리가 없다. 이 `return`을 없애고, 두 분기(reading 없음 /
    reading 있음) 모두 문장 배열을 만든 뒤 `lastUnreadableFrame`이 있을 때만 마지막 줄
    `A frame arrived but could not be read <age>: <reason>.`을 공통으로 붙이고
    `join(" ")`하는 하나의 흐름으로 합친다. Step 1에서 `PulseStore`가
    `lastUnreadableFrame`을 성공 시 스스로 지우므로, `describeStatus`는 그 값이 있으면
    그냥 붙이면 된다 — timestamp 비교가 필요 없다.
  - `lastReadingAt`이 정의된 분기에서만 `reading`/`frame`/`scene`을 문장에 넣는 지금
    규칙은 유지한다 — Step 1 덕분에 `lastReadingAt`이 정의되어 있으면
    `state.reading/frame/scene`도 항상 함께 정의되어 있으므로(둘 다 같은 성공한
    `foldInternal` 호출에서 나온다) `undefined`가 찍힐 길이 없어진다.
  - `get_unity_status` 핸들러에서 `store.getLastUnreadableFrame()`을 읽어
    `describeStatus`에 넘긴다.
- [x] **Step 3: 기존에 조용히 사라지던 `report`를 실제로 내보낸다** (`mcp/src/index.ts`)
  - `UnityConnection` 생성자에 `report: (message, error) => console.error(message, error ?? "")`를
    넘긴다. `console.error`는 `stderr`로 나가므로 stdio 위의 MCP JSON-RPC 스트림(`stdout`)을
    건드리지 않는다. 이걸로 shallow 검증 실패, 소켓 오류, reconnect 실패가 지금은 사라지는
    자리에서 실제로 보이게 된다.
  - `index.ts`는 seam이 없는 top-level 스크립트고 `mcp/test/`에 `index.test.ts`가 없다.
    이 한 줄짜리 wiring을 위해 `buildConnection()` 같은 새 factory를 뽑아 테스트 seam을
    만드는 것은 이 issue 대비 과하다(YAGNI) — 대신 Validation에 수동 확인 절차를 명시하고
    자동 테스트 없음을 그대로 인정한다.
- [x] **Step 4: Tests**
  - `mcp/test/pulse.test.ts`
    - 정상 `PULSE` frame 하나를 먼저 성공시킨 뒤(`lastReadingAt = T1`, `getState()`가 그
      reading을 담음), `by[].members`가 아예 없는(또는 순회 불가능한) `active` object로
      두 번째 `fold()`를 부른다. 이 순서로 쓰는 이유는 "성공 하나 뒤에 실패 하나"가 이론적
      구석이 아니라 #48이 고치는 실제 장면이기 때문이다 — reading이 잘 들어오다 멈추는
      순간이다. 예외를 던지지 않고, `getState()`가 **첫 번째 성공 상태 그대로** 남고,
      `getLastReadingAt()`도 `T1` 그대로 남으며(= 실패가 새 reading으로 세지지 않는다),
      `getLastUnreadableFrame()`이 이유를 담아 채워지는 것을 확인.
    - 실패한 fold 다음에 정상 `PULSE` frame이 한 번 더 오면 `getLastUnreadableFrame()`이
      다시 `undefined`가 되는 것을 확인(clock 조작 없이 — fold를 두 번 부르는 순서만으로
      검증되는 것이 이 설계의 요점이다).
  - `mcp/test/status.test.ts`
    - `lastReadingAt === undefined`이면서 `lastUnreadableFrame`이 있는 입력을 주면 문장에
      `undefined`가 전혀 없고("No scene reading has arrived" 뒤에) 실패 사실이 한 줄로
      남는 것을 확인 — Step 1의 must-fix였던 "reading 없음 분기가 죽은 끝이 되는" 문제가
      실제로 고쳐졌는지 이 테스트가 pin한다.
    - `lastReadingAt`과 `reading`/`frame`/`scene`이 모두 있는 입력에 `lastUnreadableFrame`도
      같이 주면, 기존 "reading 도착" 문장 뒤에 실패 문장이 이어 붙는 것을 확인. 이 조합은
      이론적 구석이 아니다 — Step 1에서 "실패해도 `lastReadingAt`은 건드리지 않는다"고
      정했으므로, 정상 reading이 들어오다가(→ `lastReadingAt` 값이 생김) 그 다음 frame이
      읽히지 않으면(→ `lastUnreadableFrame`도 생김) 이 두 값이 **동시에** 있는 상태가
      정상 경로에서 그대로 만들어진다. status가 "reading 42 arrived 30s ago" 뒤에
      "A frame arrived but could not be read just now: …"를 함께 말해야 하는, #48이 고쳐야
      할 바로 그 장면이다.
    - `lastUnreadableFrame`이 없으면(기존 테스트들처럼) 실패 문장이 전혀 안 붙는 것은
      기존 테스트가 이미 pin하고 있어 추가하지 않는다.
  - `mcp/test/connection.test.ts` — 새 assertion을 추가하지 않는다. `report`가 실제로
    호출되는 경로(shallow 검증 실패, 소켓 오류 등)는 이미 이 파일이 pin하고 있고,
    `index.ts`의 `console.error` wiring 자체는 Step 3에서 설명한 대로 수동 확인으로
    남긴다.

## Validation

- **Commands to run:**
  - `unfunction node npm npx 2>/dev/null; export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH"`
  - `cd mcp && npm install`(worktree라 `node_modules` 없음)
  - `npm run build`
  - `npm test`
- **Expected output:** 기존 110개 + 신규 테스트 전부 통과.
- **Result (2026-09-03):** `npm run build` 통과, `npm test` **114 pass / 0 fail**.
  `develop` baseline은 같은 명령으로 110 pass / 0 fail이었다.
- **Manual check (Step 3, 자동 테스트 없음) — 하지 않음.** 빌드된 서버를 띄워
  malformed frame을 보내고 stderr를 눈으로 보는 확인은 실행하지 않았다. `index.ts`의
  `report` wiring은 diff를 읽어 확인했을 뿐이고, 이 한 줄이 실패하면 서버는 조용히
  변경 전 동작(report 없음)으로 돌아간다. 자동 테스트도 수동 확인도 이 줄을 덮지 않는다.

## Review Findings Applied

`plan-review` fast/medium 1차 결과를 반영함(둘 다 완료, medium은 PASS):

- (fast, must-fix) `describeStatus`의 `lastReadingAt === undefined` 분기가 별도
  `return`이라 실패 문장을 못 붙이던 문제 → Step 2에서 두 분기를 하나의 흐름으로 합쳐
  해결. Step 4에 이걸 pin하는 테스트를 명시.
- (medium, should-fix) "가장 최근 사건일 때만 보여준다" 규칙을 `tools.ts`의 timestamp
  비교로 만들지 말고 `PulseStore`가 성공 시 스스로 `lastUnreadableFrame`을 지우게 함
  → Step 1에 반영. 이것으로 (fast, should-fix) tie-break 질문도 같이 해소됨(비교 자체가
  없어짐).
- (medium, should-fix) `{ at, reason }` shape을 두 파일에 따로 선언하지 말 것 →
  `pulse.ts`에 `UnreadableFrame`을 선언하고 `tools.ts`가 import하도록 Step 1/2에 반영.
- (fast, should-fix) 이 repro에서 `Ignored malformed PULSE frame` 로그 자체는 다시
  나타나지 않는다는 것을 명시 → Step 1 "주의" 문단에 반영.
- (fast, should-fix) `index.ts` wiring에 자동 테스트가 없는 문제 → factory seam을 새로
  만드는 대신(과함) Validation에 수동 확인 절차를 추가하는 쪽을 택함. 이유: 이 wiring은
  한 줄이고 실패해도 조용히 원래 상태(report 없음)로 돌아갈 뿐이라 새 추상화를 정당화할
  만큼 위험하지 않다.
- (fast, question) age 표시 문구 재사용 → Step 2에서 `describeAge` 헬퍼로 통일.

`plan-review` heavy 2차 결과(NONPASS, 1 blocker) 반영:

- (heavy, blocker) Step 4의 "`lastReadingAt`과 `lastUnreadableFrame`이 동시에 있는 입력은
  이론적 구석"이라는 근거가 틀렸음 — Step 1이 "실패해도 `lastReadingAt`은 안 건드린다"고
  정한 이상, 성공 fold 하나 뒤에 실패 fold가 오면 이 조합이 정상 경로에서 그대로 만들어지고,
  이게 바로 #48이 고치는 장면(reading이 들어오다 멈추는 순간)이다. `pulse.test.ts`와
  `status.test.ts`의 관련 항목을 "성공 → 실패" 순서로 명시하고 `getLastReadingAt()`이
  실패 뒤에도 이전 값 그대로 남는 것을 pin하도록 문구를 고침.

## Open Questions

- (없음)
