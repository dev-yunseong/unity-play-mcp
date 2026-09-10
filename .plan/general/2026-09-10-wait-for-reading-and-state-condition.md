# 2026-09-10 — 조작 이후 reading과 상태 조건을 기다리는 기능

- Date: 2026-09-10
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/60
- Status: Implemented

## Goal

`perform_actions` 성공은 Unity 가 입력을 받았다는 뜻일 뿐, 그 입력이 만든 게임 내 효과가
다음 `get_scene_state` 조회에 나타난다는 뜻은 아니다. WordVenture 에서 실제로 관찰된 것처럼
씬 전환이나 멤버 값 갱신은 몇 `reading` 뒤에야 나타날 수 있다. 새 tool `wait_for_condition`
을 추가해, 호출자가 이미 들고 있는 `reading`/`frame` 기준선보다 최신인 상태, 목표 `scene`,
또는 특정 멤버 값(들)을 제한 시간 안에 기다릴 수 있게 한다. 시간이 다 되면 마지막
`reading`/`frame`/`scene` 과 그때까지 충족되지 않은 조건을 함께 돌려주고, 연결이 끊겨서
못 기다린 것과 조건이 그저 아직 안 맞은 것을 구분해서 알려준다.

## Non-goals

- 임의의 고정 `sleep` 을 추가하지 않는다 — 모든 대기는 조건과 `timeoutMilliseconds` 로 표현한다.
- 애니메이션이 "다 끝났다" 는 것을 자동으로 판정하지 않는다. 이 tool 이 아는 것은 멤버 하나가
  특정 값과 같은지, `scene` 이 무엇인지, `reading`/`frame` 이 기준보다 새로운지뿐이다.
- 게임 상태를 바꾸지 않는다. 순수하게 기다리기만 한다.
- 기존 change 기반 `pulse` 전송 정책(변화 없으면 안 보냄)을 바꾸지 않는다. `Packages/dev.yunseong.unityplaymcp/`
  는 건드리지 않는다 — MCP 서버만으로 계약을 완성한다.
- `connection.ts` 의 `sendActions` 반환 모양은 바꾸지 않는다. "조작 이후" 기준선은 호출자가
  action 전에 `get_scene_state`/`get_unity_status` 로 미리 받아 둔 `reading`/`frame` 을 쓴다 —
  action 결과 envelope 의 `frame` 을 새로 노출할 필요가 없다.

## Context / Constraints

- `mcp/src/pulse.ts` 의 `PulseStore.fold()` 는 `PULSE` frame 이 성공적으로 접힐 때마다
  (변화가 없어도) `lastReadingAt` 을 갱신한다. 대기 중 새 `reading` 이 왔다는 신호가 필요하므로
  `fold()` 성공 경로에 리스너 호출을 하나 추가한다 (`onReading`). track 58 도 `pulse.ts` 를
  건드릴 수 있으므로 이 추가는 최소한으로 — 새 `Set` 필드 하나, `onReading`/구독 해제 함수
  하나, `fold()` 안의 호출 한 줄.
- `mcp/src/connection.ts` 의 `UnityConnection.handleDisconnect()` 가 소켓이 끊겼을 때
  유일하게 부르는 자리다. 여기에도 `onDisconnect` 구독을 하나 추가해, 대기 중 연결이 끊기면
  "조건 미충족" 과 다른 결과로 즉시 끝낼 수 있게 한다.
- `mcp/src/tools.ts` 는 track 58, 59 도 동시에 도구를 추가하므로 새 tool 등록은 한 덩어리로
  묶고, 기존 도구 로직은 건드리지 않는다. 유일한 예외는 `perform_actions` 설명 문구에 한 줄
  추가해 "action 성공이 게임 내 효과 성공을 뜻하지 않는다" 는 점과 `wait_for_condition` 을
  가리키는 안내를 남기는 것 — 이것이 acceptance criterion 의 "과장하지 않는다" 를 만족시키는
  자리다.
- MCP TypeScript SDK 의 tool callback 은 `(args, extra)` 형태이고 `extra.signal` 이
  `AbortSignal` 이다(`node_modules/@modelcontextprotocol/sdk/dist/esm/shared/protocol.d.ts:177`).
  클라이언트가 `notifications/cancelled` 를 보내면 이 signal 이 abort 된다 — 이것이 "취소"
  경로다.
- 새 로직은 전부 새 파일 `mcp/src/wait.ts` 에 둔다. `PulseStore`, `UnityConnection` 은 신호만
  준다.
