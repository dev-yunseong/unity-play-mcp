# 2026-08-13 — [SDK] affordance 런타임 씬 스캔과 리포트 이식

- Date: 2026-08-13
- Jira: [ARTEL-394](https://artel-asm.atlassian.net/browse/ARTEL-394)
- Status: Implemented

## Goal

씬 계층을 걸어 구워진 evidence를 붙이고 `artel-affordances.json`(schema 6)을 쓰는 쪽을
옮긴다. **이 이슈가 스택의 측정 복귀 지점이다** — ARTEL-391에서 샘플의 로컬 매니페스트에서
프로토타입 패키지를 뺀 뒤로 391~393 구간에는 리포트가 나오지 않았다.

## 범위가 두 번 늘었다 — 둘 다 강제된 것

계획 단계에서 파일 목록만 보고 그은 경계가 실제로 옮겨보니 어긋났다. 기록해 둔다.

### 1. `Live/`를 뗄 수 없다 (ARTEL-396 흡수)

`Runtime/Scan`과 `Runtime/Scan/Live`는 **한 asmdef이고 서로를 참조한다.**

```
Scan → Live   AffordanceBootstrap 이 Pulse·PulseFile·WatchList·IPulseSink 를 8곳에서 사용
              (WatchLiveState · Watching · StopWatching · _ours)
Live → Scan   LiveState 가 Json·ScenePath 를, Worth 가 Scan 타입을 사용
```

반쪽만 옮기면 컴파일되지 않는다. `AffordanceBootstrap`에서 live 진입점을 잘라냈다가
ARTEL-396에서 되붙이는 방법도 있으나, **지울 것을 옮겼다가 되살리는 churn**이고 그 사이
`AffordanceBootstrap`이 원본과 달라져 diff 검증도 못 하게 된다.

→ **ARTEL-396은 이 이슈에 흡수된다.** ARTEL-399(pulse 1초 배치 + WebSocket sink)는 그대로.

### 2. `Editor/Reporting` 없이는 측정할 수 없다 (ARTEL-395에서 당겨옴)

처음 측정이 이렇게 죽었다:

```
executeMethod class 'ArtelBatchScan' could not be found.
Argument was -executeMethod Artel.Affordances.Editor.ArtelBatchScan.Run
```

`tools/measure-wordventure.sh`는 하니스를 `Editor/Reporting/`에 심고
`Artel.Affordances.Editor.ArtelBatchScan.Run`을 부른다. 그 어셈블리가 SDK에 없었다.

우회로가 없다 — `Artel.Affordances.Scan` asmdef이 `autoReferenced: false`라
`Assets/` 코드로는 닿지 못하고, SDK의 기존 Editor 어셈블리들은 `noEngineReferences: true`에
`Scan`을 참조하지도 않는다.

측정이 이 이슈의 존재 이유이므로 `Editor/Reporting`(330줄)을 당겨왔다.

→ **ARTEL-395는 `Editor/Install`(Discovery 토글)과 `Runtime/Addressables`만 남는다.**

## 옮긴 것

```
Runtime/Affordance/Scan/        10 .cs + .meta + asmdef + .meta   3,023줄
Runtime/Affordance/Scan/Live/    7 .cs + .meta                    2,551줄
Editor/Affordance/Reporting/     3 .cs + .meta + asmdef + .meta     330줄
link.xml                        Artel.Affordances.Scan 줄 추가
```

원본 대비 **diff 0** — 코드는 한 글자도 바뀌지 않았다. `.meta`를 함께 옮겨 GUID를 보존했다.
`defineConstraints`도 그대로다(게이트 유지 결정).

`link.xml`의 `Artel.Affordances.Scan` 줄은 ARTEL-391에서 "그 어셈블리가 생길 때 넣는다"고
미뤄둔 것이다. 이제 넣었다.

## Validation

### 측정 — 기준선과 완전 일치

```
                  기준선   ARTEL-394
records            318       318
types               21        21
objects             27        27
atoms              820       820
  test             706       706
  always            85        85
  gesture           25        25    ← ARTEL-393 의 ReadsInput 수정이 옳았다
  unknown            4         4
call edges         245       245
with input          21        21
handles              6         6
dangling wire        0         0
```

내용 대조:

```
schema 6 · capture editor          동일
evidence 지문 d4b31e4da9504b7d     동일  ← 구워진 근거가 비트 단위로 같다
types 내용                         완전 동일
unplaced · scenes                  동일
objects 집합                       동일 (27개, 경로까지)
objects 내용                       refs 의 Unity instance ID 만 다름 (26238 → 26242)
```

instance ID 차이는 프로토타입 문서가 이미 적어 둔 것이다 — "Unity instance ID가 포함되는
scene reference 때문에 전체 리포트가 실행 간 byte-identical하다는 보장은 없다."
`field`·`type`·`name`·`path`·`asset`·`carries`는 전부 같다. 회귀가 아니다.

**사라진 조건 0건**은 atoms 820이 항목별로(test 706 · always 85 · gesture 25 · unknown 4)
동일한 것으로 확인된다.

### 그 외

- WordVenture 컴파일 `exit 0`, 오류 0
- `Artel.Affordances.Runtime.dll` · `Artel.Affordances.Scan.dll` ·
  `Unity.Artel.Affordances.CodeGen.dll` 모두 생성
- EditMode 253 passed / 0 failed
- 측정 하니스는 임시 설치 후 trap 으로 제거, 잔여 0 확인

## 이 이슈의 의미

**`kr.artel.sdk` 하나만으로 명세 근거 문서가 나온다.** WordVenture의 로컬 매니페스트에
프로토타입 패키지가 없는 상태에서 나온 결과다. 흡수의 절반(명세 JSON 추출)이 기능적으로
완료됐고 실측으로 확인됐다.

`agent-server`의 `app/specs_v2`가 읽는 스키마는 바뀌지 않았다 — schema 6, capabilities
네 개 그대로다.

## Risks

- **`Live/`가 함께 왔지만 아직 아무도 켜지 않는다.** `WatchLiveState()`는 호출자가 명시적으로
  불러야 하고 이 이슈는 부르지 않는다. 파일 sink 동작 확인은 ARTEL-399에서 전송 경로와 함께
- **측정이 editor 모드만이다.** `dev`(개발 빌드) 측정은 빌드까지 27초+가 들어 이번에 돌리지
  않았다. editor 와 dev 는 IL 모양이 달라 수치가 다르므로, dev 회귀는 아직 미확인
- instance ID 비결정성은 기존 부채다. 이 이슈가 만든 것이 아니며 별도 판단 사항

## Rollback

`git revert` 한 번. 추가만 있고 `kr.artel.sdk` 의 기존 파일 수정은 `link.xml` 한 줄뿐이다.
