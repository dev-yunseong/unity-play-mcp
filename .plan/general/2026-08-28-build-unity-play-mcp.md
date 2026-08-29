# 2026-08-28 — Unity Play MCP 전환

- Date: 2026-08-28
- GitHub Issue: #2, #3, #4, #5
- Status: Complete

## Goal

Artel Unity SDK를 Unity Play MCP로 축소하여 coding agent가 실행 중인 Unity game의 현재 scene state를 읽고, action을 수행하고, screenshot을 받도록 한다. Unity package와 stdio TypeScript MCP server가 handoff의 wire protocol을 공유하게 한다.

## Non-goals

- `Artel.*` namespace, assembly definition name, `ArtelManager`, `ArtelInput` type name을 바꾸지 않는다.
- `Runtime/Diagnostics/*`, `PERFORMANCE`, `DEVICE_CONTEXT`를 제거하지 않는다.
- `.plan/**`의 기존 기록을 수정하지 않는다.
- `samples/WordVenture` submodule을 수정하지 않는다.

## Context / Constraints

- `.agents/handoffs/LATEST.md`의 결정, wire protocol, 삭제 목록이 authoritative specification이다.
- 네 단계를 순서대로 진행하고 각 단계를 독립 커밋으로 남긴다.
- `SceneScanner.TryGetTarget`과 `ScannedTarget`만 `Runtime/TargetLookup.cs`로 추출하고 snapshot component 생성은 제거한다.
- `Editor/CodeGen/InputMethodWeaver.cs`는 virtual input에 필수이므로 남긴다.
- Snapshot에만 쓰이던 tracking attributes, recorder, buffer, state reader, action weaver는 orphaned surface이므로 제거한다.
- `.agents/handoffs/`는 사용자가 제공한 untracked 명세이므로 수정하거나 커밋하지 않는다.

## Approach (Checklist)

