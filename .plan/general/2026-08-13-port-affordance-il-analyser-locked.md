# 2026-08-13 — [SDK] affordance IL 분석기를 비활성 상태로 이식

- Date: 2026-08-13
- Jira: [ARTEL-392](https://artel-asm.atlassian.net/browse/ARTEL-392)
- Status: Draft

## Goal

`kr.artel.affordance/Editor/CodeGen` 전체를 `kr.artel.sdk`로 옮긴다. **옮기기만 하고
동작시키지 않는다** — `WillProcess`를 잠근 채로 머지한다.

실측한 규모 (티켓 본문의 "약 8,700줄"은 착수 전 어림치였다):

```
.cs        24 파일   9,472 줄
.meta      25 파일   (cs 24 + asmdef 1)
asmdef      1 파일
git 추적   50 파일
```

가장 큰 것들: `IlReading.cs` 1,518 · `VariantBuilder.cs` 1,395 ·
`AffordanceILPostProcessor.cs` 798 · `Condition.cs` 697 · `OutcomeReader.cs` 592

9,472줄을 옮기는 일과 그것이 동작하게 만드는 일은 리뷰 성격이 완전히 다르다. 한 이슈로
묶으면 회귀가 났을 때 이동 탓인지 활성화 탓인지 가릴 수 없다.

## Non-goals

- **잠금 해제** — ARTEL-393
- ILPP 실행 순서 의존성 해결 — ARTEL-393
- **`ARTEL_AFFORDANCE` 게이트 제거** — 하지 않는다. 아래 「게이트는 유지한다」 참조
- 두 ILPostProcessor 통합
- 코드 내용·스타일·네임스페이스 변경

## Context / Constraints

### 배치 — `Editor/Affordance/CodeGen/`

ARTEL-391에서 정한 관례를 따른다. `Runtime/Affordance/`의 짝이다.

`Editor/`에는 루트 asmdef이 없고 `Editor/CodeGen/Artel.CodeGen.asmdef`가 하위 폴더에
있다. 새로 오는 것을 `Editor/CodeGen/`에 넣으면 폴더가 충돌하므로
`Editor/Affordance/CodeGen/`으로 간다. 형제 서브트리라 중첩 문제가 없다.

### 어셈블리 이름은 충돌하지 않는다

```
SDK 기존   Unity.Artel.CodeGen
새로 오는  Unity.Artel.Affordances.CodeGen
```

두 ILPostProcessor가 등록만 되고, 새 쪽은 잠겨 있어 아무 일도 하지 않는다.

### `SkippedPrefixes`가 SDK 어셈블리를 이미 덮는다

`AffordanceILPostProcessor.SkippedPrefixes`에 `"Artel"`·`"Unity.Artel"`이 들어 있어
`Artel.Runtime`·`Artel.Affordances.Runtime`·`Unity.Artel.CodeGen`·
`Unity.Artel.Affordances.CodeGen`이 모두 자기 자신을 건너뛴다. **변경할 것이 없다.**

### 게이트는 유지한다 (2026-08-13 번복)

`defineConstraints: ["ARTEL_AFFORDANCE"]`도, `IsEnabledFor`·`EnableDefine`도 **그대로
가져온다.** asmdef은 한 글자도 고치지 않는다.

앞서 이 플랜과 Jira에 "게이트를 들여오지 않는다"고 적었다가 되돌린 것이다. 되돌린 이유는
[AffordanceILPostProcessor.cs:21](../../artel-sdk-prototype/Packages/kr.artel.affordance/Editor/CodeGen/AffordanceILPostProcessor.cs)의
기록이다:

> 세 차례 이 분석이 에디터를 먹통으로 만들었고, 그 에디터는 왜 그런지 알아보려 열 수조차
> 없었다. 빠져나오는 길은 Unity를 죽이고 매니페스트를 손으로 고치는 것이었다. 제거하지
> 않고 통째로 끄는 스위치는 이 분석이 찾아낼 수 있는 무엇보다 값지다.

같은 파일의 `EnableDefine` 주석은 이중 게이트가 **의도적**이며 층마다 이유가 다르다고
적는다 — ①은 컴파일 자체를 막아 "설치된 채 출시 빌드까지 가도 안전"하게 하고, ②는 어떤
경로로든 어셈블리가 빌드돼도 게임 어셈블리를 건드리지 않음을 보장한다.

제거를 주장하며 내가 든 근거는 세 군데가 틀렸다:

- **"게이트가 아끼는 건 에디터 컴파일 시간뿐"** — 틀렸다. ②는 게임 어셈블리 무접촉을
  보장하고 그것이 먹통 방지다
- **"침묵하는 실패가 최악"** — 열리지도 않는 에디터를 만드는 분석기에서는 침묵이
  안전한 쪽이다
- **"킬 스위치는 opt-out이어야 한다"** — 가장 나빴다. 먹통이 된 에디터에서는 스위치를
  끌 수 없다. opt-in이라야 새 설치가 애초에 먹통이 되지 않는다

`project.md`의 제약도 같은 방향이다 — "계측은 discovery 모드에서만 켠다. 일반 플레이
경로에서는 완전히 꺼져 있어야 한다."

**SDK 설치와 discovery 실행은 같은 선택이 아니다.** SDK는 프로젝트에 계속 설치돼 있는
제품이고, 게이트가 없으면 모든 Editor/Development 실행에서 씬이 로드될 때마다 스캔하고
파일을 쓴다.

ARTEL-395에서는 `Editor/Install` 중 `DiscoveryDefine`·`DiscoveryMenu`(끄고 켜는 수단)는
이식하고 `FirstImport`(첫 임포트에 **자동으로 켜는** 코드)만 제외한다. 켜는 것은 사람이
한다.

### **WordVenture는 `ARTEL_AFFORDANCE`를 갖고 있다** — 검증 대상 선택에 영향

이 사실을 놓치면 검증이 무의미해진다.

```
커밋된 상태     Standalone: DOTWEEN
로컬 작업트리   Standalone: DOTWEEN;ARTEL_AFFORDANCE
```

프로토타입의 일회성 설치기가 넣은 것이고 (`ProjectSettings/ArtelAffordance.txt` 표식
참조) 커밋되지 않았다. 즉 **WordVenture에서는 `defineConstraints`를 빼든 두든
`Unity.Artel.Affordances.CodeGen.dll`이 똑같이 만들어진다.** 그 프로젝트로는 게이트
제거를 증명할 수 없다.

게이트를 유지하기로 했으므로 이 사실은 두 가지를 정한다.

**컴파일 확인은 WordVenture에서 한다.** 심볼이 있어야 asmdef이 컴파일되므로, 심볼이
없는 throwaway 프로젝트에서는 `Unity.Artel.Affordances.CodeGen.dll`이 **나오지 않는
것이 정상**이다. 그것이 게이트가 일하고 있다는 증거다.

**잠금 검증도 WordVenture에서만 의미가 있다.** 심볼이 있으므로 게이트 ②(`IsEnabledFor`)가
`Assembly-CSharp`에 대해 **true를 반환한다.** 따라서 분석기와 게임 어셈블리 사이에 서
있는 것은 `Unlocked` 스위치뿐이고, 잠금이 새면 실제로 샌다. 심볼이 없는 프로젝트에서는
게이트가 먼저 막아서 잠금을 시험하지 못한다.

### 게이트 제거는 왜 여기가 아닌가

게이트는 두 곳이다:

```
① asmdef defineConstraints    ILPP 어셈블리가 컴파일되는지        → 이 이슈에서 제거
② IsEnabledFor / EnableDefine 대상 게임 어셈블리에 심볼이 있는지   → ARTEL-393
```

②를 여기서 지우지 않는 이유: **잠겨 있는 동안에는 관측되지 않는다.** `WillProcess`가
어차피 false를 반환하므로 ②를 지우든 두든 동작이 같고, 확인할 수단이 없는 변경을
넣으면 이 이슈가 "옮기기"에서 멀어지기만 한다. ②는 잠금이 풀려 효과가 보이는
ARTEL-393에서 지운다.

앞선 Jira 코멘트에는 ②도 이 이슈에서 지운다고 적었으나, 잠금과의 관계를 따져 보고
바꿨다. 이슈에 반영한다.

### 잠금 방식 — 조건을 죽이지 않고 스위치 하나

`WillProcess`를 `return false`로 바꾸면 `IsEnabledFor`와 `IsSkipped`가 **둘 다 미사용
코드가 된다.** 9,472줄을 옮기는 PR에 죽은 메서드 둘을 얹으면 리뷰어가 그것부터 묻는다.

```csharp
public override bool WillProcess(ICompiledAssembly compiledAssembly) =>
    Unlocked && IsEnabledFor(compiledAssembly) && !IsSkipped(compiledAssembly.Name);

/// <summary>ARTEL-393이 이 자리를 지운다.</summary>
private static readonly bool Unlocked = false;
```

`const`가 아니라 `static readonly`인 이유는 **이 값이 컴파일 시점에 접히는 리터럴이
아니라 런타임 스위치 상태로 읽히게 하려는 것**이다. ARTEL-393 리뷰어가 이 자리를 볼 때
"지우면 되는 스위치"로 보여야 한다.

`const`로 두면 경고가 나는지는 **확인하지 못했다.** 이 환경에 독립 C# 컴파일러가 없다
(`dotnet`·`mono`·`csc` 없음, Unity 설치본에도 없음). 처음에 CS0162를 근거로 적었으나
그 진단은 도달 불가 *문장*에 대한 것이라 이 표현식에는 맞지 않을 가능성이 높다.
확인하지 못한 진단 번호를 근거로 남기지 않는다 — 위의 가독성 이유만으로 충분하다.

원래 조건은 그대로 남아 리뷰어가 무엇이 잠겼는지 볼 수 있고, 해제는 한 줄 삭제다.

### `.meta`는 함께 옮긴다

50개 파일 전부 git이 추적한다. GUID를 보존해야 이후 스택에서 참조가 끊기지 않는다.
ARTEL-391과 같은 규율이다.

### 측정 공백

ARTEL-391에서 `samples/WordVenture` 로컬 매니페스트에서 `kr.artel.affordance`를 뺐다.
`Scan`이 ARTEL-394에서 오므로 이 이슈에서도 affordance 리포트는 나오지 않는다.
`tools/measure-wordventure.sh`를 돌리지 않는다.

## Approach (Checklist)

- [ ] **Step 0: Recon**
  - [ ] `Packages/kr.artel.sdk/Editor/Affordance/`가 없는지 확인
  - [ ] 프로토타입에서 `git ls-files Editor/CodeGen`이 50개인지 확인
  - [ ] 이식 전 EditMode 기준선 확보 (ARTEL-391 머지 후 상태 = 이 브랜치의 부모)

- [ ] **Step 1: Implementation**
  - [ ] `Editor/Affordance.meta`·`Editor/Affordance/CodeGen.meta` 생성 (새 폴더)
  - [ ] `.cs` 24개와 `.meta` 24개를 **내용 변경 없이** 복사
  - [ ] `Unity.Artel.Affordances.CodeGen.asmdef`와 `.meta` 복사 후
        `defineConstraints`만 `[]`로 — **다른 필드는 손대지 않는다**
  - [ ] `AffordanceILPostProcessor.cs`에 `Unlocked` 스위치 추가 (위 형태)
  - [ ] 그 외 `.cs` 23개는 **한 글자도 바꾸지 않는다** — diff로 확인

- [ ] **Step 2: Tests** — Validation 참조
- [ ] **Step 3: Rollout**
  - [ ] PR base를 **ARTEL-391 브랜치**로 (스택 유지)
  - [ ] draft, assignee, `enhancement` 라벨
  - [ ] 서브모듈은 건드리지 않는다

## Validation

```bash
WV=~/Desktop/soma/artel-sdk/samples/WordVenture
SRC=~/Desktop/soma/artel-sdk-prototype/Packages/kr.artel.affordance/Editor/CodeGen
DST=~/Desktop/soma/artel-sdk/Packages/kr.artel.sdk/Editor/Affordance/CodeGen

# 0) 잠금 검증의 사전 조건 — 분석기가 남기는 흔적을 미리 비운다
rm -rf "$WV/Library/ArtelScope"

# 1) WordVenture 배치 컴파일
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath "$WV" -logFile /tmp/artel-392-compile.log
grep -icE 'error CS|Assembly with name|Compilation failed' /tmp/artel-392-compile.log || true

# 2) 잠금이 새지 않았나 — 주 증거. Process() 는 모든 경로에서 Report() 를 부르고
#    Report() 는 Library/ArtelScope/<assembly>.txt 를 쓴다
ls -A "$WV/Library/ArtelScope" 2>/dev/null | wc -l
grep -c '\[Artel\]' /tmp/artel-392-compile.log || true

# 3) 보조 증거 — 구워진 attribute 흔적
strings "$WV/Library/ScriptAssemblies/Assembly-CSharp.dll" \
  | grep -cE 'AffordanceAttribute|Artel\.Affordances' || true

# 4) SDK 위빙 무회귀
strings "$WV/Library/ScriptAssemblies/Assembly-CSharp.dll" \
  | grep -E 'ArtelInput|artelActionBuffer' | sort -u

# 5) 옮긴 것이 안 바뀌었나 — .cs 만이 아니라 .meta·asmdef 까지 전부
for f in "$SRC"/*; do diff -q "$f" "$DST/$(basename "$f")"; done

# 6) 개수 — 커밋 전이라 git ls-files 는 0을 낸다(색인을 읽으므로). 파일계로 센다
find ~/Desktop/soma/artel-sdk/Packages/kr.artel.sdk/Editor/Affordance* -type f | wc -l

# 7) EditMode + 게이트 제거 증명 (심볼이 하나도 없는 프로젝트)
.github/scripts/setup-unity-test-project.sh /tmp/artel-392-tests
/Applications/Unity/Hub/Editor/2022.3.34f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath /tmp/artel-392-tests \
  -testResults /tmp/artel-392-tests/results.xml -logFile /tmp/artel-392-tests/unity.log
python3 .github/scripts/summarize-test-results.py /tmp/artel-392-tests/results.xml EditMode
ls /tmp/artel-392-tests/Library/ScriptAssemblies/ | grep -ci 'Affordances.CodeGen' || true

# 8) WordVenture(심볼 있음)에서는 컴파일된다
ls "$WV/Library/ScriptAssemblies/" | grep -i 'Affordances.CodeGen'
```

| # | 기대값 |
|---|---|
| 1 | `0` |
| 2 | **`0`** 그리고 **`0`** — ArtelScope에 파일이 없고 로그에 `[Artel]` 진단이 없다. 잠금의 **주 증거**다. 두 값 모두 ARTEL-391 로그에서 0으로 실측했다 |
| 3 | `0` — 다만 이것만으로는 부족하다. 아래 「잠금 검증이 왜 ArtelScope인가」 참조 |
| 4 | `ArtelInput`·`__artelActionBuffer` 있음 |
| 5 | **정확히 1줄**만 나온다 — `AffordanceILPostProcessor.cs`(잠금 스위치). asmdef을 포함한 나머지 49개는 바이트 동일해야 한다(GUID 보존) |
| 6 | **52** — 원본 50 + 새 폴더 `.meta` 2개(`Editor/Affordance.meta`, `Editor/Affordance/CodeGen.meta`) |
| 7 | `253 passed / 0 failed` (아래 「기준선의 출처」) 그리고 dll 개수 **`0`**. 이 프로젝트에는 define이 하나도 없으므로 컴파일되지 않는 것이 **게이트가 일하고 있다는 증거**다 |
| 8 | `Unity.Artel.Affordances.CodeGen.dll` — 심볼이 있는 곳에서는 컴파일된다 |

### 잠금 검증이 왜 ArtelScope인가

처음에는 `strings Assembly-CSharp.dll | grep AffordanceAttribute`를 주 증거로 삼았다.
**그것만으로는 잠금 누수를 못 잡는다.**

- evidence는 `kr.artel.affordance.evidence`라는 이름의 **deflate 압축 리소스**로 들어간다
  (`EvidenceResource.cs:39`, `:79`). 이름도 패턴에 안 걸리고 내용도 압축돼 있어
  `strings`에 잡히지 않는다
- attribute는 **anchor 대상이 하나라도 있을 때만** 붙는다. 분석이 돌았는데 아무것도
  못 찾았거나 writer가 거절하면 흔적이 0이다 — 잠긴 것과 구분되지 않는다
- 지금 빌드가 이미 0이라 **양성 대조군이 없다**

반면 `Process()`는 어느 경로로 빠지든 `Report()`를 부르고
(`AffordanceILPostProcessor.cs` 110·280·302·309·335·344·349·742),
`Report()`는 `Library/ArtelScope/<assembly>.txt`를 쓴다 (`ScopeReport.cs:25`, `:32`).
**분석기가 돌았다는 사실 자체가 남는다** — 무엇을 찾았는지와 무관하게.

**로그는 어셈블리 이름이 아니라 진단 접두어로 센다.** 처음에는
`kr.artel.affordance|Artel.Affordances`로 좁히려 했는데, ARTEL-391 검증 로그에 대고
실제로 재 보니 **깨끗한 빌드에서 13이 나온다.** 걸리는 것은 전부 Bee 빌드가
`Artel.Affordances.Runtime.dll`을 컴파일·복사하는 평범한 줄이다:

```
/tmp/artel-391-compile.log:273  Processing assembly .../Artel.Affordances.Runtime.dll
                          :304  WriteText .../Artel.Affordances.Runtime.rsp2
```

잠금이 완벽해도 실패하는 검사였고, 그런 검사는 한 번 무시되기 시작하면 정작 중요할 때도
무시된다. ARTEL-392 이후에는 `Unity.Artel.Affordances.CodeGen`이 같은 줄을 더해 숫자가
오히려 늘어난다.

같은 로그의 실측:

```
grep -c '\[Artel\]'   → 0
grep -c 'ArtelScope'  → 0
```

`Report()`는 `"[Artel] " + assemblyName + ": " + detail`을 낸다
(`AffordanceILPostProcessor.cs:670`). 어떤 경로로 빠지든 나오므로 원하던 양성 신호가
정확히 이것이고, `ScopeReport.TryWrite`가 실패해도 살아남는다.

SDK 자체 위버도 `[Artel] `을 쓰지만(`ActionMethodWeaver.cs:294`) **그건 문제가 아니라
이득이다.** 그쪽은 `AddError` 안에서만, 위빙이 실패했을 때만 낸다. 기준선이 0이므로
검사는 "`[Artel]` 진단이 하나라도 있으면 이상하다"가 되고, SDK 위빙 실패도 이 검증이
잡아야 할 일이다.

`grep -c`는 하나도 못 찾으면 종료 코드 1이라 `set -e` 아래에서 스크립트를 죽인다.
`|| true`를 붙인 이유다.

### 기준선의 출처와 비교 방법

부모 브랜치(ARTEL-391)의 EditMode 기준선을 **다시 잴 필요가 없다.** 그 커밋에서 같은
명령으로 이미 측정했고, 그 앞의 `origin/develop`에서도 같은 값이 나왔다:

```
origin/develop (merge-base)   253 passed · 0 failed
ARTEL-391 브랜치              253 passed · 0 failed
```

**실패 집합이 공집합이므로 이름을 대조할 것이 없다.** 이 이슈에서 하나라도 실패하면
그것이 곧 회귀다.

`summarize-test-results.py`는 실패가 있을 때 이름을 표로 출력하고
(`| \`{fullname}\` | {message} |`) `::error title=...::` 행도 낸다. 따라서 실패가
생기면 이름은 그 출력에서 바로 읽는다. 브랜치를 오가며 다시 측정할 필요가 없다.

이 이슈가 추가하는 것은 **Editor 전용 어셈블리 하나**뿐이라 테스트 대상
(`kr.artel.sdk/Tests`)의 구성이 달라지지 않는다. 총 개수 253도 그대로여야 한다 —
숫자가 늘거나 줄면 테스트 발견 경로가 바뀐 것이므로 그 자체가 조사 대상이다.

## Risks & Rollback

- **잠금 누수 (높음).** 잠금이 안 걸리면 9,472줄이 곧바로 게임 어셈블리를 건드린다.
  WordVenture에 `ARTEL_AFFORDANCE`가 있어 게이트 ②가 막아주지 않으므로 `Unlocked`가
  유일한 방벽이다. **Validation 2번(ArtelScope + 컴파일 로그)이 주 증거**이고 3번은 보조다
- **파일 누락 (중간).** 50개 중 하나라도 빠지면 컴파일이 깨지거나 조용히 기능이 준다.
  컴파일이 대부분 잡지만 `.meta` 누락은 안 잡힌다 — **Validation 6번의 개수 52**로 잡는다
- **GUID 유실 (중간).** ARTEL-391과 같은 위험. **Validation 5번이 `.meta`까지 전부
  diff**하므로 GUID가 바뀌면 그 파일이 차이 목록에 나타난다 (기대는 정확히 2줄)
- **두 ILPP 공존 (낮음, 이번엔).** 잠겨 있어 실제 상호작용이 없다. 실제 위험은
  ARTEL-393에서 시작된다

- **Rollback:** `git revert` 한 번. 추가만 있고 기존 파일 수정이 없다
  (`kr.artel.sdk` 기준). 서브모듈은 되돌릴 것이 없다

## Rejected feedback

- **medium — `const` 대신 `static readonly`를 고른 근거를 실측하라(옵션 a).** 실측을
  택하지 않고 근거를 바꾸는 쪽(옵션 b)을 골랐다. 이 환경에 독립 C# 컴파일러가 없어
  실측하려면 Unity 전체 컴파일을 한 번 더 돌려야 하는데, 얻는 것이 주석 한 줄의 진단
  번호다. `static readonly`를 고른 이유는 경고 회피가 아니라 가독성이므로 근거를
  그것으로 고쳤다. 지적 자체는 옳았다 — 확인하지 못한 기술적 주장을 근거로 적고 있었다.

## Open Questions

없음. 게이트 제거 범위(② → ARTEL-393)는 위에 근거와 함께 결정으로 적었고, 이견이 있으면
리뷰에서 나올 것이다.
