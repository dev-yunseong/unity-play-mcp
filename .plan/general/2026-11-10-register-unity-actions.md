# 2026-11-10 — Unity 액션을 클래스와 어트리뷰트로 등록

- Date: 2026-11-10
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/129
- Status: 구현 및 로컬 검증 완료

## Goal

Unity 액션은 `IUnityAction`을 구현하고 `[UnityAction]`에서 이름, 설명과 위치 인자 안내를 선언한다. Unity에서 발견한 프로젝트 액션을 MCP의 `get_play_capabilities`에 노출하고, 기존 `perform_actions`로 실행한다.

## Non-goals

액션별 MCP 도구 추가, 기존 액션 입력 스키마와 응답 변경, 배치 실행과 대상 조회 재설계.

## Context / Constraints

- 기존 Unity wire method, 실패 메시지, 인자 변환, 코루틴 대기와 생명주기를 보존한다.
- 기존 MCP 입력 스키마의 이름 있는 인자와 대상 해석을 유지한다. 새 액션용 `{ method: "unity_action", name, params }` 항목 하나를 추가한다. Unity wire에는 예약 method `unity_action`과 평탄한 위치 인자 `[name, ...params]`로 보내고, 별도 프로젝트 레지스트리에서만 이름을 조회한다.
- 기본 액션을 프로젝트 액션으로 덮어쓸 수 없다. 호스트가 예약 method를 mutating으로 분류해 입력 소유권 검사와 input revision 갱신을 기존과 같은 위치에서 수행한다.
- UnityLinker가 리플렉션으로 만드는 클래스와 기본 생성자를 보존하도록 Unity의 `PreserveAttribute`를 상속한 어트리뷰트를 사용한다.

## Approach (Checklist)

- [x] **Step 0: Recon**. Unity의 `ActionExecutor`, 호스트의 입력 잠금, MCP의 배치 스키마와 capability 경로를 확인.
- [x] **Step 1: Implementation**. 실행 메서드를 인터페이스 구현 클래스로 이동하고 레지스트리로 dispatch. executor 수명에 맞춘 context에 기존 입력 의존성과 일시 정지 및 초기 장면 상태를 유지. 런타임 어셈블리의 기본 액션과 인터페이스·어트리뷰트를 참조하는 게임 어셈블리의 프로젝트 액션을 한 번 탐색하고, 실행 인스턴스는 executor에 귀속. 예약 이름과 중복 이름은 프로젝트 목록에서 제외하고 진단을 남긴다.
- [x] **Step 2: Tests**. 같은 클래스 선언이 목록에 나타나고 배치에서 실행되는 경로, 기본 액션 이름을 프로젝트 경로로 호출할 수 없는지, 두 클라이언트의 입력 잠금이 적용되는지, 중첩 코루틴과 대기 조건의 예외가 배치를 멈추지 않는지 확인한다. MCP capabilities가 목록을 유지하고 기존 perform_actions로 보내는 경로도 검증한다.
- [x] **Step 3: Rollout / Rollback**. 단일 확장 항목을 기존 스키마에 추가한다. 구버전 Unity에서도 기존 액션을 사용하며, 프로젝트 액션을 요청하면 미지원 결과를 반환한다.

## Validation

- MCP 서버에서 `npm run build` 및 `npm test`를 실행해 테스트 392개가 통과했다. 동일한 lockfile의 설치본을 사용했다.
- Unity 2022.3.34f1에서 기존 테스트 프로젝트로 EditMode 322개와 PlayMode 87개가 통과했다. 기본 실행에서 제외되는 성능 벤치마크 1개는 건너뛰었다.

## Risks & Rollback

- **Risks:** 런타임 반사 탐색, 예약 이름 충돌, 게임 액션의 예외가 배치의 다음 실행에 영향을 줄 수 있다. 탐색 범위를 SDK 참조 어셈블리로 한정하고, 중복은 묵시적으로 선택하지 않는다. 프로젝트 액션의 호출과 중첩 IEnumerator 진행 중 예외는 해당 항목의 실패로 바꾸고 나머지 배치를 실행한다.
- **Rollback steps:** PR 커밋을 되돌리면 기존 분기와 도구 계약으로 복귀한다.

## Open Questions

없음. MCP의 `get_play_capabilities`에서 목록을 확인하고 기존 `perform_actions`에서 실행한다.

## 검토 반영

- 계획 검토에서 예약 wire method, 프로젝트 전용 조회, 입력 소유권 및 executor 상태의 수명, 중첩 코루틴 예외와 MCP 응답 검증을 반영했다.
- 페어 리뷰에서 `WaitUntil` 등 Unity 대기 조건의 예외를 발견했다. Unity의 대기 시점에 조건을 실행하면서 예외를 해당 액션의 실패로 반환하도록 보완하고 회귀 테스트를 추가했다. 직렬화할 수 없는 프로젝트 결과도 해당 항목의 실패로 처리한다.
