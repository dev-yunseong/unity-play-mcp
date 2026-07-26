# 2026-07-26 — scan_all_scenes 전체 직렬화 필드 스캔

- Date: 2026-07-26
- Jira: ARTEL-135
- Status: Implemented

## Goal

`scan_all_scenes`에 `full` 모드를 추가한다. 이 모드는 게임이 작성한 MonoBehaviour의 **직렬화 필드
전체**를 읽고, **비활성 오브젝트까지 포함**해 씬 트리를 만든다. 파라미터를 주지 않으면 지금과 완전히
같은 결과가 나온다.

## Non-goals

- **`GAME_STATE` 경로 변경.** 폴러/해시 트래커/에이전트 관찰값은 지금 그대로 `[ArtelState]`만 본다.
  전체 필드를 상시 흘리면 `GameStateTransformer`의 `observables` 플랫 맵이 잡값으로 덮인다.
- **Unity 내장 컴포넌트(Transform, Image, Camera, Rigidbody, TMP_Text …) 직렬화.** 필드 수가
  많고 대부분 에이전트에 무의미하다. 게임 어셈블리의 MonoBehaviour만 대상이다.
- **프로퍼티 읽기.** Unity가 직렬화하지 않는다. `[ArtelState]`가 붙은 프로퍼티만 기존대로 읽힌다.
- **응답 페이지네이션/스트리밍.** ARTEL-88이 남긴 열린 질문 그대로 둔다. 여기서는 상한만 건다.
- **비활성 오브젝트를 조작 대상으로 삼는 것.** 스캔 결과에는 실리지만 클릭 가능성은 별개 문제다
  (ARTEL-133).

## Context / Constraints

- **컴포넌트 필터가 두 겹이다.** [SceneScanner.cs:168](../../Packages/kr.artel.sdk/Runtime/SceneScanner.cs)
  은 `IArtelActionSource`이거나 `[ArtelState]`를 가진 컴포넌트만 통과시킨다. 필드를 넓히는 것만으로는
  부족하고 이 필터도 함께 열어야 한다.
- **`SceneScanner.Scan()`은 `ISceneSnapshotScanner`의 구현이다.** 시그니처를 바꾸면 폴러가 깨진다.
  기존 `Scan()`은 남기고 `Scan(SceneScanOptions)` 오버로드를 추가한다.
- **Newtonsoft에 Unity 값 타입을 그대로 넘기면 안 된다.** `Vector3.normalized`가 다시 `Vector3`라
  프로퍼티 직렬화가 무한 재귀한다. 리더가 **필드만** 훑어 Dictionary/List/원시값으로 미리 낮춘다.
  코덱에 도달하는 값은 항상 원시값·문자열·`Dictionary<string, object>`·`List<object>`뿐이다.
- **`UnityEngine.Object` 참조는 재귀 금지.** 스텁(`instanceId`/`name`/`type`)으로만 싣는다. 안 그러면
  `GameObject` 필드 하나가 씬 전체를 다시 끌고 온다. Unity의 가짜 null(`== null`)도 걸러야 한다.
- **런타임 리플렉션은 순환한다.** Unity 직렬화는 깊이에서 잘리지만 리플렉션은 안 잘린다. 깊이 상한 +
  현재 경로의 참조 visited 집합이 둘 다 필요하다.
- **상태 정렬을 유지한다.** `SceneStateHashTracker`가 순서에 민감하다. full 모드는 폴러가 쓰지 않지만
  기존 `StateReader`의 이름순 정렬 규칙을 그대로 따른다.
- **`AllSceneScanner`의 마지막 복구 스캔은 기본 모드여야 한다.**
  [AllSceneScanner.cs:93](../../Packages/kr.artel.sdk/Runtime/AllSceneScanner.cs)의 `scanner.Scan()`이
  `targetsById`를 되살리는 자리다. 여기서 full로 훑으면 비활성 오브젝트가 조작 대상 맵에 남는다.
- **파라미터는 위치 인자 리스트다.** `ActionRequestDto.Parameters`는 `List<object>`이고 기존
  `button_click`도 `[targetId]` 형태다. `scan_all_scenes`는 `[]`(기본) 또는 `["full"]`을 받는다.
- **씬 JSON은 서버와의 계약이다.** 필드 추가만 한다. `active`는 항상 실어 기본 모드에서도 값이 있게
  하고(항상 `true`), 서버는 필드 부재를 하위 호환으로 처리한다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `SceneScanner`/`StateReader`/`SceneStatePoller`/`AllSceneScanner`/
      `ArtelManager`의 `scan_all_scenes` 분기, `SceneSnapshotMapper`, `StateDto`, 서버측
      `SdkState.value: Any?` 확인 완료. 와이어 포맷 변경 불필요.
