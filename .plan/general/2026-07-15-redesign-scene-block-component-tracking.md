# 2026-07-15 — Scene Block 다중 Component 및 Attribute 상태 추적 구조 설계

- Date: 2026-07-15
- GitHub Issue: None
- Jira Issue: ARTEL-9
- Status: In Progress

## Goal

하나의 Unity `GameObject`를 하나의 단일 type으로 축약하던 Scene Block 모델을, GameObject 계층과 여러 component 상태를 함께 표현하는 명시적 구조로 변경한다.

`domain -> DTO -> JSON` 경계를 분리한다. Attribute가 지정된 함수 호출은 `ILPostProcessor`로 계측해 component별 action history에 누적하고, WebSocket scene snapshot 전송 시 누적분을 batch로 포함한다. Attribute가 지정된 상태도 같은 component 아래 state로 노출한다. Scene scan과 WebSocket protocol은 같은 snapshot 계약을 사용하며, JSON 처리는 검증된 상용 수준 library로 통일한다.

완료 조건:

- 하나의 Scene Block이 0개 이상의 component를 결정적 순서로 포함한다.
- domain model과 wire DTO 모두 새 구조를 사용하며 서로의 책임이 섞이지 않는다.
- `SceneScanner`가 지원 component를 전부 수집하고 action target mapping을 유지한다.
- 상태 Attribute가 붙은 member가 component의 `states`로 노출된다.
- action Attribute가 붙은 함수 호출이 tag/name/return value/timestamp를 가진 invocation record로 누적된다.
- snapshot 전송이 성공하면 포함된 action batch만 정확히 한 번 소비된다.
- 계측은 `ILPostProcessor`가 담당하며 reflection polling은 fallback 또는 진단 용도로만 제한한다.
- JSON 입력/출력이 하나의 production library와 adapter를 통해 처리된다.

## Non-goals

- 모든 Unity `Component`를 자동 직렬화한다.
- 임의 객체 graph, `UnityEngine.Object` reference, delegate, event를 일반 목적 serializer로 노출한다.
- 첫 단계에서 network delta protocol, persistence, replay까지 완성한다.
- 기존 WebSocket transport 또는 action method 전체를 재설계한다.
- WordVenture sample의 unrelated 변경이나 현재 dirty submodule 상태를 함께 정리한다.

## Context / Constraints

- 구현 기준 Scene code는 현재 작업 branch가 아니라 `origin/main`의 WebSocket PoC에 있다: `Runtime/ProtocolDtos.cs`, `SceneScanner.cs`, `ActionExecutor.cs`, `ArtelManager.cs`, `MiniJson.cs`.
- 현재 `feat/scene-block-구조-변경-ARTEL-9`는 `develop`의 `10ec31b`에서 시작해 위 PoC code가 없다. 구현 전 `origin/main`을 base로 branch를 재정렬하거나 필요한 선행 commit을 명시적으로 통합해야 한다.
- 기존 `SceneNode.type`은 `Button > EditText > Text > block` 우선순위로 GameObject를 단일 type으로 축약한다. 같은 GameObject의 복수 component가 손실되는 직접 원인이다.
- 제안 wire 구조는 `scene -> blocks -> components -> states/actions`다. `block`은 DOM의 `div`처럼 GameObject hierarchy만 나타내고, 기능과 관찰 정보는 component가 가진다.
- wire의 `actions`는 외부에서 실행 가능한 method metadata가 아니라 Attribute 함수의 실제 invocation history다. 기존 inbound `ACTION` command와 의미가 다르므로 code/domain 이름은 `ActionInvocation` 또는 `MethodInvocation`으로 구분한다.
- 예시 JSON의 `childern`은 기존 PoC plan에서도 오타로 판단했던 field다. 새 계약은 `children`으로 고정하고 golden fixture로 재발을 막는다.
- scene id와 첫 block id가 모두 `1`인 예시는 namespace가 다르면 허용 가능하지만, agent/action target이 id 하나만 받는 현재 protocol에서는 모호하다. 전역 unique id 또는 typed id를 확정해야 한다.
- 기존 JSON 경로가 inbound `MiniJson`, outbound `JsonUtility`로 분리돼 동일 wire 계약을 서로 다르게 처리한다.
- Unity target은 `2022.3.34f1`; runtime package는 `Packages/kr.artel.sdk`, asmdef는 `Artel.Runtime`이다.
- `ILPostProcessor`는 Editor/build-time assembly에서만 실행되어야 한다. processor와 Cecil dependency를 runtime assembly에 노출하지 않는다.
- domain model은 Unity object/reference와 runtime behavior를 소유할 수 있지만 DTO는 JSON-safe value만 가져야 한다.
- component와 tracked member 순서는 Unity component order 및 명시적 metadata order로 고정해 snapshot을 결정적으로 만든다.

