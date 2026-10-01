using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityPlayMcp.Affordances.CodeGen;

namespace UnityPlayMcp.Tests.CodeGen
{
    /// <summary>
    /// IL <c>switch</c> 의 각 경로에 닿는 조건이 실제 switch 의 분기와 같은 값 집합을 덮는지 확인한다 (#82).
    /// </summary>
    /// <remarks>
    /// 조건을 문자열로 비교하지 않고 값마다 계산해 실제 switch 와 견준다. IL <c>switch</c> 는 피연산자를 부호 없이
    /// 보므로 case 범위보다 작은 값(음수 포함)도 default 로 간다.
    /// </remarks>
    public sealed class SwitchDefaultConditionTests
    {
        private const string SubjectName = "kind";

        [Test]
        public void DefaultPathCoversValuesBelowAndAboveTheCases()
        {
            var path = ReachOfDefault(firstCase: 0, cases: 3);

            AssertMatchesSwitch(path, firstCase: 0, cases: 3, taken: null);
            Assert.That(Holds(path, -1), Is.True, "a value below the first case reaches default");
            Assert.That(Holds(path, int.MinValue), Is.True);
            Assert.That(Holds(path, 3), Is.True);
        }

        [Test]
        public void DefaultPathOfAShiftedSwitchCoversValuesBelowAndAboveTheCases()
        {
            // 컴파일러는 case 5..7 을 `subject - 5` 를 switch 하는 것으로 내린다.
            var path = ReachOfDefault(firstCase: 5, cases: 3);

            AssertMatchesSwitch(path, firstCase: 5, cases: 3, taken: null);
            Assert.That(Holds(path, 4), Is.True, "just below the range reaches default");
            Assert.That(Holds(path, -1), Is.True, "a negative value reaches default");
            Assert.That(Holds(path, 8), Is.True, "just above the range reaches default");
            foreach (var inRange in new[] { 5, 6, 7 })
            {
                Assert.That(Holds(path, inRange), Is.False, inRange + " is a case");
            }
        }

        [Test]
        public void CasePathIsTheCaseValueOfTheShiftedSwitch()
        {
            var path = ReachOfCase(firstCase: 5, cases: 3, index: 1);

            AssertMatchesSwitch(path, firstCase: 5, cases: 3, taken: 6);
        }

        /// <summary>조건이 -20..40 의 모든 값에서 실제 switch 와 같은 답을 내는지 본다.</summary>
        private static void AssertMatchesSwitch(Condition path, int firstCase, int cases, int? taken)
        {
            for (var value = -20; value <= 40; value++)
            {
                // IL switch 는 (value - firstCase) 를 부호 없이 case 수와 견준다.
                var inRange = unchecked((uint)(value - firstCase)) < (uint)cases;
                var expected = taken.HasValue ? value == taken.Value : !inRange;

                Assert.That(Holds(path, value), Is.EqualTo(expected), "value " + value);
            }
        }

        private static bool Holds(Condition condition, int value)
        {
            switch (condition.Kind)
            {
                case ConditionKind.Always:
                    return true;
                case ConditionKind.Every:
                    return condition.Parts.All(part => Holds(part, value));
                case ConditionKind.Either:
                    return condition.Parts.Any(part => Holds(part, value));
                case ConditionKind.Test:
                    Assert.That(condition.Test.Left, Is.EqualTo(SubjectName));
                    var right = int.Parse(condition.Test.Right);

                    switch (condition.Test.Operator)
                    {
                        case "==": return value == right;
                        case "<": return value < right;
                        case ">=": return value >= right;
                        default: throw new AssertionException("unexpected operator " + condition.Test.Operator);
                    }
                default:
                    throw new AssertionException("unexpected condition kind " + condition.Kind);
            }
        }

        private static Condition ReachOfDefault(int firstCase, int cases)
        {
            return ReachOfBlockStartingAt(firstCase, cases, (decision, _) => decision.Next);
        }

        private static Condition ReachOfCase(int firstCase, int cases, int index)
        {
            return ReachOfBlockStartingAt(firstCase, cases, (decision, targets) => targets[index]);
        }

        /// <summary>
        /// <c>static void Run(int kind)</c> 를 만든다: `kind` 를 switch 하고, 각 경로는 <c>nop; ret</c> 로 끝난다.
        /// </summary>
        private static Condition ReachOfBlockStartingAt(
            int firstCase,
            int cases,
            Func<Instruction, Instruction[], Instruction> pick)
        {
            var method = Build(firstCase, cases);
            var graph = ControlFlowGraph.Build(method.Body);
            var dependence = ControlDependence.Compute(graph);

            var decision = method.Body.Instructions.Single(one => one.OpCode.Code == Code.Switch);
            var target = pick(decision, (Instruction[])decision.Operand);
            var block = graph.Blocks.Single(one => ReferenceEquals(one.First, target));

            return VariantBuilder.ReachOf(
                graph, dependence, block.Index, new Condition[graph.Blocks.Count], new byte[graph.Blocks.Count]);
        }

        private static MethodDefinition Build(int firstCase, int cases)
        {
            var module = ModuleDefinition.CreateModule("SwitchFixture", ModuleKind.Dll);
            var type = new TypeDefinition(
                "Fixture", "Switcher",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(type);

            var method = new MethodDefinition(
                "Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition(SubjectName, ParameterAttributes.None, module.TypeSystem.Int32));
            type.Methods.Add(method);

            var il = method.Body.GetILProcessor();
            var arms = Enumerable.Range(0, cases).Select(_ => il.Create(OpCodes.Nop)).ToArray();

            il.Emit(OpCodes.Ldarg_0);

            if (firstCase != 0)
            {
                il.Emit(OpCodes.Ldc_I4, firstCase);
                il.Emit(OpCodes.Sub);
            }

            il.Emit(OpCodes.Switch, arms);

            // switch 바로 뒤가 default(fall-through) 경로다.
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);

            foreach (var arm in arms)
            {
                il.Append(arm);
                il.Emit(OpCodes.Ret);
            }

            // 명령어 offset 은 쓰고 다시 읽어야 채워진다. 메서드 본문은 접근할 때 읽으므로 스트림을 닫지 않는다
            // (MemoryStream 은 해제할 자원이 없다).
            var stream = new MemoryStream();
            module.Write(stream);
            stream.Position = 0;
            var reread = ModuleDefinition.ReadModule(stream);
            return reread.Types.Single(one => one.Name == "Switcher").Methods.Single(one => one.Name == "Run");
        }
    }
}