- [x] **Step 1: Implementation**
  - `Runtime/Tracking/SceneScanOptions.cs` 신규 — `IncludeAllSerializedFields`, `IncludeInactive`,
    프리셋 `Default`/`Full`.
  - `Runtime/Tracking/SerializedFieldReader.cs` 신규 — Unity 직렬화 규칙(public 인스턴스 필드 중
    `[NonSerialized]` 제외, 또는 `[SerializeField]`가 붙은 비공개 필드; static/readonly 제외)으로
    필드를 고르고, 값을 원시값/딕셔너리/리스트로 낮춘다. 상한: 깊이 5, 배열·리스트 64개, 문자열
    1024자. `UnityEngine.Object`는 스텁. 경로 visited 집합으로 순환 차단.
  - `Runtime/Tracking/StateReader.cs` — `Read(component, includeSerializedFields)` 추가.
    `[ArtelState]` 결과가 먼저, 같은 이름의 직렬화 필드는 건너뛴다(태그 보존). 직렬화 필드의 태그는
    빈 문자열.
  - `Runtime/SceneScanner.cs` — `Scan(SceneScanOptions)` 오버로드, `Scan()`은 `Default` 위임.
    `ScanScene`/`ScanTransform`이 옵션을 받아 비활성 순회 여부를 가른다. `CreateComponents`는 full
    모드에서 게임 어셈블리 MonoBehaviour를 전부 포함한다.
  - `Runtime/Domain/SceneBlock.cs` + `Protocol/Dto/SceneBlockDto.cs` +
    `Protocol/Mapping/SceneSnapshotMapper.cs` — `active` 추가.
  - `Runtime/AllSceneScanner.cs` — `ScanAll(SceneScanOptions, …)`. 순회는 옵션대로, 마지막 복구
    스캔은 `SceneScanOptions.Default`.
  - `Runtime/ArtelManager.cs` — `scan_all_scenes` 파라미터 파싱. 없으면 `Default`, `"full"`이면
    `Full`, 그 밖은 `ActionResultDto.Failure`.
  - `Runtime/ArtelTestPage.cs` — `Scan all scenes (full)` 버튼. 결과는 라이브 씬과 분리된 고정
    섹션에 그려 `GAME_STATE` 푸시에 덮이지 않게 하고, `Clear`까지 남긴다. 컴포넌트의 states는
    기본 펼침 + 접기 가능, 비활성 블록은 라벨과 흐림 처리, 원본 JSON은 disclosure에 보관.
  - `README.md` — full 모드와 상한, 부작용, 테스트 페이지 사용법 갱신.
- [x] **Step 2: Tests**
  - `Tests/Runtime/SerializedFieldReaderTests.cs` 신규 — 공개 필드/`[SerializeField]` 포함,
    `[NonSerialized]`·static·프로퍼티 제외, `Vector3`가 `{x,y,z}`로 낮아지는지, `GameObject` 참조가
    스텁인지, 자기 참조 그래프가 깊이 상한에서 멈추는지, 배열·문자열 절단.
  - `Tests/Runtime/SceneScannerTests.cs` — full 모드에서 비활성 자식이 `active: false`로 실리는지,
    기본 모드에서 여전히 빠지는지. 내장 컴포넌트가 full에서도 안 실리는지.
  - `Tests/Runtime/SceneJsonContractTests.cs` — 블록 JSON의 `active`, full `ALL_SCENES`의 states
    형태.
- [x] **Step 3: Rollout / Rollback** — 순수 추가. 파라미터 없이 부르면 기존 동작. 서버 배포 순서 제약
      없음. 롤백은 `git revert`.

## Validation

- **Commands to run:** Unity Test Runner(EditMode) — `Artel.Runtime.Tests`.
- **Expected output:** 신규 테스트 포함 전체 통과.
- **실제 수행:** 이 작업 환경에 Unity 에디터도 C# 컴파일러도 없어 **실행하지 못했다**. 컴파일조차
  검증되지 않았다.
- **자동화 공백:** ARTEL-88과 동일하게 씬 로드 순회 자체는 EditMode에서 검증 불가. 리더·필터·직렬화
  계약만 자동 검증되고 full 모드의 실제 다중 씬 순회는 수동 확인이 필요하다.
- **테스트 페이지 스크립트는 검증됨:** ARTEL-88과 같은 방식으로 페이지 스크립트를 C# 문자열에서
  추출해 Node에서 DOM 스텁과 가짜 `ALL_SCENES` 페이로드로 실행했다. full 버튼이 `["full"]`을
  보내는지, 결과가 고정 섹션에 그려지고 라이브 씬을 덮지 않는지, 뒤이은 `GAME_STATE`가 그것을
  지우지 않는지, 비활성 블록 표시와 상태 값 노출, `Clear` 동작까지 통과.

## Risks & Rollback

- **응답 크기.** ARTEL-88이 이미 씬 수 × 블록 트리를 리스크로 적었다. full은 여기에 비활성 서브트리와
  필드 전체를 곱한다. 상한(깊이·배열·문자열)이 최악을 자르지만 큰 프로젝트에서 한 메시지가 여전히 클
  수 있다. 스트리밍은 열린 질문으로 남는다.
- **필드 읽기 부작용.** 필드만 읽으므로 프로퍼티 게터의 부작용은 없다. 다만 게임이
  `[SerializeField]`에 담아 둔 자격 증명·토큰이 그대로 전송된다. 마스킹은 없다.
- **비활성 오브젝트가 `targetsById`에 등록된다.** full 스캔 도중에만이고 마지막 복구 스캔이 기본
  모드라 배치가 끝나면 사라진다. 그래도 같은 배치 안에서 뒤따르는 액션이 잠깐 비활성 대상을 볼 수
  있다. ARTEL-133의 상호작용 판정이 실제 차단을 맡는다.
- **Rollback steps:** `git revert`.

## Open Questions

- 없음.
