using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>명령어 하나가 만드는 변화를 알아본다.</summary>
    internal static class OutcomeReader
    {
        private const string SceneManagerType = "UnityEngine.SceneManagement.SceneManager";
        private const string ApplicationType = "UnityEngine.Application";
        private const string PlayerPrefsType = "UnityEngine.PlayerPrefs";
        private const string GameObjectType = "UnityEngine.GameObject";
        private const string TransformType = "UnityEngine.Transform";
        private const string ObjectType = "UnityEngine.Object";

        /// <summary>
        /// 명령어 하나를 읽는다. 거슬러 읽기는 <paramref name="boundary"/> 블록 안으로 제한한다.
        /// </summary>
        /// <remarks>
        /// receiver 를 찾으려면 명령어가 아니라 스택 슬롯 단위로 인자를 건너뛰어야 한다.
        /// <c>transform.localScale = Scale * 1.2f</c> 에서 명령어 하나만 되짚으면 receiver 가 <c>1.2</c> 가 된다.
        /// </remarks>
        internal static Outcome ReadDirect(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            Outcome outcome;

            switch (instruction.OpCode.Code)
            {
                case Code.Stfld:
                case Code.Stsfld:
                    outcome = Write(instruction, boundary, within);
                    break;

                case Code.Call:
                case Code.Callvirt:
                    outcome = Called(instruction, boundary, within);
                    break;

                default:
                    return null;
            }

            if (outcome != null)
            {
                outcome.Offset = instruction.Offset;
            }

            return outcome;
        }

        /// <summary>
        /// <c>PlayerPrefs</c> 에 저장되는 상태.
        /// </summary>
        /// <remarks>
        /// 저장된 값은 실행이 끝나도 남아 다음 테스트의 시작 상태에 영향을 준다.
        /// </remarks>
        private static Outcome Stored(
            Instruction instruction, MethodReference called, Instruction boundary,
            MethodDefinition within)
        {
            switch (called.Name)
            {
                case "SetInt":
                case "SetFloat":
                case "SetString":
                    // 키는 값보다 먼저 스택에 올라가므로 값 슬롯 아래에 있다.
                    return new Outcome
                    {
                        Kind = "saved",
                        Category = "state",
                        Target = Key(IlReading.Under(IlReading.Preceding(instruction, boundary), boundary)),
                        Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within)
                    };

                case "DeleteKey":
                    return new Outcome
                    {
                        Kind = "saved",
                        Category = "state",
                        Target = Key(IlReading.Preceding(instruction, boundary)),
                        Detail = "deleted"
                    };

                case "DeleteAll":
                    return new Outcome { Kind = "saved", Category = "state", Target = "*", Detail = "deleted" };

                default:
                    return null;
            }
        }

        private static string Key(Instruction instruction)
        {
            if (instruction != null && instruction.OpCode.Code == Code.Ldstr)
            {
                return instruction.Operand as string;
            }

            // 변수에 담긴 키는 추측하지 않는다. 잘못된 이름보다 모른다고 적는 편이 낫다.
            return "(not a literal)";
        }

        private static Outcome Called(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            if (!(instruction.Operand is MethodReference called))
            {
                return null;
            }

            var declaring = called.DeclaringType?.FullName;

            if (declaring == GameObjectType && called.Name == "SetActive")
            {
                return new Outcome
                {
                    Kind = "active-state",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (called.Name == "set_enabled" && IsUnityType(declaring))
            {
                return new Outcome
                {
                    Kind = "component-enabled",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (called.Name == "set_interactable" &&
                declaring != null && declaring.StartsWith("UnityEngine.UI.", System.StringComparison.Ordinal))
            {
                return new Outcome
                {
                    Kind = "interactable",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (declaring == TransformType && IsTransformSetter(called.Name))
            {
                return new Outcome
                {
                    Kind = "transform",
                    Category = "observable",
                    Target = Receiver(instruction, boundary) + "." + called.Name.Substring(4),
                    Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    WatchSource = Source(IlReading.Preceding(instruction, boundary), boundary, within)
                };
            }

            // TextMeshPro 는 `set_text` 대신 `SetText` 메서드로 라벨을 바꾸는 경우가 많다.
            if (declaring != null && called.Name == "SetText" &&
                declaring.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return new Outcome
                {
                    Kind = "ui-value",
                    Category = "observable",
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    Target = Receiver(instruction, boundary) + ".text",
                    // SetText 는 서식 문자열과 숫자를 받는 overload 가 있어 인자 전체를 적는다.
                    Detail = IlReading.Arguments(called, instruction, boundary)
                };
            }

            if (IsUiSetter(declaring, called.Name))
            {
                return new Outcome
                {
                    Kind = "ui-value",
                    Category = "observable",
                    Target = Receiver(instruction, boundary) + "." + called.Name.Substring(4),
                    Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (declaring == ObjectType &&
                (called.Name == "Instantiate" || called.Name == "Destroy" || called.Name == "DestroyImmediate"))
            {
                return new Outcome
                {
                    Kind = called.Name == "Instantiate" ? "instantiate" : "destroy",
                    Category = "observable",
                    // 대상은 항상 인자 0 이다. 앞 명령어를 보면 `Instantiate(prefab, position, rotation)` 의 회전이나
                    // `Destroy(o, t)` 의 지연을 대상으로 잘못 읽는다.
                    Target = IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                             ?? "(not a simple target)",

                    // 분기마다 다른 프리팹을 지역 변수에 넣고 합류 뒤에 만들 때, 각 분기가 넣은 값들이다.
                    TargetCandidates = IlReading.Candidates(
                        IlReading.ArgumentFrom(called, instruction, boundary, 0),
                        boundary, within, MostCandidates)
                };
            }

            if (declaring != null &&
                declaring.StartsWith("UnityEngine.Events.UnityEvent", System.StringComparison.Ordinal) &&
                called.Name == "Invoke")
            {
                return new Outcome
                {
                    Kind = "event",
                    Category = "observable",
                    Target = Receiver(instruction, boundary)
                };
            }

            // 파라미터 이름 리터럴까지 적어야 어떤 애니메이션인지 구분된다. `SetTrigger` 와 `SetFloat` 는
            // 화면에서 확인할 것이 다르므로 메서드 이름도 남긴다.
            if (declaring == "UnityEngine.Animator" && called.Name.StartsWith("Set", System.StringComparison.Ordinal))
            {
                var arguments = IlReading.Arguments(called, instruction, boundary);

                return new Outcome
                {
                    Kind = "animation",
                    Category = "observable",
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    AnimatorName = Literal(IlReading.ArgumentFrom(called, instruction, boundary, 0)),
                    Target = Receiver(instruction, boundary),
                    Detail = arguments == null
                        ? called.Name
                        : called.Name + "(" + arguments + ")"
                };
            }

            if (declaring == "UnityEngine.AudioSource" &&
                (called.Name == "Play" || called.Name == "PlayOneShot" || called.Name == "Stop"))
            {
                return new Outcome
                {
                    Kind = "audio",
                    Category = "observable",
                    Target = Receiver(instruction, boundary),
                    Detail = called.Name
                };
            }

            if (declaring != null &&
                (declaring == "UnityEngine.Rigidbody" || declaring == "UnityEngine.Rigidbody2D") &&
                (called.Name == "MovePosition" || called.Name == "MoveRotation"))
            {
                return new Outcome
                {
                    Kind = "physics-move",
                    Category = "observable",
                    Target = Receiver(instruction, boundary),
                    Detail = called.Name
                };
            }

            var tweened = TweenedTransform(called);

            if (tweened != null)
            {
                return new Outcome
                {
                    Kind = "transform",
                    Category = "observable",
                    // 확장 메서드라 transform 은 receiver 가 아니라 인자 0 으로 넘어간다.
                    Target = (IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                              ?? "(not a simple target)") + "." + tweened,
                    Detail = IlReading.ArgumentAt(called, instruction, boundary, 1),

                    // 같은 이유로 감시 대상도 인자 0 에서 찾는다. 트윈 이동을 놓치면 대입 이동만 보고된다.
                    Watch = WatchTarget.From(IlReading.RootedAt(
                        IlReading.ArgumentFrom(called, instruction, boundary, 0), boundary, within)),
                    WatchSource = Source(
                        IlReading.ArgumentFrom(called, instruction, boundary, 1), boundary, within)
                };
            }

            if (declaring == PlayerPrefsType)
            {
                return Stored(instruction, called, boundary, within);
            }

            if (declaring == SceneManagerType &&
                (called.Name == "LoadScene" || called.Name == "LoadSceneAsync"))
            {
                var argument = IlReading.Preceding(instruction, boundary);

                if (argument != null && argument.OpCode.Code == Code.Ldstr)
                {
                    return new Outcome { Kind = "scene", Category = "observable", Target = argument.Operand as string };
                }

                if (IlReading.TryConstant(argument, out var index))
                {
                    return new Outcome { Kind = "scene", Category = "observable", Target = "#" + index };
                }

                return new Outcome { Kind = "scene", Category = "observable", Target = "(not a literal)" };
            }

            // setter 만 효과로 센다. 같은 헬퍼가 getter 도 알아보는 것은 방향(`Direction`)을 읽기
            // 위해서다.
            var written = called.Name.StartsWith("set_", System.StringComparison.Ordinal)
                ? SimpleSetter.FieldBehind(called)
                : null;

            if (written != null)
            {
                // 클래스 안의 필드 저장과 같은 이름이 되도록 프로퍼티가 아니라 필드 이름을 쓴다.
                return new Outcome
                {
                    Kind = "write",
                    Category = "state",
                    Target = IlReading.FieldName(written),
                    Detail = Direction(instruction, written) ?? IlReading.Describe(instruction.Previous),
                    Watch = WatchTarget.Of(written, !called.HasThis)
                };
            }

            if (declaring == ApplicationType && called.Name == "Quit")
            {
                return new Outcome { Kind = "quit", Category = "observable", Target = string.Empty };
            }

            return null;
        }

        /// <summary>명령어가 올리는 문자열 리터럴. 리터럴이 아니면 null.</summary>
        private static string Literal(Instruction from)
        {
            return from != null && from.OpCode.Code == Code.Ldstr ? from.Operand as string : null;
        }

        /// <summary>
        /// 대입된 값을 읽어 온 멤버. 필드에 뿌리를 둔 프로퍼티 읽기일 때만.
        /// </summary>
        /// <remarks>
        /// 계산된 값은 되읽을 자리가 없으므로 받지 않는다.
        /// </remarks>
        private static WatchTarget Source(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            return WatchTarget.ReadOff(from, boundary, within);
        }

        /// <summary>
        /// 필드 쓰기와, 알 수 있으면 증감 방향.
        /// </summary>
        /// <remarks>
        /// 방향키 한 쌍은 같은 필드에 쓰므로 더하기와 빼기로만 구분된다.
        /// </remarks>
        private static Outcome Write(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            var field = instruction.Operand as FieldReference;
            var name = IlReading.FieldName(field);

            if (name == null)
            {
                return null;
            }

            var detail = Direction(instruction, field)
                         ?? IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within);
            return new Outcome
            {
                Kind = "write",
                Category = "state",
                Target = name,
                Detail = detail,
                Watch = WatchTarget.Of(field, instruction.OpCode.Code == Code.Stsfld)
            };
        }

        /// <summary>
        /// 호출의 receiver. 명령어가 아니라 스택 슬롯 단위로 거슬러 찾는다.
        /// </summary>
        /// <remarks>
        /// 인자 하나가 명령어 여러 개일 수 있어 건너뛰기는 <see cref="IlReading.Under"/> 가 맡는다. 명령어 수로
        /// 되짚으면 <c>1.2.localScale</c> 처럼 리터럴을 receiver 로 잘못 적는다.
        /// </remarks>
        private static string Receiver(Instruction call, Instruction boundary)
        {
            return IlReading.Receiver(call.Operand as MethodReference, call, boundary)
                   ?? "(not a simple receiver)";
        }

        private static string Boolean(Instruction instruction, Instruction boundary)
        {
            return IlReading.TryConstant(instruction, out var value)
                ? (value == 0 ? "false" : "true")
                : IlReading.Describe(instruction, boundary) ?? "(not a literal)";
        }

        /// <summary>대상 후보를 나열하는 최대 분기 수.</summary>
        private const int MostCandidates = 8;

        private static bool IsUnityType(string fullName)
        {
            return fullName != null && fullName.StartsWith("UnityEngine.", System.StringComparison.Ordinal);
        }

        private static bool IsTransformSetter(string name)
        {
            return name == "set_position" || name == "set_localPosition" ||
                   name == "set_rotation" || name == "set_localRotation" ||
                   name == "set_localScale";
        }

        /// <summary>
        /// DOTween 호출이 바꾸는 transform 부분. 해당하지 않으면 null.
        /// </summary>
        /// <remarks>
        /// 시그니처 목록 대신 이름 모양(Move, Rotate, Scale 등)으로 맞춘다. <c>DOKill</c>, <c>DOComplete</c>,
        /// <c>DOPause</c> 처럼 트윈을 조종만 하는 메서드는 걸리지 않는다.
        ///
        /// 참조만 읽고 resolve 하지 않으므로 DOTween 이 없는 프로젝트에서도 실패하지 않는다.
        /// </remarks>
        private static string TweenedTransform(MethodReference called)
        {
            var declaring = called.DeclaringType?.FullName;

            if (declaring == null ||
                !declaring.StartsWith("DG.Tweening.", System.StringComparison.Ordinal) ||
                !called.Name.StartsWith("DO", System.StringComparison.Ordinal) ||
                called.Parameters.Count < 2 ||
                called.Parameters[0].ParameterType?.FullName != TransformType)
            {
                return null;
            }

            var name = called.Name;

            if (name.Contains("Move") || name.Contains("Jump") || name.Contains("Path"))
            {
                return "position";
            }

            if (name.Contains("Rotat") || name.Contains("LookAt"))
            {
                return "rotation";
            }

            return name.Contains("Scale") ? "localScale" : null;
        }

        /// <summary>
        /// 새 값이 화면에 보이는 프로퍼티 setter 인지 본다.
        /// </summary>
        /// <remarks>
        /// uGUI, TMP 외에 <c>SpriteRenderer</c> 도 포함한다. 효과가 하나도 인식되지 않는 블록은 기록에서 빠진다.
        /// </remarks>
        private static bool IsUiSetter(string declaring, string name)
        {
            if (declaring == null)
            {
                return false;
            }

            if (declaring == "UnityEngine.SpriteRenderer")
            {
                return name == "set_sprite" || name == "set_color" ||
                       name == "set_flipX" || name == "set_flipY";
            }

            if (!declaring.StartsWith("UnityEngine.UI.", System.StringComparison.Ordinal) &&
                !declaring.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return false;
            }

            return name == "set_text" || name == "set_sprite" || name == "set_color" ||
                   name == "set_value" || name == "set_isOn";
        }

        private static string Direction(Instruction store, FieldReference field)
        {
            var operation = store.Previous;

            if (operation == null)
            {
                return null;
            }

            string sign;

            if (operation.OpCode.Code == Code.Add) sign = "+";
            else if (operation.OpCode.Code == Code.Sub) sign = "-";
            else return null;

            if (!IlReading.TryConstant(operation.Previous, out var step))
            {
                return null;
            }

            return ReadsSame(operation.Previous.Previous, field) ? sign + step : null;
        }

        /// <summary>
        /// 이 명령어가 쓰기 대상과 같은 필드를 읽는지 본다.
        /// </summary>
        /// <remarks>
        /// 클래스 밖의 <c>currentLife -= 1</c> 은 getter, 빼기, setter 로 컴파일되므로 사소한 getter 호출도 센다.
        /// </remarks>
        private static bool ReadsSame(Instruction load, FieldReference field)
        {
            if (load == null)
            {
                return false;
            }

            if (load.OpCode.Code == Code.Ldfld || load.OpCode.Code == Code.Ldsfld)
            {
                return load.Operand is FieldReference loaded && loaded.FullName == field.FullName;
            }

            if (load.OpCode.Code != Code.Call && load.OpCode.Code != Code.Callvirt)
            {
                return false;
            }

            var read = SimpleSetter.FieldBehind(load.Operand as MethodReference);
            return read != null && read.FullName == field.FullName;
        }
    }
}
