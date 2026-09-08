# 2026-09-08 — PULSE component 의 멤버를 `m` 에서 읽는다

- Date: 2026-09-08
- GitHub Issue: #19
- Status: Reviewed (fast · medium · heavy 반영 완료)
- Repository: unity-play-mcp
- Work Type: fix

## Goal

게임이 보내는 component 멤버 값이 MCP 서버에 도착하게 한다.

Unity 는 component 를 `{"on":<타입>,"m":[...]}` 로 쓰는데(`LiveState.cs:669`) MCP 는
`component.members` 를 읽는다(`mcp/src/pulse.ts:13`). `component.members` 가 `undefined` 라
`mergeMembers` 의 `for (const member of incoming)` 이 `TypeError` 를 던지고,
`PulseStore.fold` 의 `catch` 가 그 reading 전체를 버린다. 그래서 멤버 값이 한 번도 도착하지
않고, `get_scene_state` 는 값 없는 응답만 낸다.

끝난 상태:

1. `m` 을 실은 PULSE frame 이 접히고, `get_scene_state` 응답의 `active[].by[].members` 에
   실제 값이 실린다.
2. `mergeMembers` 는 멤버 목록이 없어도 던지지 않는다.
3. 멤버가 없는 component 와 멤버가 다른 키에 실려 온 component 를 가려 보고한다.
   `Ignored malformed PULSE frame` 한 줄로 뭉치지 않는다.
4. `mcp/test/*.test.ts` 의 fixture 가 `LiveState` 가 실제로 내는 모양을 쓴다.

## Non-goals

- **Unity package 수정.** `m` 은 `fd16e70` 이 크기를 줄이려고 고른 이름이다 — component 하나에
  6 B, 문서 하나에 component 316개, reading 은 초당 열 번. 되돌리면 그 commit 이 줄인 것을
  도로 늘린다.
- **`PulseStore` 안의 필드 이름 변경.** store 는 계속 `members` 를 쓴다. #18 과 #16 이 같은
  store 를 동시에 고치고 있어 이름을 바꾸면 부딪힌다.
- **end-to-end 실행 harness.** `LiveState` 가 낸 문서를 Unity test 에서 찍어 MCP test 의
  fixture 로 쓰는 일은 issue 본문의 Follow-up 이 적어 둔 별도 작업이다.
- **`.agents/handoffs/LATEST.md` 의 wire 명세 수정.** 다른 worktree 의 untracked 사용자
  문서다. 읽기만 한다.
- **`mcp/src/tools.ts`, `mcp/src/index.ts` 수정.** 보고 경로는 PR #49 가 이미 놓은
  `lastUnreadableFrame` → `describeStatus` 를 그대로 쓴다.

## Context / Constraints

### 게임이 실제로 쓰는 모양

`LiveState.cs:568`~`669` 이 객체 하나에 쓰는 것:

```json
{"id":41,"path":"Canvas/Card","selector":"Canvas/Card",
 "by":[{"on":"WordVenture.Card","m":[
   {"member":"count","value":3},
   {"member":"slot","among":1,"asked":false,"value":"one"}]}]}
```

- `on` 은 `type.FullName`.
- `m` 은 그 component 가 이번 reading 에 실은 멤버들. component 는 `count > 0` 일 때만
  쓰이므로 게임은 멤버 없는 component 를 아예 내지 않는다.
- 멤버 하나는 `member`, 그리고 `among`(둘째 이후 component 일 때만), `asked`(`false` 일 때만),
  그리고 값 하나 — `value` 이거나 `count` 이거나 `unread`.

`m` 말고 component 가 실어 보내는 키는 `on` 하나뿐이다. 이 사실이 아래 3번 판정의 근거다.

### 고칠 자리

frame 이 `PulseStore` 로 들어오는 자리에서 한 번 옮긴다. `foldInternal` 이 reading 번호를
확인한 직후이고, `mergeObject`/`mergeComponents` 보다 앞이다. 그 아래는 전부 `members` 만
안다.

`readComponents` 는 `pulse.active` 와 `pulse.deactive` 의 객체를 돌고 그 안에서 `by` 를
돈다. 그래서 component 를 볼 때 그것을 실은 객체가 손에 있고, 오류 message 에 쓸
`object.path` 도 거기서 나온다. component 만 따로 받는 함수가 아니다.

`connection.ts` 가 아니라 `pulse.ts` 에 두는 이유:

- `pulse.ts` 가 frame 의 모양을 아는 유일한 곳이다. `connection.ts` 는 transport 다.
- `PulseStore.fold` 를 직접 부르는 test 가 `m` 을 실은 fixture 를 그대로 쓸 수 있다. 이것이
  acceptance criteria 4번이 요구하는 것이다 — fixture 가 명세가 아니라 게임이 내는 모양을
  쓰려면 그 모양이 store 를 통과해야 한다.