## Confirmed Tracking Semantics

- `[ArtelState]`는 history/list를 저장하지 않는다. Scene scan 시 attributed field/property의 current value를 읽어 `states`를 만든다.
- `[ArtelAction]` method invocation은 해당 component instance의 action buffer에 누적한다.
- 정상 return은 `success = true`, return value, timestamp를 기록한다. `void` return value는 `null`이다.
- exception은 `success = false`, error metadata, timestamp를 기록한 뒤 original exception을 다시 throw한다.
- scan은 action buffer를 비우지 않는다. 전송할 records와 마지막 sequence watermark만 snapshot한다.
- WebSocket send 성공 후 watermark 이하 action만 제거한다. scan과 send 사이 추가된 action은 다음 batch에 남긴다.
- send 실패 시 commit하지 않는다. 같은 action batch를 다음 전송에서 재시도한다.
- ILPP는 tracking interface와 private non-serialized action buffer field/accessor를 component class에 주입한다.
- async/iterator/coroutine 완료 추적은 일반 return과 다르므로 1차 구현에서 compiler diagnostic으로 거부한다.

## Approach (Checklist)

- [ ] **Step 0: Recon** (base, protocol, tracking semantics 확정)
  - [ ] `ARTEL-9` branch를 `origin/main` 기준으로 재생성/rebase할지, PoC commit을 merge할지 결정한다. 기존 branch history와 unrelated dirty files는 보존한다.
  - [ ] 현재 consumer가 기대하는 `GAME_STATE.scene`, action target id, `type/content/placeholder` 계약을 문서화하고 golden JSON fixture로 고정한다.
  - [ ] 상태 Attribute와 action Attribute 계약을 각각 확정한다: 적용 대상, tag/name override, 지원 value/return type, instance/static 제외 여부.
  - [ ] action batch 소비 시점을 확정한다. 기본안은 snapshot 생성 시점이 아니라 WebSocket send 성공 시점에 해당 batch를 acknowledge/제거한다.
  - [ ] Unity 2022.3 호환 JSON package와 ILPP API를 작은 compile spike로 검증한다. 기본 후보는 Unity package 형태의 Newtonsoft.Json이며 exact version은 sample project resolve 결과로 pin한다.

- [x] **Step 1: Domain과 DTO 경계 설계**
  - [ ] `Runtime/Domain/`에 scene snapshot domain을 추가한다.
    - `SceneSnapshot`: scene identity와 root block collection
    - `SceneBlock`: GameObject identity/name, child block collection, component collection
    - `SceneComponent`: stable component kind, action target handle, state collection, pending invocation collection
    - `TrackedState`: tag, member name, normalized type, scan-time current value
    - `ActionInvocation`: tag, method name, success/error, normalized return value, occurred-at timestamp, sequence id
  - [ ] `Runtime/Protocol/`에 JSON 전용 DTO를 둔다. public fields와 serializer annotation은 이 경계에만 제한한다.
    - `SceneDto.children: List<SceneBlockDto>`
    - `SceneBlockDto.components: List<SceneComponentDto>`
    - `SceneComponentDto.states: List<StateDto>`
    - `SceneComponentDto.actions: List<ActionInvocationDto>`
    - timestamp는 locale 문자열 대신 ISO 8601 UTC 또는 Unix milliseconds 중 하나로 고정한다.
  - [ ] `SceneSnapshotMapper`가 domain을 DTO로 변환한다. Unity component/reference가 DTO로 누출되지 않도록 한다.
  - [ ] 구 wire 호환 정책을 정한다. 권장안은 protocol version을 추가하고, 한 release 동안 legacy projection 또는 명시적 breaking change 중 하나를 선택한다.

- [ ] **Step 2: SceneScanner와 action mapping 변경**
  - [ ] `SceneScanner`를 hierarchy scan과 component adapter scan으로 분리한다.
  - [ ] `ISceneComponentAdapter` 계약을 정의하고 Button, legacy/TMP InputField, legacy/TMP Text adapter를 구현한다.
  - [ ] GameObject마다 모든 지원 component를 수집한다. 기존 else-if 단일 kind 선택을 제거한다.
  - [ ] 일반 Unity component도 state/action Attribute가 하나 이상 있으면 component DTO에 포함한다. UI adapter 대상 여부와 Attribute 관찰 대상 여부를 분리한다.
  - [ ] action 가능한 component마다 target id를 발급하고 `id -> component target` snapshot mapping을 만든다. block id와 action target id 의미를 분리한다.
  - [ ] component adapter 등록 순서와 serialized output 순서를 고정한다. 동일 scene scan은 동일 구조와 순서를 내야 한다.
  - [ ] `ActionExecutor`가 block이 아닌 component target capability를 검사해 click/text action을 실행하도록 변경한다.

