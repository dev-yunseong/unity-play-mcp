# 2026-09-10 — selector와 표시 정보로 조작 대상을 검색하고 간결한 후보 반환

- Date: 2026-09-10
- GitHub Issue: #58
- Status: Draft

## Goal

`get_scene_state`의 `selector`는 `candidate.includes(selector)` 하나뿐이라 이름/표시
텍스트/컴포넌트/조작 가능 여부로 대상을 좁힐 수 없고, 좁혀도 전체 `changed`/`statics`가
함께 실려 검색 결과보다 배경 정보가 크다. 새 tool `search_targets`를 추가해 명시적인
exact/contains 및 대소문자 계약으로 이름, 표시 텍스트, 컴포넌트, 조작 가능 여부를 좁히고,
id/selector/scene/표시 텍스트/가능한 조작/활성 상태만 담은 간결한 후보 목록과 중복 후보·
0건·잘림을 명확히 반환한다. `get_scene_state`의 기존 `selector` 동작은 그대로 둔다.

## Non-goals

- 게임 전용 검색 규칙 (예: WordVenture의 카드 타입 같은 도메인 지식)을 SDK 나 tool 에 심지 않는다.
- 기존 `candidate.includes(selector)` 부분 문자열 검색을 새 tool 로 다시 구현하는 것이 아니다 —
  `get_scene_state`/`get_visible_elements`의 `selector` 는 그대로 두고, `search_targets`는
  별도의 명시적 계약을 갖는 새 tool 이다.
- 추측으로 객체를 만들거나 첫 후보를 자동으로 조작하지 않는다 — 검색은 순수 조회다.
- `Packages/dev.yunseong.unityplaymcp/` 변경 없음 — 이미 pulse 로 들어오는 정보로 계약을
  충족한다.

## Context / Constraints

- 대상 코드: `mcp/src/tools.ts` (`get_scene_state`, `get_visible_elements`, `registerTools`),
  `mcp/src/pulse.ts` (`PulseObject`, `PulseStore`), `mcp/src/visible.ts` (`shows` 판정,
  `visibleElements`), `mcp/src/tree.ts` (`selectorSegments`, `nameOf` — 아래에서 재사용).
- `PulseObject.offers`는 현재 `JsonValue[]`로 선언돼 있지만 실제 wire 모양은
  (`LiveState.cs:1213` `Offered`) `{"clicks":[{event,method,on}], "keys":[{key,does?}],
  "pointers":[string]}` 객체다. 이 issue 가 "가능한 조작"을 계산하려면 이 필드를 제대로 읽어야
  하므로, `pulse.ts`에 정확한 타입(`PulseOffers`, `OfferedClick`, `OfferedKey`)을 추가하고
  `PulseObject.offers`를 그 타입으로 고친다. `pulse.ts` 는 "불가피할 때만, 최소로" 쓰라는
  지시가 있으므로 이 한 필드의 타입 교정 외에는 건드리지 않는다.
- `mcp/src/tools.ts` 는 track 59, 60 도 같은 파일에 tool 을 추가하므로 병합 충돌이 예상된다.
  새 tool 등록과 schema 는 한 곳에 모아 최소한으로 삽입한다. 실제 검색 로직은
  `mcp/src/search.ts` 새 파일에 둔다.
- `mcp/test/schema.test.ts`의 `REGISTERED_TOOL_COUNT = 17`은 tool 을 하나 추가하면 18로
  바꿔야 한다.
- `mcp/src/instructions.ts`는 2000자 제한이 있다 (`instructions.test.ts`). 새 tool 을
  발견하도록 한 줄만 보탠다.
- `search_targets`는 씬을 따로 고르지 않는다. `store.getState()`가 주는 `FoldedPulseState`
  하나가 이미 "지금 로드된 씬(`state.scene`) + 거기 딸린 영속 객체"를 나타내고,
  `get_scene_state`도 이 이상 씬을 고르게 하지 않는다 — 이 tool 도 같은 범위를 따른다
  (scene 선택 파라미터는 범위 밖).

## Approach (Checklist)

- [x] **Step 0: Recon** — `tools.ts`/`pulse.ts`/`visible.ts`/`tree.ts`/`instructions.ts`
  읽음. `offers`의 실제 wire 모양을 `LiveState.cs`/`WatchList.cs`/`WatchListJson.cs`에서
  확인함.