### 더 작은 대안을 버린 이유

`mergeComponents` 안에서 `component.m ?? component.members` 로 읽으면 함수 하나를 안 만들어도
된다. 두 가지를 잃는다.

1. **`m` 이 접힌 상태에 그대로 남는다.** `mergeComponents` 는 component 를
   `{ ...component, members: ... }` 로 만든다(`pulse.ts:210`, `:213`). 원본 키를 전부 펼치므로
   `m` 과 `members` 가 같은 멤버 목록을 두 벌 들고 `FoldedPulseState` 에 앉고, 그대로
   `get_scene_state` 응답에 실린다. component 316개짜리 문서에서 응답 크기가 두 배가 된다.
2. **frame 하나를 통틀어 몇 개가 어긋났는지 셀 수 없다.** 판정이 component 마다 흩어지므로,
   개수와 첫 예를 담은 message 를 만들려면 `mergeComponents` → `mergeObject` →
   `indexObjects` 로 누산기를 꿰어야 한다. 함수 하나가 그것보다 작다.

### `PulseComponent.members` 를 optional 로 바꾸는 것

호출부는 test 뿐이다. `tools.ts` 는 `active` 를 통째로 `JSON.stringify` 해서 내보내므로
(`tools.ts:365`~`384`) 이 필드를 타입으로 읽지 않는다. `tree.ts` 도 `by` 를 만지지 않는다.
그래서 optional 로 내려도 깨지는 소비자가 없다.

### 세 가지 component 를 가른다

| 들어온 것 | 판정 | 결과 |
|---|---|---|
| `{on, m:[...]}` | 정상 | `members` 로 옮긴다 |
| `{on}` | 멤버 없는 component | 그대로 접는다. 이전에 들고 있던 멤버는 남는다 |
| `{on, <다른 키>}` | 키가 어긋났다 | 던진다. 어느 component 의 어느 키인지 이름을 대고 |

`{on}` 을 실패로 만들지 않는 이유는 그것이 "이번 reading 에 이 component 는 아무 말도 안
했다" 와 구별할 수 없는 모양이고, 실패로 만들면 그 한 component 가 나머지 315개를 함께
버리기 때문이다.

`{on, <다른 키>}` 를 던지는 것으로 두는 이유는 그 상황이 component 하나가 아니라 전부에
걸리기 때문이다. 키가 또 바뀌면 316개 전부가 멤버 없이 도착하므로, 값 없는 상태를 조용히
받아 두는 것보다 "이 reading 을 못 읽었고 이유는 이것" 이라고 말하는 편이 낫다. `fold` 의
`catch` 가 그 message 를 `lastUnreadableFrame.reason` 에 넣는다.

**그 문장이 닿는 곳은 `get_unity_status` 하나뿐이다.** `describeStatus` 를 부르는 자리는
`tools.ts:424` 와 `:430` 두 곳이고 둘 다 `get_unity_status` 안이다. `get_scene_state` 의 본체
`stateResponse`(`tools.ts:299`~`385`)는 `lastUnreadableFrame` 을 읽지 않는다. 그래서 키가 또
어긋나면 `get_scene_state` 는 **마지막으로 잘 접힌 상태를 아무 표시 없이 그대로 낸다** —
agent 가 씬이 안 움직이는 것을 눈치채고 `get_unity_status` 를 불러야 이유를 본다.
`get_scene_state` 응답에도 그 문장을 붙이려면 `tools.ts` 를 고쳐야 하고, 그것은 이 PR 의 write
scope 밖이라 follow-up 이다.

지금과 달라지는 것은 **message** 다. 지금은 `for (const member of undefined)` 가 낸
`TypeError: incoming is not iterable` 이 들어가 아무것도 가리키지 않는다. 앞으로는 component
몇 개가 어떤 키를 들고 왔는지가 들어간다.

### 타입

`PulseComponent.members` 를 optional 로 바꾼다. 지금 필수로 선언된 것이 이 버그의 절반이다 —
socket 에서 오는 값에 타입이 있다고 우겨서, 없을 때 `for ... of undefined` 하나가 reading
전체를 지웠다. `mergeMembers` 도 `previous`/`incoming` 을 `| undefined` 로 받는다.

`PulseComponent` 를 wire 용과 store 용 둘로 나누지 않는다. `PulseObject` 와 `PulseFrame` 에
index signature 가 있어 `Omit` 이 `by` 를 실제로 떼지 못하고, 키 하나 때문에 타입 네 개가
늘어난다.

### 비용

`readComponents` 가 reading 마다 새 객체를 만든다 — 객체당 하나, component 당 하나. 객체는
`mergeObject` 가 이미 매번 spread 하므로 늘어나는 것은 component 쪽이고, `by` 가 없는 객체는
그대로 돌려보내 복사하지 않는다.

