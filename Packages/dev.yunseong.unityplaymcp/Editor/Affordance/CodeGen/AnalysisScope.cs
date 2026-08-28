using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>그 타입이 무엇으로 밝혀졌는가.</summary>
    internal enum TypeVerdict
    {
        /// <summary>GameObject 위에 게임 로직을 실을 수 없다.</summary>
        NotBehaviour,

        /// <summary>MonoBehaviour 를 상속한다.</summary>
        Behaviour,

        /// <summary>
        /// 답에 닿기 전에 기반 타입 사슬이 끊겼다.
        /// </summary>
        /// <remarks>
        /// <see cref="NotBehaviour"/> 와 일부러 갈라 둔다. 기반 클래스가 열리지 않는 어셈블리에 사는 타입은
        /// 애초에 behaviour 였던 적 없는 타입과 똑같아 보이고, 한쪽을 다른 쪽으로 조용히 취급하면 게임 자신의
        /// 코드를 아무 말 없이 떨어뜨리게 된다. 둘을 가리는 데 드는 값은 카운터 하나이고, 그 차이는 작은
        /// 숫자와 틀린 답의 차이다.
        /// </remarks>
        Unresolved
    }

    /// <summary>이 메서드를 분석할 값이 있는 이유.</summary>
    internal enum MethodScope
    {
        /// <summary>플레이어의 손을 거쳐 이 메서드에 닿는 것이 없다.</summary>
        OutOfScope,

        /// <summary>인스펙터에서 UnityEvent 에 연결됐다 — 예컨대 버튼의 onClick.</summary>
        InspectorCallable,

        /// <summary>엔진이 부른다. 키와 포인터 처리가 사는 자리다.</summary>
        EngineMessage
    }

    /// <summary>
    /// 들여다보기 전에 무엇을 들여다볼 값이 있는지를 먼저 정한다.
    /// </summary>
    /// <remarks>
    /// 이 분석의 첫 판은 스크립트 수백 개짜리 프로젝트에서 느렸는데, 이유는 일이 비쌌기 때문이 아니라 그
    /// 일의 거의 전부가 결과에 영향을 줄 수 없었기 때문이다. 먼저 좁히는 것이 나중에 캐시하는 것보다 싸고,
    /// 뒤 단계들을 따져 볼 수 있을 만큼 작게 유지하는 것도 그것이다.
    /// </remarks>
    internal static class AnalysisScope
    {
        private const string BehaviourTypeName = "UnityEngine.MonoBehaviour";
        private const string ObjectTypeName = "UnityEngine.Object";

        /// <summary>
        /// 기반 타입 걷기가 쓰레기로 취급되기까지 갈 수 있는 거리.
        /// </summary>
        /// <remarks>
        /// 정상적인 것 중에 이만큼 깊은 것은 없다. 이 한계는 깊이를 위한 것이 아니라 순환을 위한 것이다: 손으로
        /// 쓰거나 난독화한 어셈블리는 타입을 제 조상으로 적을 수 있고, 이 루프는 이미 에디터를 얼려서 왜
        /// 그런지 알아보려 열 수조차 없게 만든 그런 종류다.
        /// </remarks>
        private const int MaxInheritanceDepth = 32;

        /// <summary>
        /// 이 명령어 수를 넘으면 메서드를 건드리지 않는다.
        /// </summary>
        /// <remarks>
        /// 생성된 코드는 — 상태 기계, 커다란 switch 분배기 — 사람이 쓰지 않는 크기로 도착한다. 조용히
        /// 떨어뜨리지 않고 세어서 보고한다. 이만큼 큰 메서드는 대개 컴파일러 산출물이고, 그것을 아는 것이
        /// 그것에 대해 무엇을 할지를 바꾸기 때문이다.
        /// </remarks>
        internal const int MaxInstructions = 4000;

        /// <summary>
        /// 엔진이 behaviour 위에서 부르는 메서드들.
        /// </summary>
        /// <remarks>
        /// 가시성과 무관하게 모은다. 이것들을 나열하는 이유 전체가 그것이다: 이것들은 관례상 private 이라,
        /// public 멤버를 요구하는 필터는 그 전부를 떨어뜨렸다 — 그리고 그와 함께 포인터 처리와 키 입력을 읽는
        /// <c>Update</c> 본문까지 떨어뜨렸다. 앞선 필터를 인스펙터가 부를 수 있는 것으로 제한한 것이 마우스와
        /// 드래그를 셈에서 빠뜨린 원인이다.
        /// </remarks>
        private static readonly HashSet<string> EngineMessages = new HashSet<string>(StringComparer.Ordinal)
        {
            "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
            "Update", "FixedUpdate", "LateUpdate", "OnGUI",

            // 실행의 끝. 나가는 길에 저장하는 게임은 여기서 하고, 그것은 테스트가 알아야 하는 변화다: 다음
            // 실행이 시작하는 상태를 정한 것은 플레이어가 무엇을 눌렀는지가 아니라 그가 종료했는지다. 이것을
            // 빼놓으면 샘플 게임의 "종료하면 진행이 저장된다" 에는 근거가 하나도 없었다.
            "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit",
            "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
            "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",

            // 이벤트 시스템이 인터페이스를 통해 부르는 핸들러들. 그 인자는 EventData 인데, UnityEngine.Object 도
            // 아니고 기본형도 아니어서 인스펙터가 부를 수 있는지 규칙은 그 전부를 돌려보낸다. uGUI 로 만든
            // 프로젝트에서는 클릭과 드래그가 사는 자리가 여기다 — 그 규칙에 맡겨 두면 매직 메서드를 잃었던 누락을
            // 같은 입력 범주에 대해 되풀이하게 된다.
            "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
            "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll",
            "OnInitializePotentialDrag", "OnSubmit", "OnCancel", "OnMove",
            "OnSelect", "OnDeselect"
        };

        /// <summary>그 타입이 GameObject 위에 앉아 게임 로직을 나를 수 있는지 알아낸다.</summary>
        internal static TypeVerdict Inspect(TypeDefinition type)
        {
            if (type == null || type.IsInterface || !type.IsClass)
            {
                return TypeVerdict.NotBehaviour;
            }

            var reached = Walk(type, BehaviourTypeName, out var unresolved);

            if (reached)
            {
                return TypeVerdict.Behaviour;
            }

            return unresolved ? TypeVerdict.Unresolved : TypeVerdict.NotBehaviour;
        }

        internal static MethodScope Classify(MethodDefinition method)
        {
            if (method == null || method.IsStatic || method.IsAbstract || !method.HasBody)
            {
                return MethodScope.OutOfScope;
            }

            if (EngineMessages.Contains(method.Name))
            {
                return MethodScope.EngineMessage;
            }

            return IsInspectorCallable(method) ? MethodScope.InspectorCallable : MethodScope.OutOfScope;
        }

        /// <summary>
        /// UnityEvent 가 이 메서드에 대한 지속 호출을 담을 수 있을 때 참.
        /// </summary>
        /// <remarks>
        /// Unity 인스펙터가 드롭다운에 내놓을 것을 그대로 비춘다: 아무것도 돌려주지 않는 인스턴스 메서드로,
        /// 인자가 없거나 인스펙터가 리터럴을 채워 줄 수 있는 인자 하나를 받는 것.
        /// </remarks>
        private static bool IsInspectorCallable(MethodDefinition method)
        {
            if (!method.IsPublic || method.IsSpecialName || method.HasGenericParameters)
            {
                return false;
            }

            if (method.ReturnType.MetadataType != MetadataType.Void)
            {
                return false;
            }

            if (method.Parameters.Count > 1)
            {
                return false;
            }

            return method.Parameters.Count == 0 || IsInspectorArgument(method.Parameters[0].ParameterType);
        }

        private static bool IsInspectorArgument(TypeReference type)
        {
            switch (type.MetadataType)
            {
                case MetadataType.Boolean:
                case MetadataType.Int32:
                case MetadataType.Single:
                case MetadataType.String:
                    return true;
            }

            var definition = SafeResolve(type);
            if (definition == null)
            {
                return false;
            }

            return definition.IsEnum || DerivesFrom(definition, ObjectTypeName);
        }

        /// <summary>
        /// 이 메서드를 순서대로 읽으면 틀린 답이 나올 때 참.
        /// </summary>
        /// <remarks>
        /// 결정이 하나도 없는 메서드는 경로가 하나이므로 제어 흐름 그래프가 블록 하나가 되고 그것을 만드는 일은
        /// 낭비다. 그 밖의 것은 전부 그래프를 거쳐야 한다: 명령어를 차례대로 걷는 방식은, <c>if/else</c> 사슬이
        /// <c>||</c> 를 그 사이에 넣는 순간 한 키가 지키는 본문을 다른 키의 것으로 읽는다.
        /// </remarks>
        internal static bool NeedsControlFlow(MethodDefinition method)
        {
            var body = method.Body;
            if (body.HasExceptionHandlers)
            {
                return true;
            }

            foreach (var instruction in body.Instructions)
            {
                if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsTooLarge(MethodDefinition method)
        {
            return method.Body.Instructions.Count > MaxInstructions;
        }

        private static bool DerivesFrom(TypeDefinition type, string baseTypeFullName)
        {
            return Walk(type, baseTypeFullName, out _);
        }

        /// <summary>
        /// 이름을 찾아 기반 타입 사슬을 거슬러 오른다.
        /// </summary>
        /// <param name="unresolved">
        /// 답 없이 오르기가 멈췄을 때 설정된다 — 열리지 않는 기반 타입이거나, 스스로에게 되감긴다고 의심할
        /// 만큼 긴 사슬이다.
        /// </param>
        private static bool Walk(TypeDefinition type, string baseTypeFullName, out bool unresolved)
        {
            unresolved = false;
            var current = type;

            for (var depth = 0; depth < MaxInheritanceDepth; depth++)
            {
                if (string.Equals(current.FullName, baseTypeFullName, StringComparison.Ordinal))
                {
                    return true;
                }

                var baseType = current.BaseType;
                if (baseType == null)
                {
                    // 사슬의 뿌리에 닿았다. 답은 아니오이고, 그것은 진짜 답이다.
                    return false;
                }

                var resolved = SafeResolve(baseType);
                if (resolved == null)
                {
                    unresolved = true;
                    return false;
                }

                current = resolved;
            }

            unresolved = true;
            return false;
        }

        private static TypeDefinition SafeResolve(TypeReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (Exception)
            {
                // 해석되지 않는 참조는 결함이 아니라 평범한 입력이다. 값으로 치면 타입 하나에 대한 답하지 못한 물음
                // 하나다.
                return null;
            }
        }
    }
}
