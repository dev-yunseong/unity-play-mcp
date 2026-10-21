# 2026-10-21 — hot reload 실행 상태 복구

- Date: 2026-10-21
- GitHub Issue: #115
- Status: Implemented

## Goal
Play 중 script reload 후 host와 overlay의 실행 객체가 다시 초기화되고 유휴 처리 및 MCP 요청이 예외 없이 동작한다.

## Non-goals
게임 코드 변경, 모험 자동 클리어를 위한 상태 조작, 버전 통합.

## Context / Constraints
ArcaneCasters SDK 0.5.1에서 script reload 직후 SuspendIfIdle:383 NullReferenceException 반복. Unity hot reload는 private 직렬화 가능 필드도 복원한다. ownsRuntime=true가 남고 playApi 등 일반 객체는 사라져 초기화가 생략된다. 기존 simulation은 public/SerializeField만 보존해 이를 놓쳤다. cursor/keyboard builtOverlay도 같은 가정에 의존한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 실제 로그와 Unity 2022.3 직렬화 문서, 호스트 및 overlay lifecycle을 확인했다.
- [x] **Step 1: Implementation** ownsRuntime 및 두 builtOverlay를 NonSerialized로 명시. simulation을 hot reload 대상 private 필드/지원 타입까지 보존하도록 수정하되 범용 serializer 구현은 피한다. 다른 ephemeral primitive 상태는 lifecycle reset이 충족하는지 확인한다.
- [x] **Step 2: Tests** simulation private primitive 보존/NonSerialized 제외 자체 테스트; host reload 후 play API와 idle 경로, overlay 재구성/readonly key collection 회귀 검증. 변경된 simulation으로 기존 전체 PlayMode 실행. 가능하면 격리 프로젝트에서 실제 script reload를 구동해 비교한다.
- [ ] **Step 3: Rollout / Rollback** draft 수정 PR. 배포본 게임 프로젝트를 직접 패치하지 않고 다음 patch 배포 후 재검증.

## Validation
- **Commands to run:** Unity 2022.3.34f1 EditMode/PlayMode throwaway test project.
- **Expected output:** 전체 통과, 재로드 후 idle 검사와 요청 응답 정상, overlay 중복 없음.

## Risks & Rollback
- **Risks:** simulation을 Unity보다 넓게 보존하면 허위 통과 위험. 대상 component에 존재하는 타입과 지원/제외 규칙을 명시적으로 테스트한다. 실제 reload 증거를 우선한다.
- **Rollback steps:** 수정 커밋 revert. 게임 사용자 변경 보존.

## Open Questions
없음.

## Review
Fast/medium/heavy plan review 및 구현 pair review PASS. simulation은 readonly/NonSerialized를 먼저 제외하고 primitive/enum/string/UnityEngine.Object/현재 component의 Vector2 등 지원 타입을 보존한다. 범용 IsSerializable 판정은 쓰지 않는다. 기존 lifecycle은 processingActions, collection/session/input 상태를 정리하며 새 요청은 idle 시계를 갱신한다. private이라 사라진다는 주석을 명시적 NonSerialized 설명으로 고친다. 기존 숨김/표시 overlay reload tests를 함께 실행한다.

## Results
- Unity 2022.3.34f1 PlayMode 86/86, EditMode 321/321 통과.
- 격리 프로젝트에서 EditorUtility.RequestScriptReload 실제 실행: PlayApi/transport/readings/overlay 2개 복구 및 15초 유휴 종료 PASS.
- 실제 재로드 보조 스크립트와 결과는 로컬 .unity-test-project/HotReloadProbe.cs 및 115-real-reload.txt에 보관. 게임 프로젝트/배포본은 수정하지 않음.