- [ ] **Step 1: `mcp/src/pulse.ts`** — `PulseOffers`/`OfferedClick`/`OfferedKey` 인터페이스
  추가, `PulseObject.offers?: JsonValue[]` 를 `PulseObject.offers?: PulseOffers` 로 교정.
  기존 동작에는 영향 없음 (이 필드를 지금까지 아무도 읽지 않았음).
- [ ] **Step 1.5: `mcp/src/tree.ts`** — `nameOf`, `selectorSegments`는 그대로 비공개로 두고,
  새 exported 함수 하나만 보탠다:
  ```ts
  /// selector 전체에서 마지막 마디의 표시 이름만 뽑는다 — 형제 순번 없이.
  export function leafNameOf(selector: string): string {
    const segments = selectorSegments(selector);
    const last = segments[segments.length - 1];
    return last === undefined ? selector : nameOf(last);
  }
  ```
  `search.ts`가 leaf 이름을 다시 파싱하면 마디 문법이 바뀔 때 `tree.ts`와 `search.ts`가
  따로 고쳐지다 어긋난다 — 이미 있는 규칙 하나를 내보내 재사용한다. 빈 selector(`""`)는
  `segments`가 `[]`이므로 selector 자체(`""`)를 그대로 돌려준다.
- [ ] **Step 2: `mcp/src/visible.ts`** — 새 exported 함수 `displayedTextOf(object: PulseObject):
  string | undefined` 추가. `object.by ?? []`를 앞에서부터 훑어 `shows(component)`가 참인
  첫 component 를 찾고, 그 `members`를 앞에서부터 훑어 `typeof member.value === "string"`인
  첫 멤버의 `value`를 낸다. `by`가 없거나 그런 component/멤버가 하나도 없으면 `undefined`.
  순서는 게임이 보낸 `by`/`members` 배열 순서 그대로이며 별도로 정렬하지 않는다. 기존
  export/동작은 바꾸지 않는다.
