# 2026-07-30 — Artel 브랜드를 SDK에 적용

- Date: 2026-07-30
- Jira: ARTEL-211
- Status: Complete

## Goal

선정한 Artel 심볼과 coral 포인트 컬러를 Unity SDK 오버레이에 적용한다.

## Non-goals

- 온보딩 동작, 레이아웃 구조, 상태색 변경
- SVG 런타임 패키지 추가

## Context / Constraints

- 오버레이는 에셋 프리팹 없이 코드 생성 uGUI를 사용한다.
- Unity 기본 uGUI는 SVG를 직접 렌더링하지 않는다.
- 심볼 경로는 64×64 기준 charcoal 본체와 coral 분리 선분이다.

## Approach (Checklist)
- [x] **Step 0: Recon** 기존 오버레이 생성·색상·테스트 호출부 확인
- [x] **Step 1: Implementation** uGUI `Graphic`으로 심볼을 그리고 액션 토큰을 coral로 교체
- [x] **Step 2: Tests** 심볼 구조·색·primary 버튼 대비 검증
- [x] **Step 3: Rollout / Rollback** 패키지 변경만 배포, 문제 시 해당 커밋 revert

## Validation
- **Commands to run:** Unity EditMode에서 신규 mesh·색상 테스트 실행, `git diff --check`
- **Expected output:** 2개 테스트 통과, whitespace 오류 없음

## Risks & Rollback
- **Risks:** 런타임 생성 심볼의 화면 배율별 선 두께
- **Rollback steps:** ARTEL-211 SDK 커밋 revert

## Open Questions

- 없음
