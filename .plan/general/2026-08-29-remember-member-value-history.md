# 2026-08-29 — MCP 가 값의 변화 이력과 파괴를 기억한다

- Date: 2026-08-29
- GitHub Issue: #13
- Status: Complete

## Goal

`PulseStore` 가 멤버마다 최근 변경 10개를 들고, 파괴된 객체를 지우는 대신 tombstone 으로
남긴다. agent 가 "지금 30" 이 아니라 "80 에서 30 으로 떨어졌다" 를, 그리고 "없다" 가 아니라
"있었는데 `reading` 47 에서 사라졌다" 를 읽을 수 있게 한다.

## Non-goals

- Unity package 수정. 게임은 이미 0.1초마다 읽어 1초마다 모아 보내고 (`Pulse.cs:62`,
  `Pulse.cs:72`), `reading` 을 병합하지 않고 그대로 전달한다. 받는 쪽이 버리고 있을 뿐이다.
- hierarchy fold (`root`, `depth`) — 별도 issue.
- 주목 대상 추적 (`watch_members`, `get_changes`) — 별도 issue.
- 보이는 요소 filter — PULSE 가 `Text`·`Image`·`Slider` 를 담고 있는지 확인이 먼저다.

## Context / Constraints

- 현재 `mergeMembers` 는 `Map.set` 으로 값을 덮어쓰기만 한다 (`mcp/src/pulse.ts:110`).
  이전 값이 어디에도 남지 않는다.
- 현재 `indexObjects` 는 `gone` 키를 `objects.delete()` 로 지운다 (`mcp/src/pulse.ts:158`).
- `whole` 은 주기적이지 않다. `LiveState.cs:95` 기준으로 첫 `reading`, scene 변경, 그리고 전달
  유실 뒤 복구(`repair`) 세 경우에만 온다. **`repair` 때문에 `whole` 에서 이력을 지우면 안
  된다** — 소켓이 잠깐 끊겼다는 이유로 멀쩡한 이력이 날아간다.
- 그래서 이력은 `HeldObject` 안이 아니라 `InternalPulseState` 수준의 별도 map 에 둔다.
  `whole` 은 객체 map 을 통째로 새로 만들기 때문에 안에 두면 살아남은 키의 이력을 지킬 수
  없다.
- `settled` 인 `reading`(아무것도 안 움직인 것)은 게임이 아예 보내지 않는다 (`Pulse.cs:243`).
  그래서 "변경 10개" 는 시계가 아니라 그 값이 실제로 움직인 마지막 10번이다.
- 객체 동일성은 `scene + "/" + selector`, 멤버 동일성은 `member + among` — 기존 규칙 그대로.
- `fold` 는 순수 함수로 유지한다. 게임 없이 `node --test` 로 덮을 수 있어야 한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 완료. `mcp/src/pulse.ts` 의 `mergeMembers`·`indexObjects`·
      `foldInternal`·`toPublicState`, 그리고 Unity 쪽 `Pulse.cs` 의 두 박자와
      `LiveState.cs` 의 `whole` 조건을 읽었다.

- [x] **Step 1: 이력 자료구조** — `InternalPulseState` 에
      `history: Map<string, MemberHistory>` 를 더한다. 키는
      `objectKey \0 component.on \0 memberKey` 로 세 층을 이어 붙인다. 값은
      `{ value, reading, frame }` 의 배열이고 상한은 `HISTORY_DEPTH = 10`.
      상한 상수 둘(`HISTORY_DEPTH`, `TOMBSTONE_LIMIT`)을 파일 맨 위에 모은다.

- [x] **Step 2: 변경 감지** — `mergeMembers` 가 이력 map 을 함께 받아, 들어온 멤버가 이전
      값과 다를 때만 이력에 밀어 넣는다. 같은 값이 다시 오면 칸을 쓰지 않는다. 처음 나타난
      멤버는 첫 칸을 얻는다 — 그래야 "한 번도 안 움직였다" 와 "추적된 적 없다" 가 갈린다.
      값 비교는 `JSON.stringify` 로 한다. `JsonValue` 라 구조가 게임이 만든 그대로 일관되고,
      깊은 비교를 따로 쓸 만큼 모양이 자유롭지 않다.

