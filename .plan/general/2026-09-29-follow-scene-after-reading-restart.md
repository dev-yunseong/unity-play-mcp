# 2026-09-29 — 장면 전환·reading 재시작 뒤 오래된 reading을 현재 상태로 반환하지 않는다

- Date: 2026-09-29
- GitHub Issue: https://github.com/dev-yunseong/unity-play-mcp/issues/69
- Status: Implemented

## Goal

`get_scene_state`·`get_visible_elements`·`search_targets`·`get_unity_status` 가 Unity 가 새로 시작한
reading run 을 따라가게 하고, 따라가지 못하는 동안에는 낡았다는 사실과 마지막 실제 reading 시각을
응답에 싣는다.

## Non-goals

- 게임의 장면 전환·전투 로직 수정.
- 고정 `sleep` 으로 낡은 상태를 덮기.
- MCP server 가 재연결 뒤 `start_readings` 를 스스로 보내는 것. 숨은 부수효과이고, 재연결 자체는
  package 쪽 "새 client 에 전량 reading" 으로 복구된다.

## Context / Constraints

재현(가짜 Unity endpoint + 실제 빌드된 stdio server, issue 댓글에 표)으로 확인한 원인:

1. `mcp/src/pulse.ts` `foldInternal` 이 `reading <= previous.reading` 인 frame 을 조용히 버리면서도
   `PulseStore.fold` 가 `lastReadingAt` 을 갱신했다.
2. `Pulse._reading` 은 인스턴스 필드라 `Pulse.Begin` 마다 1부터 센다. v0.2.0 과 develop 의 `Pulse.cs`
   는 같다. 그래서 동일 버전(develop+develop)에서도 재현되고, 버전 불일치는 원인이 아니다.
3. `AgentWebSocketServer.Send` 는 client 가 없어도 던지지 않아 `Pulse` 가 전달 실패로 보지 않는다.
   끊겼다 붙은 reader 는 그 사이 차이를 영영 받지 못한다.

v0.2.0 server 는 `m` 키를 못 읽어 어떤 조합에서도 reading 을 접지 못한다(별개 결함, develop 에서
이미 수정). 기존 0.2.x package 사용자도 server 만 올리면 고쳐지도록 run id 없는 frame 은 번호
역행으로 새 run 을 판정한다.

## Approach (Checklist)
- [x] **Step 0: Recon** — `pulse.ts`, `connection.ts`, `tools.ts`, `wait.ts`, `Pulse.cs`,
  `UnityPlayMcpHost.cs`, `AgentWebSocketServer.cs`, v0.2.0 과의 diff.
- [x] **Step 1: server** — `PulseStore.fold` 가 run 을 가른다(`startsNewRun`). 새 run 의 whole 은
  이전 상태·이력·tombstone 없이 새로 세우고, 새 run 의 delta 는 적용하지 않고 `restarted` 로 표시한다.
  같은 run 의 지난 번호는 버리되 시각을 건드리지 않는다. `markInterrupted` 로 `disconnected`
  (connection), `stopped`(성공한 `stop_readings`)를 적는다. `readingHeader` 가 네 읽기 tool 앞에
  `stale` 을 싣고, status 가 "Readings are stopped"/"This reading is stale" 을 말한다.
  `wait_for_condition` 은 낡은 상태로 즉시 충족하지 않고, 기다리는 사이 새 run 이 오면
  `sinceReading`/`sinceFrame` 을 충족한 것으로 본다.
- [x] **Step 2: package** — `Pulse` 가 `Begin` 마다 run GUID 를 문서 앞에 싣는다. `RequestWhole` 로
  다음 pulse 를 전량으로(쥐지 않고) 찍는다. host 는 이미 도는 채널에 대한 `start_readings` 와
  새 client 연결(`IAgentTransport.ClientsOpened`)에 이것을 부른다.
- [x] **Step 3: Tests** — `mcp/test/stale-reading.test.ts`(실제 connection·store·tool handler,
  run id 유무 두 package 모양), `connection.test.ts` 추가, `Tests/PlayMode/PulseRunTests.cs`.
- [ ] **Step 4: Manual** — 실제 게임에서 Lobby→Game→Result→Lobby, stop→start, Play Mode 재진입.

## Validation
- **Commands to run:** `cd mcp && npm run build && npm test`; CI `Unity Tests` (EditMode/PlayMode).
- **Expected output:** 전부 통과. 재현 스크립트에서 S2/S3 가 새 장면을 반환.

## Risks & Rollback
- **Risks:** 같은 run 이면서 번호가 역행하는 frame 은 이제 없다고 가정한다(단일 socket 은 순서를
  보존한다). 0.2.x package 에서는 새 run 이 이전 번호를 넘어 시작하면 whole 로만 구별되므로 이력이
  이어질 수 있다(상태 자체는 whole 로 교체된다).
- **Rollback steps:** `git revert` 로 PR 을 되돌린다. wire 에 추가한 `run` 은 이전 server 가 무시한다.

## Open Questions
- 관찰 세션에서 첫 run 재시작이 무엇이었는지(Play Mode 재진입/reload/stop→start)는 로그가 없어
  특정하지 못했다.
