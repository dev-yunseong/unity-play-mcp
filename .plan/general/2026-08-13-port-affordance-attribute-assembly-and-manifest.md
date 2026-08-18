# 2026-08-13 — [SDK] affordance attribute 어셈블리와 패키지 매니페스트 이식

- Date: 2026-08-13
- Jira: [ARTEL-391](https://artel-asm.atlassian.net/browse/ARTEL-391)
- Status: Reviewed (fast·medium 1차 반영)

## Goal

`artel-sdk-prototype`의 `kr.artel.affordance`를 `kr.artel.sdk`로 **흡수**하는 스택의 첫
단계. 나머지 전부가 의존하는 attribute 어셈블리와 패키지 매니페스트만 옮겨,
ARTEL-392 이후가 컴파일될 자리를 만든다.

흡수이지 공존이 아니다. 최종 상태는 패키지 하나이며, 전체 완료 신호는
`artel-sdk-prototype`의 `Packages/kr.artel.affordance/`가 저장소에서 삭제되고
`kr.artel.sdk` 하나만으로 WordVenture 기준선(records 318 / types 21 / objects 27 /
atoms 820 / gesture 25 / unknown 4)이 재현되는 것이다 — ARTEL-394 이후.

`samples/WordVenture`의 manifest는 **애초에 그 항목을 커밋한 적이 없으므로** 완료
신호가 될 수 없다. 아래 「어셈블리 이름 충돌」 참조.

옮기는 것:

| 파일 | 줄 |
|---|---:|
| `Runtime/AffordanceAttribute.cs` (+`.meta`) | 39 |
| `Runtime/Artel.Affordances.Runtime.asmdef` (+`.meta`) | 14 |
| `link.xml` (+`.meta`) | 15 |
| `package.json` 의존성 대조 | — |

## Non-goals

- IL 분석기 이식 (ARTEL-392)
- 런타임 씬 스캔 이식 (ARTEL-394)
- Editor 메뉴·설치기 이식 (ARTEL-395)
- 네임스페이스·어셈블리 이름 통합 리팩터 — 흡수가 끝난 뒤 별개 작업
- affordance 분석 출력의 수치 검증 — 이 단계에는 `Scan`이 없어 리포트가 생성되지
  않는다. ARTEL-394 소관

## Context / Constraints

### 이식 대상은 게이트되지 않은 유일한 어셈블리다

프로토타입의 asmdef 넷은 `defineConstraints: ["ARTEL_AFFORDANCE"]`를 건다
(`Scan`, `CodeGen`, `Editor`, `Addressables`). **`Artel.Affordances.Runtime`만 걸지
않는다.** 의도적이다 — 구워진 게임 어셈블리가 `AffordanceAttribute`를 참조하므로,
도구가 꺼져 있어도 타입이 해석돼야 한다.

따라서 이 이슈는 define 정책에 걸리는 코드를 하나도 옮기지 않는다. 정책은 아래에
결정만 기록하고, 실제로 적용되는 곳은 ARTEL-392다.

### `.meta`는 함께 옮긴다

프로토타입 패키지에 `.meta` 65개가 git에 추적되고 있고, 이식 대상 셋도 전부
추적된다. **`.meta`를 함께 옮겨야 GUID가 보존된다.** 새로 생성시키면 GUID가 바뀌어
기존 참조가 끊긴다.

### 배치 — `Runtime/Affordance/`, 새 최상위 폴더를 만들지 않는다

`kr.artel.sdk/Runtime/`에는 이미 `Artel.Runtime.asmdef`가 있으므로 같은 폴더에
두 번째 asmdef을 둘 수 없다. 하위 폴더가 필요하다.

패키지의 현재 모양을 실제로 확인했다:

```
Runtime/Artel.Runtime.asmdef          루트 asmdef 하나
Runtime/{Auth,Capture,Diagnostics,Domain,Protocol,
         Serialization,Streaming,Tracking,UnityEngine,Plugins}/
                                      asmdef 없는 서브시스템 폴더 10개
Editor/                               루트 asmdef 없음
Editor/CodeGen/Artel.CodeGen.asmdef   asmdef이 하위 폴더에 있는 형태
```

**결정: `Packages/kr.artel.sdk/Runtime/Affordance/`에 둔다.** 이후 단계의 Editor
이식분은 `Editor/Affordance/` 아래로 간다 (`Editor/Affordance/CodeGen/` 등).

최상위 `Affordance/` 폴더를 새로 만드는 안을 검토했다가 버렸다. 그러면 한 패키지
안에 `Runtime`·`Editor` 쌍이 둘 생기고, 그것은 이 플랜의 Goal이 부정하는 공존의
모양이다. 게다가 나중에 제자리로 옮기는 작업이 스택 어느 이슈에도 속하지 않아
영구화될 위험이 있다. `Runtime/Affordance/`는 기존 서브시스템 폴더 관례와 같고
평탄화가 필요 없다.

`link.xml`은 패키지 루트(`Packages/kr.artel.sdk/link.xml`)에 둔다. 링커 보존 규칙은
패키지 전체에 대한 선언이지 특정 폴더의 것이 아니고, 프로토타입도 패키지 루트에
두고 있다.

### 어셈블리 이름 충돌 — 샘플에서 프로토타입 패키지를 빼서 피한다 (적용 완료)

`kr.artel.sdk`가 `Artel.Affordances.Runtime`을 선언하는 순간,
`kr.artel.affordance`도 같은 이름을 선언하고 있으면 Unity가 거부한다:

```
Assembly with name 'Artel.Affordances.Runtime' already exists
```

**그런데 그 충돌은 이 기계에서만 재현된다.** `samples/WordVenture`가 두 패키지를 모두
설치한 것은 **로컬 작업 트리에서만** 있었던 일이고, 커밋된 매니페스트에는
`kr.artel.affordance` 항목이 들어간 적이 없다:

```
git show HEAD:Packages/manifest.json | grep -i afford   → 없음
git log --all -S'kr.artel.affordance' --oneline         → 0건
서브모듈 포인터                                          20623b18 (변경 없음)
```

따라서 **서브모듈에 커밋할 변경이 없다.** 다른 클론에서는 애초에 충돌하지 않는다.

**2026-08-13 결정: 로컬 작업 트리에서 `kr.artel.affordance`를 뺀 채로 작업한다.**
프로토타입 저장소도, WordVenture 서브모듈도 커밋하지 않는다. 이 이슈의 산출물은
`kr.artel.sdk`의 파일 셋뿐이다.

착수 시점에 이미 적용하고 확인했다 (전부 로컬 상태):

```
manifest.json          kr.artel.affordance 항목 제거 (커밋 안 함)
packages-lock.json     Unity가 갱신 (커밋 안 함)
Assets/ArtelTemp/      제거 — Artel.Affordances.Live·Scan 참조하던 dev 측정
                       하니스 잔여물. 주석 자체가 "측정 후 삭제된다"고 적고 있었고
                       스크립트 cleanup이 프로토타입 쪽만 지워 남아 있던 것.
                       untracked라 git 이력 영향 없음
배치 컴파일             exit 0, error CS 0건
Assembly-CSharp.dll    affordance 흔적 0 / ArtelInput 있음 / __artelActionBuffer 있음
```

`ProjectSettings`의 `ARTEL_AFFORDANCE` 심볼도 **커밋되지 않은 로컬 설정**이다
(`ProjectSettings.asset` 수정 + untracked `ArtelAffordance.txt`). 그대로 두되,
**ARTEL-392는 이 심볼이 다른 클론에 있다고 가정할 수 없다.** 심볼이 없으면
`defineConstraints`에 걸린 `CodeGen` asmdef이 아예 컴파일되지 않아 조용히 무동작이
된다. 설치기(ARTEL-395) 전까지는 각자 넣어야 하고, ARTEL-392의 검증 절차에 그
전제를 적어야 한다.

### 이 결정의 대가 — 측정 공백

**ARTEL-391부터 ARTEL-393까지 WordVenture에서 affordance 출력이 나오지 않는다.**
`Scan`이 ARTEL-394에서야 도착하기 때문이다. `tools/measure-wordventure.sh`는 리포트
파일이 없어 실패한다. 이 단계에서 돌릴 수 있는 명령이 아니다.

그래서 다음을 옮긴다:

| 원래 위치 | 옮긴 곳 | 무엇 |
|---|---|---|
| ARTEL-391 | ARTEL-394 | `measure-wordventure.sh` 기준선 대조 |
| ARTEL-392 | ARTEL-394 | "WordVenture 동작이 병합 전과 같다" 중 affordance 부분 |
| ARTEL-393 | ARTEL-394 | `gesture ≥ 25`, 사라진 조건 0건 |

391~393 구간에는 **SDK 자체 기능의 무회귀만** 확인할 수 있다. 회귀가 있다면
ARTEL-394에서 한꺼번에 드러나고, 그때 원인이 어느 단계인지 가려야 한다.
**그것이 이 선택의 비용이다.** Jira ARTEL-392·393에 코멘트로 기록했다.

### `link.xml`은 아직 없는 어셈블리를 가리킨다

SDK에는 `link.xml`이 없다. 프로토타입 것은 두 어셈블리를 preserve로 지정한다:

```xml
<assembly fullname="Artel.Affordances.Runtime" preserve="all"/>
<assembly fullname="Artel.Affordances.Scan" preserve="all"/>
```

`Artel.Affordances.Scan`은 ARTEL-394에서야 존재한다. **이 이슈에서는 Runtime 줄만
가져오고, Scan 줄은 ARTEL-394에서 추가한다.** 존재하지 않는 어셈블리를 적어 두면
링커가 어떻게 반응하는지 확인되지 않았고, 확인되지 않은 것을 미리 넣지 않는다.

프로토타입의 `link.xml` 주석은 이 파일이 실제로 지켜지는지 **확인된 바 없다**고
적어 두었다. High 스트리핑에서 attribute·어셈블리·스캔이 전부 사라진 것은 측정됐다.
그 확인은 이 이슈 범위 밖이고 별도 이슈로 남긴다.

### `package.json`

양쪽 다 `com.unity.nuget.mono-cecil: 1.11.4`를 이미 가진다. 프로토타입은 그것 하나뿐이다.
**변경이 필요 없을 가능성이 높으나 대조해서 확인한다.**

## Approach (Checklist)

- [ ] **Step 0: Recon**
  - [ ] `Packages/kr.artel.sdk/Runtime/Affordance/`가 없는지 확인
  - [ ] `package.json` 의존성 대조 — 양쪽 mono-cecil 버전 일치 확인
  - [ ] **프로토타입 저장소에서** `git ls-files`로 이식 대상 3쌍이 추적 중인지 재확인:
        `Runtime/AffordanceAttribute.cs`, `Runtime/Artel.Affordances.Runtime.asmdef`,
        `link.xml` 및 각 `.meta`
  - [ ] `samples/WordVenture`에서 `git show HEAD:Packages/manifest.json | grep -i afford`가
        **0건**임을 확인한다. 서브모듈 작업 트리에 남은 수정
        (`ProjectVersion.txt` 2022.3.62f3 승격, 패키지 버전 상향, `ARTEL_AFFORDANCE`
        define, `TitleScene.unity`, TMP fallback 에셋 2건)은 **이 이슈의 것이 아니므로
        커밋하지 않는다**
  - [ ] merge-base(`origin/develop`)에서 EditMode 기준선을 먼저 잡는다 (Validation 5번)

- [ ] **Step 1: Implementation** — 모두 `artel-sdk` 저장소, 프로토타입 변경 없음
  - [ ] `Packages/kr.artel.sdk/Runtime/Affordance/` 생성 (+ 폴더 `.meta`)
  - [ ] `AffordanceAttribute.cs`와 `.meta`를 그대로 복사 — **내용 변경 없음**
  - [ ] `Artel.Affordances.Runtime.asmdef`와 `.meta` 복사 —
        `name`·`rootNamespace`·`autoReferenced` 변경 없음
  - [ ] `link.xml`을 `Packages/kr.artel.sdk/`에 두되 **Runtime 줄만** 남긴다 (+`.meta`)
  - [ ] `package.json` — Step 0 대조 결과 필요하면 갱신, 아니면 손대지 않는다

- [ ] **Step 2: Tests** — 아래 Validation의 명령으로 확인
- [ ] **Step 3: Rollout**
  - [ ] PR을 `develop` 대상으로 열고 본문 끝에 `Jira: ARTEL-391` 트레일러
  - [ ] **`samples/WordVenture` 서브모듈은 건드리지 않는다.** 포인터를 올리지 않는다.
        로컬 작업 트리가 더러운 채로 남는 것은 의도된 것이며, `git status`에
        `samples/WordVenture`가 staged로 올라오면 잘못된 것이다
  - [ ] ARTEL-392 브랜치는 이 브랜치 위에 쌓는다

## Validation

이 단계에서 **실제로 실행 가능한 것만** 적는다. affordance 출력 수치 대조는
ARTEL-394로 옮겼다.

- **Commands to run:**

```bash
# 1) WordVenture 배치 컴파일 — 이름 충돌과 컴파일 오류
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath ~/Desktop/soma/artel-sdk/samples/WordVenture \
  -logFile /tmp/artel-391-compile.log
echo "exit=$?"
grep -iE 'error CS|Assembly with name|Compilation failed' /tmp/artel-391-compile.log

# 2) 어셈블리가 실제로 만들어졌나
ls ~/Desktop/soma/artel-sdk/samples/WordVenture/Library/ScriptAssemblies/ \
  | grep -i affordance

# 3) SDK 위빙이 그대로인가 / affordance는 아직 굽지 않는가
strings ~/Desktop/soma/artel-sdk/samples/WordVenture/Library/ScriptAssemblies/Assembly-CSharp.dll \
  | grep -E 'ArtelInput|artelActionBuffer|AffordanceAttribute' | sort -u

# 4) GUID 보존 — 세 쌍 전부. link.xml.meta 는 패키지 루트에 있으므로 따로 짚는다
grep guid ~/Desktop/soma/artel-sdk/Packages/kr.artel.sdk/Runtime/Affordance/*.meta \
          ~/Desktop/soma/artel-sdk/Packages/kr.artel.sdk/link.xml.meta

# 5) EditMode 테스트 — project.md 의 CI 스크립트 절차 그대로
.github/scripts/setup-unity-test-project.sh /tmp/artel-391-tests
/Applications/Unity/Hub/Editor/2022.3.34f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath /tmp/artel-391-tests \
  -testResults /tmp/artel-391-tests/results.xml \
  -logFile /tmp/artel-391-tests/unity.log
python3 .github/scripts/summarize-test-results.py /tmp/artel-391-tests/results.xml EditMode
```

- **Expected output:**

| # | 기대값 |
|---|---|
| 1 | `exit=0`, grep 결과 0건. 특히 `Assembly with name ... already exists`가 없어야 한다 |
| 2 | `Artel.Affordances.Runtime.dll`이 있어야 한다 — asmdef이 컴파일에 실제로 포함됐다는 증거 |
| 3 | `ArtelInput`·`__artelActionBuffer` **있음**(SDK 위빙 무회귀), `AffordanceAttribute` **없음**(아직 굽지 않으므로 정상) |
| 4 | 세 값이 프로토타입 것과 **일치**해야 한다:<br>`AffordanceAttribute.cs.meta` → `3e79594e04a40744a899c579e44cc70a`<br>`Artel.Affordances.Runtime.asmdef.meta` → `cec4bebcd0b6f634ea67175d2c10f5cb`<br>`link.xml.meta` → `0a45e8135782ee148a9321a5e0ef5a83` |
| 5 | **merge-base(`origin/develop`)에서 같은 명령으로 기준선을 먼저 잡고, 실패 테스트의 _이름 집합_이 동일한지 본다. 고정 숫자를 쓰지 않는다.** 문서마다 숫자가 다르다 — 현재 `project.md`는 "green이 기대값"이라 적고(8건 언급 없음), 옛 플랜 여럿이 8건으로 적었으며, `.plan/general/2026-08-13-frame-time-metrics-collection.md:138`은 실제 develop 기준선을 11건(`OverlayViewModel_*` 3건 추가)으로 기록했다. 어느 숫자도 믿지 말고 집합을 비교한다 |

`autoReferenced`가 실제로 게임 어셈블리에 참조를 내는지는 이 단계에서 증명되지
않는다. 참조가 emit되려면 게임 코드가 그 타입을 써야 하는데 아직 굽는 쪽이 없다.
asmdef 필드값 확인(`"autoReferenced": true`)과 위 2번까지가 이 단계의 한계이고,
실제 결합은 ARTEL-393에서 evidence가 구워질 때 증명된다.

## Risks & Rollback

- **Risks:**
  - **GUID 유실 (중간).** `.meta`를 빠뜨리면 Unity가 새 GUID를 생성하고 이후 스택에서
    참조가 끊긴다. Validation 4번으로 잡는다
  - **측정 공백 (중간, 의도된 것).** 391~393 구간에 affordance 회귀를 감지할 수단이
    없다. 394에서 한꺼번에 드러난다. 아프면 「Rejected feedback」의 대안으로 돌아선다
  - **서브모듈을 실수로 커밋 (중간).** `samples/WordVenture` 작업 트리에는 이 이슈와
    무관한 수정 7건이 있다 (에디터 버전 승격, 패키지 버전 상향, define, 씬, TMP 에셋).
    포인터를 올리면 그것들이 통째로 딸려 들어간다. Step 3의 `git status` 확인으로 잡는다
  - **`link.xml`이 실제로 지켜지는지 미확인 (낮음, 기존 부채).** 이 이슈가 만든 위험이
    아니며 별도 이슈로 남긴다
  - **다른 클론에는 `ARTEL_AFFORDANCE`가 없다 (ARTEL-392로 이월).** 심볼이 없으면
    `CodeGen` asmdef이 컴파일되지 않아 조용히 무동작이 된다. 이 이슈에는 영향이 없지만
    ARTEL-392의 검증이 그 전제를 명시해야 한다

- **Rollback steps:**
  - `git revert` 한 번. 이 이슈의 산출물은 `kr.artel.sdk` 아래 파일 추가뿐이다
    (`Runtime/Affordance/` 2쌍 + 폴더 `.meta`, 패키지 루트 `link.xml` 1쌍).
    `package.json`은 Step 0 대조 결과 손대지 않을 가능성이 높다
  - **서브모듈은 되돌릴 것이 없다.** 포인터를 올린 적이 없으므로 내리지도 않는다.
    `samples/WordVenture`의 더러운 작업 트리는 이 이슈와 무관하므로 그대로 둔다

## Rejected feedback

- **fast #1 — `measure-wordventure.sh`를 프로토타입 경로로 고쳐라.** 경로 지적은
  맞지만 처방이 부족하다. 패키지를 뺀 이상 리포트 파일 자체가 생기지 않아 경로를
  고쳐도 실패한다. **명령을 고치는 대신 이 단계에서 제거하고 ARTEL-394로 옮겼다.**
- **medium 질문 — `link.xml`이 패키지 루트에 있는 것이 `Affordance/` 폴더가 임시임을
  방증한다.** 방증이 아니다. 링커 보존 규칙은 패키지 전체 선언이라 루트가 정상
  위치이고 프로토타입도 그렇게 두고 있다. 다만 그 질문이 가리킨 본 지적(최상위
  `Affordance/` 폴더)은 받아들여 `Runtime/Affordance/`로 바꿨다.

## Open Questions

### ~~Q1. `ARTEL_AFFORDANCE` define 정책~~ — 해결됨 (2026-08-13)

**게이트를 들여오지 않는다.** SDK 코드베이스에 들어왔다가 나가는 것이 아니라 처음부터
들어오지 않는다. 이 이슈가 옮긴 어셈블리는 원래 게이트가 없으므로 산출물에 영향이 없고,
결정은 ARTEL-392·394·395에 적용된다.

근거 요약:

- 게이트를 관리하는 코드가 369줄(`FirstImport`·`DiscoveryDefine`·`DiscoveryMenu`)이다.
  지우기로 한 것을 옮겼다가 지우지 않는다 → ARTEL-395에서 `Editor/Install` 이식 제외
- 게이트가 실제로 아끼는 것은 에디터 컴파일 시간뿐이다. `CodeGen`·`Reporting`은
  Editor 전용이라 플레이어에 없고, `Scan`·`Live`는 코드 안의
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`가 이미 막는다
- 실패 모드가 침묵이다. 심볼이 없으면 오류도 경고도 없이 아무 일도 안 일어난다
- 심볼이 활성 빌드 타깃 그룹에만 들어가 타깃을 바꾸면 조용히 꺼지는 알려진 결함이 있다
- 프로토타입의 게이트는 "추가로 넣는 독립 패키지를 잠재우기" 위한 것이었다. SDK 안에서는
  SDK 설치 자체가 opt-in이다

킬 스위치는 값이 있으나 **opt-out**(`ARTEL_AFFORDANCE_OFF` 류)이어야 실패 모드가
뒤집힌다. **지금 만들지 않는다** — 필요한 사례가 나오기 전까지 YAGNI.

`Addressables`의 `ARTEL_ADDRESSABLES`는 남긴다. 선택적 의존 패키지의 존재 여부를 보는
것이라 성격이 다르다.

**딸려오는 위험:** 게이트가 없으면 affordance ILPP가 SDK를 설치한 모든 프로젝트의 모든
게임 어셈블리에서 돈다. 사고의 사정거리가 넓어지므로 ARTEL-393의 "분석 실패 시 원본을
그대로 둔다" 검증이 유일한 안전망이 된다. 해당 이슈에 기록했다.
