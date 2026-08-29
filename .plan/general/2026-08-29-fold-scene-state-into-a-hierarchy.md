# 2026-08-29 — get_scene_state 가 hierarchy 를 접고 펼친다

- Date: 2026-08-29
- GitHub Issue: #15
- Status: Complete

## Goal

`get_scene_state` 가 `root` 와 `depth` 를 받아 Unity Editor 의 hierarchy 처럼 접힌 tree 를
낸다. 접힌 노드는 그 아래에 객체가 몇 개인지와, 그 아래 어딘가에서 값이 마지막으로 움직인
`reading` 번호를 함께 낸다. agent 는 그 번호를 자기가 마지막으로 본 것과 견주어 펼칠지
정한다.

## Non-goals

- Unity package 수정. `path` 가 이미 hierarchy 전체를 담고 있다.
- 접기·펼치기 상태를 서버에 두는 것.
- 주목 대상 추적 (`watch_members`, `get_changes`).
- 화면에 보이는 UI 요소 — #16. 그쪽은 Unity scan 을 넓혀야 한다.

## Context / Constraints

- PULSE 의 객체는 씬 hierarchy 의 **성긴 부분집합**이다. `Worth` 가 대부분을 걸러내므로
  `Canvas/Panel/Row/Button` 은 있는데 `Canvas/Panel` 과 `Canvas/Panel/Row` 에는 객체가 없을
  수 있다. tree 는 객체 없는 중간 마디도 구조로 세워야 한다.
- `path` 를 `/` 로 쪼갠다. GameObject 이름에 `/` 가 들어가면 깨지므로, 그 경우 어떻게 되는지를
  test 로 고정하고 문서에 적는다.
- 접힌 노드의 "이 아래 변경 있음" 은 #13 이 넣은 이력의 `reading` 을 subtree 최댓값으로 접어
  답한다. 새 저장소를 만들지 않는다.
- **boolean 이 아니라 `reading` 번호를 낸다.** boolean 은 "최근" 의 기준을 서버가 정하게
  만드는데 그 기준은 agent 마다 다르다. 번호를 주면 agent 가 자기가 마지막으로 본 것과
  견준다.
- `root` 도 `depth` 도 없으면 지금과 똑같은 평평한 응답을 낸다. 기존 호출이 깨지지 않는다.
- tree 를 만드는 코드는 `mcp/src/tree.ts` 에 순수 함수로 둔다. 게임 없이 `node --test` 로
  덮는다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `mcp/src/tools.ts` 의 `stateResponse`, `mcp/src/pulse.ts` 의
      `FoldedPulseState` 와 `getObjectHistory` 를 읽는다.

- [x] **Step 1: `mcp/src/tree.ts`** — 객체 배열과 이력 map 을 받아 trie 를 세우는 순수 함수.
      마디마다 `segment`, `path`, 그 자리에 앉은 객체(있으면), 자식들. 객체 없는 중간 마디도
      만든다.

- [x] **Step 2: 자르기** — `root` 접두사로 subtree 를 고르고, `depth` 를 넘는 마디는 접는다.
      접힌 마디는 `collapsed: true`, `objects`(자기 포함 subtree 의 객체 수),
      `lastChangedReading`(subtree 안 모든 멤버 이력의 최대 `reading`, 없으면 생략)를 낸다.

- [x] **Step 3: `tools.ts` 배선** — inputSchema 에 `root: z.string().min(1).optional()` 과
      `depth: z.number().int().nonnegative().optional()` 을 더한다. 둘 중 하나라도 주면 tree
      모드, 아니면 지금 응답 그대로. `selector` 는 tree 를 세우기 전에 객체를 거른다.

- [x] **Step 4: Tests** — `mcp/test/tree.test.ts`.

- [x] **Step 5: 문서** — `mcp/README.md` 에 `root`·`depth` 와 `lastChangedReading` 을 적고,
      이름에 `/` 가 든 GameObject 가 어떻게 되는지도 적는다.

- [x] **Step 6: 최종 검토** — 전체 diff 를 scope 와 churn 기준으로 읽는다.

## Validation

- **Commands to run:** `cd mcp && npm run build && npm test`
- **Expected output:** 기존 30개가 그대로 통과하고 새 test 가 붙어 전부 green.

새 test 가 덮는 것:

| test | 고정하는 것 |
| --- | --- |
| 평평한 객체들이 tree 가 된다 | 기본 동작 |
| 객체 없는 중간 마디도 선다 | 성긴 집합에서 구조가 이어진다 |
| `root` 가 subtree 를 고른다 | 자르기 |
| `depth` 를 넘는 마디가 접힌다 | 접기 |
| 접힌 마디가 subtree 의 객체 수를 낸다 | 펼칠지 정할 근거 |
| 접힌 마디가 subtree 의 최대 `reading` 을 낸다 | 변경 표시 |
| 이력이 없는 subtree 는 `lastChangedReading` 을 안 낸다 | 없는 것과 0 을 안 헷갈린다 |
| `root`·`depth` 를 안 주면 기존 응답 | 기존 호출이 안 깨진다 |
| 이름에 `/` 가 든 객체 | 알려진 한계가 고정된다 |
| `root` 가 아무것도 안 가리키면 빈 tree | 잘못 짚은 것이 답이 된다 |
| `depth` 는 물어본 `root` 에서 센다 | 층수의 기준 |
| 잎은 접힘 표시가 안 붙는다 | 더 볼 것이 없는 것과 접힌 것을 가른다 |
| 펼쳐진 마디의 객체만 모인다 | `includeHistory` 가 안 보이는 것의 이력을 안 싣는다 |

## Risks & Rollback

- **Risks:**
  - `path` 를 `/` 로 쪼개므로 GameObject 이름의 `/` 가 구조를 갈라놓는다. 실제로 그런 이름을
    쓰는 게임을 확인하지 못했다. `selector` 의 sibling index 형태로 옮기는 길이 있지만 이번
    범위 밖이다.
  - tree 를 매 호출마다 세운다. 객체 수에 선형이고 호출은 드물지만, 객체가 수천 개일 때
    재 본 적은 없다.
- **Rollback steps:** 단일 commit 이므로 `git revert`. Unity 를 안 건드려 재빌드가 없다.

## Open Questions

- 없음.

## Outcome

`mcp/` 만 바뀌었다. tree 는 `mcp/src/tree.ts` 의 순수 함수 하나로 서고, `tools.ts` 는 그것을
부르기만 한다.

### 검증 결과

| 대상 | 결과 |
| --- | --- |
| `tsc` | 성공 |
| `node --test` | 42 passed · 0 failed (#13 의 30 + 새로 12) |

### 계획에서 달라진 것 셋

1. **이력을 찾는 방법을 함수로 받는다.** 처음에는 `tree.ts` 가 이력 map 을 직접 뒤지게
   썼는데, 그 map 의 키는 `path` 가 아니라 `scene/selector` 라 맞지 않았다. `tree.ts` 는
   객체 하나를 주면 `reading` 을 돌려주는 함수만 받는다. 저장 방식을 몰라도 되고, test 는
   Map 하나로 끝난다.
2. **tree 모드가 `statics` 는 싣고 `changed` 는 뺀다.** `statics` 는 어느 객체에도 안
   매달려 tree 로 표현되지 않고 양도 적다. `changed` 는 분주한 씬에서 길고, 마디마다 붙는
   `lastChangedReading` 이 같은 물음에 tree 모양으로 답한다.
3. **tree 모드에서도 `includeHistory` 가 듣는다.** 처음에는 조용히 무시했다. 부르는 쪽이
   준 인자가 아무 말 없이 사라지는 것은 고장이다. 펼쳐진 마디에 앉은 객체만 모아 싣는다 —
   응답에 나오지도 않는 객체의 이력을 실을 이유가 없다.

### 남은 것

- `path` 를 `/` 로 쪼개므로 GameObject 이름의 `/` 가 마디를 하나 더 만든다. test 로 고정하고
  README 에 적었지만 고치지는 않았다. 고치려면 `selector` 의 sibling index 형태로 옮겨야
  하는데, 그러면 사람이 읽기 어려워진다.
- 한 경로에 객체가 둘 이상이면 (만들어진 적 다섯이 경로를 나눠 쓰는 경우) 마지막 것이
  마디에 앉는다. 수는 `objects` 가 맞게 세지만 나머지 객체의 내용은 tree 모드에서 안 보인다.
  평평한 모드에서는 전부 보인다.
- 객체가 수천 개일 때 tree 를 세우는 비용을 재 보지 않았다. 객체 수에 선형이고 호출은 드물다.