- [x] **Step 3: `whole` 이 이력을 지키게 한다** — `replace` 가 참일 때 이력을 통째로 버리지
      않는다. scene 이 바뀌었으면 전부 버리고, 그 밖의 `whole`(첫 `reading`·`repair`)이면
      이번 `reading` 에 있는 키의 이력만 남기고 나머지를 버린다. `whole` 이 데려온 값이 마지막 이력 값과
      다르면 그것도 변경으로 쌓는다 — 유실 구간의 중간 단계는 잃어도 "움직였다" 는 남는다.

- [x] **Step 4: tombstone** — `indexObjects` 가 `gone` 키를 지우는 대신
      `tombstones: Map<string, { object, goneAtReading }>` 로 옮긴다. 마지막으로 알던 객체
      상태를 그대로 들되 이력은 함께 버린다. `TOMBSTONE_LIMIT` 을 넘으면 오래된 것부터
      버린다 (삽입 순서 = `Map` 순회 순서). 같은 키가 되살아나면 tombstone 에서 뺀다.
      scene 이 바뀌면 전부 비운다.

- [x] **Step 5: 공개 상태에 싣기** — `FoldedPulseState` 에 `gone: GoneObject[]` 를 더하고
      `toPublicState` 가 채운다. 기본 응답에 tombstone 은 표시로 나오되 이력은 없다.
      `PulseStore` 에 `getObjectHistory()` 를 더해 `stateResponse` 가 `includeHistory` 일
      때만 읽어 간다.

- [x] **Step 6: `get_scene_state` 인자** — `mcp/src/tools.ts` 의 inputSchema 에
      `includeHistory: z.boolean().optional()` 을 더한다. 기본 응답 모양은 지금과 같게 두고,
      켰을 때만 멤버마다 이력을 펼친다. 기존 호출이 갑자기 열 배로 커지지 않게 한다.

- [x] **Step 7: `foldPulseState` 제거** — 부르는 곳이 없는 export 다 (`mcp/src/pulse.ts:206`).
      공개 상태를 거쳐 내부 상태를 되돌리는 경로라 이력을 조용히 잃는다. 남겨 두면 새 기능을
      우회하는 잘못된 입구가 된다.

- [x] **Step 8: Tests** — `mcp/test/history.test.ts` 를 새로 두고 아래를 담는다. 기존
      `pulse.test.ts` 는 접기 규칙을, 새 파일은 이력과 tombstone 을 맡는다.

- [x] **Step 9: 최종 검토** — 전체 diff 를 scope 와 churn 기준으로 읽고, `README` 나
      `mcp/README.md` 가 `get_scene_state` 인자를 문서화하고 있으면 함께 고친다.

## Validation

- **Commands to run:** `cd mcp && npm run build && npm test`
- **Expected output:** 기존 17개가 그대로 통과하고, 아래 새 test 가 붙어 전부 green.

새 test 가 덮는 것:

| test | 고정하는 것 |
| --- | --- |
| 값이 바뀌면 이력에 쌓인다 | 기본 동작 |
| 같은 값이 다시 와도 칸을 안 쓴다 | 잡음이 이력을 밀어내지 않는다 |
| 11번째 변경이 가장 오래된 것을 밀어낸다 | ring buffer 상한 |
| 처음 나타난 멤버가 첫 칸을 얻는다 | "안 움직임" 과 "추적 안 됨" 이 갈린다 |
| `whole` 뒤에도 살아남은 키의 이력이 남는다 | `repair` 가 이력을 못 지운다 |
| `whole` 이 데려온 다른 값이 변경으로 쌓인다 | 유실 구간의 사실이 남는다 |
| scene 이 바뀌면 이력과 tombstone 이 비워진다 | 다른 scene 의 객체와 안 섞인다 |
| `gone` 이 삭제가 아니라 tombstone 이 된다 | 파괴 표시 |
| tombstone 상한을 넘으면 오래된 것부터 버려진다 | 메모리 상한 |
| 파괴된 키가 되살아나면 tombstone 에서 빠진다 | 되살아남 |
| `among` 이 다른 멤버가 각자 이력을 갖는다 | 멤버 동일성이 이력에도 적용된다 |
| `whole` 이 더 말하지 않는 키의 이력은 버린다 | 이력이 무한정 남지 않는다 |
| 다시 접어도 이미 읽어 간 이력은 그대로다 | 접기가 이전 상태를 건드리지 않는다 |

## Risks & Rollback

- **Risks:**
  - 이력이 메모리를 늘린다. 상한은 `멤버 수 × 10` 이라 유계지만, 감시 멤버가 수천 개인
    게임에서 실제로 얼마나 되는지 재 본 적이 없다. 실측은 이 작업 범위 밖이다.
  - `JSON.stringify` 비교는 키 순서에 민감하다. 게임이 같은 멤버를 매번 다른 키 순서로
    직렬화하면 안 움직인 값이 변경으로 잡힌다. `LiveState` 가 고정된 순서로 쓰므로
    지금은 문제가 아니지만, 그 가정에 기대고 있다는 것을 적어 둔다.
  - `FoldedPulseState` 에 `gone` 이 붙어 공개 모양이 커진다. 더하기만 하는 변경이라
    기존 독자는 깨지지 않는다.
- **Rollback steps:** 단일 commit 이므로 `git revert` 한다. Unity 쪽을 건드리지 않아
  게임 재빌드가 필요 없다.

## Open Questions

- 없음. 이력 상한(개수 10칸), tombstone 상한(개수), 응답 모양(기본 현재값·옵션 이력)은
  사용자가 정했다.

## Outcome

`mcp/` 만 바뀌었다. Unity package 는 한 줄도 건드리지 않았다 — 게임은 이미 필요한 것을
전부 보내고 있었고, 버리고 있던 것은 받는 쪽이었다.

### 검증 결과

| 대상 | 결과 |
| --- | --- |
| `tsc` | 성공 |
| `node --test` | 30 passed · 0 failed (기존 17 + 새로 13) |

### 계획에서 달라진 것 둘

1. **이력 배열을 제자리에서 밀지 않고 갈아 끼운다.** 처음에는 `push` 와 `splice` 로 쓰고
   접기마다 이력 map 을 깊게 복사했다. 감시 멤버가 수천 개면 초당 열 번 도는 자리에서
   `멤버 수 × 10` 개를 매번 복사하게 된다. 배열을 갈아 끼우도록 바꾸니 map 을 얕게만
   복사하면 되고, 움직이지 않은 멤버의 배열은 이전 상태와 그대로 나눠 쓴다. 접기가 이전
   상태를 건드리지 않는다는 것은 test 로 고정했다.
2. **`getHistory()` 를 넣었다가 뺐다.** 부르는 곳이 없었다. `foldPulseState` 를 지운 것과
   같은 이유로 남길 수 없었다.

### 남은 것

- 이력이 실제로 메모리를 얼마나 먹는지 재 보지 않았다. 상한은 `멤버 수 × 10` 으로 유계지만
  실측은 없다.
- `sameValue` 는 `JSON.stringify` 비교라 키 순서에 민감하다. `LiveState` 가 고정된 순서로
  쓰므로 지금은 맞지만, 그 가정에 기대고 있다.
- `tools.ts` 의 `historyOf` 는 `includeHistory` 일 때만 돈다. 켠 응답이 실제로 얼마나 커지는지
  돌고 있는 게임에 대고 재 본 적이 없다.