## Approach (Checklist)

- [x] **Step 0: Recon**
  - `LiveState.cs:568`~`669` 이 쓰는 키를 확인한다 — `by`, `on`, `m`, `member`, `among`,
    `asked`, `value`/`count`/`unread`.
  - `mcp/src/pulse.ts` 의 `mergeMembers`/`mergeComponents`/`foldInternal`/`fold`,
    `mcp/src/tools.ts:202` 의 `describeStatus` 를 읽는다.
  - `.agents/handoffs/LATEST.md:81` 의 명세가 `members` 로 적혀 있다는 것만 확인하고 고치지
    않는다.

- [x] **Step 1: Implementation** — `mcp/src/pulse.ts` 한 파일.
  - `PulseComponent.members` 를 optional 로 바꾸고, 왜 optional 인지 주석으로 남긴다.
  - `mergeMembers` 가 `previous`/`incoming` 을 `readonly PulseMember[] | undefined` 로 받는다.
    `previous ?? []` 로 map 을 세우고 `incoming ?? []` 를 돈다. 그래서 멤버 목록이 없는
    component 는 **이전에 들고 있던 멤버를 지우지 않는다** — 시작이 `previous` 이고 덮어쓸
    것이 하나도 없기 때문이다. 처음 보는 component 면 `members` 가 빈 배열이 된다.
  - `const WIRE_MEMBERS = "m";` 와 `readComponents(pulse: PulseFrame): PulseFrame` 를
    더한다. `active` 와 `deactive` 의 객체를 돌며 `by` 의 component 마다 `m` 을 `members` 로
    옮기고 `m` 은 뗀다. `m` 이 없고 `on` 말고 다른 키가 있는 component 를 모아, 하나라도
    있으면 개수·전체 component 수·첫 component 의 `on` 과 그것을 실은 객체의 `path`·그 키
    이름들을 담은 `Error` 를 던진다.
  - `foldInternal` 이 reading 번호를 확인한 뒤 `readComponents` 를 부른다.

- [x] **Step 2: Tests** — 바꾸는 것은 거의 전부 **들어오는 frame 을 만드는 fixture** 다.
  접힌 상태를 보는 assertion 이 읽는 필드 이름은 `members` 그대로이고, optional chain 하나만
  더한다.
  - `mcp/test/pulse.test.ts:57` 의 `members[0]` 을 `members?.[0]` 으로 고친다.
    `PulseComponent.members` 를 optional 로 내린 결과이지 assertion 의 뜻이 바뀌는 것이
    아니다. `tsconfig.json` 이 `strict: true` 이고 `include` 에 `test/**/*.ts` 가 있어
    이것을 두면 `tsc` 가 TS18048 로 멈추고, `npm test` 는 `dist` 를 돌리므로 test 가 아예
    안 돈다. `pulse.test.ts:45` 와 `history.test.ts` 는 이미 `?.members` 라 그대로다.
  - `mcp/test/pulse.test.ts`: `object()`(line 12), `delta merges members ...`(line 34, 40),
    `mcp/test/history.test.ts`: `object()`(line 18), `members that differ only by
    among ...`(line 141, 146) — 이 다섯 자리의 `members:` 를 `m:` 으로 바꾼다. 게임이 내는
    모양대로 `among` 은 그대로 두고, `asked: false` 를 쓰는 멤버를 최소 한 자리에 넣는다.
  - `mcp/test/pulse.test.ts`: `m` 을 실은 reading 이 접히고 `by[0].members` 에 값이 앉으며
    `m` 은 남지 않는 test.
  - `mcp/test/pulse.test.ts`: `by: [{ on: "Widget" }]` 만 온 reading 이 성공하고 이전 reading
    의 멤버가 그대로 남는 test.
  - `mcp/test/pulse.test.ts`: 기존 `objectWithoutMembers`(line 109~116) 를
    `componentWithUnexpectedKey` 로 이름을 바꾸고, `by: [{ on: "Widget", members: [{ member:
    "value", value: 1 }] }]` 를 내게 한다. line 106~108 의 주석도 함께 고친다 — 지금 "`members`
    없는 component" 라고 적혀 있는데 새 fixture 는 `members` 를 들고 온다.
    `members` 를 어긋난 키의 예로 쓰는 것은 의도한 것이다: #19 가 실제로 만든 모양이고,
    "`m` 이 있는지" 가 아니라 "`members` 가 있는지" 로 판정하는 순진한 수정이 들어오면 이
    test 가 잡는다.
    두 test(`a frame that cannot be folded ...`, `a successful reading clears ...`) 는 그대로
    통과해야 한다.
  - `mcp/test/pulse.test.ts`: 그 실패의 `reason` 이 `"m"` 과 어긋난 키 이름을 담는 test.
  - `mcp/test/status.test.ts`: `PulseStore` 가 낸 `lastUnreadableFrame` 을
    `describeStatus` 에 넣었을 때 사람이 읽는 문장에 component 이름과 `"m"` 이 남는 test.