- [x] **Step 3: JSON library 통합**
  - [ ] `com.unity.nuget.newtonsoft-json` 호환 version을 package dependency로 pin하고 asmdef reference를 검증한다.
  - [ ] `IJsonCodec`과 Newtonsoft 기반 구현을 추가한다. `ArtelManager`는 parser/serializer static API 대신 이 adapter에만 의존한다.
  - [ ] `MiniJson`과 `JsonUtility` 사용을 protocol 경로에서 제거한다. migration 완료 후 `MiniJson.cs`를 삭제한다.
  - [ ] unknown field, missing field, enum/value conversion, malformed request 처리 정책을 serializer settings에 명시한다.
  - [ ] DTO golden JSON round-trip test로 property name, null 처리, list/object shape를 고정한다.

- [x] **Step 4: Attribute 상태와 함수 호출 추적 API**
  - [x] `Runtime/Tracking/`에 역할별 opt-in Attribute를 정의한다.
    - `[ArtelState("hp")]`: field/property의 current value를 `states`에 노출
    - `[ArtelAction("attack")]`: method invocation을 `actions`에 누적
  - [x] `StateReader`를 추가한다. 별도 state registry/list 없이 component instance의 attributed field/property를 scan 시 읽는다.
  - [x] `ActionInvocationBuffer`를 추가한다. component instance별 bounded FIFO로 invocation record와 monotonic sequence id를 보관한다.
  - [x] scene/component mapping이 아직 만들어지기 전 호출도 instance 기준으로 누적하고, 다음 scan에서 해당 component에 결합한다.
  - [x] snapshot은 buffer를 비우지 않고 records + sequence watermark를 원자적으로 lease한다. send 성공 시 watermark 이하만 commit 제거하고, 실패 시 그대로 둔다.
  - [x] buffer 최대 크기와 overflow 정책을 명시한다. 기본안은 bounded queue + oldest drop이다.
  - [x] boxing/serialization 가능한 값은 Newtonsoft.Json에 위임하고 현재 제한을 README에 명시한다.
  - [x] action buffer는 component instance field로 소유해 instance lifecycle과 함께 정리한다.

- [ ] **Step 5: ILPostProcessor 구현**
  - [x] `Editor/CodeGen/Artel.CodeGen.asmdef`와 별도 `ILPostProcessor`를 추가한다. Unity compilation pipeline와 Mono.Cecil reference는 Editor/codegen assembly에만 둔다.
  - [ ] `[ArtelState]` member와 `[ArtelAction]` method metadata를 수집하고 deterministic member/method id를 생성한다.
  - [x] `[ArtelState]` setter/write는 계측하지 않는다. scanner가 reflection metadata를 cache한다.
  - [x] `[ArtelAction]` method의 모든 `ret` branch를 공통 success epilogue로 rewrite한다.
  - [x] `void`, value/reference return, multiple returns를 구분하고 정상 return을 success action으로 기록한다.
  - [x] exception path는 failure action을 기록한 뒤 original exception을 rethrow한다.
  - [x] async method와 iterator/coroutine은 1차 지원에서 compiler diagnostic으로 거부한다.
  - [x] action parameter capture는 현재 요구에 없으므로 1차 범위에서 제외한다.
  - [x] processor가 SDK 자체 tracker call을 재계측하지 않도록 assembly/type/member filter와 idempotency marker를 둔다.
  - [ ] PDB/sequence point 보존, generic/nested type, inheritance, auto-property, exception path를 test fixture assembly로 검증한다.
  - [x] invalid target에는 조용한 skip 대신 Unity compiler diagnostic을 출력한다.

- [ ] **Step 6: Integration과 migration**
  - [ ] `ArtelManager`가 codec, scanner, mapper, tracker를 명시적으로 조립하도록 한다. hidden static dependency를 피한다.
  - [x] scan 시 current component states와 action buffer snapshot을 합성한다. 이때 buffer를 변경하지 않는다.
  - [x] `ArtelManager` send 성공 후 watermark commit, send 실패/exception 시 no-op 경로를 보장한다.
  - [ ] sample에 한 GameObject가 Button + Text 등 여러 지원 component를 가진 fixture, `[ArtelState]` property, `[ArtelAction]` method fixture를 추가한다.
  - [x] README에 Attribute 사용법, 지원 type, ILPP 제한, JSON/protocol example을 기록한다.

