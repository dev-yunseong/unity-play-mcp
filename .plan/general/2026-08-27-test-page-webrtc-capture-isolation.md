# 2026-08-27 — 테스트 페이지 WebRTC 재현과 화면 캡처 격리

- Date: 2026-08-27
- Jira: ARTEL-618
- Status: Implemented; manual lifecycle matrix partially pending

## Goal

테스트 페이지에서 SDK의 기존 WebRTC 시그널링을 직접 구동하고 영상을 보면서 `capture_screen`을 연속 실행할 수 있게 한다. 스트리밍이 남긴 `RenderTexture.active`가 화면 캡처의 back buffer grab에 영향을 주지 않도록 격리한다.

## Non-goals

- 스트리밍 프로토콜이나 서버 시그널링 재설계
- 외부 STUN/TURN 기본값 추가
- 다중 시청자 및 외부 네트워크 연결 보장

## Context / Constraints

- 테스트 페이지와 SDK는 이미 같은 WebSocket을 양방향으로 사용한다.
- SDK가 offer를 만들므로 브라우저는 answer와 ICE candidate만 돌려보낸다.
- 기존 로컬 캡처 저장·표시 변경은 로그인 없는 `capture_screen` 검증에 필요하므로 같은 PR에 포함한다. 범위는 `LocalCaptureStore`/`LocalCaptureUploader`, 테스트 서버의 `/captures/{id}`, manager의 로컬 uploader 주입, 페이지의 단일 최신 캡처 표시와 그 테스트로 한정한다.
- `RenderTexture.active`는 프로세스 전역 렌더 상태이므로 grab 직전 격리와 호출 전 상태 복원이 모두 필요하다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `ArtelTestPage`, 스트리밍 DTO/세션, `ScreenCapturer`, `ScreenVideoSource`의 계약과 현재 변경을 확인한다.
- [ ] **Step 1: WebRTC test viewer** — 기존 WebSocket/message dispatch만 재사용한다. 열린 socket에서 생성한 streamId, 빈 iceServers, video 640px/10fps, lease 30초로 `STREAM_START`를 보내고 10초마다 `STREAM_RENEW`, 종료 시 `STREAM_STOP`을 보낸다. `WEBRTC_OFFER`를 적용해 `WEBRTC_ANSWER`를 돌려주고 양방향 `WEBRTC_ICE`를 교환한다. remote description 전 ICE는 버퍼링 후 적용하며 빈 candidate와 stale streamId는 버린다. CONNECTING/LIVE는 표시하고 FAILED/STOPPED는 정리한다.
- [ ] **Step 2: Viewer lifecycle** — 상태는 peer/streamId/renew timer 하나로 제한한다. start 재호출은 기존 세션을 교체하고 idle stop은 no-op이다. stop, socket close/error, unload, peer failed/closed가 같은 idempotent cleanup으로 timer, peer, video srcObject, ICE buffer를 비운다. video는 autoplay/playsInline/muted다. pending capture 동안 버튼을 disable하여 결과 도착 후 다음 캡처만 허용한다.
- [ ] **Step 3: Capture isolation** — `ScreenCapturer`와 `ScreenVideoSource.CaptureFrame` 각각 진입 시 active를 저장하고 grab 직전에 null로 만든다. grab/blit/readback 뒤 temporary RT release 전에 finally에서 원래 active를 복원한다. 새 render-state 추상화는 만들지 않는다.
- [ ] **Step 4: Tests** — 새 `Tests/Runtime/ArtelTestPageTests.cs`는 embedded HTML의 controls와 START/OFFER/ANSWER/ICE/RENEW/STOP message fragments, cleanup 함수 호출 지점, pending capture disable처럼 정적인 계약만 확인한다. 브라우저 JavaScript 동작을 실행했다고 주장하지 않는다. 기존 관련 테스트를 유지한다. 실제 PlayMode에서 안정적일 때만 preexisting non-null active 복원 테스트를 추가하며 테스트용 production API는 만들지 않는다. back-buffer 화상 회귀는 수동 검증 한계를 명시한다.
- [ ] **Step 5: Manual verification** — WordVenture 브라우저에서 offer→answer, 양방향 ICE, 최소 1회 renew, 명시적 stop, socket close 또는 unload cleanup을 개발자 도구와 상태 UI로 확인한다. stale/empty ICE는 관측 가능한 범위에서 무시됨을 확인한다. 이어서 stream off/on(640px, 10fps) 각각 빈 target 전체 화면 캡처를 결과 도착 후 10회 반복하고 텍스트 방향, 겹침/어긋남, 영상 지속, 종료 상태를 비교해 PR에 기록한다.
- [ ] **Step 6: Rollout / Rollback** — 문제가 있으면 PR 전체를 revert한다. 격리 revert 시 알려진 active target 오염도 돌아온다고 명시한다.

## Validation

- **Commands to run:** throwaway Unity 프로젝트의 Unity 2022.3.34f1 EditMode/PlayMode 전체 테스트와 `ArtelTestPageTests` 확인; WordVenture 수동 matrix.
- **Expected output:** Unity EditMode 323/323 및 PlayMode 24/24 통과. 사용자가 테스트 페이지에서 실제 스트림 수신 중 캡처가 정상임을 확인했다. stop/restart/reload의 signaling trace는 별도로 기록하지 않았다.

## Risks & Rollback

- **Risks:** 실제 back buffer 화상은 자동화가 완전히 증명하지 못해 수동 비교에 의존할 수 있다. disconnected는 일시적일 수 있어 즉시 정리하지 않고 FAILED/closed 또는 명시적 stop/lease로 끝낸다.
- **Rollback steps:** PR 커밋을 revert한다. 프로토콜과 서버 계약은 바뀌지 않는다.

## Open Questions

- 실제 수동 검증에 사용할 Unity Editor/WordVenture 실행 환경이 현재 머신에서 사용 가능한지는 검증 단계에서 확인한다.

## Rejected feedback

- 예외 주입용 render-state 인터페이스는 이 지역 상태 경계보다 큰 설계라 만들지 않는다. 안정적인 PlayMode 검증이 불가능하면 한계를 수동 검증에 명시한다.
