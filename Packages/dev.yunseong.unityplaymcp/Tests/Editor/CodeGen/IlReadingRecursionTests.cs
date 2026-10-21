using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityPlayMcp.Affordances.CodeGen;

namespace UnityPlayMcp.Tests.CodeGen
{
    public sealed class IlReadingRecursionTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void SelfReferencingStoreFallsBackToField(bool local, bool negate)
        {
            Assert.That(Describe((module, type, method, il) =>
            {
                var field = Field(type, "value", module.TypeSystem.Int32);
                il.Emit(OpCodes.Ldsfld, field);
                if (local)
                {
                    method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                    il.Emit(OpCodes.Stloc_0);
                    il.Emit(OpCodes.Ldloc_0);
                }
                if (negate) il.Emit(OpCodes.Neg);
                else
                {
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Stsfld, field);
                il.Emit(OpCodes.Ldsfld, field);
            }), Is.EqualTo("Subject.value"));
        }

        [Test]
        public void MutuallyReferencingStoresFallBackToField()
        {
            Assert.That(Describe((module, type, method, il) =>
            {
                var first = Field(type, "first", module.TypeSystem.Int32);
                var second = Field(type, "second", module.TypeSystem.Int32);
                il.Emit(OpCodes.Ldsfld, second);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stsfld, first);
                il.Emit(OpCodes.Ldsfld, first);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Stsfld, second);
                il.Emit(OpCodes.Ldsfld, first);
            }), Is.EqualTo("Subject.first"));
        }

        [Test]
        public void FourLevelsOfArithmeticStillHaveNames()
        {
            Assert.That(Describe((module, type, method, il) =>
            {
                il.Emit(OpCodes.Ldc_I4_1);
                for (var value = 2; value <= 5; value++)
                {
                    il.Emit(OpCodes.Ldc_I4, value);
                    il.Emit(OpCodes.Add);
                }
            }), Is.EqualTo("((((1 + 2) + 3) + 4) + 5)"));
        }

        [Test]
        public void CachedActiveSceneNameStillResolves()
        {
            Assert.That(Describe((module, type, method, il) =>
            {
                var field = Field(type, "scene", module.TypeSystem.String);
                method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(Scene))));
                il.Emit(OpCodes.Call, module.ImportReference(typeof(SceneManager).GetMethod("GetActiveScene")));
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, method.Body.Variables[0]);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(Scene).GetProperty("name").GetMethod));
                il.Emit(OpCodes.Stsfld, field);
                il.Emit(OpCodes.Ldsfld, field);
            }), Is.EqualTo("SceneManager.GetActiveScene().name"));
        }

        private static FieldDefinition Field(TypeDefinition owner, string name, TypeReference type)
        {
            var field = new FieldDefinition(name, FieldAttributes.Private | FieldAttributes.Static, type);
            owner.Fields.Add(field);
            return field;
        }

        private static string Describe(Action<ModuleDefinition, TypeDefinition, MethodDefinition, ILProcessor> emit)
        {
            using (var module = ModuleDefinition.CreateModule("RecursionFixture", ModuleKind.Dll))
            {
                var type = new TypeDefinition("Fixture", "Subject", TypeAttributes.Public, module.TypeSystem.Object);
                module.Types.Add(type);
                var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static,
                    module.TypeSystem.Void);
                type.Methods.Add(method);
                method.Body.InitLocals = true;
                var il = method.Body.GetILProcessor();
                emit(module, type, method, il);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
                // 명령어 offset을 채워 실제 컴파일 결과와 같은 경계 판정을 사용한다.
                using (var stream = new MemoryStream())
                {
                    module.Write(stream);
                    stream.Position = 0;
                    using (var read = ModuleDefinition.ReadModule(stream))
                    {
                        var run = read.Types.Single(one => one.Name == "Subject").Methods[0];
                        var instructions = run.Body.Instructions;
                        return IlReading.Describe(instructions[instructions.Count - 3], instructions[0], run);
                    }
                }
            }
        }
    }
}