- `timeoutMilliseconds` 는 안 주면 기본 5000, zod schema 로 최댓값 30000 을 강제한다("절대
  block 하지 않는다" 를 실수로 아주 긴 값을 줘도 지키기 위해서다).
- `instructions.ts` 의 "Reading the scene" 절이 지금 "조작 뒤 잠시 기다렸다가 reading 번호를
  비교하라" 는 수동 절차를 안내한다 — 이 issue 가 없애려는 바로 그 패턴이다. 여기에 한 줄을
  더해 `wait_for_condition` 을 그 자리에서 가리킨다. `prompts.ts` 는 고치지 않는다 — 네
  prompt 모두 여전히 유효한 절차고, 이 tool 은 그 절차를 대신하는 것이 아니라 agent 가 그
  절차 중 "잠시 기다림" 대신 쓸 수 있는 도구 하나를 추가하는 것뿐이다.
- `PulseStore.getLastUnreadableFrame()` 은 이미 존재하는 진단 정보다. `wait_for_condition` 이
  `timeout`/`disconnected` 로 끝날 때, frame 이 오긴 왔는데 접지 못해 `onReading` 이 아예
  안 불린 경우를 사람이 구분할 수 있어야 한다. `describeWaitOutcome` 의 payload 에
  `lastUnreadableFrame?: UnreadableFrame` 을 추가해 `store.getLastUnreadableFrame()` 이
  있을 때만 싣는다(`get_unity_status` 와 같은 관례).

## Approach (Checklist)

- [x] **Step 0: Recon** — `pulse.ts`(`PulseStore`, `fold`, `FoldedPulseState`), `connection.ts`
      (`handleDisconnect`, `ActionResultFrame`), `tools.ts`(`registerTools`, 기존 스타일),
      `test/pulse.test.ts`, `test/status.test.ts` 를 읽었다. 시계 주입(`PulseStore(() => clock)`)
      과 `node --test` 스타일을 확인했다.
- [x] **Step 1: Implementation**
  - `mcp/src/pulse.ts`: `PulseStore` 에 `private readingListeners = new Set<...>()` 와
    `onReading(listener: (state: FoldedPulseState) => void): () => void` 를 추가한다. 반환값은
    구독을 끊는 함수다. `fold()` 의 `PULSE` 성공 경로에서 `this.pulseState = folded;`(pulse.ts:477)
    바로 다음에 `folded.publicState` 를 인자로 리스너를 부른다 — `InternalPulseState` 자체는
    내부 자료구조라 밖으로 내보내지 않는다. fold 가 실패해 `lastUnreadableFrame` 을 남기고
    `return false` 하는 경로에서는 부르지 않는다. 그리고 pulse.ts:188 의
    `function sameValue` 앞에 `export` 를 붙여 `wait.ts` 가 같은 비교를 쓰게 한다.
  - `mcp/src/connection.ts`: `UnityConnection` 에 `onDisconnect(listener: () => void): () => void`
    와 리스너 `Set` 을 추가한다. `handleDisconnect()`(connection.ts:222) 안에서 `this.socket`
    을 비우고 pending 을 거절한 뒤 리스너를 부른다 — 재연결 예약보다 먼저 부르는 이유는, 기다리던
    쪽이 "지금 끊겼다" 를 재연결 성공 여부와 무관하게 알아야 하기 때문이다.
  - `mcp/src/wait.ts` (신규):
    - `MemberCondition { selector: string; on: string; member: string; among?: number; equals: JsonValue }`.
      `selector` 는 `PulseObject.selector` 와 **정확히 같은 문자열**일 때만 맞는다
      (`get_scene_state` 의 부분 일치 검색과 다르다). `on` 은 `PulseComponent.on` 과 정확히
      같아야 하고, `among` 은 `PulseMember.among` 과 `===` 로 비교한다 — 즉 `among` 을 안 주면
      `among` 이 없는 멤버에만 맞는다. 이것은 `pulse.ts` 의 `memberKey` 가 멤버를 구별하는
      규칙과 같다.
    - `WaitCondition { sinceReading?; sinceFrame?; scene?; memberEquals?: MemberCondition[]; timeoutMilliseconds }`.
      **주어진 조건은 전부 동시에 만족해야 한다(AND)**. 안 준 field 는 조건이 아니다.
      `sinceReading`/`sinceFrame` 은 "그 값보다 **큰**" 이다(같은 값은 아직 미충족).
    - `unmetReasons(state: FoldedPulseState | undefined, condition): string[]` — 빈 배열이면
      전부 충족. `state` 가 `undefined` 면(pulse 가 한 번도 안 왔으면)
      `["no scene reading has arrived yet; call start_readings"]` 하나만 돌려준다. 그래야
      "조건이 안 맞았다" 와 "읽을 상태 자체가 없다" 가 응답에서 갈린다.
    - `WaitOutcome` = `{ kind: "met"; state }` | `{ kind: "timeout"; state?; unmet }` |
      `{ kind: "disconnected"; state?; unmet }` | `{ kind: "cancelled"; state? }`.
    - `describeWaitOutcome(outcome, lastUnreadableFrame)` → `{ payload: WaitResponsePayload;
      isError: boolean }`, `WaitResponsePayload { met: boolean; disconnected: boolean;
      cancelled: boolean; reading?: number; frame?: number; scene?: string; unmet: string[];
      lastUnreadableFrame?: UnreadableFrame }`. `met` 은
      `met:true, disconnected:false, cancelled:false, unmet:[]`; `timeout` 은
      `met:false, disconnected:false, unmet:[...]`; `disconnected` 는 `disconnected:true`
      이고 `isError:true`; `cancelled` 는 `cancelled:true, unmet:[]`. `reading`/`frame`/`scene`
      은 어느 경우든 마지막으로 접힌 상태에서 채우고, 상태가 없으면 그 세 field 를 뺀다.
      `lastUnreadableFrame` 은 `store.getLastUnreadableFrame()` 이 있을 때만 싣는다 —
      `timeout` 이나 `disconnected` 로 끝났는데 이 값이 있으면, frame 은 오고 있었는데 접지
      못해 `onReading` 이 못 불린 것임을 사람이 알 수 있다.
    - `waitForCondition(store, connection, condition, signal)`: **먼저 현재 `store.getState()`
      로 `unmetReasons` 를 계산해, 이미 충족이면 구독도 타이머도 걸지 않고 바로 `met` 을
      돌려준다.** 그러지 않으면 `signal.aborted` 를 확인하고(이미 취소면 `cancelled`),
      `store.onReading`, `connection.onDisconnect`, `timers.setTimeout` 셋을 걸어 먼저 오는
      것으로 끝낸다. 어느 경로로 끝나든 세 구독을 모두 해제하고 `signal` 의 `abort` 리스너도
      뗀다. 한 번 끝난 뒤 다른 신호가 와도 다시 resolve 하지 않는다(`settled` 플래그).
      pulse 가 하나도 안 와도 timeout 타이머가 반드시 끝을 낸다.
  - `mcp/src/tools.ts`: `wait_for_condition` tool 을 `registerTools` 안에 한 덩어리로 등록.
    조건이 하나도 없으면(`sinceReading`/`sinceFrame`/`scene`/`memberEquals` 전부 `undefined`)
    `capture_screen`(tools.ts:511) 과 `press_key`(tools.ts:564) 가 쓰는 것과 같은 방식으로
    `{ ...text("..."), isError: true }` 를 돌려준다. `timeoutMilliseconds` 는 input schema 에
    `z.number().int().positive().max(30_000).optional()` 로 걸고, 안 주면 코드에서 5000 으로
    채운다. `connection.ensureConnected()` 를 먼저 시도해 애초에 Unity 가 없으면 기존
    `failureText` 로 답한다(이것이 "처음부터 연결 없음" 이고, `disconnected` 는 "기다리는 도중
    끊김" 이다). `describeWaitOutcome()` 의 payload 를 JSON 으로 응답한다. zod schema 는 이
    파일의 기존 factory 관례(`targetIdSchema()` 등)를 따라 `memberEqualsSchema()` 를 만든다.
    `equals` 값은 처음 계획한 recursive `JsonValue` 대신 primitive(`string`/`number`/`boolean`/
    `null`) 로 좁힌 `equalsValueSchema()` 를 쓴다 — 구현 중 recursive `z.lazy` 가
    `zod-to-json-schema` 를 무한 재귀로 죽이는 것을 `schema.test.ts` 가 실제로 잡았기 때문이다
    (아래 Plan review notes 참고). `perform_actions` description 에 한 줄 추가.
  - `mcp/src/instructions.ts`: "Reading the scene" 절 마지막에 한 줄 추가 — 조작 뒤 값이나
    `scene` 이 바뀌기를 그냥 기다리지 말고 `wait_for_condition` 을 부르라는 안내.
- [x] **Step 2: Tests**
  - `mcp/test/wait.test.ts` (신규): 주입 가능한 `timers`(`node:test` 의 fake timer 또는 수동
    콜백 캡처)로 (1) 이미 조건이 met 인 즉시 반환, (2) `PulseStore.fold()` 가 조건을 만족시키는
    reading 을 내면 timeout 전에 met 으로 끝남, (3) 아무 pulse 도 안 와도 timeoutMilliseconds
    뒤 `timeout` 으로 끝나고 `unmet` 에 이유가 있음, (4) `onDisconnect` 호출 시 `disconnected`
    로 끝나고 `unmet` 에 상태가 실림, (5) `AbortSignal.abort()` 호출 시 `cancelled` 로 끝나고
    이후 pulse 나 timeout 이 와도 다시 resolve 하지 않음(정리 확인), (6) `sinceReading`/
    `sinceFrame`/`scene`/`memberEquals` 각각과 조합, (7) `memberEquals` 의 `among` 유무 매칭.
    실제 시간을 재우지 않는다 — `timers` 를 가짜로 주입해 콜백을 직접 호출한다.
  - 기존 `pulse.test.ts` 에 `onReading` 이 성공한 fold 마다(변화 없어도) 불리는 것과, 실패한
    fold 에는 안 불리는 것을 한두 케이스 추가.
  - 기존 `connection.test.ts` 에 `onDisconnect` 가 소켓이 끊겼을 때 불리는 것을 한 케이스
    추가.
  - `mcp/test/tools.test.ts` 또는 신규 파일에 `wait_for_condition` tool 등록 자체보다
    `describeWaitOutcome` 의 payload 모양(각 kind 별 `met`/`disconnected`/`unmet`)을 검증.
- [x] **Step 3: Rollout / Rollback** — 새 tool 이라 기존 계약을 바꾸지 않는다. 문제가 생기면
  `wait.ts` 와 tool 등록, 그리고 `pulse.ts`/`connection.ts` 의 두 훅을 되돌리는 revert 하나로
  롤백된다. feature flag 는 필요 없다 — 호출하지 않으면 아무 영향이 없다.

## Validation

- **Commands to run:**
  ```bash
  unset -f node npm npx
  export PATH=/home/yunseong/.nvm/versions/node/v24.18.0/bin:$PATH
  cd /home/yunseong/dev/unity-play-mcp-60/mcp
  npm run build && npm test
  ```
- **Expected output:** `tsc` 가 타입 오류 없이 끝나고, `node --test` 전체가 통과.

## Risks & Rollback

- **Risks:**
  - `onReading` 리스너가 매 `reading` 마다(초당 열 번) 호출되므로, 대기가 오래 걸리는 동안
    listener Set 순회 비용이 있다 — 대기 개수가 적을 것으로 가정하고 별도 최적화는 하지 않는다.
  - `memberEquals` 의 `selector` 매칭은 정확히 일치하는 문자열만 본다(`get_scene_state` 의
    부분 일치 검색과 다르다) — 문서화해 혼동을 막는다.
  - track 58/59 가 `tools.ts`/`pulse.ts` 에 동시에 다른 내용을 추가하면 병합 시 충돌이
    예상된다. 이 PR 본문에 명시한다.
  - 실제 Unity 세션 없이 검증하므로, `AbortSignal` 이 실제 MCP client 취소 요청에서 정확히
    전달되는지는 SDK 계약을 읽고 타입으로만 확인했다 — 통합 검증은 CI/수동 몫으로 남긴다.
- **Rollback steps:** 이 브랜치의 커밋을 되돌리거나 PR 을 닫는다. `Packages/` 를 건드리지
  않았으므로 Unity 쪽 되돌릴 것이 없다.

## Open Questions

없음. `memberEquals` 가 `active`/`deactive` 양쪽을 다 뒤지는지는 이미 결정했다 — 양쪽 다
본다(대기 대상 객체가 지금 비활성일 수도 있기 때문). 실제 사용에서 문제가 되면
`includeInactive` 같은 옵션을 추가로 논의한다.

## Plan review notes

`plan-review` skill 을 한 번 돌렸다(fast + medium). fast 가 낸 must-fix 네 가지 — 기본/최대
`timeoutMilliseconds`, AND 의미론 명시, `instructions.ts`/`prompts.ts` 검토,
`lastUnreadableFrame` 처리 — 를 모두 위 Context/Constraints 와 Step 1 에 반영했다. medium
review 는 이 계획이 이미 새 파일 하나(`wait.ts`)와 기존 두 클래스에 각각 훅 하나씩만 더하는
크기라 추가로 걷어낼 추상화를 찾지 못했다 — `PulseStore`/`UnityConnection` 에 별도 event bus
라이브러리를 넣거나 `wait.ts` 를 더 쪼개는 것은 이 issue 하나짜리 기능에 과하다고 판단해
제안하지 않았다. heavy review 는 별도로 돌리지 않고 이 계획을 그대로 구현으로 넘긴다 —
task 지침이 review 루프를 한 번으로 제한한다.

구현 중 하나 발견: `memberEquals[].equals` 를 recursive `JsonValue` 로 받으려던 첫 설계가
`zod-to-json-schema` 를 무한 재귀로 죽였다(`schema.test.ts` 의 두 test 가 "Maximum call stack
size exceeded" 로 그것을 그대로 잡았다). primitive 로 좁힌 `equalsValueSchema()` 로 고쳤다 —
Step 1 과 Risks 에 반영했다.

`pair-review` skill(critic subagent)을 구현 뒤 한 번 돌렸다. `VERDICT: PASS`. should-fix
하나 — timeout 경로 test 가 `connection.listenerCount === 0` 을 확인하지 않던 것 — 를
`mcp/test/wait.test.ts` 에 반영했다. 나머지는 지적할 것이 없다는 평가였다.
