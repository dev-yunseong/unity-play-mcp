# 2026-07-15 — Jira 기반 개발 워크플로로 전환

- Date: 2026-07-15
- Jira: None
- Status: Complete

## Goal

GitHub Issue와 branch 생성을 담당하는 저장소 지침을 제거하고, 외부에서 제공된 Jira 및 branch context를 사용하는 워크플로로 전환한다.

## Non-goals

- Jira 프로젝트 키나 자동화 도구를 새로 결정하지 않는다.
- SDK 런타임 코드를 변경하지 않는다.

## Context / Constraints

- Jira 티켓은 있을 때 연결하지만, 티켓 부재가 개발과 PR 생성을 막지 않아야 한다.
- 브랜치는 번호 대신 의도를 드러내는 짧은 설명을 사용해야 한다.
- 기존 GitHub Issue 기반 로컬 skill과 agent 지침도 같은 정책을 따라야 한다.
- issue와 branch 생성 workflow 파일은 저장소에서 제거한다.
- 커밋 메시지는 제한된 type과 한글 변경 사항 형식을 사용해야 한다.

## Approach (Checklist)

- [x] **Step 0: Recon** GitHub Issue 및 숫자 브랜치명 참조 위치 확인
- [x] **Step 1: Implementation** workflow, branch, PR, agent, skill 지침을 Jira 기준으로 수정
- [x] **Step 2: Tests** 전체 참조 검색, Markdown diff, Git 상태 검증
- [x] **Step 3: Rollout / Rollback** 설명형 브랜치로 PR 교체, 잘못 만든 GitHub Issue 종료

## Validation

- **Commands to run:** `rg -n -i "gh issue|github issue|issue-number|Closes #" AGENTS.md .agents`; `git diff --check`
- **Expected output:** 강제 GitHub Issue 흐름 없음; whitespace 오류 없음

## Risks & Rollback

- **Risks:** Jira 키 형식이 아직 정의되지 않아 자동 링크 규칙은 추가하지 않음
- **Rollback steps:** 이 문서 변경 commit revert

## Open Questions

- Jira 프로젝트 키와 CLI 연동 규칙은 팀에서 확정 후 별도 추가한다.
