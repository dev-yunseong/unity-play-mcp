# 2026-10-21 — IL 분석 재귀 종료 보장

- Date: 2026-10-21
- GitHub Issue: #112
- Status: Implemented; release pending

## Goal
ArcaneCasters Assembly-CSharp 분석 중 발생하는 stack overflow를 막는다.

## Non-goals
분석 기능 재설계, 정상 요약 경고 정리, 버전 원본 통합.

## Context / Constraints
0.5.0 설치 후 Editor.log에 WhichScene/Describe/Read/Arithmetic 재귀가 반복된다. Describe→Arithmetic과 Read→Describe가 깊이를 초기화한다. 기존 수신자/산술식 분석 한도를 유지하며 이 경계에서 깊이를 잃지 않게 한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 로그와 두 초기화 지점을 확인했다.
- [ ] **Step 1: Implementation** IlReading의 재귀 깊이를 전파하고 모든 관련 경계를 확인한다. 필요한 경우 기존 한도를 구분해 정상 분석 손실을 최소화한다.
- [ ] **Step 2: Tests** 자기 참조 필드 갱신, 상호 참조, 산술식과 지역 변수 혼합의 종료를 검증하고 기존 EditMode suite를 실행한다. 위험한 기존 버전 재현은 격리 프로세스에서만 실행한다.
- [ ] **Step 3: Rollout / Rollback** 패치 PR을 작성한다. 배포된 0.5.0을 변경하지 않으며 새 패치 배포 후 실제 게임 검증을 재개한다.

## Validation
- **Commands to run:** 저장소 Unity throwaway 프로젝트 EditMode 테스트. 가능하면 ArcaneCasters 컴파일 대상 assembly로 회귀 검증.
- **Expected output:** 모든 테스트 통과, 분석이 제한된 결과로 종료되며 기존 scene/산술식 분석 유지.

## Risks & Rollback
- **Risks:** 공유 깊이 때문에 정상 표현식 분석이 조기 중단될 수 있다. 기존 테스트와 추가 정상 케이스로 확인한다.
- **Rollback steps:** 패치 커밋 revert. ArcaneCasters의 기존 사용자 변경은 보존한다.

## Open Questions
없음.

## Review
Fast/medium PASS. 두 호출에서 기존 depth를 전파한다. 혼합 식은 기존 한도를 공유하므로 더 보수적으로 잘릴 수 있다. 순환 필드 fallback, 4단 산술식, 정상 활성 씬 캐시를 검증한다. CallName의 Arguments는 within=null이며 앞선 IL만 읽으므로 이 필드 순환 수정 대상에서 제외한다.

Heavy plan review PASS; pair-review-critic PASS. 두 호출부를 수정하고 Cecil IL 회귀 테스트 6개를 추가했다. Unity 2022.3.34f1 격리 프로젝트 EditMode 321/321 통과. 결과: `.unity-test-project/recursion-edit.xml`. 실제 ArcaneCasters 플레이 검증은 패치 배포 후 진행한다. 게임 설치본은 v0.5.0을 유지했고 PackageCache를 수정하지 않았다.
