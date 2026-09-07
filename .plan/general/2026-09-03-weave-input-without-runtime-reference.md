# 2026-09-03 — game assembly가 UnityPlayMcp type을 안 써도 input을 weaving한다

- Date: 2026-09-03
- GitHub Issue: #47
- Status: Implemented · pair-review PASS · EditMode 로컬 실행 완료

## Goal

UnityPlayMcp type을 하나도 쓰지 않는 보통의 game assembly에서도 `UnityEngine.Input` 호출이
`UnityPlayMcp.VirtualInput` 호출로 바뀌게 한다. weaver 가 `UnityPlayMcp.Runtime` 에 대한
IL assembly reference 를 weaving 의 **전제**가 아니라 **결과**로 다루도록 순서를 뒤집는다.

## Non-goals

- weaver 의 다른 부분 재설계. `SupportedMethodNames`, signature 매칭 방식, `Process()` 가 도는
  범위는 그대로 둔다.
- `InputCallILPostProcessor.WillProcess` 의 판정 기준 변경. compiler reference 를 보는 지금 방식이 맞다.
- `mcp/**` 아래 어떤 파일도 건드리지 않는다 (issue #48 작업자가 소유).

## Context / Constraints

`InputMethodWeaver.TryCreate` 는 `module.AssemblyReferences` 에 `UnityPlayMcp.Runtime` 이 이미
있을 때만 weaver 를 만든다 (`Editor/CodeGen/InputMethodWeaver.cs:36-41`). IL 의 assembly reference 는
그 assembly 의 type 을 실제로 쓸 때만 생기는데, 그 reference 를 만드는 일이 바로 weaving 이다.
그래서 `UnityPlayMcpHost` 같은 type 을 손으로 참조하지 않는 game 은 조건이 영원히 성립하지 않고,
`InputCallILPostProcessor.Process` 가 assembly 를 그대로 돌려준다 (`InputCallILPostProcessor.cs:50-54`).

제약:

- `WillProcess` (`InputCallILPostProcessor.cs:24`) 는 `compiledAssembly.References` 에
  `UnityPlayMcp.Runtime.dll` 이 있을 때만 통과시킨다. 이 목록에 runtime 이 들어가는 경로는 둘이다.
  `UnityPlayMcp.Runtime.asmdef` 의 `autoReferenced: true` 는 `Assembly-CSharp` 같은 predefined
  assembly 를 덮고 (issue 의 WordVenture 가 이 경우다), asmdef 를 쓰는 assembly 는 `references` 에
  직접 적어야 한다. 어느 쪽이든 `Process` 안에서 runtime dll 경로는 손에 있다.
- `Process` 가 module 에 넘기는 `AssemblyResolver` 는 `CompiledAssemblyResolver` 이고, 이 resolver 는
  `compiledAssembly.References` 의 파일 이름만 보고 version 을 무시한다
  (`CompiledAssemblyResolver.cs:31-40`). 그래서 IL 에 reference 가 없는 module 에서도
  이름만 담은 `AssemblyNameReference` 로 runtime assembly 를 열 수 있다.
- 바꿀 `Input` 호출이 하나도 없는 assembly 는 지금처럼 손대지 않아야 한다. 이 규칙을 어기면 project 의
  모든 assembly 에 쓸모없는 `UnityPlayMcp.Runtime` reference 가 붙는다.
- Mono.Cecil 은 `com.unity.nuget.mono-cecil@1.11.4` (Cecil 0.11.4). 이 version 의
  `DefaultMetadataImporter.ImportScope` 는 `FullName` 이 같은 `AssemblyNameReference` 를 찾아
  재사용하고, 없을 때만 새로 만들어 `module.AssemblyReferences` 에 넣는다.
- 이 machine 에는 Unity editor 가 없다. EditMode/PlayMode suite 는 CI
  (`.github/workflows/unity-tests.yml`) 만 돌릴 수 있다.

## Approach (Checklist)

- [x] **Step 0: Recon** — 읽기 완료.
  `Editor/CodeGen/InputMethodWeaver.cs`, `Editor/CodeGen/InputCallILPostProcessor.cs`,
  `Editor/CodeGen/CompiledAssemblyResolver.cs`, `Runtime/UnityEngine/Input.cs`,
  `Tests/Fixtures/InputFixtureBehaviour.cs`, `Tests/Runtime/UnityEngine/VirtualKeyboardStateTests.cs`,
  `Tests/Runtime/UnityEngine/VirtualAxisStateTests.cs`, 그리고 다섯 개의 asmdef.
  `InputMethodWeaver` 를 부르는 곳은 `InputCallILPostProcessor.cs:50` 하나뿐이고,
  `InputFixtureBehaviour.Manager` 를 읽는 곳은 어디에도 없다 (repository 전체 grep).

- [x] **Step 1: Implementation**

  1. `Editor/CodeGen/InputMethodWeaver.cs` — 순서를 뒤집는다.
     - `TryCreate` 를 없애고 `public InputMethodWeaver(ModuleDefinition module)` 하나만 남긴다.
       생성자는 module 을 담기만 하고 아무것도 resolve 하지 않는다.
     - call site 를 나르는 자료형은 private nested `readonly struct InputCallSite` 하나:
       `Instruction Instruction` 과 `MethodDefinition ProxyMethod` 두 property. tuple 대신 이름 있는
       type 을 쓰는 것은 `coding-style.md` 의 "Data Shapes" 규칙을 따르는 것이기도 하다.
     - `Process()` 는 네 단계로 간다.
       1. `CollectInputCalls()` — `UnityEngine.Input` 을 declaring type 으로 하고 이름이
          `SupportedMethodNames` 에 있는 call instruction 을 `List<Instruction>` 으로 모은다.
          비어 있으면 `false` 를 돌려주고 끝. runtime assembly 를 열지도 않는다.
       2. `ResolveRuntimeAssembly()` 로 runtime 을 열고 `UnityPlayMcp.VirtualInput` 의
          `MethodDefinition` 을 signature → definition dictionary 로 만든다. **import 는 하지 않는다** —
          `ImportReference` 자체가 reference 를 만들기 때문이다.
       3. 1의 instruction 마다 signature 를 한 번 계산해 dictionary 에서 찾고, 맞는 것만
          `List<InputCallSite>` 로 모은다. 비어 있으면 `false`.
       4. 하나라도 있으면 `AddRuntimeReference` 로 `AssemblyNameReference` 를 module 에 붙이고,
          그 다음 `module.ImportReference` 로 proxy 를 import 해 `instruction.Operand` 를 갈아 끼운다.
          같은 `MethodDefinition` 은 `Dictionary<MethodDefinition, MethodReference>` 로 한 번만 import
          한다. `true` 를 돌려준다.
     - `ResolveRuntimeAssembly()` — `module.AssemblyReferences` 에 `UnityPlayMcp.Runtime` 이 이미
       있으면 그 reference 로, 없으면 `new AssemblyNameReference("UnityPlayMcp.Runtime",
       new Version(0, 0, 0, 0))` 로 `module.AssemblyResolver.Resolve` 를 부른다. 이 임시 reference 는
       resolver 에 묻기 위한 것일 뿐, module 에 넣지 않는다.
     - `AddRuntimeReference(AssemblyNameDefinition runtimeName)` — 순서는 이렇다.
       (a) `module.AssemblyReferences` 에 이름이 `UnityPlayMcp.Runtime` 인 reference 가 이미 있으면
       아무것도 하지 않고 돌아간다. (b) 없으면 resolve 된 `AssemblyDefinition.Name` 의
       `Name`, `Version`, `Culture`, `PublicKeyToken`, `HashAlgorithm`, `IsRetargetable`,
       `IsWindowsRuntime` 를 그대로 복사한 새 `AssemblyNameReference` 를 만들어
       `module.AssemblyReferences.Add` 한다. 앞의 네 값이 `FullName` 을 이루므로, 뒤이어 부르는
       `ImportReference` 는 방금 넣은 reference 를 scope 로 재사용하고 같은 이름의 reference 를
       하나 더 만들지 않는다.
     - resolve 실패(`AssemblyResolutionException`)는 잡지 않는다. `WillProcess` 가
       `compiledAssembly.References` 에 runtime dll 이 있는 경우에만 `Process` 를 부르고 그 목록이
       그대로 resolver 로 들어가므로 닿을 수 없는 경로다. 지금 code 도 같은 자리에서 같은 이유로
       잡지 않는다. 이 invariant 를 주석으로 남긴다.

  2. `Editor/CodeGen/InputCallILPostProcessor.cs` — `TryCreate` null 분기를 지우고
     `new InputMethodWeaver(assembly.MainModule).Process()` 의 `changed` 하나로 판단한다.
     `changed == false` 면 지금처럼 `new ILPostProcessResult(null, diagnostics)` 를 돌려준다.
     그 위의 주석을 새 순서에 맞게 다시 쓴다.

  3. `Tests/Fixtures/InputFixtureBehaviour.cs` — `public UnityPlayMcpHost Manager;` field 와 그 위
     XML doc comment 를 지운다. 이 field 가 없어져야 fixture assembly 가 UnityPlayMcp type 을
     하나도 이름 대지 않는, 진짜 game assembly 와 같은 모양이 된다. class 단위 `<remarks>` 는 그대로 둔다.

- [x] **Step 2: Tests**

  1. 새 fixture assembly `Tests/Fixtures/NoInput/`:
     - `UnityPlayMcp.Input.Fixtures.NoInput.asmdef` — `references` 에 `UnityPlayMcp.Runtime` 을 적는다.
       **이것은 load-bearing 이다.** asmdef 로 정의된 assembly 는 `autoReferenced` 와 무관하게
       `references` 에 적은 것만 compiler reference 로 받는다. 여기서 빼면
       `compiledAssembly.References` 에 runtime dll 이 없어 `WillProcess` 가 false 를 돌려주고,
       postprocessor 가 아예 돌지 않은 assembly 를 두고 "건드리지 않았다" 고 주장하는
       빈 test 가 된다.
     - `NoInputFixture.cs` — `Input` 호출이 하나도 없는 최소한의 public class.
       `namespace UnityPlayMcp.Tests.Fixtures.NoInput` 아래 `public sealed class NoInputFixture`,
       body 는 상수 하나를 돌려주는 method 하나. `MonoBehaviour` 를 상속하지 않는다.
     - 두 파일과 folder 의 `.meta` 를 기존 `.meta` 형식 그대로 손으로 만든다.
  2. `Tests/Runtime/UnityPlayMcp.Runtime.Tests.asmdef` 의 `references` 에
     `UnityPlayMcp.Input.Fixtures.NoInput` 을 추가한다. 이것이 없으면 새 test 가 `NoInputFixture` 를
     이름 댈 수 없어 compile 이 깨진다.
  3. 새 test `Tests/Runtime/UnityEngine/InputWeavingReferenceTests.cs` (EditMode assembly
     `UnityPlayMcp.Runtime.Tests` 안) — `Assembly.GetReferencedAssemblies()` 로 IL 의 assembly
     reference 를 직접 본다.
     - `typeof(InputFixtureBehaviour).Assembly` 는 source 에서 UnityPlayMcp type 을 하나도 쓰지 않는데도
       `UnityPlayMcp.Runtime` 을 참조한다 — weaver 가 붙였다는 뜻.
     - `typeof(NoInputFixture).Assembly` 는 `UnityPlayMcp.Runtime` 을 참조하지 않는다 — 바꿀 것이 없는
       module 은 건드리지 않았다는 뜻.
  4. 기존 `IlPostProcessor_ReroutesUnityInputCallsToVirtualInput` 과
     `IlPostProcessor_ReroutesUnityAxisCallsToVirtualInput` 은 그대로 둔다. `Manager` field 가 사라진
     뒤에도 이 둘이 통과하는 것이 이 결함을 다시 잡는 장치다.

- [x] **Step 3: Rollout / Rollback** — feature flag 없음, migration 없음. IL 만 바뀌고 public API 는
  그대로다. 되돌리려면 commit 하나를 revert 하면 된다.

## Validation

- **Commands to run (이 machine 에서 가능):**
  - `git diff origin/develop...HEAD` 로 scope 확인 —
    `Packages/dev.yunseong.unityplaymcp/Editor/CodeGen/**`, `Packages/dev.yunseong.unityplaymcp/Tests/**`,
    `.plan/general/**` 밖의 파일이 없어야 한다. `mcp/**` 는 하나도 없어야 한다.
  - 새 `.meta` 의 guid 가 repository 안에서 유일한지 `grep -r` 로 확인.
  - `InputFixtureBehaviour.Manager` 를 읽는 곳이 없는지 `grep -r` 로 확인.
- **EditMode suite 를 실제로 돌렸다.** 이 machine 은 WSL 이지만 Windows 쪽에
  `2022.3.34f1` editor 가 있고, 이것은
  `.github/unity-test-project/ProjectSettings/ProjectVersion.txt` 가 고정한 version 과 같다.
  `.github/scripts/setup-unity-test-project.sh` 로 `C:\unity-play-mcp-test-47` 에 throwaway
  project 를 만들고 `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode` 를 돌렸다.
  결과는 아래 Validation Result 에 적는다.
- **Expected output:** EditMode 와 PlayMode 가 모두 green. 특히 EditMode 에서 `Manager` field 없이
  `IlPostProcessor_Reroutes*` 두 test 가 통과하고, 새 `InputWeavingReferenceTests` 의 두 assertion 이
  통과.

## Validation Result (2026-09-03)

editor `2022.3.34f1`, project `C:\unity-play-mcp-test-47`.

| run | 결과 |
| --- | --- |
| EditMode (이 branch) | **261 passed / 0 failed** |
| PlayMode (이 branch) | **22 passed / 0 failed** |
| EditMode (weaver 만 `origin/develop` 으로 되돌림) | **258 passed / 3 failed** |

세 번째 run 이 이 change 의 근거다. `Tests/` 는 이 branch 그대로 두고
`Editor/CodeGen/InputMethodWeaver.cs` 와 `InputCallILPostProcessor.cs` 만 `origin/develop` 것으로
바꿔 돌렸다. 즉 `Manager` field 가 없는 fixture + 옛 weaver 다. 깨진 셋:

- `InputWeavingReferenceTests.Weaver_AddsRuntimeReference_ToAnAssemblyThatNamesNoUnityPlayMcpType`
  — `Expected: True But was: False`. IL 에 `UnityPlayMcp.Runtime` reference 가 안 생겼다.
- `VirtualKeyboardStateTests.IlPostProcessor_ReroutesUnityInputCallsToVirtualInput`
  — `Expected: True But was: False`
- `VirtualAxisStateTests.IlPostProcessor_ReroutesUnityAxisCallsToVirtualInput`
  — `Expected: 1.0f But was: 0.0f`

즉 `Manager` field 가 없으면 옛 weaver 는 fixture 를 통째로 건너뛴다 — issue #47 이 적은 그대로다.
`Weaver_LeavesAnAssemblyWithNoInputCallsAlone` 은 세 run 모두 통과한다. pair review 가 지적한 대로
이 test 는 결함을 잡는 test 가 아니라 이 change 가 새로 들이는 규칙을 지키는 test 다.

**주의**: 이 세 run 모두 process exit code 는 0 이었다. 실패가 있던 run 도 0 이다.
`.agents/docs/project.md` 가 말하는 대로 `results.xml` 을 파싱해야 한다 — exit code 를 믿으면 안 된다.

## Risks & Rollback

- **Risks:**
  - Cecil importer 가 손으로 넣은 `AssemblyNameReference` 를 `FullName` 으로 못 알아보면 module 에
    같은 이름의 reference 가 두 개 생긴다. `Name`/`Version`/`Culture`/`PublicKeyToken` 을 resolve 된
    정의에서 그대로 복사해 이 위험을 없앤다.
  - `CompiledAssemblyResolver` 가 version 을 무시한다는 사실에 기댄다. 같은 folder 에 있는 동작이고
    `Process` 가 넘기는 resolver 도 이것 하나뿐이지만, 주석으로 근거를 남긴다.
  - 새 fixture assembly 가 늘어나 Unity 의 compile 대상이 하나 더 생긴다. 크기는 class 하나.
  - 손으로 쓴 `.meta` guid 를 Unity 가 그대로 받는다. 형식은 기존 `.meta` 를 그대로 따르고,
    asmdef 사이의 참조는 GUID 가 아니라 이름으로 걸려 있어 guid 오타가 참조를 깨뜨리지 않는다.
  - Unity 없이 검증하므로 compile error 가 CI 에서야 드러난다. 그래서 새 test 가 이름 대는 type 과
    asmdef reference 를 plan 단계에서 못 박아 두었다.
- **Rollback steps:** `git revert` 한 commit.

## Pair review (2026-09-03)

`pair-review-critic` 결과 **PASS**. Cecil 0.11.4 API (`AssemblyNameReference` 생성자와
`Culture`/`PublicKeyToken`/`HashAlgorithm`/`IsRetargetable`/`IsWindowsRuntime` setter), `InputCallSite`
struct, 새 test 의 using 과 asmdef reference, `AssemblyDefinition` 소유권(`CompiledAssemblyResolver`
가 `assembly.Write` 뒤에 dispose 한다)을 모두 확인함. blocker 없음.

받아들인 지적 하나 — **`Weaver_LeavesAnAssemblyWithNoInputCallsAlone` 은 고치기 전 code 에서도
통과한다.** 옛 `TryCreate` gate 와 새 `candidates.Count == 0` early return 이 "`Input` 호출이 없는
assembly" 에 대해서는 같은 결과를 내기 때문이다. 이 test 를 지우지는 않는다 — 이 diff 가 새로
들이는 규칙("바꿀 것이 없으면 reference 를 붙이지 않는다")을 지키는 자리가 맞다. 다만 #47 의
결함을 잡는 test 가 아니므로 PR 에 그렇게 적는다. 결함을 잡는 것은
`Weaver_AddsRuntimeReference_ToAnAssemblyThatNamesNoUnityPlayMcpType` 와, `Manager` field 가 사라진
뒤의 기존 `IlPostProcessor_Reroutes*` 두 test 다.

## Rejected feedback

- medium review 의 "`UnityPlayMcp.Input.Fixtures.NoInput.asmdef` 의 `UnityPlayMcp.Runtime` 참조는
  `autoReferenced: true` 때문에 불필요하다" 는 지적은 받지 않는다. `autoReferenced` 는
  `Assembly-CSharp` 같은 predefined assembly 에만 적용되고, asmdef 로 정의된 assembly 는 `references`
  에 적은 것만 받는다. 기존 `UnityPlayMcp.Input.Fixtures.asmdef` 가 `UnityPlayMcp.Runtime` 을 적어 둔
  이유도 이것이다. 빼면 `WillProcess` 가 false 를 돌려주어 negative test 가 아무것도 증명하지 못한다.
  대신 Context 의 "거의 모든 assembly" 라는 뭉뚱그린 표현을 두 경로로 나누어 정확하게 고쳤다.

## Open Questions

- 없음.
