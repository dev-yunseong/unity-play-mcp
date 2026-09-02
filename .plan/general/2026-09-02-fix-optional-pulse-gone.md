# 2026-09-02 — `PULSE.gone` 생략 허용

- Date: 2026-09-02
- GitHub Issue: #23
- Status: Ready for review

## Goal

Unity runtime이 사라진 객체를 확정할 수 없어 `gone`을 생략한 `PULSE`도 Node MCP server가
정상적으로 받아 `get_scene_state`에 반영한다.

## Non-goals

- Unity runtime의 scan 또는 `gone` 판정 규칙 변경
- malformed frame 로깅 정책 변경
- 다른 `PULSE` 필드의 optional contract 확대

## Context / Constraints

Unity는 truncated scan과 whole reading에서 `gone`을 의도적으로 생략한다. 이때 빈 배열을
직렬화하면 “사라진 객체가 없다”는 확정과 “확정할 수 없다”는 상태가 같은 wire shape이 된다.
Node 쪽은 두 경우 모두 기존 객체를 제거하지 않으면 되므로, 생략을 허용하고 fold에서 제거
목록을 빈 순회로 취급한다.

## Changes

- `mcp/src/connection.ts` — `gone`이 없거나 배열인 `PULSE`를 유효하게 판정
- `mcp/src/pulse.ts` — `gone`을 optional contract로 선언하고 누락 시 제거를 건너뜀
- `mcp/test/connection.test.ts` — Unity가 실제로 보내는 `gone` 없는 frame이 fold되는 회귀 테스트
- `mcp/test/pulse.test.ts` — `gone` 없는 delta가 기존 객체를 보존하는 fold 테스트

## Validation

- `node node_modules/typescript/bin/tsc` — 성공, 신규 테스트를 `dist/`에 컴파일
- `npm test` — 44/44 통과
- Unity package code는 변경하지 않아 Unity EditMode/PlayMode suite는 실행하지 않음
- `npm run build`의 마지막 `cp` 단계는 Windows와 호환되지 않아 실행하지 않음
