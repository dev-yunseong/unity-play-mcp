# 2026-07-28 — capture_screen 액션으로 화면과 요소 이미지 올리기

- Date: 2026-07-28
- Jira: ARTEL-141
- Status: Implemented

## Goal

QA 에이전트가 판정에 쓸 정지 이미지를 SDK가 만들어 스토리지에 올리고, 그 위치를 액션
결과로 돌려준다.

## Non-goals

- 스프라이트 에셋·아틀라스 직접 추출. 질문은 "지금 이 버튼이 어떻게 보이는가"이고,
  화면 영역 크롭이 그 답이다
- WebRTC 스트리밍 변경
- 에디터 타임 내보내기

## Context / Constraints

**액션이 값을 돌려줄 자리가 없었다.** `ActionResultDto`는 `{id, success, error}`뿐이라
캡처 결과를 회신할 수단이 없다. `returnValue`를 더하되 `NullValueHandling.Ignore`로 두어,
돌려줄 것이 없는 기존 결과는 이전과 같은 바이트로 나간다.

**바이트는 WebSocket을 타지 않는다.** 오케스트레이션은 중계 프레임 전체를 QA 로그에
적재하고 SSE로 다시 발행한다. 이미지를 그 경로로 보내면 캡처 한 장마다 DB와 스트림이
같이 부푼다. 그래서 SDK가 스토리지로 직접 PUT하고, 소켓에는 URL만 실린다.

**캡처 시점은 배치 순서를 따른다.** `scan_scene`이 이미 쓰는 규칙과 같다. 클릭 뒤에 온
캡처는 그 클릭이 만든 화면을 본다.

## 설계 결정

**픽셀 경로를 인터페이스 뒤에 둔다.** `IScreenCapturer`/`ICaptureUploader`. 프레임버퍼가
필요한 부분과 판단이 있는 부분을 갈라서, 알 수 없는 대상·화면 밖 대상·업로드 거절 같은
분기를 화면 없이 검증한다. 결정이 들어 있는 쪽이 조용히 틀리는 쪽이기 때문이다.

**투영은 `CanvasCamera`를 재사용한다.** ARTEL-153이 커서와 씬 스캔의 카메라 선택을 이미
한 곳으로 모았다. 캡처가 자기 분기를 새로 쓰면 크롭 좌표와 커서 좌표가 갈라진다.

**화면 밖 대상은 잘렸다고 보고한다(`clipped`).** 화면 밖으로 반쯤 밀려난 버튼은 그 자체가
에이전트가 찾는 결함이고, 보이는 부분은 그 근거로 여전히 쓸모가 있다. 전부 밖에 있을
때만 실패다.

**전체 화면은 JPEG(q70), 크롭은 PNG.** 크롭은 대개 UI라 JPEG의 링잉이 판정 대상인 글자
가장자리와 경계선에 그대로 얹힌다. 전체 화면은 대부분 렌더된 씬이라 손실이 보이지 않고
무손실 비용만 크다. 최장 변 상한은 각각 1024·512.

**재시도하지 않는다.** 실패한 캡처는 실패한 액션이다. 다시 찍을지는 시나리오를 쥔
에이전트의 판단이지, 횟수를 세는 클라이언트가 대신할 수 있는 판단이 아니다.

**인스턴스 키는 업로드 시점에 읽는다.** 온보딩이 아직 키를 기다리는 중일 수 있다. 그때의
캡처는 stale한 값으로 올리는 대신 그렇다고 말한다.

**커서·키보드 오버레이가 캡처에 찍히는 것은 의도된 동작이다.** 오버레이 UI를 포함한 합성
화면을 읽는 이유가 그것이고, 에이전트가 자기 포인터 위치를 보는 편이 깨끗한 이미지보다
쓸모 있다.

## 상류 이슈에서 바뀐 것

오케스트레이션(ARTEL-142)의 서명 엔드포인트는 게임 인스턴스 id가 아니라 `instanceKey`로
인스턴스를 지목한다. 그 경로는 로그인 세션 없는 게임이 부르므로 엔드유저 JWT로 막히지
않는데, 순번 id를 받으면 번호를 훑어 남의 실행 중인 QA 프리픽스에 쓰는 서명을 받아낼 수
있기 때문이다. SDK는 어차피 자기 인스턴스 id를 모른다 — 등록 응답의 `instanceId`는 지금
파싱되지 않는다.

## 변경 목록

- `Runtime/Capture/CaptureRequest.cs` — params 읽기, 상한·품질 상수
- `Runtime/Capture/CaptureRect.cs` — RectTransform → 화면 픽셀 사각형, 축소 크기
- `Runtime/Capture/IScreenCapturer.cs`, `ScreenCapturer.cs` — 픽셀 경로
- `Runtime/Capture/CaptureUploader.cs` — 서명 요청 후 스토리지로 직접 PUT
- `Runtime/Protocol/Dto/ActionResultDto.cs` — `returnValue`
- `Runtime/Protocol/Dto/CaptureTicketDto.cs`, `CaptureResultDto.cs` — 와이어 형태
- `Runtime/ActionExecutor.cs` — `capture_screen` 분기
- `Runtime/ArtelManager.cs` — 실제 구현 주입

## Validation

- Unity 2022.3.34f1 Test Runner (EditMode), `samples/WordVenture`에 `testables`를 임시로
  더해 패키지 테스트를 포함시켜 실행: 111건 중 103건 통과.
  - 신규 `CaptureScreenTests` 18건 전부 통과.
  - 실패 8건은 이 브랜치 이전에도 같은 8건이 실패한다(같은 명령으로 baseline 확인).
    전부 EditMode에서 `DontDestroyOnLoad` 등 플레이모드 전용 API를 부르는 기존 테스트다.
- 픽셀 경로(캡처·크롭·플립·인코딩) 자체는 실행하지 않았다. 프레임버퍼가 필요해 EditMode에서
  검증할 수 없고, 플레이모드 수동 확인이 남아 있다. 특히 세로 플립은 틀려도 "동작하는 뒤집힌
  이미지"라 스모크 테스트를 통과하므로, 화면의 글자로 확인해야 한다.
