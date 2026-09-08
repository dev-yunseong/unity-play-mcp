# 2026-09-08 — tree 구조를 path 가 아니라 selector 로 잡는다

- Date: 2026-09-08
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/18
- Status: Reviewed

## Goal

`mcp/src/tree.ts` 의 `build()` 가 지금 객체의 `path` 를 `/` 로 쪼개 tree 구조를 세운다.
이것이 두 가지를 틀리게 만든다: GameObject 이름에 `/` 가 있으면 마디가 하나 더 생기고,
같은 `path` 를 쓰는 형제 객체는 마지막 하나만 마디에 남고 나머지는 tree 모드에서 사라진다.
`selector` (`name[siblingIndex]` 를 `/` 로 이은 문자열, `ScenePath.cs:57`, `:80`) 로 구조를
세워 두 문제를 없앤다. `path` 는 마디의 표시 이름으로 계속 쓰고, `root` 인자는 계속
사람이 읽는 `path` 를 받는다.

## Non-goals

- `path` 필드를 없애거나 API 모양을 바꾸는 것.
- 평평한 모드(`root`, `depth` 모두 없는 응답) 변경 — 거기서는 객체가 배열이라 이 문제가
  없다.
- `Packages/` 아래 어떤 파일도 건드리지 않는다. `selector` 는 이미 `PulseObject.selector` 로
  오고 있다 (`mcp/src/pulse.ts:20`).
