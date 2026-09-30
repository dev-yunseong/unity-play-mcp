using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>타입 판정 결과.</summary>
    internal enum TypeVerdict
    {
        /// <summary>GameObject 위에 게임 로직을 실을 수 없다.</summary>
        NotBehaviour,

        /// <summary>MonoBehaviour 를 상속한다.</summary>
        Behaviour,

        /// <summary>
        /// 판정 전에 기반 타입 사슬이 끊겼다.
        /// </summary>
        /// <remarks>
        /// <see cref="NotBehaviour"/> 와 구분한다. 기반 클래스를 열 수 없는 타입을 behaviour 가 아닌 것으로 취급하면
        /// 게임 코드를 말없이 떨어뜨린다.
        /// </remarks>
        Unresolved
    }

    /// <summary>메서드를 분석하는 이유.</summary>
    internal enum MethodScope
    {
        /// <summary>플레이어 입력으로 닿지 않는다.</summary>
        OutOfScope,

        /// <summary>인스펙터에서 UnityEvent 에 연결할 수 있다. 예: 버튼의 onClick.</summary>
        InspectorCallable,

        /// <summary>엔진이 부른다. 키와 포인터 처리가 여기 있다.</summary>
        EngineMessage
    }

    /// <summary>
    /// 분석하기 전에 분석할 대상을 좁힌다.
    /// </summary>
    /// <remarks>
    /// 대부분의 메서드는 결과에 영향을 주지 않으므로 먼저 좁히는 것이 뒤 단계를 작고 빠르게 유지한다.
    /// </remarks>
    internal static class AnalysisScope
    {
        private const string BehaviourTypeName = "UnityEngine.MonoBehaviour";
        private const string ObjectTypeName = "UnityEngine.Object";

        /// <summary>
        /// 기반 타입을 거슬러 오르는 최대 깊이.
        /// </summary>
        /// <remarks>
        /// 깊이가 아니라 순환을 막는 한계다. 손으로 쓰거나 난독화한 어셈블리는 타입을 제 조상으로 적을 수 있고, 그러면
        /// 에디터가 멈춘다.
        /// </remarks>
        private const int MaxInheritanceDepth = 32;

        /// <summary>
        /// 이 명령어 수를 넘으면 메서드를 건드리지 않는다.
        /// </summary>
        /// <remarks>
        /// 이만큼 큰 메서드는 대개 컴파일러가 생성한 상태 기계나 switch 분배기다. 조용히 떨어뜨리지 않고 세어서 보고한다.
        /// </remarks>
        internal const int MaxInstructions = 4000;

        /// <summary>
        /// 엔진이 behaviour 위에서 부르는 메서드.
        /// </summary>
        /// <remarks>
        /// 가시성과 무관하게 모은다. 관례상 private 이라 public 만 보는 필터는 포인터 처리와 <c>Update</c> 의 키 입력을
        /// 놓친다.
        /// </remarks>
        private static readonly HashSet<string> EngineMessages = new HashSet<string>(StringComparer.Ordinal)
        {
            "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
            "Update", "FixedUpdate", "LateUpdate", "OnGUI",

            // 종료 시 저장하는 게임은 여기서 다음 실행의 시작 상태를 바꾼다. 빼면 "종료하면 진행이 저장된다" 에
            // `evidence` 가 없다.
            "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit",
            "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
            "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",

            // 이벤트 시스템이 인터페이스로 부르는 핸들러. 인자가 EventData 라 인스펙터 호출 규칙에 걸리지 않으므로
            // 이름으로 모은다. uGUI 프로젝트의 클릭과 드래그가 여기 있다.
            "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
            "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll",
            "OnInitializePotentialDrag", "OnSubmit", "OnCancel", "OnMove",
            "OnSelect", "OnDeselect"
        };

        /// <summary>타입이 GameObject 위에서 게임 로직을 나를 수 있는지 판정한다.</summary>
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
        /// UnityEvent 가 이 메서드에 대한 persistent call 을 담을 수 있을 때 참.
        /// </summary>
        /// <remarks>
        /// 인스펙터 드롭다운 규칙을 따른다: void 를 돌려주는 인스턴스 메서드로, 인자가 없거나 인스펙터가 채울 수 있는
        /// 인자 하나를 받는다.
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
        /// 명령어를 순서대로 읽으면 틀린 답이 나올 때 참.
        /// </summary>
        /// <remarks>
        /// 분기가 없으면 블록 하나이므로 그래프가 필요 없다. 그 밖에는 그래프가 필요하다. 순서대로 읽으면 <c>||</c> 가 낀
        /// <c>if/else</c> 사슬에서 한 키가 지키는 본문을 다른 키의 것으로 읽는다.
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
        /// 답 없이 멈췄을 때 참이다. 기반 타입을 열 수 없거나 사슬이 순환을 의심할 만큼 길다.
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
                    // 사슬의 뿌리에 닿았으므로 확정된 아니오다.
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
                // 해석되지 않는 참조는 결함이 아니라 평범한 입력이다.
                return null;
            }
        }
    }
}