- [ ] **Step 3: `mcp/src/search.ts` (신규)** — 아래 타입과 `searchTargets` 순수 함수를 둔다.

  ```ts
  export interface SearchQuery {
    name?: string;
    displayedText?: string;
    component?: string;
    actionable?: boolean;
    exact?: boolean;           // 기본 false = contains
    caseSensitive?: boolean;   // 기본 false = 대소문자 무시
    includeInactive?: boolean; // 기본 false, get_scene_state 와 같은 이름·기본값
    limit?: number;            // 기본 20, 1 이상 정수 (tool schema 가 z.number().int().positive() 로 보장)
  }

  export interface SearchCandidate {
    id: number;
    selector: string;
    scene: string; // object.scene ?? state.scene — objectKey()와 같은 규칙 (pulse.ts:146)
    displayedText?: string;
    actions: string[];
    active: boolean;
  }

  export interface SearchResult {
    candidates: SearchCandidate[];
    total: number;         // limit 적용 전, 조건에 맞은 전체 개수
    truncated: boolean;    // total > candidates.length
    duplicateNames: string[]; // matched 전체(잘리기 전) 기준 leaf 이름 2개 이상 겹침
  }

  export function searchTargets(state: FoldedPulseState, query: SearchQuery = {}): SearchResult;
  ```

  - **대상 pool**: `state.active`(항상) + `includeInactive === true`면 `state.deactive`도.
    둘 다 비어 있을 수 있고, 그러면 자연히 `total: 0, candidates: [], truncated: false,
    duplicateNames: []`가 나온다 — 별도 "0건" 분기를 두지 않아도 값 자체가 명확하다.
  - **exact/caseSensitive 계약** (name, displayedText, component 세 필터가 전부 공유 —
    세 개의 서로 다른 스위치 조합을 두는 것은 이 issue 범위를 넘는 과설계라 하나로 묶는다):
    ```ts
    function normalize(value: string, caseSensitive: boolean): string {
      return caseSensitive ? value : value.toLowerCase();
    }
    function textMatches(candidate: string, query: string, exact: boolean, caseSensitive: boolean): boolean {
      const left = normalize(candidate, caseSensitive);
      const right = normalize(query, caseSensitive);
      return exact ? left === right : left.includes(right);
    }
    ```
    네 조합 예시 (leaf 이름 `"myButton"`, query `"Button"`):
    | exact | caseSensitive | 결과 | 이유 |
    |---|---|---|---|
    | false | false (기본) | 일치 | `"mybutton".includes("button")` |
    | false | true | 일치 | `"myButton".includes("Button")` |
    | true | false | 불일치 | `"mybutton" !== "button"` |
    | true | true | 불일치 | `"myButton" !== "Button"` |
  - **이름 계약**: `query.name`이 있으면 `leafNameOf(object.selector)`(Step 1.5)와
    `textMatches`.
  - **표시 텍스트 계약**: `query.displayedText`가 있으면 `displayedTextOf(object)`가
    `undefined`면 곧장 불일치, 아니면 그 값과 `textMatches`.
  - **컴포넌트 계약**: `query.component`가 있으면 `(object.by ?? [])`중 하나라도
    `textMatches(component.on, query.component, ...)`를 만족해야 통과.
  - **조작 가능 여부**: `actionsOf(object)`(아래)가 하나 이상이면 actionable. `query.actionable`이
    주어지면 그 값과 일치해야 통과 (true → 있는 것만, false → 없는 것만).
  - **actionsOf**: `offers`가 없으면 `[]`. 있으면 순서대로 —
    `offers.clicks`가 비어있지 않으면 `"click"` 하나, 이어서 `offers.keys`의 각 항목을
    `"key:<key>"`(주어진 순서 그대로), 이어서 `offers.pointers`의 각 항목을
    `"pointer:<event>"`(역시 주어진 순서 그대로). 게임 쪽(`WatchListJson.cs`)이 이미
    정렬·중복 제거해 보내므로 여기서 추가로 정렬하거나 중복 제거하지 않는다.
  - **truncation과 duplicateNames의 순서** (medium review 반영 — 잘림과 중복 신호는
    서로 다른 관심사이므로 자르기 전 전체 집합 기준으로 중복을 센다):
    1. 필터를 통과한 객체 전부를 `matched: SearchCandidate[]`로 모은다 (아직 안 자름).
    2. `matched`의 `leafNameOf(selector)`별 개수를 세어 2개 이상인 이름을 정렬해
       `duplicateNames`로 낸다 — `limit`때문에 안 보이는 후보의 이름도 여기 잡힌다. 이건
       "지금 보이는 목록 안에서 겹친다"가 아니라 "이 이름을 가진 서로 다른 인스턴스가 하나
       이상 더 있다"는 신호다.
    3. `total = matched.length`.
    4. `candidates = matched.slice(0, limit)`.
    5. `truncated = matched.length > limit`.
  - 후보마다 boolean 중복 flag를 얹는 대신 top-level `duplicateNames` 하나로 낸 이유: 이름이
    안 겹치는 보통의 경우 모든 후보에 늘 붙는 필드가 없어 응답이 그만큼 작다 — 이 issue가
    바로 "배경 정보가 검색 결과보다 크다"는 문제라, 흔한 경우의 payload를 스스로 불리지
    않는다.
- [ ] **Step 4: `mcp/src/tools.ts`** — `search_targets` tool 하나를 새로 등록. import 는
  `search.ts`에서, 로직은 그쪽에 전부 위임. 등록 블록은 `get_visible_elements` 바로 뒤
  한 곳에 모아 삽입해 diff 를 좁힌다. `get_scene_state`/`get_visible_elements`의 `selector`
  설명 문자열에 "case-sensitive substring against the full selector path" 를 명시해
  AC1 의 "기존 selector 계약 명시"를 충족한다 (동작은 바꾸지 않음).

  handler 골격은 `get_visible_elements`(`tools.ts:482-495`)와 그대로 맞춘다 — heavy
  review 지적대로, reading 이 아예 없을 때와 "조건에 맞는 후보 0건"을 구별하지 않으면
  둘 다 `total: 0`으로 보여 AC 의 "0건을 명확히 보고"를 어긴다:
  ```ts
  async ({ name, displayedText, component, actionable, exact, caseSensitive, includeInactive, limit }) => {
    try {
      await connection.ensureConnected();
      const state = store.getState();
      if (state === undefined || state === null) {
        return text("No scene reading has arrived. Call start_readings to begin a play session, then try again.");
      }
      return text(JSON.stringify({
        reading: state.reading,
        frame: state.frame,
        scene: state.scene,
        ...searchTargets(state, { name, displayedText, component, actionable, exact, caseSensitive, includeInactive, limit }),
      }, null, 2));
    } catch (error) {
      return { ...text(failureText("Target search is unavailable", error)), isError: true };
    }
  }
  ```
  `reading`/`frame`/`scene`을 얹는 것도 `get_scene_state`/`get_visible_elements`와 응답
  모양을 맞추기 위해서다.