- [x] **Step 3: Rollout / Rollback**
  - `mcp/` 안의 순수 코드 변경이고 migration 도 flag 도 없다. commit 하나를 revert 하면
    되돌아간다.
  - Unity package 는 손대지 않으므로 게임 쪽 배포 순서가 없다.

## Validation

- **Commands to run:**
  ```bash
  export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH"
  cd mcp && npm ci && npm run build && npm test
  ```
- **Expected output:** `tsc` 무오류, `node --test` 전부 통과. 새 test 는 `m` 을 실은 reading
  이 접힌다는 것과, 키가 어긋난 reading 의 `reason` 이 어느 component 의 어떤 키인지 말한다는
  것을 본다.
- **실제 수행:** `npm ci` 무오류, `npm run build` 무오류, `npm test` 119 pass / 0 fail.
  변경 전 baseline 은 같은 명령으로 114 pass / 0 fail 이었으므로 늘어난 다섯이 새 test 다.
- **하지 않는 것:** 돌고 있는 Unity 에 붙여 보는 확인. 이 작업 환경에 Unity 에디터가 없다.
  그래서 이 PR 이 대는 증거는 전부 fixture 이고, fixture 는 `LiveState.cs` 를 읽고 손으로
  옮긴 것이다. 같은 종류의 어긋남을 다시 놓치지 않으려면 issue 본문의 Follow-up 이 필요하다.

## Risks & Rollback

- **Risks:**
  - fixture 가 여전히 손으로 쓴 것이다. `LiveState.cs` 를 읽고 옮겼지만 실행해서 찍은 것이
    아니므로, 이 PR 은 `m` 이라는 키 하나가 맞다는 것만 증명하고 문서 전체가 맞다는 것은
    증명하지 않는다.
  - 멤버 값의 키(`value` / `count` / `unread`)는 이 작업에서 확인만 하고 손대지 않는다.
    `PulseMember.value` 가 필수로 선언돼 있어 `count` 나 `unread` 만 실린 멤버는 타입과
    어긋나지만, `record` 는 `member.value` 가 `undefined` 여도 던지지 않고 그대로 이력에
    넣는다. 별도 issue 감이다.
  - `{on, <다른 키>}` 를 실패로 두는 판단은 "키가 어긋나면 전부 어긋난다" 는 전제에 기댄다.
    한 component 만 어긋나는 미래가 오면 그 하나가 reading 전체를 버린다. 그때는 개수를 보고
    부분 수용으로 바꾸면 된다 — `readComponents` 가 이미 개수를 세고 있다.
  - **실패 이유가 `get_scene_state` 에는 안 실린다.** `stateResponse` 가
    `lastUnreadableFrame` 을 읽지 않으므로(`tools.ts:299`~`385`), 키가 또 어긋나면
    `get_scene_state` 는 낡은 상태를 아무 표시 없이 낸다. 이유를 보려면 `get_unity_status`
    를 불러야 한다. 그 문장을 `get_scene_state` 응답에도 붙이는 것은 `tools.ts` 를
    건드리므로 이 PR 의 write scope 밖이고, follow-up issue 감이다.
- **Rollback steps:** `git revert <commit>`.

## Open Questions

- 없음.

## Pair review

`pair-review-critic` 이 `VERDICT: PASS` 를 냈고, `npm run build` 와 `npm test` 를 스스로 다시
돌려 118 pass 를 확인했다. 막지 않는 지적 하나를 받아들였다:

- `{on, m: <배열이 아닌 값>}` 이 오면 `Array.isArray` 가 걸러 `unread` 로 가는데, `otherKeys`
  가 `m` 을 걸러내 버려 빈 배열이 되고, 결국 "멤버가 없는 component" 로 조용히 넘어간다.
  값이 실려 왔는데 아무도 그것을 못 읽는 것이라 이 issue 가 없애려는 바로 그 조용함이다.
  `m` 이 있는데 배열이 아니면 `otherKeys` 에 `m` 을 남기도록 고치고 test 를 하나 더했다
  (`npm test` 119 pass).

## Rejected feedback

- **fast: "`readComponents(pulse: PulseFrame)` 는 component 의 부모를 몰라서 `path` 를 오류
  message 에 넣을 수 없다."** 사실이 아니다. 이 함수는 `pulse.active`/`pulse.deactive` 의
  객체를 돌고 그 안에서 `by` 를 돌므로 객체가 손에 있다. 다만 plan 이 그렇게 읽히지 않았으니
  "고칠 자리" 에 한 문단을 더해 명시했다.