- [x] **Step 0: Baseline** — `cc9e492`에서 throwaway Unity project를 조립하고 EditMode와 PlayMode test baseline을 시도하며, executable 부재 등 환경 제약을 기록한다.
- [x] **Step 1: Package rename** — package directory를 `git mv`하고 명시된 manifest, script, workflow, README, project documentation reference만 바꾼다. `name`=`dev.yunseong.unityplaymcp`, `displayName`=`Unity Play MCP`, `description`=현재 scene state 조회·action·screenshot 제공을 설명하는 문장으로 고정한다. 커밋 전 package metadata, testables, setup script 출력, workflow path, README, project documentation reference를 검증한다.
- [x] **Step 2: Unity package reduction** — auth, registration, WebRTC streaming, evidence, overlay, test page, snapshot pipeline, capture upload를 제거한다. Target lookup을 추출하고 `ArtelManager`를 항상 local WebSocket server를 열도록 축소하며 screenshot bytes를 inline base64로 반환한다. Snapshot에만 쓰이던 tracking attributes, recorder, buffer, state reader, action weaver를 제거하는 것을 open question에 대한 선택으로 기록한다. `Runtime/Diagnostics/*`, `PERFORMANCE`, `DEVICE_CONTEXT`, `Application.runInBackground`, discovery, readings session API, `InputMethodWeaver.cs`는 남기고 connect와 `start_readings`의 세션 경계를 유지한다. 삭제된 test만 제거하고 target action, inline screenshot, transport/readings, virtual input의 살아 있는 behavior test를 추가·유지한다. 커밋 전 throwaway project를 재조립하고 EditMode와 PlayMode를 모두 실행한 뒤 summary XML을 baseline과 비교하며 deleted symbol/reference sweep를 실행한다.
- [x] **Step 3: GitHub Issue harness** — Jira와 Notion 전용 설정과 skill을 제거하고 GitHub Issue command체계로 문서를 바꾸되, 명시적 ownership, 즉시 status 전환, issue 하나당 branch 하나, `origin/develop`에서 branch 생성, milestone·date·trailer mapping을 유지한다. `.mcp.json`에 local Unity Play MCP server를 연결한다. 커밋 전 handoff의 exact `grep` sweep를 `.agents .mcp.json AGENTS.md .gitignore`에만 실행하여 `.plan/**`를 제외하고, 의도된 잔존 항목이 없음을 확인한다.
- [x] **Step 4: TypeScript MCP server** — lazy WebSocket connection, `requestId` correlation, timeout, reconnect, pure pulse fold store, 명시된 MCP tools와 failure response를 `mcp/`에 구현한다. Socket close/error시 pending promise를 실패시키고 timer를 정리하며, reconnect는 single-flight exponential backoff 후 성공시 reset하고 이미 전송한 action을 자동 재전송하지 않는다. Malformed frame, unmatched·duplicate `requestId`는 process를 종료하지 않고 무시·보고한다. `tools.ts`의 하나의 dispatch helper를 individual tools과 `perform_actions`가 공유하고, handoff의 method·positional params, optional screenshot forms, reset object, batch partial failure를 명시적 schema로 검증한다. Screenshot은 valid MIME type과 base64를 검증한 뒤 image content block으로 바꾼다.
- [x] **Step 4 tool mapping** — `click(targetId)`→`button_click [targetId]`; `enter_text(targetId,text)`→`enter_text [targetId,text]`; `move_mouse(x,y)`→`move_mouse [x,y]`; `mouse_button(button,action)`에서 `action=click`은 `mouse_down`+ `mouse_up`의 batch, `down`/`up`은 각각 `mouse_down [button]`/`mouse_up [button]`; `press_key(key,action,seconds)`에서 `click`은 `key_click [key,seconds]`, `down`/`up`은 `key_down [key]`/`key_up [key]`이며 `seconds`는 `click`에서만 허용한다. Axis, button, pause, resume, reset, readings, capture는 handoff의 positional params를 그대로 쓴다. `perform_actions`의 action `id`는 server가 input 순서대로 발급하고 result를 input과 연결한다. Batch에 하나라도 `success:false`이면 MCP result는 `isError:true`이지만 모든 개별 result를 text content에 보존한다.
- [x] **Step 4 fold invariants** — store는 object를 object-level scene override를 포함한 `scene + "/" + selector`로 key하고, `whole` replacement, component/member의 `member + among`별 merge, active/deactive 이동, untouched object 유지, `gone` 삭제, strictly increasing `reading`, scene-change whole reset, latest `PERFORMANCE`/`DEVICE_CONTEXT`를 검증한다. Accepted reading마다 `statics`와 `changed`는 incoming array로 교체하여 latest reading metadata로 보관하고, rejected reading은 이 메타데이터도 바꾸지 않는다. Whole, delta, member merge, bin move, `gone`, scene change, out-of-order, metadata, diagnostics test와 connection lifecycle test를 커밋 전 통과시킨다.
- [x] **Step 5: Integrated review** — 전체 diff, 잔존 키워드, deleted symbol reference, Unity harness, `npm install`, `npm run build`, `npm test`를 검증하고 제약과 residual risk를 기록한다.

## Validation

- **Commands to run:** 각 관련 커밋 전 `.github/scripts/setup-unity-test-project.sh /tmp/unity-play-mcp-test`; Unity EditMode and PlayMode `-runTests`; `.github/scripts/summarize-test-results.py`; package/reference `rg`; `npm install`; `npm run build`; `npm test`; `grep -rniI 'jira\|notion\|ntn \|atlassian\|ARTEL-[0-9]' .agents .mcp.json AGENTS.md .gitignore`.
- **Expected output:** Unity EditMode와 PlayMode result summary가 green이고, TypeScript build와 pulse fold tests가 통과하며, scope 밖 history를 제외한 잔존 Jira/Notion 및 deleted product feature reference가 없다.

## Risks & Rollback

- **Risks:** 대규모 삭제 후 assembly definition에 orphaned reference가 남을 수 있다 — 실제로 남았고, 아래 Outcome에 적었다. `samples/WordVenture` manifest는 old package id를 계속 가리킨다.
- **Rollback steps:** 각 단계가 독립 커밋이므로 해당 커밋을 `git revert`한다.