- [ ] **Step 5: `mcp/src/instructions.ts`** — "Reading the scene" 블록에 한 줄 추가:
  `search_targets`로 이름/표시 텍스트/컴포넌트/조작 가능 여부로 좁혀 간결한 후보를 받고,
  후보의 `selector`로 `get_scene_state`를 이어 부르라는 안내. 2000자 제한 확인.
- [ ] **Step 6: Tests** — `mcp/test/search.test.ts` 신규: exact/contains, 대소문자,
  이름/표시 텍스트/컴포넌트/조작 가능 여부 각각의 좁히기, `includeInactive`, `limit`/
  `truncated`/`total`, `duplicateNames`, 0건, 기존 `offers` 모양(clicks/keys/pointers)
  기준 `actionsOf` 검증. `mcp/test/visible.test.ts`에 `displayedTextOf` 케이스 추가.
  `mcp/test/schema.test.ts`의 `REGISTERED_TOOL_COUNT`를 18로 갱신하고 `search_targets`
  schema 도 draft-07 잔재 검사에 자동으로 포함됨을 확인. `mcp/test/instructions.test.ts`는
  기존 assertion 만으로 충분 (새 tool 이름을 필수 목록에 넣지 않아도 통과하지만, 문서화
  차원에서 instructions 에 넣는 이상 자연히 언급됨).
- [ ] **Step 7: Rollout** — 새 tool 추가는 additive, 기존 tool 계약 불변. rollback 은 이
  PR revert 하나로 충분.

## Validation

- **Commands to run:**
  ```bash
  unset -f node npm npx
  export PATH=/home/yunseong/.nvm/versions/node/v24.18.0/bin:$PATH
  cd /home/yunseong/dev/unity-play-mcp-58/mcp
  npm install
  npm run build && npm test
  ```
- **Expected output:** `tsc` 통과, `node --test dist/test/*.test.js` 전부 green.

## Risks & Rollback

- **Risks:**
  - `PulseObject.offers` 타입 교정이 index signature(`[key: string]: JsonValue |
    PulseComponent[] | undefined`)와 구조적으로 충돌해 `tsc` 가 거절할 수 있다 — 발생하면
    `PulseOffers`를 `JsonValue`와 구조적으로 호환되게 좁히거나 index signature 쪽을 넓힌다.
  - "가능한 조작"의 의미(`actionsOf`)는 실제 Unity 실행 없이 `LiveState.cs`/`WatchList*.cs`
    코드 읽기로만 확인한 가정이다. 실제 pulse 가 이 모양을 어떻게 채우는지는 라이브 Unity
    세션이 없어 여기서 재확인할 수 없다 — PR 에 명시.
  - "중복 후보"의 정확한 의미가 issue 본문에 명시적 정의 없이 예시로만 있다 — leaf 이름 겹침
    으로 해석했고, 다른 해석(같은 `id`가 두 번 나오는 것)은 구조적으로 불가능함을 코드로
    보장한다.
- **Rollback steps:** 이 변경은 새 파일과 `search_targets` tool 하나를 더하는 additive
  변경이라 PR 을 revert 하면 끝난다. `pulse.ts`의 `offers` 타입 교정만 되돌리려면 해당
  hunk 만 되돌리면 된다.

## Open Questions

- (해결됨, 가정으로 진행) "중복 후보" 보고가 leaf 이름 중복인지 다른 뜻인지 — PR 설명에
  가정을 명시하고 리뷰에서 조정 여지를 남긴다.
