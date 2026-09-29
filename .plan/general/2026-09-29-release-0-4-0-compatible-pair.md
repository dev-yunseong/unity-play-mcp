# 2026-09-29 — 최신 플레이 조작 기능을 호환 버전으로 검증하고 배포한다

- Date: 2026-09-29
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/73
- Status: In review (배포는 병합·release 대기)

## Goal

#69~#72 를 한 코드 기준으로 합쳐 Unity package 와 MCP server 를 함께 검증하고, 같은 commit 에서 Unity package
`v0.4.0` 과 npm `unity-play-mcp@0.4.0` 을 호환 쌍으로 배포할 준비를 한다. README 의 설치 예시와 호환 지침이
실제 배포본을 가리키게 한다.

## Non-goals

- 기능을 검증 없이 합치기. ArcaneCasters Client 게임 로직 변경. 기존 v0.2.0/v0.3.0 사용자를 강제로 올리기.

## Context / Constraints

- 작업 도중 저장소 주인이 develop(6c92690 + version bump)에서 `v0.3.0` Release 와 npm `0.3.0` 을 배포했다(PR #76,
  2026-09-29 07:47Z). 그 쌍은 #69~#72 를 담지 않는다. 그래서 이 통합 배포는 `0.4.0` 이다.
- 배포 절차(README "Release procedure"): version bump PR → develop 병합 → `v<version>` GitHub Release(사람) →
  `publish-mcp.yml` 이 npm 배포. bump 는 workflow 대신 같은 script(`set-package-version.sh`,
  `set-mcp-server-version.sh`)로 이 branch 에 넣었다 — 기능 PR 없이 0.4.0 이 먼저 나가는 일을 막기 위해서다.
- agent 는 PR 을 ready 로 바꾸거나 병합하지 않는다. Release 생성 도구도 없다.

## Approach (Checklist)
- [x] develop 위에 `fix/69`, `feat/70`, `feat/71`, `fix/72` 병합, 충돌 해결(`tools.ts`: stale 머리 + redaction/범위).
- [x] 통합 glue: 스크린샷 reading 관계에 `stale` (#69 × #71).
- [x] version 0.4.0 (Unity package.json, PackageVersion.cs, mcp/package.json, mcp-server-version.txt).
- [x] README/README.ko 설치 예시 `#v0.4.0`, 호환 표.
- [x] 검증: `npm test`(225), `npm pack` + `verify-mcp-package.sh`, 설치한 tarball 로 stdio smoke, 배포된 0.3.0 대조.
- [ ] CI `Unity Tests` EditMode/PlayMode (이 PR).
- [ ] 사람: 기능 PR(#74, #75, #77, #79)과 이 PR 검토·병합, `v0.4.0` Release 생성 → npm 배포.
- [ ] 배포 뒤 새 Unity project 에서 `#v0.4.0` + `unity-play-mcp@0.4.0` 연결·플레이 smoke (Unity Editor 필요).

## Validation
- **Commands to run:** `cd mcp && npm ci && npm run build && npm test`; `npm pack`; `.github/scripts/verify-mcp-package.sh`.

## Risks & Rollback
- **Risks:** 0.3.x 이하 server 는 0.4.0 package 의 캡처를 거절한다(호환 표에 적음).
- **Rollback steps:** 병합 전이면 PR 을 닫는다. 배포 뒤라면 `latest` tag 를 `v0.3.0` 으로 되돌리고 npm `latest` dist-tag 를 0.3.0 으로 옮긴다.

## Open Questions
- 실제 게임에서의 장면 전환·hover·좌표·토큰 확인은 Unity Editor 가 있는 사람이 해야 한다.
