using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Artel.CodeGen
{
    internal sealed class ActionMethodWeaver
    {
        private const string ActionAttributeName = "Artel.Tracking.ArtelActionAttribute";
        private const string AsyncAttributeName = "System.Runtime.CompilerServices.AsyncStateMachineAttribute";
        private const string IteratorAttributeName = "System.Runtime.CompilerServices.IteratorStateMachineAttribute";
        private const string BufferFieldName = "__artelActionBuffer";

        private readonly ModuleDefinition module;
        private readonly List<DiagnosticMessage> diagnostics;
        private readonly TypeReference actionSourceType;
        private readonly TypeReference bufferType;
        private readonly MethodReference interfaceGetter;
        private readonly MethodReference getOrCreate;
        private readonly MethodReference recordSuccess;
        private readonly MethodReference recordFailure;
        private readonly MethodReference nonSerializedConstructor;
        private readonly TypeReference exceptionType;

        public ActionMethodWeaver(ModuleDefinition module, List<DiagnosticMessage> diagnostics)
        {
            this.module = module;
            this.diagnostics = diagnostics;
            var runtimeReference = module.AssemblyReferences
                .First(reference => reference.Name == "Artel.Runtime");
            var runtimeModule = module.AssemblyResolver.Resolve(runtimeReference).MainModule;
            var actionSource = runtimeModule.GetType("Artel.Tracking.IArtelActionSource");
            var buffer = runtimeModule.GetType("Artel.Tracking.ActionInvocationBuffer");
            var recorder = runtimeModule.GetType("Artel.Tracking.ArtelActionRecorder");
            var coreModule = module.TypeSystem.Object.Resolve().Module;
            var nonSerialized = coreModule.GetType("System.NonSerializedAttribute");

            actionSourceType = module.ImportReference(actionSource);
            bufferType = module.ImportReference(buffer);
            interfaceGetter = module.ImportReference(actionSource.Properties
                .First(property => property.Name == "ArtelActionBuffer").GetMethod);
            getOrCreate = module.ImportReference(recorder.Methods.First(method => method.Name == "GetOrCreate"));
            recordSuccess = module.ImportReference(recorder.Methods.First(method => method.Name == "RecordSuccess"));
            recordFailure = module.ImportReference(recorder.Methods.First(method => method.Name == "RecordFailure"));
            nonSerializedConstructor = module.ImportReference(nonSerialized.Methods
                .First(method => method.IsConstructor && !method.HasParameters));
            exceptionType = module.ImportReference(coreModule.GetType("System.Exception"));
        }

        public bool Process()
        {
            var changed = false;
            foreach (var type in module.Types.SelectMany(SelfAndNestedTypes))
            {
                var actionMethods = type.Methods.Where(HasActionAttribute).ToList();
                if (actionMethods.Count == 0)
                {
                    continue;
                }

                if (type.Fields.Any(field => field.Name == BufferFieldName))
                {
                    continue;
                }

                if (!IsComponent(type))
                {
                    AddError(type.FullName + " uses [ArtelAction] but does not derive from UnityEngine.Component.");
                    continue;
                }

                EnsureActionSource(type);
                changed = true;

                foreach (var method in actionMethods)
                {
                    if (!CanProcess(method))
                    {
                        continue;
                    }

                    Weave(method, GetTag(method));
                }
            }

            return changed;
        }

        private void EnsureActionSource(TypeDefinition type)
        {
            if (type.Interfaces.Any(item => item.InterfaceType.FullName == actionSourceType.FullName))
            {
                return;
            }

            var field = new FieldDefinition(BufferFieldName, FieldAttributes.Private, bufferType);
            field.CustomAttributes.Add(new CustomAttribute(nonSerializedConstructor));
            type.Fields.Add(field);

            var getter = new MethodDefinition(
                "get_ArtelActionBuffer",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual |
                MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
                bufferType);
            getter.Overrides.Add(interfaceGetter);
            var processor = getter.Body.GetILProcessor();
            processor.Append(processor.Create(OpCodes.Ldarg_0));
            processor.Append(processor.Create(OpCodes.Ldflda, field));
            processor.Append(processor.Create(OpCodes.Call, getOrCreate));
            processor.Append(processor.Create(OpCodes.Ret));
            type.Methods.Add(getter);

            var property = new PropertyDefinition("ArtelActionBuffer", PropertyAttributes.None, bufferType)
            {
                GetMethod = getter
            };
            type.Properties.Add(property);
            type.Interfaces.Add(new InterfaceImplementation(actionSourceType));
        }

        private bool CanProcess(MethodDefinition method)
        {
            if (method.IsStatic || method.IsConstructor || !method.HasBody || method.IsAbstract)
            {
                AddError(method.FullName + " must be a concrete instance method.");
                return false;
            }

            if (method.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName == AsyncAttributeName ||
                    attribute.AttributeType.FullName == IteratorAttributeName))
            {
                AddError(method.FullName + " cannot be async, iterator, or coroutine in the first tracking version.");
                return false;
            }

            if (method.ReturnType.IsByReference || method.ReturnType.IsPointer)
            {
                AddError(method.FullName + " has an unsupported by-reference or pointer return type.");
                return false;
            }

            return true;
        }

        private void Weave(MethodDefinition method, string tag)
        {
            var processor = method.Body.GetILProcessor();
            var originalReturns = method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToList();
            var returnsValue = method.ReturnType.MetadataType != MetadataType.Void;
            VariableDefinition returnValue = null;
            if (returnsValue)
            {
                returnValue = new VariableDefinition(method.ReturnType);
                method.Body.Variables.Add(returnValue);
                method.Body.InitLocals = true;
            }

            var exception = new VariableDefinition(exceptionType);
            method.Body.Variables.Add(exception);
            method.Body.InitLocals = true;

            var successStart = processor.Create(OpCodes.Ldarg_0);
            processor.Append(successStart);
            processor.Append(processor.Create(OpCodes.Ldstr, tag));
            processor.Append(processor.Create(OpCodes.Ldstr, method.Name));
            if (returnsValue)
            {
                processor.Append(processor.Create(OpCodes.Ldloc, returnValue));
                if (method.ReturnType.IsValueType || method.ReturnType.IsGenericParameter)
                {
                    processor.Append(processor.Create(OpCodes.Box, module.ImportReference(method.ReturnType)));
                }
            }
            else
            {
                processor.Append(processor.Create(OpCodes.Ldnull));
            }
            processor.Append(processor.Create(OpCodes.Call, recordSuccess));
            if (returnsValue)
            {
                processor.Append(processor.Create(OpCodes.Ldloc, returnValue));
            }
            processor.Append(processor.Create(OpCodes.Ret));

            var failureStart = processor.Create(OpCodes.Stloc, exception);
            processor.Append(failureStart);
            processor.Append(processor.Create(OpCodes.Ldarg_0));
            processor.Append(processor.Create(OpCodes.Ldstr, tag));
            processor.Append(processor.Create(OpCodes.Ldstr, method.Name));
            processor.Append(processor.Create(OpCodes.Ldloc, exception));
            processor.Append(processor.Create(OpCodes.Call, recordFailure));
            processor.Append(processor.Create(OpCodes.Rethrow));
            var handlerEnd = processor.Create(OpCodes.Nop);
            processor.Append(handlerEnd);

            foreach (var instruction in originalReturns)
            {
                if (returnsValue)
                {
                    processor.InsertBefore(instruction, processor.Create(OpCodes.Stloc, returnValue));
                }
                instruction.OpCode = OpCodes.Leave;
                instruction.Operand = successStart;
            }

            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = exceptionType,
                TryStart = method.Body.Instructions[0],
                TryEnd = successStart,
                HandlerStart = failureStart,
                HandlerEnd = handlerEnd
            });
        }

        private static bool HasActionAttribute(MethodDefinition method) =>
            method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == ActionAttributeName);

        private static string GetTag(MethodDefinition method)
        {
            var attribute = method.CustomAttributes.First(item => item.AttributeType.FullName == ActionAttributeName);
            return attribute.ConstructorArguments.Count == 0
                ? string.Empty
                : attribute.ConstructorArguments[0].Value as string ?? string.Empty;
        }

        private static IEnumerable<TypeDefinition> SelfAndNestedTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
            {
                yield return nested;
            }
        }

        private static bool IsComponent(TypeDefinition type)
        {
            try
            {
                var current = type;
                while (current != null)
                {
                    if (current.FullName == "UnityEngine.Component")
                    {
                        return true;
                    }
                    current = current.BaseType?.Resolve();
                }
            }
            catch (AssemblyResolutionException)
            {
            }

            return false;
        }

        private void AddError(string message)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Error,
                MessageData = "[Artel] " + message
            });
        }
    }
}