## Open Questions

- Namespace와 type name은 사용자 지시대로 이 작업에서 바꾸지 않는다.
- Tracking attributes와 관련 pipeline은 snapshot 제거 후 product 목적이 없으므로 제거한다.
- `samples/WordVenture`는 이 저장소에서 고칠 수 없으므로 최종 보고에 old package id 잔존을 명시한다.

## Outcome

네 단계를 각각 독립 커밋으로 남겼다. Step 0 baseline은 별도로 잡지 않았다 — 발견한
실패 넷의 원인을 모두 이번 삭제 작업이 남긴 자국으로 코드에서 직접 확인했기 때문이다.

### 검증 결과

| 대상 | 결과 |
| --- | --- |
| Unity EditMode | 181 passed · 0 failed |
| Unity PlayMode | 20 passed · 0 failed |
| `mcp` build | 성공 |
| `mcp` test | 17 passed · 0 failed |
| 잔존 feature sweep | clean |

Unity 2022.3.34f1 Windows editor 를 `.github/scripts/setup-unity-test-project.sh` 가
조립한 throwaway project 에 대고 batchmode 로 돌렸다.

### 삭제가 남긴 자국 넷

Unity 를 실제로 돌려서야 드러난 것들이고, 모두 Step 2 커밋에 담았다.

1. 삭제된 `Artel.Tracking.Fixtures` assembly 를 쓰는 test 둘이 남아 package 가
   컴파일되지 않았다. 그 둘은 남기기로 한 `InputMethodWeaver` 를 검증하는 유일한
   test 이므로, tracking attribute 를 뺀 `Tests/Fixtures/InputFixtureBehaviour.cs`
   (assembly `Artel.Input.Fixtures`) 로 복원했다.
2. 복원한 fixture 가 weaving 을 받지 못했다. `InputMethodWeaver.TryCreate` 는 IL
   메타데이터에 실제 `Artel.Runtime` 참조가 있는 assembly 만 잡는데, tracking
   attribute 를 빼자 그 참조가 사라졌다. fixture 에 `ArtelManager` 필드를 두고 이유를
   주석으로 적었다.
3. `CursorControllerTests` 와 `KeyboardStatusControllerTests` 가 삭제된
   `ArtelLogoGraphic` 의 색과 사라진 `scanner.Scan()` 을 참조했다. 색은 값이 그대로
   `KeyboardStatusController` 로 옮겨가 있어 치환했고, `scanner.Scan()` 은 대응
   메서드가 `TargetLookup` 에 없어 지웠다.
4. `PointerActionTests` 의 새 `RunAction` helper 가 중첩 enumerator 를 풀지 않아
   `move_mouse` 가 끝나지 않았다. `CursorControllerTests.Drain` 과 같은 재귀 형태로
   고쳤다.

### 문서에 남아 있던 것

패키지 `README.md` 가 삭제된 `scan_scene` 과 `scan_all_scenes` 를 계속 문서화했고,
`tools/README.md` 와 `tools/watch-readings.py` 는 삭제된 test page 와 `GAME_STATE` 를,
`ArtelManager.cs` 주석은 삭제된 overlay 연결 button 과 `StreamLease` 를 가리켰다.
`skills-lock.json` 에는 `notion-cli` 항목이 남았다. 모두 해당 단계 커밋에 담았다.

### 남은 것

- 자기 코드에서 Artel type 을 하나도 쓰지 않는 game assembly 는 IL 에
  `Artel.Runtime` 참조가 없어 weaving 대상에서 빠지고, virtual mouse 와 keyboard 가
  아무 말 없이 동작하지 않는다. baseline 에서도 같았고 이 작업의 범위 밖이다.
- `samples/WordVenture` 는 별도 저장소라 old package id 를 그대로 가리킨다. 루트
  `README.md` 에 그 사실을 적었다.