- `mcp/src/pulse.ts`, `mcp/src/connection.ts` 는 건드리지 않는다 (issue #19 소유).
- `mcp/src/visible.ts` 를 만들지 않는다 (issue #16 소유). `mcp/src/tools.ts` 는 이번 변경이
  필요로 하는 최소한만 건드린다 — 실제로는 건드릴 필요가 없을 가능성이 높다 (아래 Context
  참고).

## Context / Constraints

- `ScenePath.SelectorOf` (`Packages/dev.yunseong.unityplaymcp/Runtime/Affordance/Scan/ScenePath.cs:38`)
  는 `transform` 에서 부모를 따라 올라가며 각 마디를 `name + "[" + siblingIndex + "]"` 로 적고
  `/` 로 잇는다. 마디 경계는 언제나 `]/` 이고, 이름 안의 `/` 는 `[`, `]` 를 포함하지 않으므로
  `]/` 를 경계로 쪼개면 이름 안의 `/` 에 걸리지 않는다.
- 루트(부모가 없는 마디)의 자리는 sibling index 가 아니라 `SelectorOf` 를 부르는 순회가 매긴
  `rootIndex` 다 (`ScenePath.cs:64`) — Unity 가 루트에 대해 언제나 `GetSiblingIndex() == 0`
  을 답하기 때문이다. 이 값이 이미 selector 문자열 안에 박혀서 온다. `mcp/src/tree.ts` 는
  이 계산을 다시 할 필요가 없다 — 문자열을 그대로 쪼갠다.
- 잘린 계층은 `.../` 로 시작한다 (`ScenePath.cs:73-78`). 같은 `Of` 함수가 `path` 도 `selector`
  도 만들므로, `path` 에서도 `selector` 에서도 이 접두사가 똑같이 붙는다.
  - `path` 를 `/` 로 쪼개면 `".../A/B/C".split("/")` 가 `["...", "A", "B", "C"]` 를 낸다 —
    `"..."` 가 저절로 첫 마디가 된다. (`.../` 자체가 `...` + `/` 이기 때문이다.)
  - `selector` 를 `]/` 로 쪼개면 `"..."` 에는 `]` 가 없어서 다음 마디에 붙어버린다:
    `".../A[2]/B[0]/C[1]".split("]/")` 는 `[".../A[2", "B[0", "C[1]"]` 를 낸다.
  - 이 비대칭을 없애려고, `selector` 를 쪼개기 전에 `.../` 접두사를 따로 떼어 자기 마디
    `"..."` 로 만든다. 그러면 `path` 의 마디 수와 `selector` 의 마디 수가 항상 같아지고,
    사람이 잘린 경로를 그대로 `root` 로 넘겨도 (`"...".../A"`) 같은 자리에 선 마디를 찾는다.
- `PulseObject.selector` 는 항상 있는 필드다 (`mcp/src/pulse.ts:20`, optional 이 아니다). 다만
  방어적으로 문자열이 아닐 때는 빈 배열로 다룬다 — 기존 `path` 처리와 같은 관례. `selector` 가
  빈 문자열이면 (`.../` 로 시작하지 않는 한) 마디가 하나도 없는 것으로 다룬다 — 그 객체는
  `path` 가 빈 문자열일 때와 마찬가지로 root 마디 자체에는 앉지 않고 조용히 트리에서 빠진다
  (`build()` 의 `node !== root` 검사가 이미 이 경우를 막는다). 이것을 넘어서는 형식 검증(각
  마디가 정말 `name[숫자]` 모양인지 확인하는 것)은 하지 않는다 — `ScenePath.cs` 가 이미
  보장하는 것을 다시 검증하는 투기적 방어 코드다.
- 지금 `mcp/src/tools.ts:343` 은 `foldIntoTree` 를 그대로 부른다. `foldIntoTree` 의 signature
  (매개변수, 반환 타입)는 바뀌지 않고, `tree.ts` 를 부르는 곳은 `tools.ts` 와
  `test/tree.test.ts` 둘뿐임을 확인했다 (`grep -rn "tree.js" mcp/src mcp/test`). 그러므로
  **`tools.ts` 는 건드리지 않는다** — "필요해지면 고친다" 가 아니라 확정된 결정이다.
- `descend()` 가 같은 표시 이름의 형제 중 하나를 고르는 규칙은 `build()` 가 `objects` 배열을
  받은 순서 그대로 순회해 `Map` 에 넣는다는 사실에 기댄다 — `Map` 은 삽입 순서를 유지하므로,
  `node.children.values()` 를 앞에서부터 훑으면 언제나 입력 배열에서 먼저 나온 객체의 마디를
  먼저 만난다. 이것이 사람이 두 형제를 이름만으로 구분 못 하는 경우의 동작을 결정한다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — 끝남. `mcp/src/tree.ts`, `mcp/test/tree.test.ts`,
  `Packages/dev.yunseong.unityplaymcp/Runtime/Affordance/Scan/ScenePath.cs` 를 다 읽었다.
- [ ] **Step 1: Implementation**
  - `mcp/src/tree.ts` 의 `build()` 를 `selector` 기반으로 바꾼다.
    - `selectorSegments(selector: string): string[]` 헬퍼를 추가한다. `.../` 접두사를 떼어
      `"..."` 를 첫 마디로 넣고, 나머지를 `]/` 로 쪼개 마지막을 뺀 모든 조각에 `]` 를
      되돌려 붙인다.
    - `nameOf(segment: string): string` 헬퍼를 추가한다. `"..."` 는 그대로 두고, 그 외에는
      끝의 `[숫자]` 를 지워 표시 이름을 낸다. 이 이름은 반드시 `selector` 마디 문자열
      자체에서 뽑는다 — 같은 순번의 `path.split("/")` 원소를 대신 쓰지 않는다. 이름 안에
      `/` 가 있으면 `path` 와 `selector` 의 마디 수가 서로 달라지기 때문이다: 이름이
      `A/B` 인 객체의 `selector` 는 `"Canvas[0]/A/B[0]"` (두 마디, `"Canvas[0]"` 와
      `"A/B[0]"`) 인데 `path` 는 `"Canvas/A/B"` 로 `split("/")` 하면 세 조각
      (`["Canvas","A","B"]`) 이 나온다. 인덱스로 짝지으면 `A/B` 자리에 `"A"` 만 앉아
      표시 이름이 조용히 잘린다 — 이름에 `/` 가 있어도 마디가 늘지 않아야 한다는 acceptance
      criteria 를 되레 깨는 것이다. `path` 와 `selector` 의 마디 수가 항상 같다는 것은
      Context 의 `.../` 문단에서만 성립하는 좁은 사실이고(`"..."` 를 양쪽 다 한 마디로
      세도록 맞췄기 때문), 이름 안에 `/` 가 있는 일반 마디에는 적용되지 않는다.
    - `Building.children` 은 여전히 `Map<string, Building>` 이지만 키를 selector 의 원본
      마디 문자열로 바꾼다 (형제마다 다른 sibling index 를 담고 있어 유일하다). 값은
      `nameOf(segment)` 를 노드의 `segment`(표시 이름)로, 지금까지 걸어온 이름들을 `/` 로
      이은 것을 `path`(표시 경로)로 담는다.
    - 마지막 마디(객체 자신이 앉는 마디)에서는 재구성한 `path` 대신 `object.path` 를 그대로
      쓴다 — 사람이 읽는 필드는 여전히 서버가 준 값이 근거고, 재구성값은 중간 마디에서만
      쓰는 최선의 추정이다. 이 마디가 다른 객체를 걷다가 먼저 중간 마디로 만들어졌을 수도
      있으므로 (예: `Canvas/Panel` 이 먼저 중간 마디로 섰다가 나중에 그 자리에 실제 객체가
      오는 경우), `object.path` 대입은 노드를 새로 만들 때(`emptyNode()` 호출 시점)가 아니라
      객체를 그 마디에 앉힐 때마다 매번 한다 — 기존 코드가 `path` 를 노드 생성 시점에만
      정하던 것과 다른 지점이다.
    - `descend()` 는 여전히 사람이 넘긴 `path` 를 `/` 로 쪼개 걷지만, `children.get(segment)`
      대신 `node.children` 의 값들 중 `child.segment === segment` 인 첫 번째를 찾는다 — 맵
      키가 이제 selector 마디라 사람이 읽는 이름과 다르기 때문이다. 같은 이름의 형제가
      여럿이면 먼저 만들어진 쪽(= `objects` 배열에서 먼저 나온 쪽)을 골라 걷는다.
  - `summarize()`, `render()`, `emptyNode()`, `foldIntoTree()` 는 `Building` 의 필드 이름이
    그대로라 손대지 않는다.
  - `mcp/src/tools.ts` 는 우선 안 건드린다. `foldIntoTree` 호출부(`tools.ts:343`)가 넘기는
    인자가 그대로 맞는지만 구현 중 확인한다.
- [ ] **Step 2: Tests** — `mcp/test/tree.test.ts` 를 고쳐서, 테스트가 실제 Unity 가 내는
  모양과 같은 `selector` 문자열(`Name[index]` 를 `/` 로 이은 것)을 쓰게 한다. 기존 테스트는
  전부 `object(selector, path)` 시그니처를 실제 형태로 다시 채운다. 새/바뀐 테스트:
  - 같은 `path` 를 쓰는 형제 다섯이 다섯 마디가 된다 (`RangedCat(Clone)` 다섯, 서로 다른
    sibling index).
  - GameObject 이름에 `/` 가 있어도 마디가 늘지 않는다 — `tree.test.ts:103` 의 "알려진
    한계" 테스트를 이 동작으로 바꾼다.
  - `root` 가 여전히 `path` 를 받아 그 subtree 를 골라낸다.
  - 잘린 계층(`.../` 로 시작하는 `selector`/`path`)이 `"..."` 를 첫 마디로 세운다.
  - 기존 테스트(중간 마디가 남는 것, depth 로 접히는 것, 이력 집계) 는 동작 자체는 안
    바뀌므로 `selector` 값만 채워 넣고 기대값은 그대로 둔다.
- [ ] **Step 3: Rollout / Rollback** — 순수 라이브러리 함수 변경이라 별도 플래그나 마이그레이션이
  없다. 되돌릴 때는 이 커밋을 `git revert` 한다.

## Validation

- **Commands to run:**
  - `export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH" && cd /home/yunseong/dev/unity-play-mcp-18/mcp && npm ci`
  - `export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH" && cd /home/yunseong/dev/unity-play-mcp-18/mcp && npm run build`
  - `export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH" && cd /home/yunseong/dev/unity-play-mcp-18/mcp && npm test`
- **Expected output:** build 가 타입 오류 없이 끝나고, `npm test` 의 모든 테스트가 통과한다
  (`tree.test.ts` 를 포함해서).

## Risks & Rollback

- **Risks:**
  - `descend()` 가 이름이 같은 형제 중 먼저 만들어진 쪽을 고르는 규칙은 사람이 두 형제를
    구분 못 하고 `root` 를 줄 때는 여전히 모호하다 — 이전에도 모호했던 자리라 새 위험은
    아니다.
  - 재구성한 중간 마디 `path` 는 GameObject 이름이 이례적으로 `[숫자]` 로 끝나는 경우
    이론상 어긋날 수 있다. 실제 씬에서 극히 드물고, 마지막 마디(객체가 실제로 앉는 자리)는
    항상 `object.path` 를 그대로 쓰므로 사람이 보는 값 자체는 영향받지 않는다.
  - 이름 안에 리터럴 `]/` 가 통째로 들어간 GameObject 는 `selectorSegments()` 가 그 자리를
    경계로 오인해 마디를 하나 더 만든다 — issue 의 Constraints 가 이미 받아들이기로 정한
    한계다("`]/` 를 경계로 쪼개면... 이름에 `]/` 가 통째로 들어가는 경우만 남는데, `/`
    하나보다 훨씬 드물다"). pair review 에서 다시 지적받았지만 새 코드 변경은 하지 않는다.
  - `descend()` 는 사람이 준 `root` 를 `/` 로 쪼개 한 마디씩 걷는다. 이름 안에 `/` 가 있는
    마디(예: `Canvas/A/B`)를 향해 `root: "Canvas/A/B"` 를 그대로 주면, `/` 로 쪼갠 세 조각
    (`"Canvas"`, `"A"`, `"B"`) 중 `"A"` 라는 이름의 자식을 못 찾아 빈 트리를 낸다 — 예전처럼
    (우연히) 엉뚱한 마디에 닿는 대신 정직하게 못 찾았다고 답하는 것이라 acceptance criteria
    를 어기지 않지만, 이름에 `/` 가 있는 마디를 `root` 로 직접 겨눌 방법이 없다는 잔여
    한계로 남긴다.
- **Rollback steps:** 이 변경은 `mcp/src/tree.ts`, `mcp/test/tree.test.ts` 로 국한되므로
  `git revert <commit>` 으로 되돌릴 수 있다.

## Open Questions

- 없음 — issue 의 Constraints 가 `.../` 처리와 루트 자리 처리를 이미 결정했다.

## Plan Review

- **Fast (haiku): NONPASS → 반영.**
  - selector 가 문자열이 아닐 때의 방어적 처리를 Approach 에도 명시 (Context 에 반영).
  - `descend()` 가 이름이 같은 형제 중 먼저 만들어진 쪽을 고르는 규칙이 `Map` 삽입 순서
    (= `objects` 배열 순서)에 기대는 것을 명시적으로 적었다 (Context 에 추가한 문단).
  - 빈 문자열 `selector` 의 동작을 명시했다 (Context).
  - `tools.ts` 를 건드릴지 말지를 "필요하면" 이 아니라 확정된 결정("건드리지 않는다")으로
    바꾸고, 그 근거(호출부가 `tools.ts` 와 test 파일뿐임을 `grep` 으로 확인)를 남겼다.
- **Medium (sonnet): NONPASS → 부분 반영, 핵심 제안은 반려.**
  - **반려:** `nameOf()` 헬퍼를 없애고 `path.split("/")` 와 `selectorSegments()` 를 같은
    인덱스로 짝지어 표시 이름을 뽑자는 제안. 이유: 이 짝짓기는 이름 안에 `/` 가 있는 마디에서
    깨진다. `A/B` 라는 이름의 객체는 `selector` 마디가 `"A/B[0]"` 하나인데 `path` 는
    `"Canvas/A/B"` 로 세 조각으로 쪼개져(`["Canvas","A","B"]`) 마디 수가 어긋난다. 인덱스로
    짝지으면 표시 이름이 `"A"` 로 잘려, 이 issue 가 고쳐야 할 acceptance criteria("이름 안의
    `/` 가 마디를 안 늘린다")를 정확히 다시 깬다. 리뷰어가 근거로 든 "마디 수가 항상 같다"는
    말은 Context 의 `.../` 문단에서만 성립하는 좁은 사실이지, 일반적인 불변식이 아니다 —
    이 오해를 막으려고 Approach 의 `nameOf()` 항목에 반례를 그대로 적어 두었다.
  - **반영:** 두 배열의 길이가 다를 수 있다는 것 자체는 맞는 지적이라, `nameOf()` 가
    `path` 가 아니라 `selector` 마디 문자열만 갖고 동작하도록 — 즉 애초에 두 배열의 길이가
    맞아야 할 필요가 없도록 — Approach 문단에 명시했다. 이렇게 하면 리뷰어가 물은 "길이가
    다르면 어떻게 하는가" 라는 질문 자체가 생기지 않는다(짝짓기를 안 하므로).
- **Heavy (opus): PASS.** 반려된 zip 제안이 실제로 이 issue 의 acceptance criteria 를
  다시 깬다는 것을 `ScenePath.cs:57-58`, `:80` 을 직접 대조해 확인했고, 다섯 acceptance
  criteria 모두 설계로 만족됨을 검증했다. non-blocking 노트 두 개를 반영했다: (1)
  `descend()` 가 이름에 `/` 가 있는 마디를 `root` 로 직접 겨누지 못하는 잔여 한계를 Risks
  에 적었다. (2) `object.path` 대입이 노드 생성 시점이 아니라 객체를 앉히는 매 순간
  일어나야 한다는 것을 Approach 에 명시했다.

- **결론: 세 단계 모두 통과, 구현 시작.**

## Pair Review

- **Critic (sonnet, `pair-review-critic`): PASS.** `mcp/src/tree.ts`, `mcp/test/tree.test.ts`
  를 직접 읽고 `npm test` (117/117) 를 재확인한 뒤, acceptance criteria 여섯 개가 모두
  구현과 테스트로 만족됨을 확인했다. `mcp/src/tools.ts` 를 건드리지 않은 결정도 옳다고
  확인했다(`get_scene_state` 의 설명과 `root` schema 가 내부 키를 언급하지 않으므로 이번
  변경으로 부정확해지지 않는다).
  - **반영:** `descend()` 가 같은 표시 이름의 형제 중 먼저 만들어진 쪽을 고르는 동작이
    테스트로 고정되어 있지 않다는 should-fix 지적을 받아, "root naming a display path
    shared by siblings selects the first-inserted one" 테스트를 추가했다(`tree.test.ts`).
  - **반려하지 않고 기록만:** 이름 안에 리터럴 `]/` 가 통째로 들어간 GameObject 는 여전히
    마디를 하나 더 만든다는 잔여 한계를 지적받았다. issue 의 Constraints 가 이미 "`]/` 를
    경계로 삼으면 이름 안의 `/` 하나보다 훨씬 드문 `]/` 통째짜리 이름만 남는다"고 이 한계를
    받아들이기로 정했으므로 새 코드 변경은 하지 않는다 — Risks 문단의 `[숫자]`-접미사
    한계 옆에 같이 적어 둔다.
