using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
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
        /// 명령어 하나를 읽는다. 그것이 앉아 있는 블록으로 가둔 채.
        /// </summary>
        /// <remarks>
        /// 이 경계는 늦게 도착했고 내내 필요했다. 호출이 이루어진 객체의 이름을 대려면 그 인자들을 거슬러
        /// 걸어야 하는데, 인자는 명령어가 아니라 *스택 슬롯* 이다 — <c>transform.localScale = Scale * 1.2f</c>
        /// 에서 명령어 하나를 되짚으면 리터럴에 닿아 수신자를 <c>1.2</c> 라고 보고했다. 샘플 게임의 관측 가능한
        /// 효과 119건 중 49건이 숫자의 이름을 대거나 포기했다.
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
        /// 게임이 닫힌 뒤에도 쥐고 있는 상태.
        /// </summary>
        /// <remarks>
        /// 저장된 값은 실행보다 오래 살고, 그래서 테스트가 가장 단언하고 싶어 하는 것이면서 동시에 다음 테스트가
        /// 예상 밖의 자리에서 시작하게 만드는 것이다. 이것들을 읽는 것이 명세로 하여금 그 저장을 만든 것이
        /// 버튼이었다고 말할 수 있게 한다.
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
                    // 키가 먼저, 그다음 값: 키는 두 번 밀어 넣기 이전이다.
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

            // 변수에 담긴 키. 어느 슬롯이 쓰였는지는 여기서 답할 수 없고, 그렇다고 말하는 편이 엉뚱한 것의 이름을
            // 대는 것보다 낫다.
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

            // TextMeshPro 자신의 라벨 설정 방식. 실측한 모든 게임이 프로퍼티가 아니라 메서드를 쓰므로,
            // `set_text` 만 알아보면 샘플 게임의 채팅 창 둘은 관측 가능한 효과가 아예 없고 적들은 보이는 체력이
            // 없게 됐다.
            if (declaring != null && called.Name == "SetText" &&
                declaring.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return new Outcome
                {
                    Kind = "ui-value",
                    Category = "observable",
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    Target = Receiver(instruction, boundary) + ".text",
                    // 슬롯 하나가 아니라 인자 목록 전체를 본다: SetText 에는 서식 문자열과 숫자를 받는 오버로드가 있고,
                    // 그중 무엇이 돌았는지가 무엇이 바뀌었는지의 일부다.
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
                    // 인자 0 이지 앞 명령어가 아니다. 만들어지거나 파괴되는 것은 언제나 첫 인자이고 오버로드는 그 뒤가
                    // 다르므로, 하나 되짚으면 마지막에 온 것에 닿았다 — `Instantiate(prefab, position, rotation)` 의
                    // 회전, `Destroy(o, t)` 의 지연. 앞의 것 열셋과 뒤의 것 넷이 엉뚱한 이름을 댔고, 그중 넷은 모호한 것이
                    // 아니라 거짓이었다: `destroy Iceball.lifetime`.
                    Target = IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                             ?? "(not a simple target)",

                    // 여러 분기 중 하나에서 고른 프리팹을 분기가 합쳐진 뒤에 만든다. 이름은 지역 변수의 것이고, 이것들이
                    // 분기들이 거기 넣은 것이다.
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

            // 어떤 애니메이션인지이지 애니메이션이 있었다는 것만이 아니다. `SetTrigger` 는 자기가 당기는 매개변수의
            // 이름을 대고 그 이름은 명령어 안에 앉은 리터럴이다 — `CompareTag()` 를 `CompareTag("Spell")` 로
            // 만드는 것과 같은 읽기다. 그것이 없으면 이것들은 전부 "애니메이션이 바뀐다" 로 나왔는데, 그것은
            // 한꺼번에 전부에 대해 참이라 아무 말도 하지 않는다: 개발 빌드에서 기록 열넷, 에디터 스캔에서 열.
            //
            // 메서드 자신의 이름을 인자 앞에 남긴다. 트리거를 세우는 일과 숫자를 세우는 일은 화면에서 찾아볼 것이
            // 서로 다르기 때문이다.
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
                    // 인자 0 이지 수신자가 아니다: 이것들은 확장 메서드라 옮겨지는 transform 이 대고 불리는 것이 아니라
                    // 넘어가고, `Receiver` 는 없다고 옳게 말한다.
                    Target = (IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                              ?? "(not a simple target)") + "." + tweened,
                    Detail = IlReading.ArgumentAt(called, instruction, boundary, 1),

                    // 대상을 인자에서 읽는 것과 같은 이유로 인자에서 뿌리를 잡는다. 맵 커서가 `village` 로 걷는 것은
                    // 트윈이고 `battle1` 로 걷는 것은 평범한 대입이라, 한쪽만 본 감시자는 맵 이동 다섯 중 넷을 보고하고
                    // 그것을 전부라고 부르게 된다.
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

            // setter 만 본다. getter 도 같은 헬퍼가 알아보는데, 변화의 방향을 읽으려면 그것이 필요하기 때문이다 —
            // `currentLife -= 1` 은 setter 로 저장하기 전에 getter 로 가져온다 — 다만 가져오기는 변화가 아니고
            // 변화로 적어서도 안 된다.
            var written = called.Name.StartsWith("set_", System.StringComparison.Ordinal)
                ? SimpleSetter.FieldBehind(called)
                : null;

            if (written != null)
            {
                // 필드에 대입만 하는 프로퍼티는 필드를 쓰는 것과 같은 변화이고, 게임 자신의 코드가 둘 다로 한다 —
                // 클래스 안에서는 컴파일러가 필드를 쓰고, 밖에서는 setter 를 부른다. 두 절반이 같은 말을 하고 함께
                // 놓일 수 있도록 필드의 이름을 붙인다.
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

        /// <summary>
        /// 쓰인 값을 어디서 읽어 왔는가. 그것이 다른 객체에서 읽힌 것일 때.
        /// </summary>
        /// <remarks>
        /// 맵 커서는 다른 마커의 위치를 대입해서 옮기므로, 그 값에는 감시할 수 있는 제 자리가 있다. 그 모양일
        /// 때만 본다: 명령어가 무언가에 대한 프로퍼티 읽기여야 하고, 그 무언가가 필드로 뿌리내려야 한다. 셋에서
        /// 계산해 낸 값은 어딘가에 있는 것이 아니고, 있다고 말하면 물은 것과 다른 물음에 답하는 멤버를 감시
        /// 목록에 넣게 된다.
        /// </remarks>
        /// <summary>명령어가 밀어 넣는 문자열. 코드에 쓰인 것을 밀어 넣을 때.</summary>
        private static string Literal(Instruction from)
        {
            return from != null && from.OpCode.Code == Code.Ldstr ? from.Operand as string : null;
        }

        private static WatchTarget Source(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            return WatchTarget.ReadOff(from, boundary, within);
        }

        /// <summary>
        /// 쓰이고 있는 필드, 그리고 가릴 수 있을 때는 어느 방향으로인지.
        /// </summary>
        /// <remarks>
        /// 방향이 요점이다. 방향키 한 쌍은 같은 메서드에서 같은 필드에 쓰고, 그 둘을 가르는 것은 하나는 더하고
        /// 하나는 뺀다는 점이다.
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
        /// 호출이 무엇에 대고 이루어졌는가. 명령어가 아니라 스택 슬롯으로 걸어서 찾는다.
        /// </summary>
        /// <remarks>
        /// 인자의 개수는 시그니처에서 오고, 그 하나하나를 건너뛰는 것은 <see cref="IlReading.Under"/> 의
        /// 몫이다 — 인자 하나가 명령어 하나일 수도 스물일 수도 있다. 정해진 수만큼 명령어를 되짚는 방식은
        /// 리터럴을 수신자로 지목했는데, 그것은 모른다고 인정하는 것보다 나쁘다: <c>1.2.localScale</c> 과
        /// <c>0.sprite</c> 는 누군가 행동할 수 있는 값처럼 읽힌다.
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

        /// <summary>값 하나를 고르는 분기가 몇 개까지 여전히 나열할 값이 있는 선택인지.</summary>
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
        /// 트윈 라이브러리가 transform 의 어느 부분을 바꾸라고 들었는가, 또는 null.
        /// </summary>
        /// <remarks>
        /// 트윈은 프로퍼티에 대입하는 것만큼이나 확실하게 화면 위의 것을 옮기고, 그것을 빼놓은 값으로 샘플
        /// 게임은 맵 전체를 잃었다: 화살표 키 기록 여덟이 레인 인덱스만 바꾸고 그 밖에는 아무것도 바꾸지
        /// 않았으므로, 사람이 보는 유일한 것 — 캐릭터가 다음 스테이지로 걸어가는 것 — 을 적을 수 없었다.
        ///
        /// 시그니처 목록이 아니라 이름의 모양으로 맞춘다. 낡아 가는 부분이 그 목록이기 때문이다. 무언가를 바꾸는
        /// 모든 단축 메서드는 자기가 바꾸는 것의 이름을 따르고, 돌고 있는 트윈을 조종하기만 하는 것들은
        /// (<c>DOKill</c>, <c>DOComplete</c>, <c>DOPause</c>) 그쪽 이름을 따라 걸리지 않고 빠진다 — 이것은
        /// 모양에 의한 허용 목록이지 "DO 로 시작하는 아무거나" 가 아니다.
        ///
        /// 여기서는 아무것도 해석하지 않는다. 네임스페이스와 매개변수 타입은 어셈블리가 저장해 둔 그대로 참조
        /// 에서 읽으므로, 그 라이브러리가 없는 프로젝트는 찾지 못해 실패하는 대신 전과 똑같이 읽힌다. 제3자의
        /// 이름을 여기서 댈 수 있는 이유 전체가 그것이다: <see cref="CallGraph"/> 는 본문을 읽을 수 없는 호출을
        /// 따라가지 못하지만, 호출 지점에서 변화의 이름을 대는 데는 본문이 필요한 적이 없었다.
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
        /// 새 값이 화면 위에 나타나는 프로퍼티.
        /// </summary>
        /// <remarks>
        /// uGUI 와 TMP 뿐 아니라 Renderer 도 여기 있다. <c>SpriteRenderer</c> 에서 바뀐 스프라이트는
        /// <c>Image</c> 에서 바뀐 것만큼 잘 보이고, 그것을 빼놓은 값은 놓치기 쉬웠다: 유일한 효과가 인식되지
        /// 않는 블록은 효과가 하나도 없는 것이 되어, 그것에 대한 다른 무엇이 읽히기도 전에 떨어져 나간다. 샘플
        /// 게임의 맵 배경이 그렇게 그려지는데, 그것을 결정하는 <c>switch</c> 는 올바르게 읽히면서 정작 그것이
        /// 다스리는 기록은 더 이상 존재하지 않았다.
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
        /// 이 명령어가 쓰기의 대상인 바로 그 필드를 가져왔는지.
        /// </summary>
        /// <remarks>
        /// 필드를 읽어서든, 그것을 읽는 프로퍼티를 불러서든. 클래스 밖에서 쓴 <c>currentLife -= 1</c> 은
        /// getter, 빼기, setter 로 컴파일되고 — 방향이 그 문장의 요점 전부이므로 getter 도 세어야 한다.
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