- [ ] **Step 7: Tests**
  - [ ] Runtime EditMode tests: block/component mapping, 복수 component 보존, child traversal, inactive object 정책, deterministic order/id, action routing.
  - [ ] JSON tests: request parse, response serialize, round-trip, malformed/unknown input, golden fixture compatibility.
  - [ ] State tests: tag/name/type/value mapping, null, cleanup, unsupported type diagnostic.
  - [ ] Action buffer tests: order, sequence, bounded overflow, failed send retry, concurrent enqueue during send. (snapshot/commit watermark는 완료)
  - [x] ILPP tests: runtime fixture assembly를 rewrite한 뒤 success/failure/void return, original return/exception, interface 주입을 검증한다.
  - [ ] PlayMode integration: attributed method를 여러 번 호출한 뒤 다음 scan에 순서대로 포함되고, successful send 뒤 다음 scan에서는 제거되는지 검증한다.
  - [ ] package consumer compile: WordVenture Unity 2022.3.34f1에서 Runtime/Editor asmdef와 Newtonsoft dependency resolve를 확인한다.

- [ ] **Step 8: Rollout / Rollback**
  - [ ] protocol breaking change면 version negotiation 또는 release note를 먼저 적용한다.
  - [ ] ILPP opt-in Attribute 미사용 assembly는 IL 변경이 없음을 검증한다.
  - [ ] 기능을 작은 commit으로 분리한다: model/scanner, JSON migration, tracking runtime, ILPP, tests/docs.
  - [ ] rollback은 새 protocol projection과 ILPP/codegen assembly를 commit 단위로 revert 가능하게 유지한다.

## Validation

- **Commands to run:**
  - `git diff --check`
  - Unity 2022.3.34f1 batchmode EditMode test command — project에 test assembly와 CI command를 추가한 뒤 exact command 확정
  - Unity 2022.3.34f1 batchmode PlayMode test command — project에 test scene/assembly를 추가한 뒤 exact command 확정
  - sample project batchmode compile/import — 로컬 Unity executable path 확인 후 exact command 확정
  - focused golden JSON/ILPP fixture tests
- **Expected output:**
  - 한 GameObject의 모든 지원 component가 하나의 block 아래 보존된다.
  - domain과 DTO JSON shape가 golden fixture와 일치한다.
  - Attribute method 정상 호출당 invocation record가 정확히 한 번 발생하고 original return/side effect가 보존된다.
  - 전송 실패 시 action batch가 유실되지 않고, 성공 시 중복 전송되지 않는다.
  - Attribute 미사용 assembly의 IL은 의미 있게 변하지 않는다.
  - Editor/Player compile error, Cecil resolution error, JSON dependency conflict가 없다.

현재 `.agents/docs/project.md`에 test/build command가 TODO이므로 구현 전 실제 Unity batchmode command를 확정하고 문서를 갱신한다. 검증 명령은 추정해서 실행하지 않는다.

## Risks & Rollback

- **Risks:**
  - 현재 ARTEL-9 branch base에 구현 대상 PoC가 없어 잘못된 base에서 개발하면 대규모 충돌 또는 누락이 생긴다.
  - wire shape 변경은 기존 browser/client consumer를 즉시 깨뜨릴 수 있다.
  - 여러 `ret`, exception, async/coroutine을 잘못 계측하면 original method semantics 또는 stack balance를 깨뜨릴 수 있다.
  - ILPP/API/Cecil version 결합은 Unity upgrade 시 깨질 수 있다.
  - arbitrary component reflection은 성능, 보안, 직렬화 순환 문제를 만든다. allowlist adapter와 opt-in Attribute가 필요하다.
  - Newtonsoft package가 consumer project의 다른 version과 충돌할 수 있다.
  - tracker registry가 destroyed Unity object를 강하게 참조하면 memory leak이 발생한다.
- **Rollback steps:**
  - branch base 정렬은 기존 branch ref/tag를 보존한 뒤 수행한다.
  - protocol adapter/legacy projection을 되살려 consumer를 구 shape로 복귀시킨다.
  - ILPP assembly와 Attribute 사용을 제거하면 runtime scan-only path로 복귀 가능하게 유지한다.
  - JSON migration commit만 revert해 기존 codec으로 임시 복귀할 수 있게 commit 경계를 분리한다.

## Open Questions

- exception action에 exception type/message만 보낼지 stack trace까지 보낼지 확정 필요. 기본안은 type/message만 전송한다.
- action parameters는 현재 요구에 없으므로 1차 범위에서 제외한다.
- WebSocket client가 여러 개면 action batch는 client별 acknowledge인가, 최초 successful broadcast 후 전역 소비인가?
- 기존 `SceneNode` JSON consumer가 존재하는가? 존재하면 legacy projection/버전 협상이 필요하다.
- component DTO는 범용 `attributes` map이 필요한가, `ButtonDto`/`TextDto` 같은 typed DTO가 필요한가?
- 비활성 GameObject/component와 disabled component도 scan/track 대상인가?
